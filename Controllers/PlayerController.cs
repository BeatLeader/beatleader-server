using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class PlayerController : Controller
    {
        private readonly AppContext _context;
        private readonly IConfiguration _configuration;

        private readonly IServerTiming _serverTiming;

        public PlayerController(AppContext context, IConfiguration configuration, IServerTiming serverTiming)
        {
            _context = context;
            _configuration = configuration;
            _serverTiming = serverTiming;
        }

        [HttpGet("~/player/{id}")]
        public async Task<ActionResult<Player>> Get(string id, bool stats = true)
        {
            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(id);
            } catch {}
            AccountLink? link = null;
            if (oculusId < 20000000000000000) {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
                }
            }
            string userId = (link != null ? link.SteamID : id);
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                if (stats) {
                    player = await _context.Players.Where(p => p.Id == userId).Include(p => p.ScoreStats).Include(p => p.Badges).Include(p => p.StatsHistory).Include(p => p.Clans).Include(p => p.PatreonFeatures).FirstOrDefaultAsync();
                } else {
                    player = await _context.Players.FindAsync(userId);
                }
                
            }
            if (player == null)
            {
                using (_serverTiming.TimeAction("lazy"))
                {
                    player = (await GetLazy(id, true)).Value;
                }

                return player;
            }
            return player;
        }

        [NonAction]
        public async Task<ActionResult<Player>> GetLazy(string id, bool addToBase = true)
        {
            Player? player = await _context.Players.Include(p => p.ScoreStats).FirstOrDefaultAsync(p => p.Id == id);

            if (player == null) {
                Int64 userId = Int64.Parse(id);
                if (userId > 70000000000000000) {
                    player = await PlayerUtils.GetPlayerFromSteam(id, _configuration.GetValue<string>("SteamKey"));
                    if (player == null) {
                        return NotFound();
                    }
                } else if (userId > 2000000000000000) {
                    player = await PlayerUtils.GetPlayerFromOculus(id, _configuration.GetValue<string>("OculusToken"));
                    if (player == null)
                    {
                        return NotFound();
                    }
                } else {
                    player = await GetPlayerFromBL(id);
                    if (player == null)
                    {
                        return NotFound();
                    }
                }
                player.Id = id;
                if (addToBase) {
                    _context.Players.Add(player);
                    await _context.SaveChangesAsync();
                }
            }

            return player;
        }

        [HttpDelete("~/player/{id}")]
        [Authorize]
        public async Task<ActionResult> DeletePlayer(string id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Player? player = await _context.Players.FindAsync(id);

            if (player == null)
            {
                return NotFound();
            }

            _context.Players.Remove(player);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/playerlink/{login}")]
        [Authorize]
        public async Task<ActionResult<AccountLink>> GetPlayerLink(string login)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (info == null)
            {
                return NotFound("No info");
            }
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == info.Id);
            if (link == null)
            {
                return NotFound("No link");
            }

            return link;
        }

        [HttpDelete("~/authinfo/{login}")]
        [Authorize]
        public async Task<ActionResult> DeleteAuthInfo(string login)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (info == null)
            {
                return NotFound("No info");
            }
            _context.Auths.Remove(info);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/authips/")]
        [Authorize]
        public async Task<ActionResult> DeleteAuthIps()
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var info = _context.AuthIPs.ToArray();
            foreach (var item in info)
            {
                _context.AuthIPs.Remove(item);
            }
            
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/playerlink/{id}")]
        [Authorize]
        public async Task<ActionResult> DeletePlayerLinked(string id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.SteamID == id);
            if (link == null)
            {
                return NotFound();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Id == link.OculusID);
            if (info == null)
            {
                return NotFound();
            }
            _context.AccountLinks.Remove(link);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/player/{id}/scores")]
        public async Task<ActionResult<ResponseWithMetadata<Score>>> GetScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] string order = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null)
        {
            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence"))
            {
                sequence = _context.Scores.Where(t => t.PlayerId == id);
                switch (sortBy)
                {
                    case "date":
                        sequence = sequence.Order(order, t => t.Timeset);
                        break;
                    case "pp":
                        sequence = sequence.Order(order, t => t.Pp);
                        break;
                    case "acc":
                        sequence = sequence.Order(order, t => t.Accuracy);
                        break;
                    case "pauses":
                        sequence = sequence.Order(order, t => t.Pauses);
                        break;
                    case "rank":
                        sequence = sequence.Order(order, t => t.Rank);
                        break;
                    case "stars":
                        sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Order(order, t => t.Leaderboard.Difficulty.Stars);
                        break;
                    default:
                        break;
                }
                if (search != null)
                {
                    string lowSearch = search.ToLower();
                    sequence = sequence
                        .Include(lb => lb.Leaderboard)
                        .ThenInclude(lb => lb.Song)
                        .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
                }
                if (diff != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Ranked : !p.Leaderboard.Difficulty.Ranked);
                }
                if (stars_from != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
                }
            }

            ResponseWithMetadata<Score> result; 
            using (_serverTiming.TimeAction("db"))
            {
                result = new ResponseWithMetadata<Score>()
                {
                    Metadata = new Metadata()
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = sequence.Count()
                    },
                    Data = await sequence
                            .Skip((page - 1) * count)
                            .Take(count)
                            .Include(lb => lb.Leaderboard)
                                .ThenInclude(lb => lb.Song)
                                .ThenInclude(lb => lb.Difficulties)
                            .Include(lb => lb.Leaderboard)
                                .ThenInclude(lb => lb.Difficulty)
                            .Include(sc => sc.ScoreImprovement)
                            .ToListAsync()
                };
            }
            return result;
        }

        [HttpDelete("~/player/{id}/score/{leaderboardID}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(string id, string leaderboardID)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Leaderboard? leaderboard = _context.Leaderboards.Where(l => l.Id == leaderboardID).Include(l => l.Scores).FirstOrDefault();
            if (leaderboard == null) {
                return NotFound();
            } 
            Score? scoreToDelete = leaderboard.Scores.Where(t => t.PlayerId == id).FirstOrDefault();

            if (scoreToDelete == null) {
                return NotFound();
            }

            _context.Scores.Remove(scoreToDelete);
            await _context.SaveChangesAsync();
            return Ok ();

        }

        [HttpGet("~/player/{id}/scorevalue/{hash}/{difficulty}/{mode}")]
        public ActionResult<int> GetScoreValue(string id, string hash, string difficulty, string mode)
        {
            Score? score = _context
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Where(s => s.PlayerId == id && s.Leaderboard.Song.Hash == hash && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode)
                .FirstOrDefault();

            if (score == null)
            {
                return NotFound();
            }

            return score.ModifiedScore;

        }

        public class GraphResponse {
            public string LeaderboardId { get; set; }
            public string Diff { get; set; }
            public string Mode { get; set; }
            public string Modifiers { get; set; }
            public string SongName { get; set; }
            public string Mapper { get; set; }
            public float Acc { get; set; }
            public string Timeset { get; set; }
            public float Stars { get; set; }
        }

        [HttpGet("~/player/{id}/accgraph")]
        public ActionResult<ICollection<GraphResponse>> GetScoreValue(string id)
        {
            return _context
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Where(s => s.PlayerId == id && s.Leaderboard.Difficulty.Ranked)
                .Select(s => new GraphResponse { 
                    LeaderboardId = s.Leaderboard.Id,
                    Diff = s.Leaderboard.Difficulty.DifficultyName,
                    SongName = s.Leaderboard.Song.Name,
                    Mapper = s.Leaderboard.Song.Author,
                    Mode = s.Leaderboard.Difficulty.ModeName,
                    Stars = (float)s.Leaderboard.Difficulty.Stars,
                    Acc = s.Accuracy,
                    Timeset = s.Timeset,
                    Modifiers = s.Modifiers
                    })
                .ToList();

        }

        [HttpGet("~/players")]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetPlayers(
            [FromQuery] string sortBy = "pp", 
            [FromQuery] int page = 1, 
            [FromQuery] int count = 50, 
            [FromQuery] string search = "", 
            [FromQuery] string countries = "")
        {
            IQueryable<Player> request = _context.Players.Include(p => p.ScoreStats).Include(p => p.Clans).Where(p => !p.Banned);
            if (countries.Length != 0)
            {
                request = request.Where(p => countries.Contains(p.Country));
            }
            if (search.Length != 0)
            {
                request = request.Where(p => p.Name.Contains(search));
            }
            switch (sortBy)
            {
                case "pp":
                    request = request.OrderByDescending(p => p.Pp);
                    break;
                case "dailyImprovements":
                    request = request.OrderByDescending(p => p.ScoreStats.DailyImprovements);
                    break;
                default:
                    break;
            }
            return new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = request.Count()
                },
                Data = request.Skip((page - 1) * count).Take(count).Select(ResponseWithStatsFromPlayer).ToList()
            };
        }

        private string GenerateListString(string current, int value) {
            var histories = current.Length == 0 ? new string[0] : current.Split(",");
            if (histories.Length == 50)
            {
                histories = histories.Skip(1).Take(49).ToArray();
            }
            histories = histories.Append(value.ToString()).ToArray();
            return string.Join(",", histories);
        }

        private string GenerateListString(string current, float value)
        {
            var histories = current.Length == 0 ? new string[0] : current.Split(",");
            if (histories.Length == 50)
            {
                histories = histories.Skip(1).Take(49).ToArray();
            }
            histories = histories.Append(value.ToString("#.#####")).ToArray();
            return string.Join(",", histories);
        }

        [HttpGet("~/players/sethistories")]
        public async Task<ActionResult> SetHistories()
        {
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            CronTimestamps? cronTimestamps = _context.cronTimestamps.Find(1);
            if (cronTimestamps != null) {
                if ((timestamp - cronTimestamps.HistoriesTimestamp) < 60 * 60 * 24 - 5 * 60)
                {
                    return BadRequest("Allowed only at midnight");
                }
                else
                {
                    cronTimestamps.HistoriesTimestamp = timestamp;
                    _context.Update(cronTimestamps);
                }
            } else
            {
                cronTimestamps = new CronTimestamps();
                cronTimestamps.HistoriesTimestamp = timestamp;
                _context.Add(cronTimestamps);
            }
            var ranked = _context.Players.Where(p => !p.Banned).Include(p => p.ScoreStats).Include(p => p.StatsHistory).ToList();
            foreach (Player p in ranked)
            {
                p.Histories = GenerateListString(p.Histories, p.Rank);

                var stats = p.ScoreStats;
                var statsHistory = p.StatsHistory;
                if (statsHistory == null) {
                    statsHistory = new PlayerStatsHistory();
                }

                statsHistory.Pp = GenerateListString(statsHistory.Pp, p.Pp);
                statsHistory.Rank = p.Histories;
                statsHistory.CountryRank = GenerateListString(statsHistory.CountryRank, p.CountryRank);
                statsHistory.TotalScore = GenerateListString(statsHistory.TotalScore, stats.TotalScore);
                statsHistory.AverageRankedAccuracy = GenerateListString(statsHistory.AverageRankedAccuracy, stats.AverageRankedAccuracy);
                statsHistory.TopAccuracy = GenerateListString(statsHistory.TopAccuracy, stats.TopAccuracy);
                statsHistory.TopPp = GenerateListString(statsHistory.TopPp, stats.TopPp);
                statsHistory.AverageAccuracy = GenerateListString(statsHistory.AverageAccuracy, stats.AverageAccuracy);
                statsHistory.MedianAccuracy = GenerateListString(statsHistory.MedianAccuracy, stats.MedianAccuracy);
                statsHistory.MedianRankedAccuracy = GenerateListString(statsHistory.MedianRankedAccuracy, stats.MedianRankedAccuracy);
                statsHistory.TotalPlayCount = GenerateListString(statsHistory.TotalPlayCount, stats.TotalPlayCount);
                statsHistory.RankedPlayCount = GenerateListString(statsHistory.RankedPlayCount, stats.RankedPlayCount);
                statsHistory.ReplaysWatched = GenerateListString(statsHistory.ReplaysWatched, stats.ReplaysWatched);

                p.StatsHistory = statsHistory;
                stats.DailyImprovements = 0;
            }

            await _context.SaveChangesAsync();

            return Ok();
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
                    p.AllTime = update.AllTime;
                    p.LastTwoWeeksTime = update.LastTwoWeeksTime;

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
            public int Hmd;
            public int ModifiedScore ;
            public float Accuracy;
            public float Pp;
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
                await _context.Scores.Where(s => s.PlayerId == player.Id).Select(s => new SubScore
                {
                    Platform = s.Platform,
                    Hmd = s.Hmd,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                }).ToListAsync();

            if (allScores.Count() == 0) return;
            var rankedScores = allScores.Where(s => s.Pp != 0);
            player.ScoreStats.TotalPlayCount = allScores.Count();

            var lastScores = allScores.TakeLast(50);
            Dictionary<string, int> platforms = new Dictionary<string, int>();
            Dictionary<int, int> hmds = new Dictionary<int, int>();
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

            player.ScoreStats.RankedPlayCount = rankedScores.Count();
            if (player.ScoreStats.TotalPlayCount > 0)
            {
                int count = allScores.Count() / 2;
                player.ScoreStats.TotalScore = allScores.Sum(s => s.ModifiedScore);
                player.ScoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                player.ScoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
            }

            if (player.ScoreStats.RankedPlayCount > 0)
            {
                int count = rankedScores.Count() / 2;
                player.ScoreStats.AverageRankedAccuracy = rankedScores.Average(s => s.Accuracy);
                player.ScoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                player.ScoreStats.TopAccuracy = rankedScores.Max(s => s.Accuracy);
                player.ScoreStats.TopPp = rankedScores.Max(s => s.Pp);

                player.ScoreStats.SSPPlays = rankedScores.Where(s => s.Accuracy > 0.95).Count();
                player.ScoreStats.SSPlays = rankedScores.Where(s => 0.9 < s.Accuracy && s.Accuracy < 0.95).Count();
                player.ScoreStats.SPPlays = rankedScores.Where(s => 0.85 < s.Accuracy && s.Accuracy < 0.9).Count();
                player.ScoreStats.SPlays = rankedScores.Where(s => 0.8 < s.Accuracy && s.Accuracy < 0.85).Count();
                player.ScoreStats.APlays = rankedScores.Where(s => s.Accuracy < 0.8).Count();
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
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var scores = await _context.Scores.Where(s => s.Pp != 0).ToListAsync();
            var players = _context.Players.ToList();
            Dictionary<string, int> countries = new Dictionary<string, int>();
            foreach (Player p in players)
            {
                _context.RecalculatePP(p, scores.Where(s => s.PlayerId == p.Id).OrderByDescending(s => s.Pp).ToList());
            }
            var ranked = players.OrderByDescending(t => t.Pp).ToList();
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
            var players = await _context.Players.Where(p => !p.Banned).Include(p => p.ScoreStats).ToListAsync();
            var scores = await _context.Scores.Select(s => new SubScore {
                PlayerId = s.PlayerId,
                Platform = s.Platform,
                Hmd = s.Hmd,
                ModifiedScore = s.ModifiedScore,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
            }).ToListAsync();
            foreach (var player in players)
            {
                await RefreshStats(player, scores.Where(s => s.PlayerId == player.Id).ToList());
            }
            _context.SaveChanges();
            return Ok();
        }

        [HttpGet("~/players/rankrefresh")]
        [Authorize]
        public async Task<ActionResult> RefreshRanks()
        {
            //string currentId = HttpContext.CurrentUserID();
            //Player? currentPlayer = _context.Players.Find(currentId);
            //if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            //{
            //    return Unauthorized();
            //}
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

        [HttpPut("~/badge")]
        [Authorize]
        public ActionResult<Badge> CreateBadge([FromQuery] string description, [FromQuery] string image) {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Badge badge = new Badge {
                Description = description,
                Image = image
            };

            _context.Badges.Add(badge);
            _context.SaveChanges();

            return badge;
        }

        [HttpPut("~/player/badge/{playerId}/{badgeId}")]
        [Authorize]
        public async Task<ActionResult<Player>> AddBadge(string playerId, int badgeId)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Badges).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound("Player not found");
            }

            Badge? badge = _context.Badges.Find(badgeId);
            if (badge == null)
            {
                return NotFound("Badge not found");
            }
            if (player.Badges == null) {
                player.Badges = new List<Badge>();
            }

            player.Badges.Add(badge);
            _context.SaveChanges();

            return player;
        }

        [NonAction]
        public async Task<Player?> GetPlayerFromBL(string playerID)
        {
            AuthInfo? authInfo = _context.Auths.FirstOrDefault(el => el.Id.ToString() == playerID);

            if (authInfo == null) return null;

            Player result = new Player();
            result.Id = playerID;
            result.Name = authInfo.Login;
            result.Platform = "oculus";
            result.SetDefaultAvatar();

            return result;
        }
    }
}
