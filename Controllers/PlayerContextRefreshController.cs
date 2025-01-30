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
                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = await _context.PlayerContextExtensions
                    .Where(p => p.Pp > 0 && p.Context == context)
                    .AsNoTracking()
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new PlayerContextExtension { Id = p.Id, Country = p.Country })
                    .ToListAsync();
                foreach ((int i, var p) in ranked.Select((value, i) => (i, value)))
                {
                    p.Rank = i + 1;
                    if (!countries.ContainsKey(p.Country))
                    {
                        countries[p.Country] = 1;
                    }

                    p.CountryRank = countries[p.Country];

                    countries[p.Country]++;
                }
                await _context.BulkUpdateAsync(ranked, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });
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
                var playerUpdates = new List<PlayerContextExtension>();
                var scoreUpates = new List<ScoreContextExtension>();

                foreach (var group in groups)
                {
                    var player = new PlayerContextExtension { Id = playerMap[group.Key] };

                    float resultPP = 0f;
                    float accPP = 0f;
                    float techPP = 0f;
                    float passPP = 0f;

                    foreach ((int i, var s) in group.OrderByDescending(s => s.Pp).Select((value, i) => (i, value)))
                    {
                        float weight = weights[i];
                        if (s.Weight != weight)
                        {
                            scoreUpates.Add(new ScoreContextExtension() { Id = s.Id, Weight = weight });
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
                    playerUpdates.Add(player);
                }
                
                await anotherContext.BulkUpdateAsync(playerUpdates, options => options.ColumnInputExpression = c => new { c.Pp, c.AccPp, c.TechPp, c.PassPp });
                await anotherContext.BulkUpdateAsync(scoreUpates, options => options.ColumnInputExpression = c => new { c.Weight });
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
                .AsNoTracking()
                .ToListAsync();

            var playerMap = _context
                .PlayerContextExtensions
                .Where(ce => ce.Context == context)
                .AsNoTracking()
                .ToDictionary(ce => ce.PlayerId, ce => ce.Id);

            var scoreGroups = scores.GroupBy(s => s.PlayerId).ToList();
            for (int i = 0; i < scoreGroups.Count; i += 5000)
            {
                await CalculateBatch(scoreGroups.Skip(i).Take(5000).ToList(), playerMap, weights);
            }

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context.PlayerContextExtensions
                .Where(ce => ce.Context == context && !ce.Banned && ce.Pp > 0)
                .AsNoTracking()
                .OrderByDescending(t => t.Pp)
                .Select(ce => new { ce.Id, ce.Country })
                .ToListAsync();

            var updates = new List<PlayerContextExtension>();
            foreach ((int i, var p) in ranked.Select((value, i) => (i, value)))
            {
                var ce = new PlayerContextExtension { Id = p.Id };
                ce.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                ce.CountryRank = countries[p.Country];
                countries[p.Country]++;

                updates.Add(ce);
            }

            await _context.BulkUpdateAsync(updates, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });
            await _context.BulkSaveChangesAsync();

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

            var scoresCount = await _context.ScoreContextExtensions
                .AsNoTracking()
                .Where(s => s.Context == context && (!s.ScoreInstance.Banned || s.ScoreInstance.Bot) && !s.ScoreInstance.IgnoreForStats)
                .CountAsync();

            var allScores = new List<SubScore>();

            for (int i = 0; i < scoresCount; i += 1000000) {
                var sublist = await _context.ScoreContextExtensions
                    .AsNoTracking()
                    .OrderBy(s => s.Id)
                    .Where(s => s.Context == context && (!s.ScoreInstance.Banned || s.ScoreInstance.Bot) && !s.ScoreInstance.IgnoreForStats)
                    .Skip(i)
                    .Take(1000000)
                    .Select(s => new SubScore
                    {
                        PlayerId = s.PlayerId,
                        Platform = s.ScoreInstance.Platform,
                        Hmd = s.ScoreInstance.Hmd,
                        ModifiedScore = s.ModifiedScore,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        BonusPp = s.BonusPp,
                        PassPP = s.PassPP,
                        AccPP = s.AccPP,
                        TechPP = s.TechPP,
                        Rank = s.Rank,
                        Timeset = s.ScoreInstance.Timepost,
                        Weight = s.Weight,
                        Qualification = s.Qualification,
                        MaxStreak = s.ScoreInstance.MaxStreak,
                        RightTiming = s.ScoreInstance.RightTiming,
                        LeftTiming = s.ScoreInstance.LeftTiming,
                    }).ToListAsync();
                allScores.AddRange(sublist);
            }

            var players = await _context
                    .PlayerContextExtensions
                    .AsNoTracking()
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

            await Task.WhenAll(playersWithScores.Select(player => PlayerRefreshControllerHelper.RefreshStats(
                    _context, 
                    player.ScoreStats, 
                    player.Id, 
                    player.Rank, 
                    player.Pp > 0 ? player.Rank / (float)playerCount : 0,
                    player.Pp > 0 ? player.CountryRank / (float)countryCounts[player.Country] : 0, 
                    context, 
                    player.Scores)));
            await _context.BulkUpdateAsync(players.Select(p => p.ScoreStats).ToList());

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
                        .Select(p => new { p.PlayerId, p.ScoreStats, p.PlayerInstance.Rank })
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

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context.PlayerContextExtensions
                .Where(p => !p.Banned && p.Context == context)
                .AsNoTracking()
                .OrderByDescending(t => t.Pp)
                .Select(p => new PlayerContextExtension { Id = p.Id, Country = p.Country })
                .ToListAsync();
            foreach ((int i, var p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];

                countries[p.Country]++;
            }
            await _context.BulkUpdateAsync(ranked, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });

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
