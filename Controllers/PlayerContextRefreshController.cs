using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class PlayerContextRefreshController : Controller {

        private readonly AppContext _context;

        private readonly IConfiguration _configuration;
        private readonly IDbContextFactory<AppContext> _dbFactory;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerContextRefreshController(
            AppContext context,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IDbContextFactory<AppContext> dbFactory,
            IWebHostEnvironment env)
        {
            _context = context;
            _dbFactory = dbFactory;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
        }

        [NonAction]
        public async Task RefreshPlayer(Player player, LeaderboardContexts context, bool refreshRank = true, bool refreshStats = true) {
            await _context.RecalculatePPAndRankFastContext(context, player);
            await _context.BulkSaveChangesAsync();

            if (refreshRank)
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = await _context.PlayerContextExtensions
                    .Where(p => p.Pp > 0 && p.Context == context)
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new { Id = p.Id, Country = p.Country })
                    .ToListAsync();
                foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
                {
                    PlayerContextExtension? p = new PlayerContextExtension { Id = pp.Id, Country = pp.Country };
                    try {
                        _context.PlayerContextExtensions.Attach(p);
                    } catch (Exception e) {
                        continue;
                    }

                    p.Rank = i + 1;
                    _context.Entry(p).Property(x => x.Rank).IsModified = true;
                    if (!countries.ContainsKey(p.Country))
                    {
                        countries[p.Country] = 1;
                    }

                    p.CountryRank = countries[p.Country];
                    _context.Entry(p).Property(x => x.CountryRank).IsModified = true;

                    countries[p.Country]++;
                }
                await _context.BulkSaveChangesAsync();

                _context.ChangeTracker.AutoDetectChangesEnabled = true;
            }
            if (refreshStats) {
                var ext = player.ContextExtensions.FirstOrDefault(ce => ce.Context == context);
                if (ext == null) {
                    ext = new PlayerContextExtension {
                        Context = context,
                        ScoreStats = new PlayerScoreStats(),
                        PlayerId = player.Id,
                        Country = player.Country
                    };
                    player.ContextExtensions.Add(ext);
                }

                await PlayerRefreshControllerHelper.RefreshStats(_context, ext.ScoreStats, player.Id, player.Rank, null, null, context);
            }

            await _context.SaveChangesAsync();
        }

        [HttpGet("~/player/{id}/refresh/{context}")]
        public async Task<ActionResult> RefreshPlayerContext(string id, LeaderboardContexts context, [FromQuery] bool refreshRank = true)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Player? player = await _context
                .Players
                .Where(p => p.Id == id)
                .Include(p => p.ScoreStats)
                .Include(p => p.ContextExtensions)
                .ThenInclude(ce => ce.ScoreStats)
                .FirstOrDefaultAsync();
            if (player == null)
            {
                return NotFound();
            }
            await RefreshPlayer(player, context, refreshRank);

            return Ok();
        }

        [HttpGet("~/player/{id}/refresh/allContexts")]
        public async Task<ActionResult> RefreshPlayerAllContexts(string id, [FromQuery] bool refreshRank = true)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            foreach (var context in ContextExtensions.NonGeneral)
            {
                await RefreshPlayerContext(id, context, refreshRank);
            }

            return Ok();
        }

        [HttpGet("~/players/refresh/allContexts")]
        [Authorize]
        public async Task<ActionResult> RefreshPlayersAllContext()
        {
            if (HttpContext != null)
            {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            foreach (var context in ContextExtensions.NonGeneral) {
                await RefreshPlayersContext(context);
            }

            return Ok();
        }

        [NonAction]
        private async Task CalculateBatch(
            List<IGrouping<string, ScoreSelection>> groups, 
            Dictionary<string, int> playerMap,
            Dictionary<int, float> weights)
        {
            using (var anotherContext = _dbFactory.CreateDbContext()) {
                anotherContext.ChangeTracker.AutoDetectChangesEnabled = false;
                foreach (var group in groups)
                {
                    try {

                        var player = new PlayerContextExtension { Id = playerMap[group.Key] };
                        try {
                            anotherContext.PlayerContextExtensions.Attach(player);
                        } catch { }

                        float resultPP = 0f;
                        float accPP = 0f;
                        float techPP = 0f;
                        float passPP = 0f;

                        foreach ((int i, var s) in group.OrderByDescending(s => s.Pp).Select((value, i) => (i, value)))
                        {
                            float weight = weights[i];
                            if (s.Weight != weight)
                            {
                                var score = new ScoreContextExtension() { Id = s.Id, Weight = weight };
                                try {
                                    anotherContext.ScoreContextExtensions.Attach(score);
                                } catch { }
                                anotherContext.Entry(score).Property(x => x.Weight).IsModified = true;
                            }
                            resultPP += s.Pp * weight;
                            accPP += s.AccPP * weight;
                            techPP += s.TechPP * weight;
                            passPP += s.PassPP * weight;
                        }
                        player.Pp = resultPP;
                        player.AccPp = accPP;
                        player.TechPp = techPP;
                        player.PassPp = passPP;

                        anotherContext.Entry(player).Property(x => x.Pp).IsModified = true;
                        anotherContext.Entry(player).Property(x => x.AccPp).IsModified = true;
                        anotherContext.Entry(player).Property(x => x.TechPp).IsModified = true;
                        anotherContext.Entry(player).Property(x => x.PassPp).IsModified = true;
                    } catch (Exception e) {
                    }
                }
                
                await anotherContext.BulkSaveChangesAsync();
            }
        }
        private class ScoreSelection {
            public int Id { get; set; } 
            public float Accuracy { get; set; } 
            public int Rank { get; set; } 
            public float Pp { get; set; } 
            public float AccPP { get; set; }  
            public float TechPP { get; set; } 
            public float PassPP { get; set; } 
            public float Weight { get; set; } 
            public string PlayerId { get; set; }
        }

        [HttpGet("~/players/refresh/{context}")]
        [Authorize]
        public async Task<ActionResult> RefreshPlayersContext(LeaderboardContexts context)
        {
            if (HttpContext != null)
            {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            var weights = new Dictionary<int, float>();
            for (int i = 0; i < 10000; i++)
            {
                weights[i] = MathF.Pow(0.965f, i);
            }

            var scores = await _context
                .ScoreContextExtensions
                .Where(s => s.Pp != 0 && s.Context == context && s.ScoreId != null && !s.Banned && !s.Qualification)
                .Select(s => new ScoreSelection { 
                    Id = s.Id, 
                    Accuracy = s.Accuracy, 
                    Rank = s.Rank, 
                    Pp = s.Pp, 
                    AccPP = s.AccPP, 
                    TechPP = s.TechPP, 
                    PassPP = s.PassPP, 
                    Weight = s.Weight, 
                    PlayerId = s.PlayerId
                })
                .ToListAsync();
            var playerMap = _context.PlayerContextExtensions.Where(ce => ce.Context == context).ToDictionary(ce => ce.PlayerId, ce => ce.Id);

            var scoreGroups = scores.GroupBy(s => s.PlayerId).ToList();
            var tasks = new List<Task>();
            for (int i = 0; i < scoreGroups.Count; i += 5000)
            {
                tasks.Add(CalculateBatch(scoreGroups.Skip(i).Take(5000).ToList(), playerMap, weights));
            }
            Task.WaitAll(tasks.ToArray());

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context.PlayerContextExtensions
                .Where(ce => ce.Context == context && !ce.Banned && ce.Pp > 0)
                .OrderByDescending(t => t.Pp)
                .ToListAsync();
            foreach ((int i, PlayerContextExtension p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                _context.Entry(p).Property(x => x.Rank).IsModified = true;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                _context.Entry(p).Property(x => x.CountryRank).IsModified = true;
                countries[p.Country]++;
            }
            await _context.BulkSaveChangesAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }

        [HttpGet("~/players/stats/refresh/allContexts")]
        public async Task<ActionResult> RefreshPlayersStatsAllContexts()
        {
            if (HttpContext != null) {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            foreach (var context in ContextExtensions.NonGeneral) {
                await RefreshPlayersStats(context);
            }

            return Ok();
        }

        [HttpGet("~/players/stats/refresh/{context}")]
        public async Task<ActionResult> RefreshPlayersStats(LeaderboardContexts context)
        {
            if (HttpContext != null) {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }
            var allScores =
                await _context.ScoreContextExtensions
                .Where(s => s.Context == context && (!s.Score.Banned || s.Score.Bot) && !s.Score.IgnoreForStats)
                .Select(s => new SubScore
                {
                    PlayerId = s.PlayerId,
                    Platform = s.Score.Platform,
                    Hmd = s.Score.Hmd,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    BonusPp = s.BonusPp,
                    PassPP = s.PassPP,
                    AccPP = s.AccPP,
                    TechPP = s.TechPP,
                    Rank = s.Rank,
                    Timeset = s.Score.Timepost,
                    Weight = s.Weight,
                    Qualification = s.Score.Qualification,
                    MaxStreak = s.Score.MaxStreak,
                    RightTiming = s.Score.RightTiming,
                    LeftTiming = s.Score.LeftTiming,
                }).ToListAsync();

            var players = await _context
                    .PlayerContextExtensions
                    .Where(p => p.Context == context && p.ScoreStats != null)
                    .OrderBy(p => p.Rank)
                    .Select(p => new { p.PlayerId, p.ScoreStats, p.Rank, p.Pp, p.Country, p.CountryRank })
                    .ToListAsync();

            var scoresById = allScores.GroupBy(s => s.PlayerId).ToDictionary(g => g.Key, g => g.ToList());

            var playersWithScores = players.Select(p => new { Id = p.PlayerId, p.Rank, p.Pp, p.Country, p.CountryRank, p.ScoreStats, Scores = scoresById.ContainsKey(p.PlayerId) ? scoresById[p.PlayerId] : new List<SubScore>{ } }).ToList();

            var playerCount = players.Where(p => p.Pp > 0).Count();
            var countryCounts = players
                .Where(p => p.Pp > 0)
                .GroupBy(p => p.Country)
                .ToDictionary(g => g.Key, g => g.Count());
            
            await playersWithScores.ParallelForEachAsync(async player => {
                await PlayerRefreshControllerHelper.RefreshStats(
                    _context, 
                    player.ScoreStats, 
                    player.Id, 
                    player.Rank, 
                    player.Pp > 0 ? player.Rank / (float)playerCount : 0,
                    player.Pp > 0 ? player.CountryRank / (float)countryCounts[player.Country] : 0, 
                    context, 
                    player.Scores);
            }, maxDegreeOfParallelism: 50);

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/players/stats/refresh/allContexts/slowly")]
        public async Task<ActionResult> RefreshPlayersStatsAllContextsSlowly()
        {
            if (HttpContext != null) {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            foreach (var context in ContextExtensions.NonGeneral) {
                await RefreshPlayersStatsSlowly(context);
            }

            return Ok();
        }

        [HttpGet("~/players/stats/refresh/{context}/slowly")]
        public async Task<ActionResult> RefreshPlayersStatsSlowly(LeaderboardContexts context)
        {
            if (HttpContext != null) {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            var playerCount = await _context
                    .PlayerContextExtensions
                    .Where(p => p.Context == context && p.ScoreStats != null)
                    .CountAsync();

            for (int i = 0; i < playerCount; i += 10000)
            {
                var players = await _context
                        .PlayerContextExtensions
                        .OrderBy(p => p.Id)
                        .Skip(i)
                        .Take(10000)
                        .Where(p => p.Context == context && p.ScoreStats != null)
                        .OrderBy(p => p.Rank)
                        .Select(p => new { p.PlayerId, p.ScoreStats, p.Player.Rank })
                        .ToListAsync();

                foreach (var player in players)
                {
                    await PlayerRefreshControllerHelper.RefreshStats(_context, player.ScoreStats, player.PlayerId, player.Rank, null, null, context);
                }

                await _context.BulkSaveChangesAsync();
            }

            return Ok();
        }

        [HttpGet("~/players/rankrefresh/{context}")]
        [Authorize]
        public async Task<ActionResult> RefreshRanks(LeaderboardContexts context)
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context.PlayerContextExtensions
                .Where(p => !p.Banned && p.Context == context)
                .OrderByDescending(t => t.Pp)
                .Select(p => new { Id = p.Id, Country = p.Country })
                .ToListAsync();
            foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
            {
                var p = new PlayerContextExtension { Id = pp.Id, Country = pp.Country };
                try {
                    _context.PlayerContextExtensions.Attach(p);
                } catch {}

                p.Rank = i + 1;
                _context.Entry(p).Property(x => x.Rank).IsModified = true;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                _context.Entry(p).Property(x => x.CountryRank).IsModified = true;

                countries[p.Country]++;
            }
            await _context.BulkSaveChangesAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }

        [HttpGet("~/players/rankrefresh/allContexts")]
        [Authorize]
        public async Task<ActionResult> RefreshRanksAllContexts()
        {
            if (HttpContext != null)
            {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            foreach (var context in ContextExtensions.NonGeneral) {
                await RefreshRanks(context);
            }

            return Ok();
        }
    }
}
