using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReplayDecoder;
using System.Buffers;

namespace BeatLeader_Server.Controllers
{
    public class DistanceWeightFunction
    {
        public double Divider { get; }
        public double HalfPower { get; }

        public DistanceWeightFunction(double bellWidth, double steepnessPower)
        {
            Divider = -(2 * Math.Pow(bellWidth, steepnessPower));
            HalfPower = steepnessPower / 2;
        }

        public double GetWeight(double distance)
        {
            double sqr = distance * distance;
            double expPower = Math.Pow(sqr, HalfPower) / Divider;
            return Math.Exp(expPower);
        }
    }

    public class ChartProcessor
    {
        public static List<double> ProcessChartData(List<List<double>> chartData, double resolution, double smoothPeriodPercentage, double weightFunctionSteepness)
        {
            List<double> data = new List<double>();
            if (chartData.Count == 0 || resolution == 0) return data;

            double songDuration = chartData[chartData.Count - 1][1];
            DistanceWeightFunction distanceWeightFunction = new DistanceWeightFunction(songDuration * smoothPeriodPercentage, weightFunctionSteepness);

            for (double i = 0.0; i < resolution; i += 1.0)
            {
                double songTime = (songDuration * i) / (resolution - 1);
                double sum = 0;
                double divider = 0;

                foreach (var item in chartData)
                {
                    double weight = distanceWeightFunction.GetWeight(item[1] - songTime);
                    sum += item[0] * weight;
                    divider += weight;
                }

                if (divider == 0) continue;
                double value = 100 + (sum / divider) * 15;
                data.Add(value);
            }

            return data;
        }
    }
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class StatsAdminController : Controller
    {
        private readonly AppContext _context;
        CurrentUserController _currentUserController;
        ScoreRefreshController _scoreRefreshController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;
        private readonly IAmazonS3 _s3Client;

        public StatsAdminController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController currentUserController,
            ScoreRefreshController scoreRefreshController,
            ReplayController replayController,
            IConfiguration configuration)
        {
            _context = context;
            _currentUserController = currentUserController;
            _scoreRefreshController = scoreRefreshController;
            _replayController = replayController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        public static float Clamp(float value)
        {
            if (value < 0.0) return 0.0f;
            return value > 1.0f ? 1.0f : value;
        }

        public class ComparisonResult {
            public double MaxDiff { get; set; }
            public string LbID { get; set; }
            public int Time { get; set; }
        }

        [HttpGet("~/admin/compareTopScores")]
        [Authorize]
        public async Task<ActionResult> CompareToPredicted()
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var lbs = await _context
                .Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                .Include(lb => lb.Scores.Where(s => s.Rank == 1))
                .Include(lb => lb.Difficulty)
                .Include(lb => lb.Song)
                .ToListAsync();
            var result = new List<ComparisonResult>();

            foreach (var lb in lbs)
            {
                var score = lb.Scores.First();

                string? name = score.Replay.Split("/").LastOrDefault();
                var replayStream = await _s3Client.DownloadReplay(name);

                Replay? replay;
                ReplayOffsets? offsets;
                byte[] replayData;

                try
                {
                int length = 0;
                List<byte> replayDataList = new List<byte>();
                byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

                while (true)
                {
                    var bytesRemaining = await replayStream.ReadAsync(buffer, offset: 0, buffer.Length);
                    if (bytesRemaining == 0)
                    {
                        break;
                    }
                    length += bytesRemaining;
                    replayDataList.AddRange(new Span<byte>(buffer, 0, bytesRemaining).ToArray());
                }

                ArrayPool<byte>.Shared.Return(buffer);

                replayData = replayDataList.ToArray();
                
                    (replay, offsets) = ReplayDecoder.ReplayDecoder.Decode(replayData);
                } catch (Exception)
                {
                    continue;
                }

                float speed = 1;
                if (score.Modifiers.Contains("SF"))
                {
                    speed = 1.5f;
                } else if (score.Modifiers.Contains("FS"))
                {
                    speed = 1.2f;
                } else if (score.Modifiers.Contains("SS"))
                {
                    speed = 0.85f;
                }

                var exmachinaAcc = await SongUtils.ExmachinaAcc(lb.Song.Hash, lb.Difficulty.Value, lb.Difficulty.ModeName, speed);

                if (exmachinaAcc?.Notes?.Rows == null)
                {
                    continue;
                }

                var actual = ChartProcessor.ProcessChartData(
                    replay
                    .notes
                    .Where(n => n.eventType == NoteEventType.good)
                    .Select(n => new List<double> { 1 - Clamp(n.noteCutInfo.cutDistanceToCenter / 0.3f), n.spawnTime })
                    .ToList(), 100, 0.02, 3);
                var predicted = ChartProcessor.ProcessChartData(exmachinaAcc.Notes.Rows, 100, 0.02, 3);

                double max = 0;
                int time = 0;
                for (int i = 0; i < Math.Min(actual.Count, predicted.Count); i++)
                {
                    var diff = predicted[i] - actual[i];
                    if (diff < 0 && Math.Abs(diff) > max)
                    {
                        max = -diff;
                        time = i;
                    }
                }
                result.Add(new ComparisonResult
                {
                    LbID = lb.Id,
                    MaxDiff = max,
                    Time = time
                });
            }

            return Ok(result.OrderByDescending(r => r.MaxDiff).ToList());
        }

        [HttpGet("~/admin/bigexport")]
        [Authorize]
        public async Task<ActionResult> BigExport()
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            return Ok(new {
                Players = _context
                    .Players
                    .AsNoTracking()
                    .Where(p => p.Pp > 0 && !p.Banned)
                    .Select(p => new {
                        p.Name,
                        p.Country,
                        p.Id,
                        p.Avatar
                    }).ToList(),

                Scores = _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && !s.Player.Banned && s.Player.Pp > 0 && s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked && s.Pp > 0)
                    .Select(s => new {
                        s.Id,
                        s.LeaderboardId,
                        s.Accuracy,
                        s.Modifiers,
                        s.PlayerId
                    }).ToList(),

                Maps = _context
                    .Leaderboards
                    .AsNoTracking()
                    .Where(l => l.Difficulty.Status == DifficultyStatus.ranked)
                    .Select(l => new {
                        l.Song.Hash,
                        l.Song.Name,
                        l.Id,
                        l.SongId,
                        l.Difficulty.ModeName,
                        l.Difficulty.DifficultyName,

                        l.Difficulty.AccRating,
                        l.Difficulty.PassRating,
                        l.Difficulty.TechRating,
                        l.Difficulty.PredictedAcc,
                        l.Difficulty.ModifiersRating
                    }).ToList()
            });
        }
    }
}
