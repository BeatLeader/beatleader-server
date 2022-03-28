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

        public ScoreController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env)
        {
            _context = context;
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
            var score = await _context.Scores.FindAsync(id);
            if (score == null)
            {
                return NotFound();
            }

            _context.Scores.Remove(score);

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
            var allScores = _context.Scores.Include(s => s.Leaderboard).ThenInclude(l => l.Difficulty).Where(s => s.Leaderboard.Difficulty.Stars != null && s.Leaderboard.Difficulty.Stars != 0).ToList();
            foreach (Score s in allScores)
            {
                s.Accuracy = (float)s.ModifiedScore / (float)ReplayUtils.MaxScoreForNote(s.Leaderboard.Difficulty.Notes);
                s.Pp = (float)s.Accuracy * (float)s.Leaderboard.Difficulty.Stars * 44;
                _context.Scores.Update(s);
                await _context.SaveChangesAsync();
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

        private ScoreResponse RemoveLeaderboard(Score s)
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
                Rank = s.Rank,
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

        [HttpGet("~/v2/scores/{hash}/{diff}/{mode}")]
        public ActionResult<ResponseWithMetadataAndSelection<ScoreResponse>> GetByHash2(
            string hash,
            string diff,
            string mode,
            [FromQuery] string? country,
            [FromQuery] string? aroundPlayer,
            [FromQuery] string? player,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8)
        {
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

                IEnumerable<Score> query = leaderboard
                    .Include(el => el.Scores)
                    .ThenInclude(s => s.Player)
                    .ThenInclude(p => p.ScoreStats)
                    .FirstOrDefault()
                    .Scores;
                Score? highlightedScore = query.FirstOrDefault(el => el.Player.Id == player);
                if (query.Count() == 0)
                {
                    return result;
                }
                if (country != null)
                {
                    query = query.Where(s => s.Player.Country == country);
                }
                if (aroundPlayer != null)
                {
                    Score? playerScore = query.FirstOrDefault(el => el.Player.Id == aroundPlayer);
                    if (playerScore != null)
                    {
                        page += (int)Math.Floor((double)(playerScore.Rank - 1) / (double)count);
                        result.Metadata.Page = page;
                    }
                    else
                    {
                        return result;
                    }
                }
                List<ScoreResponse> resultList = query
                    .OrderByDescending(p => p.ModifiedScore)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(RemoveLeaderboard)
                    .ToList();
                result.Metadata.Total = query.Count();
                result.Data = resultList;
                if (highlightedScore != null)
                {
                    result.Selection = RemoveLeaderboard(highlightedScore);
                }

                return result;
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("~/v3/scores/{hash}/{diff}/{mode}/{context}/{scope}/{method}")]
        public ActionResult<ResponseWithMetadataAndSelection<ScoreResponse>> GetByHash3(
            string hash,
            string diff,
            string mode,
            string context,
            string scope,
            string method,
            [FromQuery] string player,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8)
        {
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

                IEnumerable<Score> query = leaderboard
                    .Include(el => el.Scores)
                    .ThenInclude(s => s.Player)
                    .ThenInclude(p => p.ScoreStats)
                    .FirstOrDefault()
                    .Scores;

                if (method != "around")
                {
                    Score? highlightedScore = query.FirstOrDefault(el => el.Player.Id == player);
                    if (highlightedScore != null)
                    {
                        result.Selection = RemoveLeaderboard(highlightedScore);
                    }
                }
                
                if (query.Count() == 0)
                {
                    return result;
                }
                if (scope == "friends")
                {

                } else if (scope != "global")
                {
                    query = query.Where(s => s.Player.Country.ToLower() == scope.ToLower());
                }

                if (method == "around")
                {
                    Score? playerScore = query.FirstOrDefault(el => el.Player.Id == player);
                    if (playerScore != null)
                    {
                        page += (int)Math.Floor((double)(playerScore.Rank - 1) / (double)count);
                        result.Metadata.Page = page;
                    }
                    else
                    {
                        return result;
                    }
                }

                List<ScoreResponse> resultList = query
                    .OrderByDescending(p => p.ModifiedScore)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(RemoveLeaderboard)
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
        public async Task<ActionResult<ScoreStatistic>> CalculateStatistic(string id)
        {
            Score? score = _context.Scores.Where(s => s.Id == Int64.Parse(id)).Include(s => s.Leaderboard).ThenInclude(l => l.Song).FirstOrDefault();
            if (score == null)
            {
                return NotFound("Score not found");
            }
            return await CalculateStatisticScore(score);
        }

        public async Task<ActionResult<ScoreStatistic>> CalculateStatisticScore(Score score)
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

            return await CalculateStatisticReplay(replay, score);
        }

        public async Task<ActionResult<ScoreStatistic>> CalculateStatisticReplay(Replay replay, Score score)
        {
            //ScoreStatistic statistic = await Task.Run(() =>
            //{
            ScoreStatistic? statistic = null;

            try
            {
                statistic = ReplayStatisticUtils.ProcessReplay(replay, score.Leaderboard);
                statistic.ScoreId = score.Id;
                ReplayStatisticUtils.EncodeArrays(statistic);
            } catch { }

            if (statistic == null)
            {
                return Ok();
            }
            

                //return statistic;
            //});

            ScoreStatistic? currentStatistic = _context.ScoreStatistics.FirstOrDefault(s => s.ScoreId == score.Id);
            _context.ScoreStatistics.Add(statistic);
            if (currentStatistic != null)
            {
                _context.ScoreStatistics.Remove(currentStatistic);
            }
            await _context.SaveChangesAsync();

            return statistic;
        }
    }
}

