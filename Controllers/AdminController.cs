using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace BeatLeader_Server.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppContext _context;
        CurrentUserController _currentUserController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;

        public AdminController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController currentUserController,
            ReplayController replayController)
        {
            _context = context;
            _currentUserController = currentUserController;
            _replayController = replayController;
            _environment = env;
        }

        [HttpPost("~/admin/role")]
        public async Task<ActionResult> AddRole([FromQuery] string playerId, [FromQuery] string role)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin") || role == "admin")
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.FindAsync(playerId);
            if (player != null) {
                player.Role = string.Join(",", player.Role.Split(",").Append(role));
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/admin/role")]
        public async Task<ActionResult> RemoveRole([FromQuery] string playerId, [FromQuery] string role)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin") || role == "admin")
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.FindAsync(playerId);
            if (player != null)
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(r => r != role));
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/admin/clan/{id}/setLeader")]
        public async Task<ActionResult> SetLeader(int id, [FromQuery] string newLeader)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.FindAsync(newLeader);
            var clan = _context.Clans.FirstOrDefault(c => c.Id == id);
            if (player != null)
            {
                clan.LeaderID = newLeader;

                player.Clans.Add(clan);

                await _context.SaveChangesAsync();
            } else {
                return NotFound();
            }

            return Ok();
        }

        [HttpPost("~/admin/clan/{id}/addMember")]
        public async Task<ActionResult> AddMember(int id, [FromQuery] string newLeader)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.FindAsync(newLeader);
            var clan = _context.Clans.FirstOrDefault(c => c.Id == id);
            if (player != null)
            {
                player.Clans.Add(clan);
                clan.PlayersCount++;
                clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, player.ScoreStats.AverageRankedAccuracy);
                clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, player.Rank);
                await _context.SaveChangesAsync();

                clan.Pp = _context.RecalculateClanPP(clan.Id);

                await _context.SaveChangesAsync();
            }
            else
            {
                return NotFound();
            }

            return Ok();
        }

        [HttpGet("~/admin/ip")]
        public async Task<ActionResult<string>> GetIP1()
        {
            return Request.HttpContext.GetIpAddress();
        }

        [HttpGet("~/admin/allScores")]
        public async Task<ActionResult<List<Score>>> GetAllScores([FromQuery] int from, [FromQuery] int to)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            return _context.Scores.Where(s => s.Timepost >= from && s.Timepost <= to).ToList();
        }

        #region RecalculateLeaderboardTimestamps

        public class LeaderboardTimestampsRecalculationResult {
            public int Total { get; set; }
            public int Failed { get; set; }
        }

        [HttpGet("~/admin/recalculateLeaderboardTimestamps")]
        public async Task<ActionResult<LeaderboardTimestampsRecalculationResult>> RecalculateLeaderboardTimestamps() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false;

            var allLeaderboards = _context.Leaderboards.Select(l => new { Id = l.Id, ScoreTimesets = l.Scores.Select(s => s.Timeset).ToList() }).ToList();


            var result = new LeaderboardTimestampsRecalculationResult();

            foreach (var leaderboard in allLeaderboards) {
                var firstScoreTimestamp = long.MaxValue;
                var timeset = leaderboard.ScoreTimesets.OrderBy(s => s).FirstOrDefault();
                if (timeset != null) {
                    long.TryParse(timeset, out firstScoreTimestamp);
                }

                result.Total += 1;
                if (firstScoreTimestamp == long.MaxValue) {
                    result.Failed += 1;
                    continue;
                }

                var lb = new Leaderboard() { Id = leaderboard.Id, Timestamp = firstScoreTimestamp };
                _context.Leaderboards.Attach(lb);
                _context.Entry(lb).Property(x => x.Timestamp).IsModified = true;
            }

            await _context.BulkSaveChangesAsync();

            return result;
        }

        #endregion

        #region RecalculateLeaderboardGroups

        public class LeaderboardGroupsRecalculationResult {
            public int Total { get; set; }
        }

        [HttpGet("~/admin/recalculateLeaderboardGroups")]
        public async Task<ActionResult<LeaderboardGroupsRecalculationResult>> RecalculateLeaderboardGroups() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var allLeaderboards = _context.Leaderboards
                .ToList();

            var map = new Dictionary<string, List<Leaderboard>>();
            
            foreach (var leaderboard in allLeaderboards) {
                var baseId = leaderboard.Id.Replace("x", "");
                List<Leaderboard> entry;
                if (map.ContainsKey(baseId)) {
                    entry = map[baseId];
                } else {
                    entry = new List<Leaderboard>();
                    map[baseId] = entry;
                }
                entry.Add(leaderboard);
            }

            foreach (var (_, leaderboards) in map) {
                if (leaderboards.Count == 1) continue;

                var group = new LeaderboardGroup {
                    Leaderboards = leaderboards
                };
                foreach (var leaderboard in leaderboards) {
                    leaderboard.LeaderboardGroup = group;
                }
            }

            await _context.BulkSaveChangesAsync();
            
            return new LeaderboardGroupsRecalculationResult {
                Total = map.Count
            };
        }

        #endregion

        [HttpGet("~/admin/migrateDurations")]
        public async Task<ActionResult> GetAllScores()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var lbs = _context.Leaderboards.Include(l => l.Song).Include(l => l.Difficulty).ToList();
            foreach (var lb in lbs)
            {
                lb.Difficulty.Duration = lb.Song.Duration;
            }
            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/migrateCDN")]
        public async Task<ActionResult> migrateCDN()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            var scount = await _context.Scores.CountAsync();
            for (int i = 440000; i < scount; i += 50000)
            {
                var scores = _context.Scores.OrderByDescending(s => s.Id).Skip(i).Take(50000).Select(s => new { Id = s.Id, Replay = s.Replay }).ToList();
                foreach (var item in scores)
                {
                    if (item.Replay.StartsWith("https://beatleadercdn.blob.core.windows.net"))
                    {
                        Score score = new Score { Id = item.Id };
                        score.Replay = item.Replay.Replace("https://beatleadercdn.blob.core.windows.net/replays/", "https://api.beatleader.xyz/replay/");

                        _context.Scores.Attach(score);
                        _context.Entry(score).Property(x => x.Replay).IsModified = true;
                    }
                }

                await _context.BulkSaveChangesAsync();
            }


            return Ok();
        }

        [HttpGet("~/admin/migrateMappers")]
        public async Task<ActionResult> MigrateMappers()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var lbs = _context.Leaderboards.Where(l => l.Qualification != null).Include(l => l.Qualification).ToList();
            foreach (var lb in lbs)
            {
                if (lb.Qualification.MapperId != null) {
                    var player = _context.Players.FirstOrDefault(p => p.Id == lb.Qualification.MapperId);
                    if (player == null) {
                        string beatSaverId = lb.Qualification.MapperId.Substring(1);

                        var link = _context.BeatSaverLinks.FirstOrDefault(p => p.BeatSaverId == beatSaverId);

                        if (link != null) {
                            player = _context.Players.FirstOrDefault(p => p.Id == link.Id);
                            if (player != null) {
                                lb.Qualification.MapperId = player.Id;
                            }
                        }
                    }
                }
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/admin/playerHistory/{id}")]
        public async Task<ActionResult> DeleteHistory(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var history = _context.PlayerScoreStatsHistory.FirstOrDefault(h => h.Id == id);
            if (history == null) {
                return NotFound();
            }
            _context.PlayerScoreStatsHistory.Remove(history);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/admin/unlinkbeatsaver")]
        public async Task<ActionResult> unlinkbeatsaver([FromQuery] string beatSaverId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var link = _context.BeatSaverLinks.FirstOrDefault(link => link.BeatSaverId == beatSaverId);
            if (link == null) {
                return NotFound();
            }
            _context.BeatSaverLinks.Remove(link);
            _context.SaveChanges();

            return Ok();
        }


        [HttpDelete("~/admin/playerHistory/time/{time}")]
        public async Task<ActionResult> DeleteHistoryTime(int time)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var history = _context.PlayerScoreStatsHistory.Where(h => h.Timestamp == time).ToList();
            foreach (var item in history)
            {
                _context.PlayerScoreStatsHistory.Remove(item);
            }
            
            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/map/refresh")]
        public async Task<ActionResult> RefreshHash()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var songs = _context.Songs.Where(el => el.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.outdated) != null).Include(song => song.Difficulties).ToList();

            foreach (var song in songs)
            {
                Song? updatedSong = await SongUtils.GetSongFromBeatSaver("https://api.beatsaver.com/maps/hash/" + song.Hash);

                if (updatedSong != null && updatedSong.Hash == song.Hash)
                {
                    foreach (var diff in song.Difficulties)
                    {
                        if (diff.Status == DifficultyStatus.outdated) {
                            diff.Status = DifficultyStatus.unranked;
                        }
                    }
                }
            }

            _context.BulkSaveChanges();

            return Ok();
        }

        [HttpGet("~/admin/maps/newranking")]
        public async Task<ActionResult> newranking()
        {
            var songs = _context.Songs.Where(el => el.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.ranked) != null).Include(song => song.Difficulties).ToList();

            foreach (var song in songs)
            {
                foreach (var diff in song.Difficulties)
                {
                    if (diff.Status == DifficultyStatus.ranked) {
                        var response = await SongUtils.ExmachinaStars(song.Hash, diff.Value);
                        if (response != null) {
                            diff.PassRating = response.none.lack_map_calculation.passing_difficulty;
                            diff.TechRating = response.none.lack_map_calculation.balanced_tech * 10;
                            diff.PredictedAcc = response.none.AIacc;
                            diff.AccRating = ReplayUtils.AccRating(
                                response.none.AIacc, 
                                response.none.lack_map_calculation.passing_difficulty,
                                response.none.lack_map_calculation.balanced_tech * 10);

                            diff.ModifiersRating = new ModifiersRating {
                                SSPassRating = response.SS.lack_map_calculation.passing_difficulty,
                                SSTechRating = response.SS.lack_map_calculation.balanced_tech * 10,
                                SSPredictedAcc = response.SS.AIacc,
                                SSAccRating = ReplayUtils.AccRating(response.SS.AIacc, response.SS.lack_map_calculation.passing_difficulty, response.SS.lack_map_calculation.balanced_tech * 10),
                                SFPassRating = response.SFS.lack_map_calculation.passing_difficulty,
                                SFTechRating = response.SFS.lack_map_calculation.balanced_tech * 10 ,
                                SFPredictedAcc = response.SFS.AIacc,
                                SFAccRating = ReplayUtils.AccRating(response.SFS.AIacc, response.SFS.lack_map_calculation.passing_difficulty, response.SFS.lack_map_calculation.balanced_tech * 10),
                                FSPassRating = response.FS.lack_map_calculation.passing_difficulty,
                                FSTechRating = response.FS.lack_map_calculation.balanced_tech * 10,
                                FSPredictedAcc = response.FS.AIacc,
                                FSAccRating = ReplayUtils.AccRating(response.FS.AIacc, response.FS.lack_map_calculation.passing_difficulty, response.FS.lack_map_calculation.balanced_tech * 10),
                            };

                        } else {
                            diff.PassRating = diff.Stars ?? 4.2f;
                            diff.PredictedAcc = 0.98f;
                            diff.TechRating = 4.2f;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        public static string GolovaID = "76561198059961776";
    }
}
