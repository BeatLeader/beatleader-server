using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReplayDecoder;
using CsvHelper;
using CsvHelper.Configuration;
using System.Threading.Tasks;
using System.Globalization;
using System.Net;
using BeatLeader_Server.ControllerHelpers;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppContext _context;
        private readonly StorageContext _storageContext;
        CurrentUserController _currentUserController;
        ScoreRefreshController _scoreRefreshController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;
        private readonly IAmazonS3 _s3Client;

        public AdminController(
            AppContext context,
            StorageContext storageContext,
            IWebHostEnvironment env,
            CurrentUserController currentUserController,
            ScoreRefreshController scoreRefreshController,
            ReplayController replayController,
            IConfiguration configuration)
        {
            _context = context;
            _storageContext = storageContext;

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

            var attempts = await _context.LoginAttempts.ToListAsync();
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
            var clan = await _context.Clans.FirstOrDefaultAsync(c => c.Id == id);
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
            var clan = await _context.Clans.FirstOrDefaultAsync(c => c.Id == id);
            if (player != null)
            {
                player.Clans.Add(clan);
                clan.PlayersCount++;
                clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, player.ScoreStats.AverageRankedAccuracy);
                clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, player.Rank);
                await _context.SaveChangesAsync();

                clan.Pp = await _context.RecalculateClanPP(clan.Id);

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

            return await _context.Scores.Where(s => s.Timepost >= from && s.Timepost <= to).ToListAsync();
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

            var allLeaderboards = await _context.Leaderboards.Select(l => new { Id = l.Id, ScoreTimesets = l.Scores.Select(s => s.Timeset).ToList() }).ToListAsync();


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

            var allLeaderboards = await _context.Leaderboards
                .ToListAsync();

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
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var lbs = await _context.Leaderboards.Include(l => l.Song).Include(l => l.Difficulty).ToListAsync();
            foreach (var lb in lbs)
            {
                lb.Difficulty.Duration = lb.Song.Duration;
            }
            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/mappersList")]
        public async Task<ActionResult> mappersList()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var lbs = _context
                .Leaderboards
                .Include(l => l.Song)
                .Include(l => l.Difficulty)
                .Where(lb => lb.Difficulty.RankedTime > 1672531200)
                .ToList();

            var mappers = new List<string>();

            foreach (var lb in lbs)
            {
                foreach (var item in lb.Song.Mapper.Split("&"))
                {
                    foreach (var item1 in item.Split(",")) {
                        mappers.Add(item1.Trim());
                    }
                }
            }

            return Ok(mappers.Distinct().Count());
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

            var history = await _storageContext.PlayerScoreStatsHistory.FirstOrDefaultAsync(h => h.Id == id);
            if (history == null) {
                return NotFound();
            }
            _storageContext.PlayerScoreStatsHistory.Remove(history);
            await _storageContext.SaveChangesAsync();

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

            var link = await _context.BeatSaverLinks.FirstOrDefaultAsync(link => link.BeatSaverId == beatSaverId);
            if (link == null) {
                return NotFound();
            }
            _context.BeatSaverLinks.Remove(link);
            await _context.SaveChangesAsync();

            return Ok();
        }


        [HttpDelete("~/admin/playerHistory/time/{time}")]
        public async Task<ActionResult> DeleteHistoryTime(int time)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var history = await _storageContext.PlayerScoreStatsHistory.Where(h => h.Timestamp == time).ToListAsync();
            foreach (var item in history)
            {
                _storageContext.PlayerScoreStatsHistory.Remove(item);
            }
            
            await _storageContext.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/map/refresh")]
        public async Task<ActionResult> RefreshHash()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var songs = _context.Songs.Where(el => el.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.outdated) != null).Include(song => song.Difficulties).ToList();

            foreach (var song in songs)
            {
                Song? updatedSong = await SongUtils.GetSongFromBeatSaver(song.Hash);

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

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/refreshClanRankings")]
        public async Task<ActionResult> RefreshClanRankings([FromQuery] string? lbId = null)
        {
            // refreshClanRankings: Http Put endpoint that recalculates the clan rankings for all ranked leaderboards.

            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            //_context.BulkDelete(_context.ClanRanking);

            var leaderboardsRecalc = _context
                .Leaderboards
                .Where(lb => 
                    lb.Difficulty.Status == DifficultyStatus.ranked &&
                    (lbId == null || lb.Id == lbId))
                .Include(lb => lb.ClanRanking)
                .ToList();
            foreach (var leaderboard in leaderboardsRecalc)
            {
                _ = _context.CalculateClanRankingSlow(leaderboard);
            }

            await _context.BulkSaveChangesAsync();

            var clans = _context
                .Clans
                .Select(c => new { Clan = c, CaptureLeaderboardsCount = c.CapturedLeaderboards.Count() })
                .OrderByDescending(c => c.CaptureLeaderboardsCount)
                .ToList();
            var rank = 1;
            foreach (var c in clans)
            {
                var clan = c.Clan;
                clan.CaptureLeaderboardsCount = c.CaptureLeaderboardsCount;
                clan.RankedPoolPercentCaptured = ((float)clan.CaptureLeaderboardsCount) / (float)ConstantsService.RankedMapCount;

                clan.Rank = rank;
                rank++;
            }
            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/refreshClanRankingsScore")]
        public async Task<ActionResult> RefreshClanRankingsScore([FromQuery] int scoreId)
        {
            // refreshClanRankings: Http Put endpoint that recalculates the clan rankings for all ranked leaderboards.

            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            //_context.BulkDelete(_context.ClanRanking);

            var score = _context
                .Scores
                .Where(s => s.Id == scoreId)
                .Include(s => s.Leaderboard)
                .ThenInclude(lb => lb.Difficulty)
                .FirstOrDefault();

            _context.UpdateClanRanking(score.Leaderboard, score);

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/deleteunrankedclanranking")]
        public async Task<ActionResult> DeleteUnrankedClanranking()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var rankings = _context
                .ClanRanking
                .Where(cr => cr.Leaderboard.Difficulty.Status != DifficultyStatus.ranked)
                .ToList();
            await _context.BulkDeleteAsync(rankings);

            return Ok();
        }

        [HttpGet("~/admin/deleteclanorderchanges")]
        public async Task<ActionResult> deleteclanorderchanges([FromQuery] string id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var rankings = _context
                .ClanOrderChanges
                .Where(cr => cr.PlayerId == id)
                .ToList();
            await _context.BulkDeleteAsync(rankings);

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
            await _context.SaveChangesAsync();

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

            var leaderboards = _context
                .Leaderboards
                .Where(lb => 
                    lb.Difficulty.Status == DifficultyStatus.ranked || 
                    lb.Difficulty.Status == DifficultyStatus.qualified || 
                    lb.Difficulty.Status == DifficultyStatus.nominated)
                .Include(lb => lb.Difficulty)
                .Include(lb => lb.Changes)
                .Include(lb => lb.Song)
                .ToList();

            foreach (var leaderboard in leaderboards)
            {
                var diff = leaderboard.Difficulty;
                var song = leaderboard.Song;

                LeaderboardChange rankChange = new LeaderboardChange
                {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    PlayerId = RankingBotID,
                    OldStars = diff.Stars ?? 0,
                    OldAccRating = diff.AccRating ?? 0,
                    OldPassRating = diff.PassRating ?? 0,
                    OldTechRating = diff.TechRating ?? 0,
                };

                if (leaderboard.Changes == null) {
                    leaderboard.Changes = new List<LeaderboardChange>();
                }

                leaderboard.Changes.Add(rankChange);

                await RatingUtils.UpdateFromExMachina(leaderboard, rankChange);
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
                                (replay, _) = ReplayDecoder.ReplayDecoder.Decode(ms.ToArray());
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

            await _context.SaveChangesAsync();

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
            await _context.SaveChangesAsync();

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

        [HttpGet("~/externalstatuscheck")]
        public async Task<ActionResult> externalstatuscheck()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var songs = _context
                .Songs
                .Where(s => s.ExternalStatuses != null && s.ExternalStatuses.Count > 0 && s.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.outdated) != null)
                .Include(song => song.ExternalStatuses)
                .ToList();

            foreach (var song in songs)
            {
                var newSong = _context
                    .Songs
                    .Where(s => s.Id.StartsWith(song.Id + "x") && s.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.outdated) == null)
                    .Include(song => song.ExternalStatuses)
                    .FirstOrDefault();
                if (newSong == null) continue;

                if (newSong.ExternalStatuses == null) {
                    newSong.ExternalStatuses = new List<ExternalStatus>();
                }

                foreach (var status in song.ExternalStatuses)
                {
                    newSong.ExternalStatuses.Add(status);
                    song.ExternalStatuses.Remove(status);
                }
            }

            await _context.BulkSaveChangesAsync();

            return Ok();
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
            await _context.SaveChangesAsync();

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
                        (replay, _) = ReplayDecoder.ReplayDecoder.Decode(ms.ToArray());
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

            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                await _scoreRefreshController.RefreshScores(score.LeaderboardId);
            });

            return statistic;
        }

        [HttpGet("~/admin/checkheadsets")]
        public async Task<ActionResult> CheckHeadsets()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var scores = _context.Scores.Where(s => s.Hmd == HMD.unknown).OrderByDescending(s => s.Timepost).ToList();
            var asyncDecoder = new AsyncReplayDecoder();

            foreach (var score in scores)
            {
                string fileName = score.Replay.Split("/").Last();
                ReplayInfo? replayInfo;

                using (var replayStream = await _s3Client.DownloadReplay(fileName))
                {
                    if (replayStream == null) continue;

                    try
                    {
                        replayInfo = await asyncDecoder.DecodeInfoOnly(replayStream);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                score.Hmd = ReplayUtils.HMDFromName(replayInfo.hmd);
                score.Controller = ReplayUtils.ControllerFromName(replayInfo.controller);
            }

            await _context.SaveChangesAsync();

            return Ok();
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
                    var scpmExtension = ReplayUtils.SCPMContextExtension(score, difficulty);
                    if (scpmExtension != null) {
                        scpmExtension.LeaderboardId = score.LeaderboardId;
                        scpmExtension.PlayerId = score.PlayerId;
                        score.ContextExtensions.Add(scpmExtension);
                        score.ValidContexts |= LeaderboardContexts.SCPM;
                    }
                }

                await _context.BulkSaveChangesAsync();
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
                    new PlayerContextExtension {
                        Context = LeaderboardContexts.SCPM,
                        ScoreStats = new PlayerScoreStats(),
                        PlayerId = player.Id,
                        Country = player.Country
                    },
                };
            }

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/checkratings")]
        public async Task<ActionResult> checkratings()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var songs = await _context
                .Songs
                .Where(s => s.Mapper != "Beat Sage" && s.Mapper != "TK2774" && s.Duration < 1500 && s.Difficulties.FirstOrDefault(d =>
                (d.Status == DifficultyStatus.unranked || d.Status == DifficultyStatus.nominated || d.Status == DifficultyStatus.inevent || d.Status == DifficultyStatus.unrankable) && 
                !d.Requirements.HasFlag(Requirements.Noodles) && 
                !d.Requirements.HasFlag(Requirements.MappingExtensions) &&
                (d.Stars == null || d.Stars == 0) &&
                d.Notes > 20 &&
                d.ModeName != "Lightshow" &&
                d.ModeName != "Generated360Degree" &&
                d.ModeName != "Generated90Degree" &&
                d.ModeName != "StandardOldDots" &&
                d.ModeName != "InvertedStandard" &&
                d.ModeName != "VerticalStandard" &&
                d.ModeName != "HorizontalStandard" &&
                d.ModeName != "InverseStandard" &&
                d.ModeName !=  "RhythmGameStandard" &&
                !d.ModeName.Contains("PinkPlay") &&
                !d.ModeName.Contains("Controllable")) != null)
                .OrderByDescending(s => s.UploadTime)
                .Include(s => s.Difficulties)
                .ToListAsync();

            foreach (var song in songs) {
                try {
                    var newSong = await SongUtils.GetSongFromBeatSaver(song.Hash);
                    if (newSong == null || newSong.Hash.ToLower() != song.Hash.ToLower()) {
                        foreach (var d in song.Difficulties) {
                            d.Status = DifficultyStatus.outdated;
                        }
                        continue;
                    } else {
                        
                        foreach (var d in song.Difficulties) {
                            d.Status = DifficultyStatus.unranked;
                            d.Requirements |= newSong.Difficulties.FirstOrDefault(dd => dd.DifficultyName == d.DifficultyName && dd.ModeName == d.ModeName)?.Requirements ?? Requirements.None;
                        }
                    }
                } catch {
                    foreach (var d in song.Difficulties) {
                        d.Status = DifficultyStatus.outdated;
                    }
                    continue;
                }

                foreach (var d in song.Difficulties) {
                    if ((d.Stars == null || d.Stars == 0) && 
                        !d.Requirements.HasFlag(Requirements.Noodles) && 
                        !d.Requirements.HasFlag(Requirements.MappingExtensions) &&
                        d.Notes > 20 &&
                        d.ModeName != "Lightshow" &&
                        d.ModeName != "InvertedStandard" &&
                        d.ModeName != "VerticalStandard" &&
                        d.ModeName != "Generated360Degree" &&
                        d.ModeName != "Generated90Degree" &&
                        d.ModeName != "StandardOldDots" &&
                        d.ModeName != "HorizontalStandard" &&
                        d.ModeName != "InverseStandard" &&
                        d.ModeName !=  "RhythmGameStandard" &&
                        !d.ModeName.Contains("PinkPlay") && 
                        !d.ModeName.Contains("Controllable")) {
                        await RatingUtils.UpdateFromExMachina(d, song, null);
                    }
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("~/admin/mugrateScores")]
        public async Task<ActionResult> MigrateScores(
            [FromQuery] string oldId,
            [FromQuery] string newId)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var scores = _context.Scores.Where(s => s.LeaderboardId == oldId).Include(s => s.ContextExtensions).ToList();
            foreach (var score in scores)
            {
                score.LeaderboardId = newId;
                foreach (var ce in score.ContextExtensions)
                {
                    ce.LeaderboardId = newId;
                }
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        public class YouTubeVideo
        {
            public string Link { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public DateTime PublishedDate { get; set; }
        }

        [HttpPost("~/admin/importCC")]
        public async Task<IActionResult> importCC()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var videos = new List<YouTubeVideo>();
            var cultureInfo = new CultureInfo("en-US");
            var config = new CsvConfiguration(cultureInfo) { HasHeaderRecord = true };

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            using (var reader = new StreamReader(ms))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Context.RegisterClassMap<YouTubeVideoMap>();
                videos = csv.GetRecords<YouTubeVideo>().ToList();
            }

            foreach (var video in videos)
            {
                if (!video.Title.Contains("Noodle Map Monday")) continue;

                string id = video.Description.Split("https://beatsaver.com/maps/").Last().Split(".").First().Split("\n").First();

                var lastVersion = await SongUtils.GetSongFromBeatSaverId(id);

                if (lastVersion == null) continue;
                var song = _context.Songs.Where(s => s.Hash.ToLower() == lastVersion.Hash.ToLower()).Include(s => s.ExternalStatuses).FirstOrDefault();

                if (song == null) continue;
                if (song.ExternalStatuses == null) {
                    song.ExternalStatuses = new List<ExternalStatus>();
                }
                if (song.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.NoodleMonday) != null) continue;

                song.ExternalStatuses.Add(new ExternalStatus {
                    Status = SongStatus.NoodleMonday,
                    Timeset = (int)video.PublishedDate.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    Link = video.Link
                });
            }
            _context.SaveChanges();

            return Ok(videos);
        }

        [HttpPost("~/admin/addnoodle")]
        public async Task<IActionResult> addnoodle(
            [FromQuery] string hash,
            [FromQuery] int time)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var song = _context.Songs.Where(s => s.Hash.ToLower() == hash.ToLower()).Include(s => s.ExternalStatuses).FirstOrDefault();

            if (song == null) return BadRequest();
            if (song.ExternalStatuses == null) {
                song.ExternalStatuses = new List<ExternalStatus>();
            }
            if (song.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.NoodleMonday) != null) return BadRequest();

            song.ExternalStatuses.Add(new ExternalStatus {
                Status = SongStatus.NoodleMonday,
                Timeset = time,
                Link = "https://beatsaver.com/playlists/4197"
            });
            _context.SaveChanges();

            return Ok();
        }

        public sealed class YouTubeVideoMap : ClassMap<YouTubeVideo>
        {
            public YouTubeVideoMap()
            {
                Map(m => m.Link).Index(0);
                Map(m => m.Title).Index(1);
                Map(m => m.Description).Index(2);
                Map(m => m.PublishedDate).Index(3).TypeConverterOption.Format("M/d/yy, h:mm:ss tt");
            }
        }
        [NonAction]
        //[HttpPost("~/admin/recalculateIndexes")]
        public async Task<ActionResult> RecalculateIndexes()
        {
            // https://stackoverflow.com/a/74454347
            await _context.Database.ExecuteSqlRawAsync(
                """
                SET QUOTED_IDENTIFIER ON
                SET ARITHABORT ON
                SET NUMERIC_ROUNDABORT OFF
                SET CONCAT_NULL_YIELDS_NULL ON
                SET ANSI_NULLS ON
                SET ANSI_PADDING ON
                SET ANSI_WARNINGS ON

                DECLARE @TableName varchar(255);
                DECLARE @IndexName varchar(255);
                DECLARE @Fragmentation FLOAT;
                DECLARE @IndexScript varchar(255);

                SELECT 
                    dbtables.[name], 
                    dbindexes.[name],
                    indexstats.avg_fragmentation_in_percent,
                    indexstats.page_count [pages]
                FROM 
                    sys.dm_db_index_physical_stats (DB_ID(), NULL, NULL, NULL, NULL) AS indexstats
                    INNER JOIN sys.tables dbtables 
                        on dbtables.[object_id] = indexstats.[object_id]
                    INNER JOIN sys.schemas dbschemas 
                        on dbtables.[schema_id] = dbschemas.[schema_id]
                    INNER JOIN sys.indexes AS dbindexes 
                        ON dbindexes.[object_id] = indexstats.[object_id]
                        AND indexstats.index_id = dbindexes.index_id
                WHERE 
                    indexstats.database_id = DB_ID()
                    AND indexstats.avg_fragmentation_in_percent >= 5.0
                    AND indexstats.page_count > 10
                ORDER BY 
                    indexstats.page_count ASC,
                    indexstats.avg_fragmentation_in_percent ASC

                DECLARE TableCursor CURSOR FOR  
                    SELECT 
                        dbtables.[name], 
                        dbindexes.[name],
                        indexstats.avg_fragmentation_in_percent 
                    FROM 
                        sys.dm_db_index_physical_stats (DB_ID(), NULL, NULL, NULL, NULL) AS indexstats
                        INNER JOIN sys.tables dbtables 
                            on dbtables.[object_id] = indexstats.[object_id]
                        INNER JOIN sys.schemas dbschemas 
                            on dbtables.[schema_id] = dbschemas.[schema_id]
                        INNER JOIN sys.indexes AS dbindexes 
                            ON dbindexes.[object_id] = indexstats.[object_id]
                            AND indexstats.index_id = dbindexes.index_id
                    WHERE 
                        indexstats.database_id = DB_ID()
                        AND indexstats.avg_fragmentation_in_percent >= 5.0
                        AND indexstats.page_count > 10
                    ORDER BY 
                        indexstats.page_count ASC,
                        indexstats.avg_fragmentation_in_percent ASC;

                OPEN TableCursor

                FETCH NEXT FROM TableCursor INTO
                    @TableName,
                    @IndexName,
                    @Fragmentation

                WHILE @@FETCH_STATUS = 0 

                BEGIN 
                    IF (@Fragmentation >= 30.0)
                        SET @IndexScript = 'ALTER INDEX ' + @IndexName + ' ON ' + @TableName + ' REBUILD';
                    ELSE IF (@Fragmentation >= 5.0)
                        SET @IndexScript = 'ALTER INDEX ' + @IndexName + ' ON ' + @TableName + ' REORGANIZE';
                    ELSE
                        SET @IndexScript = NULL;

                    IF (@IndexScript IS NOT NULL)
                    BEGIN
                        RAISERROR (@IndexScript, 10, 0) WITH NOWAIT
                        WAITFOR DELAY '00:00:01';
                        EXEC(@IndexScript); 
                    END

                    FETCH NEXT FROM TableCursor INTO
                        @TableName,
                        @IndexName,
                        @Fragmentation;

                END 

                CLOSE TableCursor;

                DEALLOCATE TableCursor;
                """);
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

            _context.AuthIPs.BulkDelete(_context.AuthIPs);
            _context.Bans.BulkDelete(_context.Bans);
            _context.TwitterLinks.BulkDelete(_context.TwitterLinks);
            _context.TwitchLinks.BulkDelete(_context.TwitchLinks);
            _context.DiscordLinks.BulkDelete(_context.DiscordLinks);

            foreach (var item in _context.YouTubeLinks.ToList()) {
                _context.YouTubeLinks.Remove(item);
            }

            foreach (var item in _context.PatreonLinks.ToList()) {
                _context.PatreonLinks.Remove(item);
            }

            foreach (var item in _context.BeatSaverLinks.ToList()) {
                _context.BeatSaverLinks.Remove(item);
            }
            _context.WatchingSessions.BulkDelete(_context.WatchingSessions);
            await _context.BulkSaveChangesAsync();

            foreach (var item in _context.RankVotings.ToList()) {
                _context.RankVotings.Remove(item);
            }
            await _context.BulkSaveChangesAsync();

            foreach (var item in _context.Friends.ToList()) {
                _context.Friends.Remove(item);
            }

            foreach (var item in _context.VoterFeedback.ToList()) {
                _context.VoterFeedback.Remove(item);
            }

            foreach (var item in _context.ValentineMessages.ToList()) {
                _context.ValentineMessages.Remove(item);
            }

            foreach (var item in _context.Clans.ToList()) {
                item.ClanRankingDiscordHook = "";
                item.PlayerChangesCallback = "";
            }

            foreach (var item in _context.CriteriaCommentary.ToList()) {
                _context.CriteriaCommentary.Remove(item);
            }
            await _context.BulkSaveChangesAsync();

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
            foreach (var item in _context.DeveloperProfile.ToList()) {
                _context.DeveloperProfile.Remove(item);
            }
            await _context.BulkSaveChangesAsync();

            foreach (var item in _context.Leaderboards.Where(lb => lb.Qualification != null).Include(lb => lb.Qualification).ToList())
            {
                item.Qualification = null;
            }
            foreach (var item in _context.ScoreRemovalLogs.ToList())
            {
                _context.ScoreRemovalLogs.Remove(item);
            }

            _context.BulkDelete(_context.OpenIddictTokens);
            _context.BulkDelete(_context.OpenIddictScopes);
            _context.BulkDelete(_context.OpenIddictAuthorizations);
            _context.BulkDelete(_context.OpenIddictApplications);

            _context.Database.ExecuteSqlRaw("TRUNCATE TABLE PlayerLeaderboardStats;");

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        public static string GolovaID = "76561198059961776";
        public static string RankingBotID = "19573";
    }
}
