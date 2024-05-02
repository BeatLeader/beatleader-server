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
            await _context.RecalculatePP(player);
            await _context.SaveChangesAsync();

            if (refreshRank)
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = await _context.Players
                    .Where(p => p.Pp > 0 && !p.Banned)
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new { p.Id, p.Country, p.Rank })
                    .ToListAsync();
                foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
                {
                    Player? p = new Player { Id = pp.Id, Country = pp.Country, Rank = pp.Rank };
                    try {
                        _context.Players.Attach(p);
                    } catch { }

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
        private async Task CalculateBatch(List<IGrouping<string, ScoreSelection>> groups, Dictionary<int, float> weights)
        {
            using (var anotherContext = _dbFactory.CreateDbContext()) {
                anotherContext.ChangeTracker.AutoDetectChangesEnabled = false;
                foreach (var group in groups)
                {
                    try {
                        Player player = new Player { Id = group.Key };
                        try {
                            anotherContext.Players.Attach(player);
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
                                var score = new Score() { Id = s.Id, Weight = weight };
                                try {
                                    anotherContext.Scores.Attach(score);
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

            _context.ChangeTracker.AutoDetectChangesEnabled = false;

            var weights = new Dictionary<int, float>();
            for (int i = 0; i < 10000; i++)
            {
                weights[i] = MathF.Pow(0.965f, i);
            }

            var scores = await _context
                .Scores
                .Where(s => s.Pp != 0 && !s.Banned && !s.Qualification && s.ValidContexts.HasFlag(LeaderboardContexts.General))
                .Select(s => new ScoreSelection { 
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
            var tasks = new List<Task>();
            for (int i = 0; i < scoreGroups.Count; i += 5000)
            {
                tasks.Add(CalculateBatch(scoreGroups.Skip(i).Take(5000).ToList(), weights));
            }
            Task.WaitAll(tasks.ToArray());
            

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context
                .Players
                .Where(p => p.Pp > 0 && !p.Banned)
                .OrderByDescending(t => t.Pp)
                .ToListAsync();
            foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
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
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await _context.Players
                .Where(p => p.Pp > 0 && !p.Banned)
                .OrderByDescending(t => t.Pp)
                .Select(p => new { Id = p.Id, Country = p.Country })
                .ToListAsync();
            foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
            {
                Player? p = new Player { Id = pp.Id, Country = pp.Country };
                try {
                    _context.Players.Attach(p);
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
    }
}
