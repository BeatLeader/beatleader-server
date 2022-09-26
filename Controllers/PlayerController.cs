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
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        BlobContainerClient _assetsContainerClient;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerController(
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
        public async Task<ActionResult<PlayerResponseFull>> Get(string id, bool stats = true)
        {
            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(id);
            }
            catch { }
            AccountLink? link = null;
            if (oculusId < 1000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _readContext.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
                }
            }
            if (link == null && oculusId < 70000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _readContext.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
                }
            }
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : id);
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                if (stats) {
                    player = _readContext
                        .Players
                        .Where(p => p.Id == userId)
                        .Include(p => p.ScoreStats)
                        .Include(p => p.Badges)
                        .Include(p => p.StatsHistory)
                        .Include(p => p.Clans)
                        .Include(p => p.PatreonFeatures)
                        .Include(p => p.ProfileSettings)
                        .Include(p => p.Socials)
                        .Include(p => p.EventsParticipating)
                        .FirstOrDefault();
                } else {
                    player = _readContext.Players.Find(userId);
                }
                
            }
            if (player == null)
            {
                using (_serverTiming.TimeAction("lazy"))
                {
                    player = (await GetLazy(id, false)).Value;
                }
            }
            if (player != null) {
                var result = PostProcessSettings(ResponseFullFromPlayer(player));
                
                result.PinnedScores = _readContext
                    .Scores
                    .Include(s => s.Metadata)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(s => s.Song)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(s => s.Difficulty)
                    .Where(s => s.PlayerId == player.Id && s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned)
                    .OrderBy(s => s.Metadata.Priority)
                    .Select(ScoreWithMyScore)
                    .ToList();
                return result;
            } else {
                return NotFound();
            }
        }

        [NonAction]
        public async Task<ActionResult<Player>> GetLazy(string id, bool addToBase = true)
        {
            Player? player = _context
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.EventsParticipating)
                .Include(p => p.ProfileSettings)
                .FirstOrDefault(p => p.Id == id);

            if (player == null) {
                Int64 userId = Int64.Parse(id);
                if (userId > 70000000000000000) {
                    player = await PlayerUtils.GetPlayerFromSteam(id, _configuration.GetValue<string>("SteamKey"));
                    if (player == null) {
                        return NotFound();
                    }
                } else if (userId > 1000000000000000) {
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
            Player? player = _context.Players.Find(id);

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
            [FromQuery] float? stars_to = null,
            [FromQuery] int? eventId = null)
        {
            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence"))
            {
                sequence = _readContext.Scores.Where(t => t.PlayerId == id);
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
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
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
                if (eventId != null) {
                    var leaderboardIds = _context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefault();
                    if (leaderboardIds?.Count() != 0) {
                        sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                    }
                }
                if (diff != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
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
                                .ThenInclude(d => d.ModifierValues)
                            .Include(sc => sc.ScoreImprovement)
                            .Select(ScoreWithMyScore)
                            .ToList()
                };
            }

            string? currentID = HttpContext.CurrentUserID(_readContext);
            if (currentID != null && currentID != id) {
                var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

                var myScores = _readContext.Scores.Where(s => s.PlayerId == currentID && leaderboards.Contains(s.LeaderboardId)).Select(RemoveLeaderboard).ToList();
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
            string currentId = HttpContext.CurrentUserID(_context);
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
            Score? score = _readContext
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
                    string? currentID = HttpContext.CurrentUserID(_readContext);
                    var friends = _readContext.Friends.Where(f => f.Id == currentID).Include(f => f.Friends).FirstOrDefault();
                    if (friends != null)
                    {
                        var friendsList = friends.Friends.Select(f => f.Id).ToList();
                        sequence = _readContext.Scores.Where(s => s.PlayerId == currentID || friendsList.Contains(s.PlayerId));
                    }
                    else
                    {
                        sequence = _readContext.Scores.Where(s => s.PlayerId == currentID);
                    }
                }
                else
                {
                    sequence = _readContext.Scores.Where(t => t.PlayerId == id);
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
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
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
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
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
            if (values.Count() == 0) {
                return "";
            }
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
            if (values.Count() == 0) return "";
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
            return _readContext
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Where(s => s.PlayerId == id && s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked)
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
            [FromQuery] string mapsType = "ranked",
            [FromQuery] bool friends = false,
            [FromQuery] string? pp_range = null,
            [FromQuery] string? score_range = null,
            [FromQuery] string? platform = null,
            [FromQuery] string? role = null,
            [FromQuery] string? hmd = null,
            [FromQuery] string? clans = null,
            [FromQuery] int? activityPeriod = null)
        {
            IQueryable<Player> request = _readContext.Players.Include(p => p.ScoreStats).Include(p => p.Clans).Where(p => !p.Banned);
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
                    var hmds = hmd.ToLower().Split(",").Select(s => (HMD)Int32.Parse(s));
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
                    switch (mapsType)
                    {
                        case "ranked":
                            request = request.Where(p => p.ScoreStats.RankedPlayCount >= from && p.ScoreStats.RankedPlayCount <= to);
                            break;
                        case "unranked":
                            request = request.Where(p => p.ScoreStats.UnrankedPlayCount >= from && p.ScoreStats.UnrankedPlayCount <= to);
                            break;
                        case "all":
                            request = request.Where(p => p.ScoreStats.TotalPlayCount >= from && p.ScoreStats.TotalPlayCount <= to);
                            break;
                    }
                    
                }
                catch { }
            }
            if (friends) {
                string userId = HttpContext.CurrentUserID(_readContext);
                var player = _readContext.Players.Find(userId);
                if (player == null)
                {
                    return NotFound();
                }
                var friendsContainer = _readContext.Friends.Where(f => f.Id == player.Id).Include(f => f.Friends).FirstOrDefault();
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
            if (activityPeriod != null) {
                int timetreshold = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - (int)activityPeriod;

                switch (mapsType)
                {
                    case "ranked":
                        request = request.Where(p => p.ScoreStats.LastRankedScoreTime >= timetreshold);
                        break;
                    case "unranked":
                        request = request.Where(p => p.ScoreStats.LastUnrankedScoreTime >= timetreshold);
                        break;
                    case "all":
                        request = request.Where(p => p.ScoreStats.LastScoreTime >= timetreshold);
                        break;
                }
            }
            request = Sorted(request, sortBy, order, mapsType);
            
            return new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = request.Count()
                },
                Data = request.Skip((page - 1) * count).Take(count).Select(ResponseWithStatsFromPlayer).ToList().Select(PostProcessSettings)
            };
        }

        private IQueryable<Player> Sorted(IQueryable<Player> request, string sortBy, string order, string mapsType) {
            switch (mapsType)
            {
                case "ranked":
                    switch (sortBy)
                    {
                        case "pp":
                            request = request.Order(order, p => p.Pp);
                            break;
                        case "rank":
                            request = request
                                .Where(p => p.ScoreStats.AverageRankedRank != 0)
                                .Order(order == "desc" ? "asc" : "desc", p => Math.Round(p.ScoreStats.AverageRankedRank))
                                .ThenOrder(order, p => p.ScoreStats.RankedPlayCount); 
                            break;
                        case "acc":
                            request = request.Order(order, p => p.ScoreStats.AverageRankedAccuracy);
                            break;
                        case "weightedAcc":
                            request = request.Order(order, p => p.ScoreStats.AverageWeightedRankedAccuracy);
                            break;
                        case "topAcc":
                            request = request.Order(order, p => p.ScoreStats.TopRankedAccuracy);
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
                            request = request.Order(order, p => p.ScoreStats.TotalRankedScore);
                            break;
                        case "lastplay":
                            request = request.Order(order, p => p.ScoreStats.LastRankedScoreTime);
                            break;
                        default:
                            break;
                    }
                    break;
                case "unranked":
                    switch (sortBy)
                    {
                        case "rank":
                            request = request
                                .Where(p => p.ScoreStats.AverageUnrankedRank != 0)
                                .Order(order == "desc" ? "asc" : "desc", p => Math.Round(p.ScoreStats.AverageUnrankedRank))
                                .ThenOrder(order, p => p.ScoreStats.UnrankedPlayCount);
                            break;
                        case "acc":
                            request = request.Order(order, p => p.ScoreStats.AverageUnrankedAccuracy);
                            break;
                        case "topAcc":
                            request = request.Order(order, p => p.ScoreStats.TopUnrankedAccuracy);
                            break;
                        case "hmd":
                            request = request.Order(order, p => p.ScoreStats.TopHMD);
                            break;
                        case "playCount":
                            request = request.Order(order, p => p.ScoreStats.UnrankedPlayCount);
                            break;
                        case "score":
                            request = request.Order(order, p => p.ScoreStats.TotalUnrankedScore);
                            break;
                        case "lastplay":
                            request = request.Order(order, p => p.ScoreStats.LastUnrankedScoreTime);
                            break;
                        default:
                            break;
                    }
                    break;
                case "all":
                    switch (sortBy)
                    {
                        case "pp":
                            request = request.Order(order, p => p.Pp);
                            break;
                        case "rank":
                            request = request
                                .Where(p => p.ScoreStats.AverageRank != 0)
                                .Order(order == "desc" ? "asc" : "desc", p => Math.Round(p.ScoreStats.AverageRank))
                                .ThenOrder(order, p => p.ScoreStats.TotalPlayCount);
                            break;
                        case "acc":
                            request = request.Order(order, p => p.ScoreStats.AverageAccuracy);
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
                            request = request.Order(order, p => p.ScoreStats.TotalPlayCount);
                            break;
                        case "score":
                            request = request.Order(order, p => p.ScoreStats.TotalScore);
                            break;
                        case "lastplay":
                            request = request.Order(order, p => p.ScoreStats.LastScoreTime);
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    break;
            }

            return request;
        }

        [HttpGet("~/event/{id}/players")]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetEventPlayers(
            int id,
            [FromQuery] string sortBy = "pp", 
            [FromQuery] int page = 1, 
            [FromQuery] int count = 50, 
            [FromQuery] string search = "",
            [FromQuery] string order = "desc",
            [FromQuery] string countries = ""
            )
        {
            IQueryable<Player> request = _readContext.Players.Include(p => p.ScoreStats).Include(p => p.EventsParticipating).Where(p => !p.Banned);
            
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

            var players = request.Where(p => p.EventsParticipating.FirstOrDefault(e => e.EventId == id) != null)
                .Select(ResponseWithStatsFromPlayer)
                .OrderByDescending(p => p.EventsParticipating.First(e => e.EventId == id).Pp);

            var allPlayers = players.Skip((page - 1) * count).Take(count).ToList();

            foreach (var resultPlayer in allPlayers)
            {
                var eventPlayer = resultPlayer.EventsParticipating.First(e => e.EventId == id);

                resultPlayer.Rank = eventPlayer.Rank;
                resultPlayer.Pp = eventPlayer.Pp;
                resultPlayer.CountryRank = eventPlayer.CountryRank;
            }

            return new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = players.Count()
                },
                Data = allPlayers
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
            HttpContext.Response.OnCompleted(async () =>
            {
                await RefreshPlayersStats();
                var playersCount = _context.Players.Where(p => !p.Banned).Count();
                for (int i = 0; i < playersCount; i += 2000)
                {
                    var ranked = _context.Players.Where(p => !p.Banned).Include(p => p.ScoreStats).Include(p => p.StatsHistory).Skip(i).Take(2000).ToList();
                    foreach (Player p in ranked)
                    {
                        p.Histories = GenerateListString(p.Histories, p.Rank);

                        var stats = p.ScoreStats;
                        var statsHistory = p.StatsHistory;
                        if (statsHistory == null)
                        {
                            statsHistory = new PlayerStatsHistory();
                        }

                        statsHistory.Pp = GenerateListString(statsHistory.Pp, p.Pp);
                        statsHistory.Rank = p.Histories;
                        statsHistory.CountryRank = GenerateListString(statsHistory.CountryRank, p.CountryRank);
                        statsHistory.TotalScore = GenerateListString(statsHistory.TotalScore, stats.TotalScore);
                        statsHistory.AverageRankedAccuracy = GenerateListString(statsHistory.AverageRankedAccuracy, stats.AverageRankedAccuracy);
                        statsHistory.AverageWeightedRankedAccuracy = GenerateListString(statsHistory.AverageWeightedRankedAccuracy, stats.AverageWeightedRankedAccuracy);
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

                    _context.SaveChanges();
                }
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

            if (allScores.Count() == 0) return;

            var rankedScores = allScores.Where(s => s.Pp != 0 && !s.Qualification).ToList();
            var unrankedScores = allScores.Where(s => s.Pp == 0 || s.Qualification).ToList();

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

            player.ScoreStats.TotalPlayCount = allScores.Count();
            player.ScoreStats.UnrankedPlayCount = unrankedScores.Count();
            player.ScoreStats.RankedPlayCount = rankedScores.Count();

            player.ScoreStats.TopPlatform = platforms.OrderByDescending(s => s.Value).First().Key;
            player.ScoreStats.TopHMD = hmds.OrderByDescending(s => s.Value).First().Key;

            if (player.ScoreStats.TotalPlayCount > 0)
            {
                int count = allScores.Count() / 2;
                player.ScoreStats.TotalScore = allScores.Sum(s => s.ModifiedScore);
                player.ScoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                player.ScoreStats.TopAccuracy = allScores.Max(s => s.Accuracy);
                player.ScoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).ElementAt(count).Accuracy;
                player.ScoreStats.AverageRank = allScores.Average(s => (float)s.Rank);
                player.ScoreStats.LastScoreTime = allScores.OrderByDescending(s => s.Timeset).First().Timeset;
            }

            if (player.ScoreStats.UnrankedPlayCount > 0)
            {
                int count = unrankedScores.Count() / 2;
                player.ScoreStats.TotalUnrankedScore = unrankedScores.Sum(s => s.ModifiedScore);
                player.ScoreStats.AverageUnrankedAccuracy = unrankedScores.Average(s => s.Accuracy);
                player.ScoreStats.TopUnrankedAccuracy = unrankedScores.Max(s => s.Accuracy);
                player.ScoreStats.AverageUnrankedRank = unrankedScores.Average(s => (float)s.Rank);
                player.ScoreStats.LastUnrankedScoreTime = unrankedScores.OrderByDescending(s => s.Timeset).First().Timeset;
            }

            if (player.ScoreStats.RankedPlayCount > 0)
            {
                int count = rankedScores.Count() / 2;
                player.ScoreStats.TotalRankedScore = rankedScores.Sum(s => s.ModifiedScore);
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
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var scores = _context.Scores.Where(s => s.Pp != 0).ToList();
            var players = _context.Players.Where(s => s.Pp != 0).ToList();
            Dictionary<string, int> countries = new Dictionary<string, int>();

            var transaction = _context.Database.BeginTransaction();

            int counter = 0;
            foreach (Player p in players)
            {
                _context.RecalculatePP(p, scores.Where(s => s.PlayerId == p.Id && !s.Banned && !s.Qualification).OrderByDescending(s => s.Pp).ToList());

                counter++;
                if (counter == 1000) {
                    counter = 0;
                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception e)
                    {

                        _context.RejectChanges();
                        transaction.Rollback();
                        transaction = _context.Database.BeginTransaction();
                        continue;
                    }
                    transaction.Commit();
                    transaction = _context.Database.BeginTransaction();
                }
            }
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _context.RejectChanges();
            }
            transaction.Commit();
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
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
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
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("~/badge")]
        [Authorize]
        public ActionResult<Badge> CreateBadge([FromQuery] string description, [FromQuery] string image, [FromQuery] string? link = null) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Badge badge = new Badge {
                Description = description,
                Image = image,
                Link = link
            };

            _context.Badges.Add(badge);
            _context.SaveChanges();

            return badge;
        }

        [HttpPut("~/badge/{id}")]
        [Authorize]
        public ActionResult<Badge> UpdateBadge(int id, [FromQuery] string? description, [FromQuery] string? image, [FromQuery] string? link = null)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var badge = _context.Badges.Find(id);

            if (badge == null) {
                return NotFound();
            }

            if (description != null) {
                badge.Description = description;
            }

            if (image != null) {
                badge.Image = image;
            }

            if (Request.Query.ContainsKey("link"))
            {
                badge.Link = link;
            }

            _context.SaveChanges();

            return badge;
        }

        [HttpPut("~/player/badge/{playerId}/{badgeId}")]
        [Authorize]
        public async Task<ActionResult<Player>> AddBadge(string playerId, int badgeId)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Include(p => p.Badges).FirstOrDefault(p => p.Id == playerId);
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

            var link = _readContext.AccountLinks.Where(l => l.PCOculusID == id).FirstOrDefault();
            if (link != null)
            {
                string playerId = link.SteamID.Length > 0 ? link.SteamID : id;

                var player = _readContext.Players.Find(playerId);

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
