using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Prometheus.Client;
using System.Net;
using static BeatLeader_Server.ControllerHelpers.LeaderboardControllerHelper;

namespace BeatLeader_Server.Services
{
    public class MinuteRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        private readonly IGauge _rankedPlayerCounter;
        private readonly IGauge _rankedScoreCounter;

        private readonly IGauge _playerCounter;
        private readonly IGauge _scoreCounter;

        public static string CurrentHost = "";

        public static int ScoresCount = 0;
        public static int PpScoresCount = 0;

        public static List<MassLeaderboardsInfoResponse> massLeaderboards = new List<MassLeaderboardsInfoResponse>();

        public MinuteRefresh(IServiceScopeFactory serviceScopeFactory, IMetricFactory metricFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;

            _rankedPlayerCounter = metricFactory.CreateGauge("ranked_player_count", "Ranked player count in the last 3 month");
            _rankedScoreCounter = metricFactory.CreateGauge("ranked_score_count", "Ranked score count");
            _playerCounter = metricFactory.CreateGauge("player_count", "Total player count");
            _scoreCounter = metricFactory.CreateGauge("score_count", "Total score count");
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                try {
                    await RefreshMaps();
                    await RefreshPrometheus();
                    await RefreshAllContextsPp();
                    //await RefreshTreeMaps();
                } catch (Exception e) {
                    Console.WriteLine($"EXCEPTION MinuteRefresh {e}");
                }

                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshMaps() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                massLeaderboards = await _context.Leaderboards.FilterRanking(_context, MapSortBy.None, Order.Asc)
                    .TagWithCallerS()
                    .AsNoTracking()
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Select(lb => new MassLeaderboardsInfoResponse {
                        Id = lb.Id,
                        Song = new SongInfo {
                            Id = lb.Song.Id,
                            Hash = lb.Song.Hash,
                            UploadTime = lb.Song.UploadTime
                        },
                        Difficulty = new MassLeaderboardsDiffInfo {
                            Id = lb.Difficulty.Id,
                            Value = lb.Difficulty.Value,
                            Mode = lb.Difficulty.Mode,
                            DifficultyName = lb.Difficulty.DifficultyName,
                            ModeName = lb.Difficulty.ModeName,
                            Status = lb.Difficulty.Status,
                            ModifierValues = lb.Difficulty.ModifierValues,
                            ModifiersRating = lb.Difficulty.ModifiersRating,
                            NominatedTime  = lb.Difficulty.NominatedTime,
                            QualifiedTime  = lb.Difficulty.QualifiedTime,
                            RankedTime = lb.Difficulty.RankedTime,

                            Stars  = lb.Difficulty.Stars,
                            PassRating  = lb.Difficulty.PassRating,
                            AccRating  = lb.Difficulty.AccRating,
                            TechRating  = lb.Difficulty.TechRating,
                            Type  = lb.Difficulty.Type,
                            MaxScore = lb.Difficulty.MaxScore,
                        },
                        Qualification = lb.Qualification != null ? new QualificationInfo {
                            Id = lb.Qualification.Id,
                            Timeset = lb.Qualification.Timeset,
                            RTMember = lb.Qualification.RTMember,
                            CriteriaMet = lb.Qualification.CriteriaMet,
                            CriteriaTimeset = lb.Qualification.CriteriaTimeset,
                            CriteriaChecker = lb.Qualification.CriteriaChecker,
                            CriteriaCommentary = lb.Qualification.CriteriaCommentary,
                            MapperAllowed = lb.Qualification.MapperAllowed,
                            MapperId = lb.Qualification.MapperId,
                            MapperQualification = lb.Qualification.MapperQualification,
                            ApprovalTimeset = lb.Qualification.ApprovalTimeset,
                            Approved = lb.Qualification.Approved,
                            Approvers = lb.Qualification.Approvers,
                        } : null
                    })
                    .ToListAsync();
            }
        }

        public async Task RefreshPrometheus()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                CurrentHost = scope.ServiceProvider.GetApplicationUrls().FirstOrDefault(s => s.Contains("https")) ?? "";
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 30 * 3;

                _rankedPlayerCounter.Set(await _context.Players.Where(p => !p.Banned && p.ScoreStats.LastRankedScoreTime >= timeset).CountAsync());
                _playerCounter.Set(await _context.Players.CountAsync());

                ScoresCount = await _context.Scores.Where(s => s.ValidForGeneral && !s.Banned).TagWithCaller().CountAsync();
                PpScoresCount = await _context.Scores.Where(s => s.Pp > 0 && s.ValidForGeneral && !s.Banned).TagWithCaller().CountAsync();

                _rankedScoreCounter.Set(await _context.Scores.TagWithCaller().Where(s => s.Pp > 0 && !s.Qualification && !s.Banned).CountAsync());
                _scoreCounter.Set(ScoresCount);
            }
        }

        public async Task RefreshAllContextsPp() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                await PlayerRefreshControllerHelper.RefreshAllContextsPp(_context);
            }
        }

        public async Task RefreshTreeMaps() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var now = Time.UnixNow();
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();
                var maps = await _context.TreeMaps.Where(m => m.Timestart < now).OrderByDescending(m => m.Timestart).ToListAsync();

                var ids = maps.Select(m => m.SongId).ToList();

                var songs = _context.Songs.Where(s => ids.Contains(s.Id)).Include(s => s.Leaderboards).ThenInclude(l => l.Difficulty).ToList();
                foreach (var song in songs) {
                    foreach (var lb in song.Leaderboards) {
                        if (lb.Difficulty.Status != Models.DifficultyStatus.inevent) {
                            lb.Difficulty.Status = Models.DifficultyStatus.inevent;
                            await RatingUtils.UpdateFromExMachina(lb, null);
                            await _context.SaveChangesAsync();
                            await ScoreRefreshControllerHelper.BulkRefreshScores(_context, lb.Id);
                        }
                    }
                }

                dynamic? playlist = null;

                using (var stream = await _s3Client.DownloadPlaylist("83999.bplist")) {
                    if (stream != null) {
                        playlist = stream.ObjectFromStream();
                    }
                }

                if (playlist == null)
                {
                    return;
                }

                var psongs = songs.Select(s => new
                {
                    hash = s.Hash,
                    songName = s.Name,
                    levelAuthorName = s.Mapper,
                    difficulties = s.Difficulties.Select(d => new
                    {
                        name = d.DifficultyName.FirstCharToLower(),
                        characteristic = d.ModeName
                    })
                }).ToList();

                playlist.songs = psongs;

                await S3Helper.UploadPlaylist(_s3Client, "83999.bplist", playlist);
            }
        }
    }
}
