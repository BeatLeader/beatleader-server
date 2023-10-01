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
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerContextRefreshController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
        }

        [NonAction]
        public async Task RefreshPlayer(Player player, LeaderboardContexts context, bool refreshRank = true, bool refreshStats = true) {
            _context.RecalculatePPAndRankFastContext(context, player);
            await _context.BulkSaveChangesAsync();

            if (refreshRank)
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = _context.PlayerContextExtensions
                    .Where(p => p.Pp > 0 && p.Context == context)
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new { Id = p.Id, Country = p.Country })
                    .ToList();
                foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
                {
                    PlayerContextExtension? p = _context.PlayerContextExtensions.Local.FirstOrDefault(ls => ls.Id == pp.Id);
                    if (p == null) {
                        try {
                            p = new PlayerContextExtension { Id = pp.Id, Country = pp.Country };
                            _context.PlayerContextExtensions.Attach(p);
                        } catch (Exception e) {
                            continue;
                        }
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
                await RefreshStats(player.ContextExtensions.First(ce => ce.Context == context).ScoreStats, player.Id, context);
            }

            await _context.SaveChangesAsync();
        }

        [HttpGet("~/player/{id}/refresh/{context}")]
        public async Task<ActionResult> RefreshPlayerContext(string id, LeaderboardContexts context, [FromQuery] bool refreshRank = true)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _readContext.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Player? player = _context
                .Players
                .Where(p => p.Id == id)
                .Include(p => p.ScoreStats)
                .Include(p => p.ContextExtensions)
                .ThenInclude(ce => ce.ScoreStats)
                .FirstOrDefault();
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
            Player? currentPlayer = await _readContext.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            await RefreshPlayerContext(id, LeaderboardContexts.NoMods, refreshRank);
            await RefreshPlayerContext(id, LeaderboardContexts.NoPause, refreshRank);
            await RefreshPlayerContext(id, LeaderboardContexts.Golf, refreshRank);

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

            await RefreshPlayersContext(LeaderboardContexts.NoMods);
            await RefreshPlayersContext(LeaderboardContexts.NoPause);
            await RefreshPlayersContext(LeaderboardContexts.Golf);

            return Ok();
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

            var scores = _context
                .ScoreContextExtensions
                .Where(s => s.Pp != 0 && s.Context == context && s.ScoreId != null && !s.Banned)
                .OrderByDescending(s => s.Pp)
                .Select(s => new { s.Id, s.Accuracy, s.Rank, s.Pp, s.AccPP, s.TechPP, s.PassPP, s.Weight, s.PlayerId })
                .ToList();

            var query = _context.PlayerContextExtensions
                .Where(ce => ce.Context == context && !ce.Banned)
                .OrderByDescending(p => p.Pp)
                .Select(p => new { p.Id, p.PlayerId, p.Country })
                .ToList();

            var allPlayers = new List<PlayerContextExtension>();
            foreach (var p in query) {

            
            //await query.ParallelForEachAsync(async p => {
                //try {
                    var player = new PlayerContextExtension { Id = p.Id, PlayerId = p.PlayerId, Country = p.Country };
                    _context.PlayerContextExtensions.Attach(player);
                    allPlayers.Add(player);

                //    if (allPlayers.Count(p => p.PlayerId == p.PlayerId) > 1) {
                //    int x = 4353;
                //}

                    float resultPP = 0f;
                    float accPP = 0f;
                    float techPP = 0f;
                    float passPP = 0f;
                    var playerScores = scores.Where(s => s.PlayerId == p.PlayerId).ToList();
                    foreach ((int i, var s) in playerScores.Select((value, i) => (i, value)))
                    {
                        float weight = weights[i];
                        if (s.Weight != weight)
                        {
                            var score = new ScoreContextExtension() { Id = s.Id, Weight = weight };
                            _context.ScoreContextExtensions.Attach(score);
                            _context.Entry(score).Property(x => x.Weight).IsModified = true;
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

                    _context.Entry(player).Property(x => x.Pp).IsModified = true;
                    _context.Entry(player).Property(x => x.AccPp).IsModified = true;
                    _context.Entry(player).Property(x => x.TechPp).IsModified = true;
                    _context.Entry(player).Property(x => x.PassPp).IsModified = true;
                //} catch (Exception e) {
                //}
                //}, maxDegreeOfParallelism: 50);
        }

        await _context.BulkSaveChangesAsync();

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = allPlayers
                .Where(p => p.Pp > 0)
                .OrderByDescending(t => t.Pp)
                .ToList();
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
        public async Task RefreshStats(PlayerScoreStats scoreStats, string playerId, LeaderboardContexts context, List<SubScore>? scores = null)
        {
            var allScores = scores ??
                _context
                .ScoreContextExtensions
                .Where(s => s.PlayerId == playerId && s.Context == context && !s.Score.IgnoreForStats)
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
                }).ToList();

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
        }

        [HttpGet("~/players/stats/refresh/allContexts")]
        public async Task<ActionResult> RefreshPlayersStatsAllContexts()
        {
            await RefreshPlayersStats(LeaderboardContexts.NoMods);
            await RefreshPlayersStats(LeaderboardContexts.NoPause);
            await RefreshPlayersStats(LeaderboardContexts.Golf);

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
                _context.ScoreContextExtensions.Where(s => s.Context == context && (!s.Score.Banned || s.Score.Bot) && !s.Score.IgnoreForStats).Select(s => new SubScore
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
                }).ToList();

            var players = _context
                    .PlayerContextExtensions
                    .Where(p => p.Context == context && p.ScoreStats != null)
                    .OrderBy(p => p.Rank)
                    .Select(p => new { p.PlayerId, p.ScoreStats })
                    .ToList();

            var scoresById = allScores.GroupBy(s => s.PlayerId).ToDictionary(g => g.Key, g => g.ToList());

            var playersWithScores = players.Select(p => new { Id = p.PlayerId, p.ScoreStats, Scores = scoresById.ContainsKey(p.PlayerId) ? scoresById[p.PlayerId] : new List<SubScore>{ } }).ToList();
            
            await playersWithScores.ParallelForEachAsync(async player => {
                await RefreshStats(player.ScoreStats, player.Id, context, player.Scores);
            }, maxDegreeOfParallelism: 50);

            await _context.BulkSaveChangesAsync();

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
            var ranked = _context.PlayerContextExtensions
                .Where(p => !p.Banned && p.Context == context)
                .OrderByDescending(t => t.Pp)
                .Select(p => new { Id = p.Id, Country = p.Country })
                .ToList();
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

            await RefreshRanks(LeaderboardContexts.NoMods);
            await RefreshRanks(LeaderboardContexts.NoPause);
            await RefreshRanks(LeaderboardContexts.Golf);

            return Ok();
        }
    }
}
