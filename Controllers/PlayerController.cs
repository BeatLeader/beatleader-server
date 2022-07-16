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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Linq.Expressions;
using System.Net;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class PlayerController : Controller
    {
        private readonly AppContext _context;
        private readonly IConfiguration _configuration;
        BlobContainerClient _assetsContainerClient;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerController(
            AppContext context, 
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env)
        {
            _context = context;
            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            if (env.IsDevelopment())
            {
                _assetsContainerClient = new BlobContainerClient(config.Value.AccountName, config.Value.AssetsContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.AssetsContainerName);

                _assetsContainerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
        }

        [HttpGet("~/player/{id}")]
        public async Task<ActionResult<Player>> Get(string id, bool stats = true)
        {
            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(id);
            }
            catch { }
            AccountLink? link = null;
            if (oculusId < 2000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
                }
            }
            if (link == null && oculusId < 70000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _context.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
                }
            }
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : id);
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
                    if (addToBase) {
                        var net = new System.Net.WebClient();
                        var data = net.DownloadData(player.Avatar);
                        var readStream = new System.IO.MemoryStream(data);
                        string fileName = player.Id;

                        (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(readStream);
                        fileName += extension;

                        await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                        await _assetsContainerClient.UploadBlobAsync(fileName, stream);

                        player.Avatar = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://cdn.beatleader.xyz/assets/") + fileName;
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
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> GetScores(
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
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.Stars)
                                    .Where(s => s.Leaderboard.Difficulty.Ranked);
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

            ResponseWithMetadata<ScoreResponseWithMyScore> result; 
            using (_serverTiming.TimeAction("db"))
            {
                result = new ResponseWithMetadata<ScoreResponseWithMyScore>()
                {
                    Metadata = new Metadata()
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = sequence.Count()
                    },
                    Data = sequence
                            .Skip((page - 1) * count)
                            .Take(count)
                            .Include(lb => lb.Leaderboard)
                                .ThenInclude(lb => lb.Song)
                                .ThenInclude(lb => lb.Difficulties)
                            .Include(lb => lb.Leaderboard)
                                .ThenInclude(lb => lb.Difficulty)
                            .Include(sc => sc.ScoreImprovement)
                            .Select(ScoreWithMyScore)
                            .ToList()
                };
            }

            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID != null && currentID != id) {
                var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

                var myScores = _context.Scores.Where(s => s.PlayerId == currentID && leaderboards.Contains(s.LeaderboardId)).Select(RemoveLeaderboard).ToList();
                foreach (var score in result.Data)
                {
                    score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                }
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

        public class HistogrammValue {
            public int Value { get; set; }
            public int Page { get; set; }
        }

        [HttpGet("~/player/{id}/histogram")]
        public async Task<ActionResult<string>> GetHistogram(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] string order = "desc",
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? batch = null)
        {
            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence"))
            {
                if (id == "user-friends")
                {
                    string? currentID = HttpContext.CurrentUserID(_context);
                    var friends = await _context.Friends.Where(f => f.Id == currentID).Include(f => f.Friends).FirstOrDefaultAsync();
                    if (friends != null)
                    {
                        var friendsList = friends.Friends.Select(f => f.Id).ToList();
                        sequence = _context.Scores.Where(s => s.PlayerId == currentID || friendsList.Contains(s.PlayerId));
                    }
                    else
                    {
                        sequence = _context.Scores.Where(s => s.PlayerId == currentID);
                    }
                }
                else
                {
                    sequence = _context.Scores.Where(t => t.PlayerId == id);
                }

                
                switch (sortBy)
                {
                    case "date":
                        sequence = sequence.Order(order, t => t.Timeset);
                        break;
                    case "pp":
                        sequence = sequence.Where(t => t.Pp > 0).Order(order, t => t.Pp);
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
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.Stars)
                                    .Where(s => s.Leaderboard.Difficulty.Ranked);
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

            switch (sortBy)
            {
                case "date":
                    return HistogrammValuee(order, sequence.Select(s => int.Parse(s.Timeset)).ToList(), (int)(batch ?? 60 * 60 * 24), count);
                case "pp":
                    return HistogrammValuee(order, sequence.Select(s => s.Pp).ToList(), batch ?? 5, count);
                case "acc":
                    return HistogrammValuee(order, sequence.Select(s => s.Accuracy).ToList(), batch ?? 0.0025f, count);
                case "pauses":
                    return HistogrammValuee(order, sequence.Select(s => s.Pauses).ToList(), (int)(batch ?? 1), count);
                case "rank":
                    return HistogrammValuee(order, sequence.Select(s => s.Rank).ToList(), (int)(batch ?? 1), count);
                case "stars":
                    return HistogrammValuee(order, sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToList(), batch ?? 0.15f, count);
                default:
                    return BadRequest();
            }
        }

        public string HistogrammValuee(string order, List<int> values, int batch, int count)
        {
            Dictionary<int, HistogrammValue> result = new Dictionary<int, HistogrammValue>();
            int normalizedMin = (values.Min() / batch) * batch;
            int normalizedMax = (values.Max() / batch + 1) * batch;
            int totalCount = 0;
            if (order == "desc")
            {
                for (int i = normalizedMax; i > normalizedMin; i -= batch)
                {
                    int value = values.Count(s => s <= i && s >= i - batch);
                    result[i - batch] = new HistogrammValue { Value = value,  Page = totalCount / count };
                    totalCount += value;
                }
            }
            else
            {
                for (int i = normalizedMin; i < normalizedMax; i += batch)
                {
                    int value = values.Count(s => s >= i && s <= i + batch);
                    result[i + batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        public string HistogrammValuee(string order, List<float> values, float batch, int count)
        {

            Dictionary<float, HistogrammValue> result = new Dictionary<float, HistogrammValue>();
            int totalCount = 0;
            float normalizedMin = (int)(values.Min() / batch) * batch;
            float normalizedMax = (int)(values.Max() / batch + 1) * batch;
            if (order == "desc")
            {
                for (float i = normalizedMax; i > normalizedMin; i -= batch)
                {
                    int value = values.Count(s => s <= i && s >= i - batch);
                    result[i - batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }
            else
            {
                for (float i = normalizedMin; i < normalizedMax; i += batch)
                {
                    int value = values.Count(s => s >= i && s <= i + batch);
                    result[i + batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        public class GraphResponse
        {
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
                .Select(s => new GraphResponse
                {
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
            [FromQuery] string order = "desc",
            [FromQuery] string countries = "",
            [FromQuery] bool friends = false,
            [FromQuery] string? pp_range = null,
            [FromQuery] string? score_range = null,
            [FromQuery] string? platform = null,
            [FromQuery] string? role = null,
            [FromQuery] string? hmd = null,
            [FromQuery] string? clans = null)
        {
            IQueryable<Player> request = _context.Players.Include(p => p.ScoreStats).Include(p => p.Clans).Where(p => !p.Banned);
            if (countries.Length != 0)
            {
                request = request.Where(p => countries.Contains(p.Country));
            }
            if (search.Length != 0)
            {
                var player = Expression.Parameter(typeof(Player), "p");
                
                var contains = "".GetType().GetMethod("Contains", new[] { typeof(string) });

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in search.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Call(Expression.Property(player, "Name"), contains, Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<Player, bool>>)Expression.Lambda(exp, player));
            }

            if (clans != null)
            {
                request = request.Where(p => p.Clans.FirstOrDefault(c => clans.Contains(c.Tag)) != null);
            }
            if (platform != null) {
                var platforms = platform.ToLower().Split(",");
                request = request.Where(p => platforms.Contains(p.ScoreStats.TopPlatform.ToLower()));
            }
            if (role != null)
            {
                var player = Expression.Parameter(typeof(Player), "p");

                var contains = "".GetType().GetMethod("Contains", new[] { typeof(string) });

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in role.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Call(Expression.Property(player, "Role"), contains, Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<Player, bool>>)Expression.Lambda(exp, player));
            }
            if (hmd != null)
            {
                try
                {
                    var hmds = hmd.ToLower().Split(",").Select(s => Int32.Parse(s));
                    request = request.Where(p => hmds.Contains(p.ScoreStats.TopHMD));
                }
                catch { }
            }
            if (pp_range != null)
            {
                try {
                    var array = pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    request = request.Where(p => p.Pp >= from && p.Pp <= to);
                } catch { }
            }
            if (score_range != null)
            {
                try
                {
                    var array = score_range.Split(",").Select(s => int.Parse(s)).ToArray();
                    int from = array[0]; int to = array[1];
                    request = request.Where(p => p.ScoreStats.RankedPlayCount >= from && p.ScoreStats.RankedPlayCount <= to);
                }
                catch { }
            }
            if (friends) {
                string userId = HttpContext.CurrentUserID(_context);
                var player = await _context.Players.FindAsync(userId);
                if (player == null)
                {
                    return NotFound();
                }
                var friendsContainer = await _context.Friends.Where(f => f.Id == player.Id).Include(f => f.Friends).FirstOrDefaultAsync();
                if (friendsContainer != null)
                {
                    var friendsList = friendsContainer.Friends.Select(f => f.Id).ToList();
                    request = request.Where(p => p.Id == player.Id || friendsList.Contains(p.Id));
                }
                else
                {
                    request = request.Where(p => p.Id == player.Id);
                }
            }
            switch (sortBy)
            {
                case "pp":
                    request = request.Order(order, p => p.Pp);
                    break;
                case "rank":
                    request = request.Order(order, p => p.ScoreStats.AverageRankedRank);
                    break;
                case "acc":
                    request = request.Order(order, p => p.ScoreStats.AverageRankedAccuracy);
                    break;
                case "topAcc":
                    request = request.Order(order, p => p.ScoreStats.TopAccuracy);
                    break;
                case "topPp":
                    request = request.Order(order, p => p.ScoreStats.TopPp);
                    break;
                case "hmd":
                    request = request.Order(order, p => p.ScoreStats.TopHMD);
                    break;
                case "playCount":
                    request = request.Order(order, p => p.ScoreStats.RankedPlayCount);
                    break;
                case "score":
                    request = request.Order(order, p => p.ScoreStats.TotalScore);
                    break;

                case "dailyImprovements":
                    request = request.Order(order, p => p.ScoreStats.DailyImprovements);
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
            var transaction = _context.Database.BeginTransaction();
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            CronTimestamps? cronTimestamps = _context.cronTimestamps.Find(1);
            if (cronTimestamps != null)
            {
                if ((timestamp - cronTimestamps.HistoriesTimestamp) < 60 * 60 * 24 - 5 * 60)
                {
                    return BadRequest("Allowed only at midnight");
                }
                else
                {
                    cronTimestamps.HistoriesTimestamp = timestamp;
                    _context.Update(cronTimestamps);
                }
            }
            else
            {
                cronTimestamps = new CronTimestamps();
                cronTimestamps.HistoriesTimestamp = timestamp;
                _context.Add(cronTimestamps);
            }
            transaction.Commit();
            HttpContext.Response.OnCompleted(async () => {
                await RefreshPlayersStats();
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
            });

            return Ok();
        }

        [HttpGet("~/players/steam/refresh")]
        public async Task<ActionResult> RefreshSteamPlayers()
        {
            HttpContext.Response.OnCompleted(async () => {
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
            });

            return Ok();
        }

        [NonAction]
        public async Task RefreshStats(Player player)
        {
            if (player.ScoreStats == null)
            {
                player.ScoreStats = new PlayerScoreStats();
                _context.Stats.Add(player.ScoreStats);
            }
            var allScores = await _context.Scores.Where(s => s.PlayerId == player.Id).Select(s => new {
                Platform = s.Platform,
                Hmd = s.Hmd,
                ModifiedScore = s.ModifiedScore,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
                Rank = s.Rank
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
                player.ScoreStats.AverageRank = allScores.Average(s => (float)s.Rank);
            }

            if (player.ScoreStats.RankedPlayCount > 0)
            {
                int count = rankedScores.Count() / 2;
                player.ScoreStats.AverageRankedAccuracy = rankedScores.Average(s => s.Accuracy);
                player.ScoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                player.ScoreStats.TopAccuracy = rankedScores.Max(s => s.Accuracy);
                player.ScoreStats.TopPp = rankedScores.Max(s => s.Pp);
                player.ScoreStats.AverageRankedRank = rankedScores.Average(s => (float)s.Rank);

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
            Player? player = _context.Players.Include(p => p.ScoreStats).FirstOrDefault(p => p.Id == id);
            if (player == null)
            {
                return NotFound();
            }
            await RefreshPlayer(player, refreshRank);

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
            var players = _context.Players.ToList();
            Dictionary<string, int> countries = new Dictionary<string, int>();
            foreach (Player p in players)
            {
                await RefreshPlayer(p);
            }
            var ranked = _context.Players.OrderByDescending(t => t.Pp).ToList();
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
            foreach (var player in players)
            {
                await RefreshStats(player);
            }
            _context.SaveChanges();
            return Ok();
        }

        [HttpGet("~/players/rankrefresh")]
        [Authorize]
        public async Task<ActionResult> RefreshRanks()
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID();
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

                _context.Players.Update(p);
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

        [HttpGet("~/oculususer")]
        public async Task<ActionResult<OculusUser>> GetOculusUser([FromQuery] string token)
        {
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(token, _configuration);
            if (id == null)
            {
                return NotFound();
            }

            var link = _context.AccountLinks.Where(l => l.PCOculusID == id).FirstOrDefault();
            if (link != null)
            {
                string playerId = link.SteamID.Length > 0 ? link.SteamID : id;

                var player = _context.Players.Find(playerId);

                return new OculusUser
                {
                    Id = id,
                    Migrated = true,
                    MigratedId = playerId,
                    Name = player.Name,
                    Avatar = player.Avatar,
                };
            }
            var oculusPlayer = await PlayerUtils.GetPlayerFromOculus(id, token);

            return new OculusUser
            {
                Id = id,
                Name = oculusPlayer.Name,
                Avatar = oculusPlayer.Avatar,
            };
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
