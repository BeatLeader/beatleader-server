using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
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
            IOptions<AzureStorageConfig> config,
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
        }

        [NonAction]
        public async Task RefreshStats(Player player, List<SubScore>? scores = null)
        {
            if (player.ScoreStats == null)
            {
                player.ScoreStats = new PlayerScoreStats();
                _context.Stats.Add(player.ScoreStats);
            }
            var allScores = scores ??
                _context.Scores.Where(s => s.PlayerId == player.Id).Select(s => new SubScore
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
                    Qualification = s.Qualification
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

                player.ScoreStats.TopPlatform = platforms.OrderByDescending(s => s.Value).First().Key;
                player.ScoreStats.TopHMD = hmds.OrderByDescending(s => s.Value).First().Key;
            }

            player.ScoreStats.TotalPlayCount = allScores.Count();
            player.ScoreStats.UnrankedPlayCount = unrankedScores.Count();
            player.ScoreStats.RankedPlayCount = rankedScores.Count();

            if (player.ScoreStats.TotalPlayCount > 0)
            {
                int count = allScores.Count() / 2;
                player.ScoreStats.TotalScore = allScores.Sum(s => (long)s.ModifiedScore);
                player.ScoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                player.ScoreStats.TopAccuracy = allScores.Max(s => s.Accuracy);
                player.ScoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                player.ScoreStats.AverageRank = allScores.Average(s => (float)s.Rank);
                player.ScoreStats.LastScoreTime = allScores.OrderByDescending(s => s.Timeset).First().Timeset;
            } else {
                player.ScoreStats.TotalScore = 0;
                player.ScoreStats.AverageAccuracy = 0;
                player.ScoreStats.TopAccuracy = 0;
                player.ScoreStats.MedianAccuracy = 0;
                player.ScoreStats.AverageRank = 0;
                player.ScoreStats.LastScoreTime = 0;
            }

            if (player.ScoreStats.UnrankedPlayCount > 0)
            {
                int count = unrankedScores.Count() / 2;
                player.ScoreStats.TotalUnrankedScore = unrankedScores.Sum(s => (long)s.ModifiedScore);
                player.ScoreStats.AverageUnrankedAccuracy = unrankedScores.Average(s => s.Accuracy);
                player.ScoreStats.TopUnrankedAccuracy = unrankedScores.Max(s => s.Accuracy);
                player.ScoreStats.AverageUnrankedRank = unrankedScores.Average(s => (float)s.Rank);
                player.ScoreStats.LastUnrankedScoreTime = unrankedScores.OrderByDescending(s => s.Timeset).First().Timeset;
            } else {
                player.ScoreStats.TotalUnrankedScore = 0;
                player.ScoreStats.AverageUnrankedAccuracy = 0;
                player.ScoreStats.TopUnrankedAccuracy = 0;
                player.ScoreStats.AverageUnrankedRank = 0;
                player.ScoreStats.LastUnrankedScoreTime = 0;
            }

            if (player.ScoreStats.RankedPlayCount > 0)
            {
                int count = rankedScores.Count() / 2;
                player.ScoreStats.TotalRankedScore = rankedScores.Sum(s => (long)s.ModifiedScore);
                player.ScoreStats.AverageRankedAccuracy = rankedScores.Average(s => s.Accuracy);


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

                player.ScoreStats.AverageWeightedRankedAccuracy = sum / weights;
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
                player.ScoreStats.AverageWeightedRankedRank = sum / weights;

                player.ScoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                player.ScoreStats.TopRankedAccuracy = rankedScores.Max(s => s.Accuracy);
                player.ScoreStats.TopPp = rankedScores.Max(s => s.Pp);
                player.ScoreStats.TopBonusPP = rankedScores.Max(s => s.BonusPp);
                player.ScoreStats.AverageRankedRank = rankedScores.Average(s => (float)s.Rank);
                player.ScoreStats.LastRankedScoreTime = rankedScores.OrderByDescending(s => s.Timeset).First().Timeset;

                player.ScoreStats.SSPPlays = rankedScores.Where(s => s.Accuracy > 0.95).Count();
                player.ScoreStats.SSPlays = rankedScores.Where(s => 0.9 < s.Accuracy && s.Accuracy < 0.95).Count();
                player.ScoreStats.SPPlays = rankedScores.Where(s => 0.85 < s.Accuracy && s.Accuracy < 0.9).Count();
                player.ScoreStats.SPlays = rankedScores.Where(s => 0.8 < s.Accuracy && s.Accuracy < 0.85).Count();
                player.ScoreStats.APlays = rankedScores.Where(s => s.Accuracy < 0.8).Count();
            } else {
                player.ScoreStats.TotalRankedScore = 0;
                player.ScoreStats.AverageRankedAccuracy = 0;
                player.ScoreStats.AverageWeightedRankedAccuracy = 0;
                player.ScoreStats.AverageWeightedRankedRank = 0;
                player.ScoreStats.MedianRankedAccuracy = 0;
                player.ScoreStats.TopRankedAccuracy = 0;
                player.ScoreStats.TopPp = 0;
                player.ScoreStats.TopBonusPP = 0;
                player.ScoreStats.AverageRankedRank = 0;
                player.ScoreStats.LastRankedScoreTime = 0;

                player.ScoreStats.SSPPlays = 0;
                player.ScoreStats.SSPlays = 0;
                player.ScoreStats.SPPlays = 0;
                player.ScoreStats.SPlays = 0;
                player.ScoreStats.APlays = 0;
            }
        }

        [NonAction]
        public async Task RefreshPlayer(Player player, bool refreshRank = true) {
            _context.RecalculatePP(player);

            if (refreshRank)
            {
                var ranked = _context.Players.OrderByDescending(t => t.Pp).ToList();
                var country = player.Country; var countryRank = 1;
                foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
                {
                    p.Rank = i + 1;
                    if (p.Country == country)
                    {
                        p.CountryRank = countryRank;
                        countryRank++;
                    }
                }
            }
            await RefreshStats(player);

            _context.SaveChanges();
        }

        [HttpGet("~/player/{id}/refresh")]
        public async Task<ActionResult> RefreshPlayerAction(string id, [FromQuery] bool refreshRank = true)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
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
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Leaderboard? leaderboard = _context.Leaderboards.Where(p => p.Id == id).Include(l => l.Scores).ThenInclude(s => s.Player).ThenInclude(s => s.ScoreStats).FirstOrDefault();

            if (leaderboard == null)
            {
                return NotFound();
            }

            foreach (var score in leaderboard.Scores)
            {
                await RefreshPlayer(score.Player, true);
            }

            return Ok();
        }

        [HttpGet("~/players/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshPlayers()
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = _context.Players.Find(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var playersCount = _context.Players.Where(p => !p.Banned).Count();
            for (int i = 0; i < playersCount; i += 2000)
            {
                var players = _context
                    .Players
                    .Skip(i)
                    .Take(2000)
                    .Include(p => p.ScoreStats)
                    .Where(s => s.Pp != 0)
                    .ToList();
                var transaction = _context.Database.BeginTransaction();

                foreach (Player p in players)
                {
                    _context.RecalculatePP(p);
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    _context.RejectChanges();
                    transaction.Rollback();
                    transaction = _context.Database.BeginTransaction();
                }
                transaction.Commit();
            }

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = _context
                .Players
                .Where(p => p.Pp > 0)
                .OrderByDescending(t => t.Pp)
                .ToList();
            foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;
            }
            await _context.SaveChangesAsync();

            return Ok();
        }


        [HttpGet("~/players/stats/refresh")]
        public async Task<ActionResult> RefreshPlayersStats()
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = _context.Players.Find(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            var playersCount = _context.Players.Where(p => !p.Banned).Count();
            for (int i = 0; i < playersCount; i += 2000)
            {
                var players = _context.Players.Where(p => !p.Banned).Include(p => p.ScoreStats).Skip(i).Take(2000).ToList();
                foreach (var player in players)
                {
                    await RefreshStats(player);
                }
                _context.SaveChanges();
            }
            
            return Ok();
        }

        [HttpGet("~/players/rankrefresh")]
        [Authorize]
        public async Task<ActionResult> RefreshRanks()
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = _context.Players.Find(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            Dictionary<string, int> countries = new Dictionary<string, int>();
            
            var ranked = _context.Players.Where(p => !p.Banned).OrderByDescending(t => t.Pp).ToList();
            foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;
            }
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
