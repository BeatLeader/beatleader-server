using System;
using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeatLeader_Server.Controllers
{
    public class ScoreController : Controller
    {
        private readonly AppContext _context;
        BlobContainerClient _containerClient;
        PlayerController _playerController;

        public ScoreController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            PlayerController playerController)
        {
            _context = context;
            _playerController = playerController;
            if (env.IsDevelopment())
            {
                _containerClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.ReplaysContainerName);

                _containerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
        }

        [HttpDelete("~/score/{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(int id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var score = _context.Scores.Where(s => s.Id == id).Include(s => s.Leaderboard).FirstOrDefault();
            if (score == null)
            {
                return NotFound();
            }

            score.Leaderboard.Scores.Remove(score);
            _context.Leaderboards.Update(score.Leaderboard);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/scores/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshScores()
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var allLeaderboards = _context.Leaderboards.Include(s => s.Scores).Include(l => l.Difficulty).ToList();
            foreach (Leaderboard leaderboard in allLeaderboards) {
                var allScores = leaderboard.Scores;
                foreach (Score s in allScores)
                {
                    s.ModifiedScore = (int)((float)s.BaseScore * ReplayUtils.GetTotalMultiplier(s.Modifiers));
                    s.Accuracy = (float)s.ModifiedScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                    if (leaderboard.Difficulty.Ranked) {
                        s.Pp = (float)s.Accuracy * (float)leaderboard.Difficulty.Stars * 44;
                    }
                }

                var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
                foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                    _context.Scores.Update(s);
                }
                leaderboard.Scores = rankedScores;
                _context.Leaderboards.Update(leaderboard);
                try {
                    await _context.SaveChangesAsync();
                } catch (Exception e) {
                    _context.RejectChanges();
                    int l = 0;
                }
            }

            return Ok();
        }

        [HttpGet("~/scores/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<IEnumerable<Score>>> GetByHash(string hash, string diff, string mode, [FromQuery] string? country, [FromQuery] string? player, [FromQuery] int page = 1, [FromQuery] int count = 8)
        {
            var leaderboard = _context.Leaderboards.Include(el => el.Song).Include(el => el.Difficulty).FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null)
            {
                IEnumerable<Score> query = _context.Leaderboards.Include(el => el.Scores).ThenInclude(s => s.Player).First(el => el.Id == leaderboard.Id).Scores;
                if (query.Count() == 0)
                {
                    return new List<Score>();
                }
                if (country != null)
                {
                    query = query.Where(s => s.Player.Country == country);
                }
                if (player != null)
                {
                    Score? playerScore = query.FirstOrDefault(el => el.Player.Id == player);
                    if (playerScore != null)
                    {
                        page = (int)Math.Floor((double)(playerScore.Rank - 1) / (double)count) + 1;
                    }
                    else
                    {
                        return new List<Score>();
                    }
                }
                return query.OrderByDescending(p => p.ModifiedScore).Skip((page - 1) * count).Take(count).ToArray();
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("~/failedscores/")]
        public async Task<ActionResult<IEnumerable<FailedScore>>> FailedSсores()
        {
            return _context.FailedScores.OrderByDescending(s => s.Id).Include(el => el.Leaderboard).Include(el => el.Player).ToList();
        }

        public class ScoreResponse
        {
            public int Id { get; set; }
            public int BaseScore { get; set; }
            public int ModifiedScore { get; set; }
            public float Accuracy { get; set; }
            public string PlayerId { get; set; }
            public float Pp { get; set; }
            public float Weight { get; set; }
            public int Rank { get; set; }
            public int CountryRank { get; set; }
            public string Replay { get; set; }
            public string Modifiers { get; set; }
            public int BadCuts { get; set; }
            public int MissedNotes { get; set; }
            public int BombCuts { get; set; }
            public int WallsHit { get; set; }
            public int Pauses { get; set; }
            public bool FullCombo { get; set; }
            public int Hmd { get; set; }
            public string Timeset { get; set; }
            public Player Player { get; set; }
        }

        public ScoreResponse RemoveLeaderboard(Score s, int i)
        {
            return new ScoreResponse
            {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                Accuracy = s.Accuracy,
                PlayerId = s.PlayerId,
                Pp = s.Pp,
                Weight = s.Weight,
                Rank = i >= 0 ? i + 1 : s.Rank,
                CountryRank = s.CountryRank,
                Replay = s.Replay,
                Modifiers = s.Modifiers,
                BadCuts = s.BadCuts,
                MissedNotes = s.MissedNotes,
                BombCuts = s.BombCuts,
                WallsHit = s.WallsHit,
                Pauses = s.Pauses,
                FullCombo = s.FullCombo,
                Hmd = s.Hmd,
                Timeset = s.Timeset,
                Player = s.Player,
            };
        }

        private void RemovePositiveModifiers(Score s)
        {
            Score result = s;

            int maxScore = (int)(result.ModifiedScore / result.Accuracy);
            if (result.Pp > 0)
            {
                result.Pp /= result.Accuracy;
            }

            (string modifiers, float value) = ReplayUtils.GetNegativeMultipliers(s.Modifiers);

            result.ModifiedScore = (int)(result.BaseScore * value);
            result.Accuracy = (float)result.ModifiedScore / (float)maxScore;
            result.Modifiers = modifiers;

            if (result.Pp > 0)
            {
                result.Pp *= result.Accuracy;
            }
        }

        [HttpGet("~/v3/scores/{hash}/{diff}/{mode}/{context}/{scope}/{method}")]
        public async Task<ActionResult<ResponseWithMetadataAndSelection<ScoreResponse>>> GetByHash3(
            string hash,
            string diff,
            string mode,
            string context,
            string scope,
            string method,
            [FromQuery] string player,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            Player? currentPlayer = (await _playerController.Get(player)).Value;
            if (currentPlayer == null) {
               return NotFound();
            }

            var leaderboard = _context.Leaderboards.Include(el => el.Song).Include(el => el.Difficulty).Where(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null)
            {
                ResponseWithMetadataAndSelection<ScoreResponse> result = new ResponseWithMetadataAndSelection<ScoreResponse>
                {
                    Data = new List<ScoreResponse>(),
                    Metadata =
                    {
                        ItemsPerPage = count,
                        Page = page,
                        Total = 0
                    }
                };

                Leaderboard? selectedLeaderboard = leaderboard
                    .Include(el => el.Scores)
                    .ThenInclude(s => s.Player)
                    .ThenInclude(p => p.ScoreStats)
                    .FirstOrDefault();

                if (selectedLeaderboard == null) {
                    return result;
                }

                IEnumerable<Score> query = selectedLeaderboard.Scores.ToList();
                if (context.ToLower() == "standard") {
                    query.ToList().ForEach(RemovePositiveModifiers);
                }

                query = query.OrderByDescending(p => p.ModifiedScore);

                if (query.Count() == 0)
                {
                    return result;
                }
                Dictionary<string, int> countries = new Dictionary<string, int>();
                query = query.Select((s, i) => {
                    if (s.CountryRank == 0) {
                        if (!countries.ContainsKey(s.Player.Country))
                        {
                            countries[s.Player.Country] = 1;
                        }

                        s.CountryRank = countries[s.Player.Country];
                        countries[s.Player.Country]++;
                    }
                    return s;
                });
                if (scope.ToLower() == "friends")
                {

                } else if (scope.ToLower() == "country")
                {
                    query = query.Where(s => s.Player.Country == currentPlayer.Country);
                }

                if (method.ToLower() == "around")
                {
                    Score? playerScore = query.FirstOrDefault(el => el.Player.Id == player);
                    if (playerScore != null)
                    {
                        int rank = query.TakeWhile(s => s.PlayerId != player).Count();
                        page += (int)Math.Floor((double)(rank) / (double)count);
                        result.Metadata.Page = page;
                    }
                    else
                    {
                        return result;
                    }
                } else
                {
                    Score? highlightedScore = query.FirstOrDefault(el => el.Player.Id == player);
                    if (highlightedScore != null)
                    {
                        int rank = query.TakeWhile(s => s.PlayerId != player).Count();
                        result.Selection = RemoveLeaderboard(highlightedScore, rank);
                    }
                }

                List<ScoreResponse> resultList = query
                    .Select(RemoveLeaderboard)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .ToList();
                result.Metadata.Total = query.Count();
                result.Data = resultList;

                return result;
            }
            else
            {
                return NotFound();
            }
        }


        [HttpGet("~/score/{playerID}/{hash}/{diff}/{mode}")]
        public ActionResult<Score> GetPlayer(string playerID, string hash, string diff, string mode)
        {
            var leaderboard = _context.Leaderboards.Include(el => el.Song).Include(el => el.Difficulty).Where(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null && leaderboard.Count() != 0)
            {
                Int64 oculusId = Int64.Parse(playerID);
                AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
                string userId = (link != null ? link.SteamID : playerID);
                return leaderboard.Include(el => el.Scores).ThenInclude(el => el.Player).First().Scores.FirstOrDefault(el => el.Player.Id == userId);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("~/score/statistic/{id}")]
        public async Task<ActionResult<ScoreStatistic>> GetStatistic(string id)
        {
            ScoreStatistic? scoreStatistic = _context.ScoreStatistics.Where(s => s.ScoreId == Int64.Parse(id)).Include(s => s.AccuracyTracker).Include(s => s.HitTracker).Include(s => s.ScoreGraphTracker).Include(s => s.WinTracker).FirstOrDefault();
            if (scoreStatistic == null)
            {
                return NotFound();
            }
            ReplayStatisticUtils.DecodeArrays(scoreStatistic);
            return scoreStatistic;
        }

        [HttpGet("~/score/calculatestatistic/players")]
        public async Task<ActionResult> CalculateStatisticPlayers()
        {
            var players = _context.Players.ToList();
            foreach (Player p in players)
            {
                await CalculateStatisticPlayer(p.Id);
            }

            return Ok();
        }

        [HttpGet("~/score/calculatestatistic/player/{id}")]
        public async Task<ActionResult> CalculateStatisticPlayer(string id)
        {
           Player? player = _context.Players.Find(id);
            if (player == null)
            {
                return NotFound();
            }

            var scores = _context.Scores.Where(s => s.PlayerId == id).Include(s => s.Leaderboard).ThenInclude(l => l.Song).ToArray();
            foreach (var score in scores)
            {
                await CalculateStatisticScore(score);
            }
            return Ok();
        }

        [HttpGet("~/score/calculatestatistic/{id}")]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatistic(string id)
        {
            Score? score = _context.Scores.Where(s => s.Id == Int64.Parse(id)).Include(s => s.Leaderboard).ThenInclude(l => l.Song).FirstOrDefault();
            if (score == null)
            {
                return NotFound("Score not found");
            }
            return await CalculateStatisticScore(score);
        }

        [NonAction]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatisticScore(Score score)
        {
            string blobName = score.Replay.Split("/").Last();

            BlobClient blobClient = _containerClient.GetBlobClient(blobName);
            MemoryStream ms = new MemoryStream(5);
            await blobClient.DownloadToAsync(ms);
            Replay replay;
            try
            {
                replay = ReplayDecoder.Decode(ms.ToArray());
            }
            catch (Exception)
            {
                return BadRequest("Error decoding replay");
            }

            (ScoreStatistic? statistic, string? error) = CalculateStatisticReplay(replay, score);
            if (statistic == null) {
                return BadRequest(error);
            }

            return statistic;
        }

        [NonAction]
        public (ScoreStatistic?, string?) CalculateStatisticReplay(Replay replay, Score score)
        {
            ScoreStatistic? statistic = null;

            try
            {
                statistic = ReplayStatisticUtils.ProcessReplay(replay, score.Leaderboard);
                statistic.ScoreId = score.Id;
                ReplayStatisticUtils.EncodeArrays(statistic);
            } catch (Exception e) {
                return (null, e.ToString());
            }

            if (statistic == null)
            {
                return (null, "Could not calculate statistics");
            }

            ScoreStatistic? currentStatistic = _context.ScoreStatistics.FirstOrDefault(s => s.ScoreId == score.Id);
            _context.ScoreStatistics.Add(statistic);
            if (currentStatistic != null)
            {
                _context.ScoreStatistics.Remove(currentStatistic);
            }
            _context.SaveChanges();

            return (statistic, null);
        }
    }
}

