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

namespace BeatLeader_Server.Controllers
{
    public class PlayerRefreshController : Controller
    {
        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerRefreshController(
            AppContext context,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IDbContextFactory<AppContext> dbFactory,
            IWebHostEnvironment env)
        {
            _context = context;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _dbFactory = dbFactory;
        }

        [NonAction]
        public async Task RefreshPlayer(Player player, bool refreshRank = true, bool refreshStats = true) {
            await _context.RecalculatePPAndRankFast(player, LeaderboardContexts.General, null);
            await _context.BulkSaveChangesAsync();

            if (refreshRank)
            {
                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = await _context.Players
                    .Where(p => p.Pp > 0 && !p.Banned)
                    .AsNoTracking()
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new Player { Id = p.Id, Country = p.Country, Rank = p.Rank })
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
                await PlayerRefreshControllerHelper.RefreshStats(_context, player.ScoreStats, player.Id, player.Rank, null, null, LeaderboardContexts.General);
            }

            await _context.SaveChangesAsync();
        }

        [HttpGet("~/player/{id}/refresh")]
        public async Task<ActionResult> RefreshPlayerAction(string id, [FromQuery] bool refreshRank = true)
        {
            if (HttpContext != null) {
                string? currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            Player? player = await _context.Players.Where(p => p.Id == id).Include(p => p.ScoreStats).FirstOrDefaultAsync();
            if (player == null)
            {
                return NotFound();
            }
            await RefreshPlayer(player, refreshRank);

            return Ok();
        }

        [HttpGet("~/players/leaderboard/{id}/refresh")]
        public async Task<ActionResult> RefreshLeaderboardPlayers(string id)
        {
            if (HttpContext != null)
            {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            Leaderboard? leaderboard = await _context.Leaderboards.Where(p => p.Id == id).Include(l => l.Scores).ThenInclude(s => s.Player).ThenInclude(s => s.ScoreStats).FirstOrDefaultAsync();

            if (leaderboard == null)
            {
                return NotFound();
            }

            foreach (var score in leaderboard.Scores)
            {
                await RefreshPlayer(score.Player, false, false);
            }

            return Ok();
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
            public string Country { get; set; } 
        }

        [NonAction]
        private async Task CalculateBatch(List<IGrouping<string, Score>> groups, Dictionary<int, float> weights)
        {
            var playerUpdates = new List<Player>();
            var scoreUpdates = new List<Score>();

            foreach (var group in groups)
            {
                Player player = new Player { Id = group.Key };

                float resultPP = 0f;
                float accPP = 0f;
                float techPP = 0f;
                float passPP = 0f;

                foreach ((int i, var s) in group.OrderByDescending(s => s.Pp).Select((value, i) => (i, value)))
                {
                    float weight = weights[i];
                    if (s.Weight != weight)
                    {
                        s.Weight = weight;
                    }
                    resultPP += s.Pp * weight;
                    accPP += s.AccPP * weight;
                    techPP += s.TechPP * weight;
                    passPP += s.PassPP * weight;

                    scoreUpdates.Add(s);
                }

                player.Pp = resultPP;
                player.AccPp = accPP;
                player.TechPp = techPP;
                player.PassPp = passPP;

                playerUpdates.Add(player);
            }

            await _context.BulkUpdateAsync(playerUpdates, options => options.ColumnInputExpression = c => new { c.Pp, c.AccPp, c.TechPp, c.PassPp });
            await _context.BulkUpdateAsync(scoreUpdates, options => options.ColumnInputExpression = c => new { c.Weight });
        }

        [HttpGet("~/players/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshPlayers()
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
                .Scores
                .Where(s => s.Pp != 0 && !s.Banned && !s.Qualification && s.ValidContexts.HasFlag(LeaderboardContexts.General))
                .Select(s => new Score { 
                    Id = s.Id, 
                    Accuracy = s.Accuracy, 
                    Rank = s.Rank, 
                    Pp = s.Pp, 
                    AccPP = s.AccPP, 
                    TechPP = s.TechPP, 
                    PassPP = s.PassPP, 
                    Weight = s.Weight, 
                    PlayerId = s.PlayerId, 
                    Country = s.Player.Country 
                })
                .ToListAsync();

            var scoreGroups = scores.GroupBy(s => s.PlayerId).ToList();
            for (int i = 0; i < scoreGroups.Count; i += 5000) {
                await CalculateBatch(scoreGroups.Skip(i).Take(5000).ToList(), weights);
            }

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context.Players
                .Where(p => p.Pp > 0 && !p.Banned)
                .AsNoTracking()
                .OrderByDescending(t => t.Pp)
                .Select(p => new Player { Id = p.Id, Country = p.Country })
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

        [HttpGet("~/players/stats/refresh")]
        public async Task<ActionResult> RefreshPlayersStats()
        {
            if (HttpContext != null) {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }
            var allScores =
                await _context.Scores.Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && (!s.Banned || s.Bot) && !s.IgnoreForStats).Select(s => new SubScore
                {
                    PlayerId = s.PlayerId,
                    Platform = s.Platform,
                    Hmd = s.Hmd,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    BonusPp = s.BonusPp,
                    PassPP = s.PassPP,
                    AccPP = s.AccPP,
                    TechPP = s.TechPP,
                    Rank = s.Rank,
                    Timeset = s.Timepost,
                    Weight = s.Weight,
                    Qualification = s.Qualification,
                    MaxStreak = s.MaxStreak,
                    RightTiming = s.RightTiming,
                    LeftTiming = s.LeftTiming,
                }).ToListAsync();

            var players = await _context
                    .Players
                    .Where(p => (!p.Banned || p.Bot) && p.ScoreStats != null)
                    .OrderBy(p => p.Rank)
                    .Select(p => new { p.Id, p.ScoreStats, p.Rank, p.Country, p.Pp, p.CountryRank })
                    .ToListAsync();

            var scoresById = allScores.GroupBy(s => s.PlayerId).ToDictionary(g => g.Key, g => g.ToList());

            var playersWithScores = players.Where(p => scoresById.ContainsKey(p.Id)).Select(p => new { 
                p.Id, 
                p.ScoreStats, 
                p.Rank, 
                p.Pp,
                p.Country,
                p.CountryRank,
                Scores = scoresById[p.Id] 
            }).ToList();
            
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
                    LeaderboardContexts.General,
                    player.Scores);
            }, maxDegreeOfParallelism: 50);

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/players/stats/refresh/slowly")]
        public async Task<ActionResult> RefreshPlayersStatsSlowly()
        {
            if (HttpContext != null) {
                // Not fetching player here to not mess up context
                if (HttpContext.CurrentUserID(_context) != AdminController.GolovaID)
                {
                    return Unauthorized();
                }
            }

            var playerCount = await _context
                    .Players
                    .Where(p => (!p.Banned || p.Bot) && p.ScoreStats != null)
                    .CountAsync();

            for (int i = 0; i < playerCount; i += 10000)
            {
                var players = await _context
                    .Players
                    .OrderBy(p => p.Id)
                    .Skip(i)
                    .Take(10000)
                    .Where(p => (!p.Banned || p.Bot) && p.ScoreStats != null)
                    .Select(p => new { p.Id, p.ScoreStats, p.Rank })
                    .ToListAsync();

                foreach (var player in players)
                {
                    await PlayerRefreshControllerHelper.RefreshStats(
                        _context,
                        player.ScoreStats, 
                        player.Id, 
                        player.Rank,
                        null,
                        null,
                        LeaderboardContexts.General);
                }

                await _context.BulkSaveChangesAsync();
            }

            return Ok();
        }

        [HttpGet("~/players/rankrefresh")]
        [Authorize]
        public async Task<ActionResult> RefreshRanks()
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
            var ranked = await _context.Players
                .Where(p => p.Pp > 0 && !p.Banned)
                .AsNoTracking()
                .OrderByDescending(t => t.Pp)
                .Select(p => new Player { Id = p.Id, Country = p.Country })
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

        [HttpGet("~/players/allcontextspprefresh")]
        [Authorize]
        public async Task<ActionResult> allcontextspprefresh()
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            await PlayerRefreshControllerHelper.RefreshAllContextsPp(_context);

            return Ok();
        }
    }
}
