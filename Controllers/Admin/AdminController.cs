using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppContext _context;
        CurrentUserController _currentUserController;
        ScoreRefreshController _scoreRefreshController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;
        private readonly IAmazonS3 _s3Client;

        public AdminController(
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

        [HttpDelete("~/admin/resetattempts")]
        public async Task<ActionResult> resetattempts()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var attempts = _context.LoginAttempts.ToList();
            foreach (var item in attempts)
            {
                _context.LoginAttempts.Remove(item);
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
                try {
                    _context.Leaderboards.Attach(lb);
                } catch { }
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
                        try {
                            _context.Scores.Attach(score);
                        } catch { }
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

        [HttpPut("~/admin/refreshClanRankings")]
        public async Task<ActionResult> RefreshClanRankings()
        {
            // refreshClanRankings: Http Put endpoint that recalculates the clan rankings for all ranked leaderboards.

            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            try
            {
                var clans = _context.Clans.ToList();
                foreach (var clan in clans)
                {
                    clan.CaptureLeaderboardsCount = 0;
                    clan.RankedPoolPercentCaptured = 0;
                }
                await _context.BulkSaveChangesAsync();

                var leaderboardsRecalc = _context
                    .Leaderboards
                    .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                    .Include(lb => lb.ClanRanking)
                    .ToList();
                leaderboardsRecalc.ForEach(obj => obj.ClanRanking = _context.CalculateClanRankingSlow(obj));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/admin/ban/countrychanges")]
        public async Task<ActionResult> BanCountrychanges([FromQuery] string playerId)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var changeBan = new CountryChangeBan {
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                PlayerId = playerId
            };
            _context.CountryChangeBans.Add(changeBan);
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/admin/maps/newranking")]
        public async Task<ActionResult> newranking()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var songs = _context.Songs.Where(el => el.Difficulties.FirstOrDefault(d => 
                d.Status == DifficultyStatus.ranked || 
                d.Status == DifficultyStatus.qualified || 
                d.Status == DifficultyStatus.nominated) != null)
                .Include(song => song.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .ToList();

            await songs.ParallelForEachAsync(async song => {
                foreach (var diff in song.Difficulties)
                {
                    if (diff.Status == DifficultyStatus.ranked || diff.Status == DifficultyStatus.qualified || diff.Status == DifficultyStatus.nominated) {
                        var response = await SongUtils.ExmachinaStars(song.Hash, diff.Value, diff.ModeName);
                        if (response != null)
                        {
                            diff.PassRating = response.none.lack_map_calculation.balanced_pass_diff;
                            diff.TechRating = response.none.lack_map_calculation.balanced_tech * 10;
                            diff.PredictedAcc = response.none.AIacc;
                            diff.AccRating = ReplayUtils.AccRating(diff.PredictedAcc, diff.PassRating, diff.TechRating);

                            diff.ModifiersRating = new ModifiersRating
                            {
                                SSPassRating = response.SS.lack_map_calculation.balanced_pass_diff,
                                SSTechRating = response.SS.lack_map_calculation.balanced_tech * 10,
                                SSPredictedAcc = response.SS.AIacc,
                                FSPassRating = response.FS.lack_map_calculation.balanced_pass_diff,
                                FSTechRating = response.FS.lack_map_calculation.balanced_tech * 10,
                                FSPredictedAcc = response.FS.AIacc,
                                SFPassRating = response.SFS.lack_map_calculation.balanced_pass_diff,
                                SFTechRating = response.SFS.lack_map_calculation.balanced_tech * 10,
                                SFPredictedAcc = response.SFS.AIacc,
                            };

                            var rating = diff.ModifiersRating;
                            rating.SSAccRating = ReplayUtils.AccRating(
                                    rating.SSPredictedAcc, 
                                    rating.SSPassRating, 
                                    rating.SSTechRating);
                            rating.FSAccRating = ReplayUtils.AccRating(
                                    rating.FSPredictedAcc, 
                                    rating.FSPassRating, 
                                    rating.FSTechRating);
                            rating.SFAccRating = ReplayUtils.AccRating(
                                    rating.SFPredictedAcc, 
                                    rating.SFPassRating, 
                                    rating.SFTechRating);
                        }
                        else
                        {
                            diff.PassRating = 0.0f;
                            diff.PredictedAcc = 1.0f;
                            diff.TechRating = 0.0f;
                        }
                    }
                }
            }, maxDegreeOfParallelism: 3);

            await _context.SaveChangesAsync();

            var leaderboards = _context
                .Leaderboards
                .Where(lb => 
                    lb.Difficulty.Status == DifficultyStatus.ranked || 
                    lb.Difficulty.Status == DifficultyStatus.qualified || 
                    lb.Difficulty.Status == DifficultyStatus.nominated)
                .Include(lb => lb.Difficulty)
                .Include(lb => lb.Changes)
                .ToList();
            foreach (var leaderboard  in leaderboards) {
                var diff = leaderboard.Difficulty;

                LeaderboardChange rankChange = new LeaderboardChange
                {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    PlayerId = RankingBotID,
                    OldStars = diff.Stars ?? 0,
                    NewStars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0),
                    NewAccRating = diff.AccRating ?? 0,
                    NewPassRating = diff.PassRating ?? 0,
                    NewTechRating = diff.TechRating ?? 0,
                };
                diff.Stars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);
                if (leaderboard.Changes == null) {
                    leaderboard.Changes = new List<LeaderboardChange>();
                }

                leaderboard.Changes.Add(rankChange);
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/admin/playlist/rank")]
        public async Task<ActionResult> RankPlaylist()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            dynamic? playlist = null;

            using (var ms = new MemoryStream(5))
            {
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;
                playlist = ms.ObjectFromStream();
            }

            var players = new List<EventPlayer>();
            var basicPlayers = new List<Player>();
            var playerScores = new Dictionary<string, List<Score>>();

            foreach (var songy in playlist.songs) {
                foreach (var diffy in songy.difficulties)
                {
                    string hash = songy.hash.ToLower();
                    string diffName = diffy.name.ToLower();
                    string characteristic = diffy.characteristic.ToLower();

                    var lb = _context.Leaderboards.Where(lb => 
                        lb.Song.Hash.ToLower() == hash && 
                        lb.Difficulty.DifficultyName.ToLower() == diffName &&
                        lb.Difficulty.ModeName.ToLower() == characteristic).Include(lb => lb.Difficulty).Include(lb => lb.Song).FirstOrDefault();

                    if (lb != null && lb.Difficulty.Status != DifficultyStatus.outdated) {

                        if (lb.Difficulty.Status == DifficultyStatus.unranked || lb.Difficulty.Status == DifficultyStatus.inevent)
                        {
                            var diff = lb.Difficulty;
                            lb.Difficulty.Status = DifficultyStatus.ranked;
                            var response = await SongUtils.ExmachinaStars(lb.Song.Hash, diff.Value, diff.ModeName);
                            if (response != null) {
                                diff.PassRating = response.none.lack_map_calculation.balanced_pass_diff;
                                diff.TechRating = response.none.lack_map_calculation.balanced_tech * 10;
                                diff.PredictedAcc = response.none.AIacc;
                                diff.AccRating = ReplayUtils.AccRating(response.none.AIacc, response.none.lack_map_calculation.balanced_pass_diff, response.none.lack_map_calculation.balanced_tech * 10);

                                diff.ModifiersRating = new ModifiersRating {
                                    SSPassRating = response.SS.lack_map_calculation.balanced_pass_diff,
                                    SSTechRating = response.SS.lack_map_calculation.balanced_tech * 10,
                                    SSPredictedAcc = response.SS.AIacc,
                                    SSAccRating = ReplayUtils.AccRating(response.SS.AIacc, response.SS.lack_map_calculation.balanced_pass_diff, response.SS.lack_map_calculation.balanced_tech * 10),
                                    SFPassRating = response.SFS.lack_map_calculation.balanced_pass_diff,
                                    SFTechRating = response.SFS.lack_map_calculation.balanced_tech * 10,
                                    SFPredictedAcc = response.SFS.AIacc,
                                    SFAccRating = ReplayUtils.AccRating(response.SFS.AIacc, response.SFS.lack_map_calculation.balanced_pass_diff, response.SFS.lack_map_calculation.balanced_tech * 10),
                                    FSPassRating = response.FS.lack_map_calculation.balanced_pass_diff,
                                    FSTechRating = response.FS.lack_map_calculation.balanced_tech * 10,
                                    FSPredictedAcc = response.FS.AIacc,
                                    FSAccRating = ReplayUtils.AccRating(response.FS.AIacc, response.FS.lack_map_calculation.balanced_pass_diff, response.FS.lack_map_calculation.balanced_tech * 10),
                                };

                                diff.Stars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);

                            } else {
                                diff.PassRating = diff.Stars ?? 0;
                                diff.PredictedAcc = 0.98f;
                                diff.TechRating = 0;
                            }
                            await _context.SaveChangesAsync();

                            await _scoreRefreshController.RefreshScores(lb.Id);
                        }
                    }
                }
            }
            
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/reweightsummary")]
        public async Task<ActionResult<string>> ReweightSummary()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 5;
            var scores = _context.Leaderboards.Where(s => s.Changes.FirstOrDefault(c => c.Timeset > Timeset) != null).Include(s => s.Changes).Include(s => s.Song).OrderByDescending(lb => lb.Difficulty.Stars).ToList();

            var result = "Name,Old stars,New Stars,Old acc rating,New acc rating,Old pass rating,New pass rating,Link\n";

            foreach (var lb in scores) {
                var change = lb.Changes.OrderByDescending(c => c.Timeset).FirstOrDefault();

                result += $"{lb.Song.Name.Replace(",","")},{change.OldStars},{change.NewStars},{change.OldAccRating},{change.NewAccRating},{change.OldPassRating},{change.NewPassRating},https://www.beatleader.xyz/leaderboard/global/{lb.Id}/1\n";
            }

            return result;
        }

        [HttpGet("~/admin/refreshv3")]
        public async Task<ActionResult> refreshv3()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var leaderboards = _context
                .Leaderboards
                .Where(lb => lb.Difficulty.Requirements.HasFlag(Requirements.V3))
                .Include(lb => lb.Scores.OrderBy(s => s.Rank).Take(3))
                .Include(lb => lb.Difficulty)
                .ToList();

            foreach (var item in leaderboards) {
                var maxScores = new List<int>();

                foreach (var score in item.Scores) {
                    string fileName = score.Replay.Split("/").Last();
                    if (fileName.Length == 0) continue;
                    Replay? replay;
                    using (var replayStream = await _s3Client.DownloadReplay(fileName))
                    {
                        if (replayStream == null) continue;

                        using (var ms = new MemoryStream(5))
                        {
                            await replayStream.CopyToAsync(ms);
                            long length = ms.Length;
                            try
                            {
                                (replay, _) = ReplayDecoder.Decode(ms.ToArray());
                            }
                            catch (Exception)
                            {
                                continue;
                            }
                        }
                    }

                    (var statistic, string? error) = ReplayStatisticUtils.ProcessReplay(replay, item);
                    if (statistic != null) {
                        maxScores.Add(statistic.winTracker.maxScore);
                    }
                }

                if (maxScores.Count > 0 && maxScores.Max() != item.Difficulty.MaxScore) {
                    item.Difficulty.MaxScore = maxScores.Max();
                }
                
            }

            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/admin/maps/newranking2")]
        public async Task<ActionResult> newranking2()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var songs = _context.Songs.Where(el => el.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.ranked || d.Status == DifficultyStatus.qualified || d.Status == DifficultyStatus.nominated) != null).Include(song => song.Difficulties).ThenInclude(d => d.ModifiersRating).ToList();

            await songs.ParallelForEachAsync(async song => {
                foreach (var diff in song.Difficulties)
                {
                    if (diff.Status == DifficultyStatus.ranked || diff.Status == DifficultyStatus.qualified || diff.Status == DifficultyStatus.nominated) {
                        //diff.AccRating = ReplayUtils.AccRating(
                        //        diff.PredictedAcc, 
                        //        diff.PassRating,
                        //        diff.TechRating);

                        diff.Stars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);

                        var modifiersRating = diff.ModifiersRating;
                        if (modifiersRating != null) {
                            modifiersRating.SSStars = ReplayUtils.ToStars(modifiersRating.SSAccRating, modifiersRating.SSPassRating, modifiersRating.SSTechRating);
                            modifiersRating.SFStars = ReplayUtils.ToStars(modifiersRating.SFAccRating, modifiersRating.SFPassRating, modifiersRating.SFTechRating);
                            modifiersRating.FSStars = ReplayUtils.ToStars(modifiersRating.FSAccRating, modifiersRating.FSPassRating, modifiersRating.FSTechRating);
                        }

                        //var rating = diff.ModifiersRating;
                        //if (rating != null) {
                        //    rating.SSAccRating = ReplayUtils.AccRating(
                        //            rating.SSPredictedAcc, 
                        //            rating.SSPassRating, 
                        //            rating.SSTechRating);
                        //    rating.SFAccRating = ReplayUtils.AccRating(
                        //            rating.SFPredictedAcc, 
                        //            rating.SFPassRating, 
                        //            rating.SFTechRating);
                        //    rating.FSAccRating = ReplayUtils.AccRating(
                        //            rating.FSPredictedAcc, 
                        //            rating.FSPassRating, 
                        //            rating.FSTechRating);
                        //}
                    }
                }
            }, maxDegreeOfParallelism: 20);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/admin/ban/countrychanges")]
        public async Task<ActionResult> UnbanCountrychanges([FromQuery] string playerId)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var changeBan = _context.CountryChangeBans.Where(b => b.PlayerId == playerId).FirstOrDefault();
            if (changeBan != null) {
                _context.CountryChangeBans.Remove(changeBan);
            }
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/admin/leaderboards/plot")]
        public async Task<ActionResult<string>> LeaderboardsPlot()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var result = "x,y\n";
            var scores = _context
                .Scores
                .Where(s => 
                    s.Pp > 0 && 
                    s.Rank == 1 && 
                    !s.Banned && 
                    // s.Leaderboard.Scores.Count() > 500 &&
                    s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                .Select(s => new { 
                    s.Accuracy, 
                    s.Pp, 
                    s.Modifiers, 
                    s.Leaderboard.Difficulty.Stars,
                    s.Leaderboard.Difficulty.ModifierValues,
                    s.LeaderboardId }).OrderByDescending(s => s.Accuracy).ToList();

            foreach (var score in scores)
            {
                if ((score.Modifiers.Contains("SF") || score.Modifiers.Contains("FS")) && score.ModifierValues != null)
                {
                    if (score.Modifiers.Contains("SF"))
                    {
                        result += $"{score.Stars * (score.ModifierValues.SF + 1f) },{score.Accuracy}\n";
                    }
                    else
                    {
                        result += $"{score.Stars * (score.ModifierValues.FS + 1f) },{score.Accuracy}\n";
                    }

                }
                else
                {
                    result += $"{score.Stars},{score.Accuracy}\n";
                }
            }

            return result;
        }

        [Authorize]
        [HttpGet("~/recalculateVoting")]
        public async Task<ActionResult> recalculateVoting()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var leaderboards = _context
                .Leaderboards
                .Where(lb => lb.Qualification != null)
                .Include(lb => lb.Qualification)
                .ThenInclude(q => q.Votes)
                .Include(lb => lb.Scores.Where(s => s.RankVoting != null))
                .ThenInclude(s => s.RankVoting)
                .ToList();

            foreach (var lb in leaderboards)
            {
                lb.PositiveVotes = 0;
                lb.NegativeVotes = 0;
                foreach (var score in lb.Scores)
                {
                    if (score.RankVoting.Rankability > 0) {
                        lb.PositiveVotes++;
                    } else {
                        lb.NegativeVotes++;
                    }
                }

                foreach (var vote in lb.Qualification.Votes)
                {
                    if (vote.Value == MapQuality.Good) {
                        lb.PositiveVotes += 8;
                    } else if (vote.Value == MapQuality.Bad) {
                        lb.NegativeVotes += 8;
                    }
                }

            }
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/admin/removescoreduplicates/{id}")]
        public async Task<ActionResult<ScoreStatistic?>> RemoveDuplicates(string id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Score? score = _context.Scores.Where(s => s.Id == Int64.Parse(id)).Include(s => s.Leaderboard).ThenInclude(l => l.Song).Include(s => s.Leaderboard).ThenInclude(l => l.Difficulty).FirstOrDefault();
            if (score == null)
            {
                return NotFound("Score not found");
            }

            string fileName = score.Replay.Split("/").Last();
            Replay? replay;

            using (var replayStream = await _s3Client.DownloadReplay(fileName))
            {
                if (replayStream == null) return NotFound();

                using (var ms = new MemoryStream(5))
                {
                    await replayStream.CopyToAsync(ms);
                    long length = ms.Length;
                    try
                    {
                        (replay, _) = ReplayDecoder.Decode(ms.ToArray());
                    }
                    catch (Exception)
                    {
                        return BadRequest("Error decoding replay");
                    }
                }
            }

            string? error = ReplayUtils.RemoveDuplicates(replay, score.Leaderboard);
            if (error != null) {
                return BadRequest("Failed to delete duplicate note: " + error);
            }

            ScoreStatistic? statistic = null;
            try
            {
                (statistic, error) = ReplayStatisticUtils.ProcessReplay(replay, score.Leaderboard);
                if (statistic == null && error != null) {
                    return BadRequest(error);
                }
            } catch (Exception e) {
                return BadRequest(e.ToString());
            }

            if (statistic.winTracker.totalScore == score.BaseScore) {
                return BadRequest("Recalculated score is the same");
            }

            await _s3Client.UploadScoreStats(score.Id + ".json", statistic);

            score.BaseScore = statistic.winTracker.totalScore;
            score.Accuracy = statistic.scoreGraphTracker.graph.Last();

            _context.SaveChanges();

            HttpContext.Response.OnCompleted(async () => {
                await _scoreRefreshController.RefreshScores(score.LeaderboardId);
            });

            return statistic;
        }

        [HttpGet("~/admin/createContexts")]
        public async Task<ActionResult> CreateContexts()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var allScoresCount = _context.Scores.Where(s => s.ValidContexts == LeaderboardContexts.None).Count();

            for (int i = 0; i < allScoresCount; i += 10000) {
                var scores = _context
                    .Scores
                    .Where(s => s.ValidContexts == LeaderboardContexts.None)
                    .OrderBy(s => s.Id)
                    .Skip(i)
                    .Take(10000)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .ToList();

                foreach (var score in scores) {
                    var difficulty = score.Leaderboard.Difficulty;
                    score.ContextExtensions = new List<ScoreContextExtension>();
                    score.ValidContexts = LeaderboardContexts.General;
                    var noModsExtension = ReplayUtils.NoModsContextExtension(score, difficulty);
                    if (noModsExtension != null) {
                        noModsExtension.LeaderboardId = score.LeaderboardId;
                        noModsExtension.PlayerId = score.PlayerId;
                        score.ContextExtensions.Add(noModsExtension);
                        score.ValidContexts |= LeaderboardContexts.NoMods;
                    }
                    var noPauseExtenstion = ReplayUtils.NoPauseContextExtension(score);
                    if (noPauseExtenstion != null) {
                        noPauseExtenstion.LeaderboardId = score.LeaderboardId;
                        noPauseExtenstion.PlayerId = score.PlayerId;
                        score.ContextExtensions.Add(noPauseExtenstion);
                        score.ValidContexts |= LeaderboardContexts.NoPause;
                    }
                    var golfExtension = ReplayUtils.GolfContextExtension(score, difficulty);
                    if (golfExtension != null) {
                        golfExtension.LeaderboardId = score.LeaderboardId;
                        golfExtension.PlayerId = score.PlayerId;
                        score.ContextExtensions.Add(golfExtension);
                        score.ValidContexts |= LeaderboardContexts.Golf;
                    }
                }

                _context.BulkSaveChanges();
            }

            var players = _context.Players.Where(p => p.ContextExtensions == null).ToList();
            foreach (var player in players) {
                player.ContextExtensions = new List<PlayerContextExtension> {
                    new PlayerContextExtension {
                        Context = LeaderboardContexts.NoMods,
                        ScoreStats = new PlayerScoreStats(),
                        PlayerId = player.Id,
                        Country = player.Country
                    },
                    new PlayerContextExtension {
                        Context = LeaderboardContexts.NoPause,
                        ScoreStats = new PlayerScoreStats(),
                        PlayerId = player.Id,
                        Country = player.Country
                    },
                    new PlayerContextExtension {
                        Context = LeaderboardContexts.Golf,
                        ScoreStats = new PlayerScoreStats(),
                        PlayerId = player.Id,
                        Country = player.Country
                    },
                };
            }

            _context.BulkSaveChanges();

            return Ok();
        }

        [NonAction]
        //[HttpPost("~/admin/cleandb")]
        public async Task<ActionResult> CleanDb()
        {
            var auths = _context.Auths.ToList();

            foreach (var auth in auths) {
                auth.Login = "login" + auth.Id;
                auth.Password = "password" + auth.Id;
            }

            foreach (var item in _context.AuthIPs.ToList()) {
                _context.AuthIPs.Remove(item);
            }

            foreach (var item in _context.AuthIDs.ToList()) {
                _context.AuthIDs.Remove(item);
            }

            foreach (var item in _context.PlayerLeaderboardStats.ToList()) {
                _context.PlayerLeaderboardStats.Remove(item);
            }

            foreach (var item in _context.TwitterLinks.ToList()) {
                _context.TwitterLinks.Remove(item);
            }

            foreach (var item in _context.TwitchLinks.ToList()) {
                _context.TwitchLinks.Remove(item);
            }

            foreach (var item in _context.DiscordLinks.ToList()) {
                _context.DiscordLinks.Remove(item);
            }

            foreach (var item in _context.YouTubeLinks.ToList()) {
                _context.YouTubeLinks.Remove(item);
            }

            foreach (var item in _context.PatreonLinks.ToList()) {
                _context.PatreonLinks.Remove(item);
            }

            foreach (var item in _context.BeatSaverLinks.ToList()) {
                _context.BeatSaverLinks.Remove(item);
            }

            foreach (var item in _context.WatchingSessions.ToList()) {
                _context.WatchingSessions.Remove(item);
            }

            foreach (var item in _context.RankVotings.ToList()) {
                _context.RankVotings.Remove(item);
            }

            foreach (var item in _context.Friends.ToList()) {
                _context.Friends.Remove(item);
            }

            foreach (var item in _context.VoterFeedback.ToList()) {
                _context.VoterFeedback.Remove(item);
            }

            foreach (var item in _context.CriteriaCommentary.ToList()) {
                _context.CriteriaCommentary.Remove(item);
            }

            foreach (var item in _context.QualificationCommentary.ToList()) {
                _context.QualificationCommentary.Remove(item);
            }
            foreach (var item in _context.LoginAttempts.ToList()) {
                _context.LoginAttempts.Remove(item);
            }
            foreach (var item in _context.LoginChanges.ToList()) {
                _context.LoginChanges.Remove(item);
            }
            foreach (var item in _context.AccountLinkRequests.ToList()) {
                _context.AccountLinkRequests.Remove(item);
            }

            foreach (var item in _context.Leaderboards.Where(lb => lb.Qualification != null).Include(lb => lb.Qualification).ToList()) {
                item.Qualification = null;
            }
            foreach (var item in _context.ScoreRemovalLogs.ToList()) {
                _context.ScoreRemovalLogs.Remove(item);
            }

            _context.BulkSaveChanges();

            return Ok();
        }

        public static string GolovaID = "76561198059961776";
        public static string RankingBotID = "19573";
    }
}
