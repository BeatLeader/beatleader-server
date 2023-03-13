using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Lib.AspNetCore.ServerTiming;
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
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerRefreshController(
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

        [HttpGet("~/players/steam/refresh")]
        public async Task<ActionResult> RefreshSteamPlayers()
        {
            var players = _context.Players.ToList();
            foreach (Player p in players)
            {
                if (Int64.Parse(p.Id) <= 70000000000000000) { continue; }
                Player? update = await PlayerUtils.GetPlayerFromSteam(p.Id, _configuration.GetValue<string>("SteamKey"));

                if (update != null)
                {
                    p.ExternalProfileUrl = update.ExternalProfileUrl;

                    if (p.Avatar.Contains("steamcdn"))
                    {
                        p.Avatar = update.Avatar;
                    }

                    if (p.Country == "not set" && update.Country != "not set")
                    {
                        p.Country = update.Country;
                    }
                }
            }

            await _context.SaveChangesAsync();

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
            public int Rank;
            public int Timeset;
            public float Weight;
            public bool Qualification;
            public int MaxStreak;
            public float LeftTiming { get; set; }
            public float RightTiming { get; set; }
        }

        [NonAction]
        public async Task RefreshStats(PlayerScoreStats scoreStats, string playerId, List<SubScore>? scores = null)
        {
            var allScores = scores ??
                _context.Scores.Where(s => s.PlayerId == playerId && !s.IgnoreForStats).Select(s => new SubScore
                {
                    PlayerId = s.PlayerId,
                    Platform = s.Platform,
                    Hmd = s.Hmd,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    BonusPp = s.BonusPp,
                    Rank = s.Rank,
                    Timeset = s.Timepost,
                    Weight = s.Weight,
                    Qualification = s.Qualification,
                    MaxStreak = s.MaxStreak,
                    RightTiming = s.RightTiming,
                    LeftTiming = s.LeftTiming,
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

            scoreStats.TotalPlayCount = allScores.Count();
            scoreStats.UnrankedPlayCount = unrankedScores.Count();
            scoreStats.RankedPlayCount = rankedScores.Count();

            if (scoreStats.TotalPlayCount > 0)
            {
                int count = allScores.Count() / 2;
                scoreStats.TotalScore = allScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                scoreStats.TopAccuracy = allScores.Max(s => s.Accuracy);
                scoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                scoreStats.AverageRank = allScores.Average(s => (float)s.Rank);
                scoreStats.LastScoreTime = allScores.MaxBy(s => s.Timeset).Timeset;

                scoreStats.MaxStreak = allScores.Max(s => s.MaxStreak);
                scoreStats.AverageLeftTiming = allScores.Average(s => s.LeftTiming);
                scoreStats.AverageRightTiming = allScores.Average(s => s.RightTiming);
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
                int count = unrankedScores.Count() / 2;
                scoreStats.TotalUnrankedScore = unrankedScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageUnrankedAccuracy = unrankedScores.Average(s => s.Accuracy);
                scoreStats.TopUnrankedAccuracy = unrankedScores.Max(s => s.Accuracy);
                scoreStats.AverageUnrankedRank = unrankedScores.Average(s => (float)s.Rank);
                scoreStats.LastUnrankedScoreTime = unrankedScores.MaxBy(s => s.Timeset).Timeset;
                scoreStats.UnrankedMaxStreak = unrankedScores.Max(s => s.MaxStreak);
            } else {
                scoreStats.TotalUnrankedScore = 0;
                scoreStats.AverageUnrankedAccuracy = 0;
                scoreStats.TopUnrankedAccuracy = 0;
                scoreStats.AverageUnrankedRank = 0;
                scoreStats.LastUnrankedScoreTime = 0;
            }

            if (scoreStats.RankedPlayCount > 0)
            {
                int count = rankedScores.Count() / 2;
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

                scoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                scoreStats.TopRankedAccuracy = rankedScores.Max(s => s.Accuracy);
                scoreStats.TopPp = rankedScores.Max(s => s.Pp);
                scoreStats.TopBonusPP = rankedScores.Max(s => s.BonusPp);
                scoreStats.AverageRankedRank = rankedScores.Average(s => (float)s.Rank);
                scoreStats.LastRankedScoreTime = rankedScores.MaxBy(s => s.Timeset).Timeset;
                scoreStats.RankedMaxStreak = rankedScores.Max(s => s.MaxStreak);

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
                scoreStats.AverageRankedRank = 0;
                scoreStats.LastRankedScoreTime = 0;
                
                scoreStats.SSPPlays = 0;
                scoreStats.SSPlays = 0;
                scoreStats.SPPlays = 0;
                scoreStats.SPlays = 0;
                scoreStats.APlays = 0;
            }
        }

        [NonAction]
        public async Task RefreshPlayer(Player player, bool refreshRank = true, bool refreshStats = true) {
            _context.RecalculatePP(player);
            await _context.SaveChangesAsync();

            if (refreshRank)
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = _context.Players
                    .Where(p => p.Pp > 0 && !p.Banned)
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new { Id = p.Id, Country = p.Country })
                    .ToList();
                foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
                {
                    Player? p = _context.Players.Local.FirstOrDefault(ls => ls.Id == pp.Id);
                    if (p == null) {
                        try {
                            p = new Player { Id = pp.Id, Country = pp.Country };
                            _context.Players.Attach(p);
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
                await RefreshStats(player.ScoreStats, player.Id);
            }

            await _context.SaveChangesAsync();
        }

        [HttpGet("~/player/{id}/refresh")]
        public async Task<ActionResult> RefreshPlayerAction(string id, [FromQuery] bool refreshRank = true)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _readContext.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Player? player = _context.Players.Where(p => p.Id == id).Include(p => p.ScoreStats).FirstOrDefault();
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
            Leaderboard? leaderboard = _context.Leaderboards.Where(p => p.Id == id).Include(l => l.Scores).ThenInclude(s => s.Player).ThenInclude(s => s.ScoreStats).FirstOrDefault();

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

        [HttpGet("~/players/refresh")]
        //[Authorize]
        public async Task<ActionResult> RefreshPlayers()
        {
            //if (HttpContext != null)
            //{
            //    string currentId = HttpContext.CurrentUserID(_context);
            //    Player? currentPlayer = await _context.Players.FindAsync(currentId);
            //    if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            //    {
            //        return Unauthorized();
            //    }
            //}

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            var weights = new Dictionary<int, float>();
            for (int i = 0; i < 5000; i++)
            {
                weights[i] = MathF.Pow(0.965f, i);
            }

            var scores = _context
                .Scores
                .Where(s => s.Pp != 0 && !s.Banned && !s.Qualification)
                .OrderByDescending(s => s.Pp)
                .Select(s => new { s.Id, s.Accuracy, s.Rank, s.Pp, s.AccPP, s.TechPP, s.PassPP, s.Weight, s.PlayerId })
                .ToList();

            var query = _context.Players
                .OrderByDescending(p => p.Pp)
                .Where(p => p.Pp != 0 && !p.Banned)
                .Select(p => new { Id = p.Id, Country = p.Country });

            var allPlayers = new List<Player>();
            await query.ParallelForEachAsync(async p => {
                try {
                    Player player = new Player { Id = p.Id, Country = p.Country };
                    _context.Players.Attach(player);
                    allPlayers.Add(player);

                    float resultPP = 0f;
                    float accPP = 0f;
                    float techPP = 0f;
                    float passPP = 0f;
                    var playerScores = scores.Where(s => s.PlayerId == p.Id).ToList();
                    foreach ((int i, var s) in playerScores.Select((value, i) => (i, value)))
                    {
                        float weight = weights[i];
                        if (s.Weight != weight)
                        {
                            var score = new Score() { Id = s.Id, Weight = weight };
                            _context.Scores.Attach(score);
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
                } catch (Exception e) {
                }
            }, maxDegreeOfParallelism: 50);

            await _context.BulkSaveChangesAsync();

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = allPlayers
                .Where(p => p.Pp > 0)
                .OrderByDescending(t => t.Pp)
                .ToList();
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
            //if (HttpContext != null) {
            //    string currentId = HttpContext.CurrentUserID(_context);
            //    Player? currentPlayer = await _context.Players.FindAsync(currentId);
            //    if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            //    {
            //        return Unauthorized();
            //    }
            //}
            var allScores =
                _context.Scores.Where(s => !s.Banned && !s.IgnoreForStats).Select(s => new SubScore
                {
                    PlayerId = s.PlayerId,
                    Platform = s.Platform,
                    Hmd = s.Hmd,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    BonusPp = s.BonusPp,
                    Rank = s.Rank,
                    Timeset = s.Timepost,
                    Weight = s.Weight,
                    Qualification = s.Qualification,
                    MaxStreak = s.MaxStreak,
                    RightTiming = s.RightTiming,
                    LeftTiming = s.LeftTiming,
                }).ToList();

            var players = _context
                    .Players
                    .Where(p => !p.Banned && p.ScoreStats != null)
                    .OrderBy(p => p.Rank)
                    .Select(p => new { Id = p.Id, ScoreStats = p.ScoreStats })
                    .ToList();

            var scoresById = allScores.GroupBy(s => s.PlayerId).ToDictionary(g => g.Key, g => g.ToList());

            var playersWithScores = players.Where(p => scoresById.ContainsKey(p.Id)).Select(p => new { Id = p.Id, ScoreStats = p.ScoreStats, Scores = scoresById[p.Id] }).ToList();
            
            await playersWithScores.ParallelForEachAsync(async player => {
                await RefreshStats(player.ScoreStats, player.Id, player.Scores);
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
            var ranked = _context.Players
                .Where(p => p.Pp > 0 && !p.Banned)
                .OrderByDescending(t => t.Pp)
                .Select(p => new { Id = p.Id, Country = p.Country })
                .ToList();
            foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
            {
                Player? p = _context.Players.Local.FirstOrDefault(ls => ls.Id == pp.Id);
                if (p == null) {
                    try {
                        p = new Player { Id = pp.Id, Country = pp.Country };
                        _context.Players.Attach(p);
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

            return Ok();
        }
    }
}
