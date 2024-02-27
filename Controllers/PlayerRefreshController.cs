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

        public struct SubScore
        {
            public string PlayerId;
            public string Platform;
            public HMD Hmd;
            public int ModifiedScore ;
            public float Accuracy;
            public float Pp;
            public float BonusPp;
            public float PassPP;
            public float AccPP;
            public float TechPP;
            public int Rank;
            public int Timeset;
            public float Weight;
            public bool Qualification;
            public int? MaxStreak;
            public float LeftTiming { get; set; }
            public float RightTiming { get; set; }
        }

        [NonAction]
        public async Task RefreshStats(
            PlayerScoreStats scoreStats, 
            string playerId, 
            int rank, 
            float? percentile,
            float? countryPercentile,
            List<SubScore>? scores = null)
        {
            var allScores = scores ??
                await _context.Scores.Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && (!s.Banned || s.Bot) && s.PlayerId == playerId && !s.IgnoreForStats).Select(s => new SubScore
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

            List<SubScore> rankedScores = new();
            List<SubScore> unrankedScores = new();

            if (allScores.Count() > 0) {

                rankedScores = allScores.Where(s => s.Pp != 0 && !s.Qualification).ToList();
                unrankedScores = allScores.Where(s => s.Pp == 0 || s.Qualification).ToList();

                var lastScores = allScores.OrderByDescending(s => s.Timeset).Take(50).ToList();
                Dictionary<string, int> platforms = new Dictionary<string, int>();
                Dictionary<HMD, int> hmds = new Dictionary<HMD, int>();
                foreach (var s in lastScores)
                {
                    string? platform = s.Platform.Split(",").FirstOrDefault();
                    if (platform != null) {
                        if (!platforms.ContainsKey(platform))
                        {
                            platforms[platform] = 1;
                        }
                        else
                        {

                            platforms[platform]++;
                        }
                    }

                    if (!hmds.ContainsKey(s.Hmd))
                    {
                        hmds[s.Hmd] = 1;
                    }
                    else
                    {

                        hmds[s.Hmd]++;
                    }
                }

                scoreStats.TopPlatform = platforms.MaxBy(s => s.Value).Key;
                scoreStats.TopHMD = hmds.MaxBy(s => s.Value).Key;

                if (rank < scoreStats.PeakRank || scoreStats.PeakRank == 0) {
                    scoreStats.PeakRank = rank;
                }
            }

            int allScoresCount = allScores.Count();
            int unrankedScoresCount = unrankedScores.Count();
            int rankedScoresCount = rankedScores.Count();

            scoreStats.TotalPlayCount = allScoresCount;
            scoreStats.UnrankedPlayCount = unrankedScoresCount;
            scoreStats.RankedPlayCount = rankedScoresCount;

            if (scoreStats.TotalPlayCount > 0)
            {
                int middle = (int)MathF.Round(allScoresCount / 2f);
                scoreStats.TotalScore = allScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                scoreStats.TopAccuracy = allScores.Max(s => s.Accuracy);
                if (allScoresCount % 2 == 1) {
                    scoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).ElementAt(middle).Accuracy;
                } else {
                    scoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).Skip(middle - 1).Take(2).Average(s => s.Accuracy);
                }
                scoreStats.AverageRank = allScores.Average(s => (float)s.Rank);
                scoreStats.LastScoreTime = allScores.MaxBy(s => s.Timeset).Timeset;

                scoreStats.MaxStreak = allScores.Max(s => s.MaxStreak) ?? 0;
                scoreStats.AverageLeftTiming = allScores.Average(s => s.LeftTiming);
                scoreStats.AverageRightTiming = allScores.Average(s => s.RightTiming);
                scoreStats.Top1Count = allScores.Where(s => s.Rank == 1).Count();
                scoreStats.Top1Score = allScores.Select(s => ReplayUtils.ScoreForRank(s.Rank)).Sum();
            } else {
                scoreStats.TotalScore = 0;
                scoreStats.AverageAccuracy = 0;
                scoreStats.TopAccuracy = 0;
                scoreStats.MedianAccuracy = 0;
                scoreStats.AverageRank = 0;
                scoreStats.LastScoreTime = 0;
            }

            if (scoreStats.UnrankedPlayCount > 0)
            {
                scoreStats.TotalUnrankedScore = unrankedScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageUnrankedAccuracy = unrankedScores.Average(s => s.Accuracy);
                scoreStats.TopUnrankedAccuracy = unrankedScores.Max(s => s.Accuracy);
                scoreStats.AverageUnrankedRank = unrankedScores.Average(s => (float)s.Rank);
                scoreStats.LastUnrankedScoreTime = unrankedScores.MaxBy(s => s.Timeset).Timeset;
                scoreStats.UnrankedMaxStreak = unrankedScores.Max(s => s.MaxStreak) ?? 0;
                scoreStats.UnrankedTop1Count = unrankedScores.Where(s => s.Rank == 1).Count();
                scoreStats.UnrankedTop1Score = unrankedScores.Select(s => ReplayUtils.ScoreForRank(s.Rank)).Sum();
            } else {
                scoreStats.TotalUnrankedScore = 0;
                scoreStats.AverageUnrankedAccuracy = 0;
                scoreStats.TopUnrankedAccuracy = 0;
                scoreStats.AverageUnrankedRank = 0;
                scoreStats.LastUnrankedScoreTime = 0;
            }

            if (scoreStats.RankedPlayCount > 0)
            {
                int middle = (int)MathF.Round(rankedScoresCount / 2f);
                scoreStats.TotalRankedScore = rankedScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageRankedAccuracy = rankedScores.Average(s => s.Accuracy);


                var scoresForWeightedAcc = rankedScores.OrderByDescending(s => s.Accuracy).Take(100).ToList();
                var sum = 0.0f;
                var weights = 0.0f;

                for (int i = 0; i < 100; i++)
                {
                    float weight = MathF.Pow(0.95f, i);
                    if (i < scoresForWeightedAcc.Count) {
                        sum += scoresForWeightedAcc[i].Accuracy * weight;
                    }

                    weights += weight;
                }

                scoreStats.AverageWeightedRankedAccuracy = sum / weights;
                var scoresForWeightedRank = rankedScores.OrderBy(s => s.Rank).Take(100).ToList();
                sum = 0.0f;
                weights = 0.0f;

                for (int i = 0; i < 100; i++)
                {
                    float weight = MathF.Pow(1.05f, i);
                    if (i < scoresForWeightedRank.Count)
                    {
                        sum += scoresForWeightedRank[i].Rank * weight;
                    } else {
                        sum += i * 10 * weight;
                    }

                    weights += weight;
                }
                scoreStats.AverageWeightedRankedRank = sum / weights;

                if (rankedScoresCount % 2 == 1) {
                    scoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).ElementAt(middle).Accuracy;
                } else {
                    scoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).Skip(middle - 1).Take(2).Average(s => s.Accuracy);
                }

                scoreStats.TopRankedAccuracy = rankedScores.Max(s => s.Accuracy);
                scoreStats.TopPp = rankedScores.Max(s => s.Pp);
                scoreStats.TopBonusPP = rankedScores.Max(s => s.BonusPp);
                scoreStats.TopPassPP = rankedScores.Max(s => s.PassPP);
                scoreStats.TopAccPP = rankedScores.Max(s => s.AccPP);
                scoreStats.TopTechPP = rankedScores.Max(s => s.TechPP);
                scoreStats.AverageRankedRank = rankedScores.Average(s => (float)s.Rank);
                scoreStats.LastRankedScoreTime = rankedScores.MaxBy(s => s.Timeset).Timeset;
                scoreStats.RankedMaxStreak = rankedScores.Max(s => s.MaxStreak) ?? 0;
                scoreStats.RankedTop1Count = rankedScores.Where(s => s.Rank == 1).Count();
                scoreStats.RankedTop1Score = rankedScores.Select(s => ReplayUtils.ScoreForRank(s.Rank)).Sum();

                scoreStats.SSPPlays = rankedScores.Count(s => s.Accuracy > 0.95);
                scoreStats.SSPlays = rankedScores.Count(s => 0.9 < s.Accuracy && s.Accuracy < 0.95);
                scoreStats.SPPlays = rankedScores.Count(s => 0.85 < s.Accuracy && s.Accuracy < 0.9);
                scoreStats.SPlays = rankedScores.Count(s => 0.8 < s.Accuracy && s.Accuracy < 0.85);
                scoreStats.APlays = rankedScores.Count(s => s.Accuracy < 0.8);
            } else {
                scoreStats.TotalRankedScore = 0;
                scoreStats.AverageRankedAccuracy = 0;
                scoreStats.AverageWeightedRankedAccuracy = 0;
                scoreStats.AverageWeightedRankedRank = 0;
                scoreStats.MedianRankedAccuracy = 0;
                scoreStats.TopRankedAccuracy = 0;
                scoreStats.TopPp = 0;
                scoreStats.TopBonusPP = 0;
                scoreStats.TopPassPP = 0;
                scoreStats.TopAccPP = 0;
                scoreStats.TopTechPP = 0;
                scoreStats.AverageRankedRank = 0;
                scoreStats.LastRankedScoreTime = 0;
                scoreStats.RankedTop1Count = 0;
                scoreStats.RankedTop1Score = 0;
                
                scoreStats.SSPPlays = 0;
                scoreStats.SSPlays = 0;
                scoreStats.SPPlays = 0;
                scoreStats.SPlays = 0;
                scoreStats.APlays = 0;
            }

            if (percentile != null) {
                scoreStats.TopPercentile = (float)percentile;
            }
            if (countryPercentile != null) {
                scoreStats.CountryTopPercentile = (float)countryPercentile;
            }
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
                await RefreshStats(player.ScoreStats, player.Id, player.Rank, null, null);
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
                await RefreshStats(
                    player.ScoreStats, 
                    player.Id, 
                    player.Rank, 
                    player.Pp > 0 ? player.Rank / (float)playerCount : 0,
                    player.Pp > 0 ? player.CountryRank / (float)countryCounts[player.Country] : 0,
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
                    await RefreshStats(
                        player.ScoreStats, 
                        player.Id, 
                        player.Rank,
                        null,
                        null);
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
