using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Services.SearchService;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class PlayerController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerController(
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
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpGet("~/player/{id}")]
        [SwaggerOperation(Summary = "Get player profile", Description = "Retrieves a Beat Saber profile data for a specific player ID.")]
        [SwaggerResponse(200, "Returns the player's full profile", typeof(PlayerResponseFull))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<PlayerResponseFull>> Get(
            [FromRoute, SwaggerParameter("The ID of the player")] string id,
            [FromQuery, SwaggerParameter("Include stats in the response")]  bool stats = true,
            [FromQuery, SwaggerParameter("Whether to keep original ID (for migrated players)")] bool keepOriginalId = false,
            [FromQuery, SwaggerParameter("Leaderboard context, 'general' by default")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            string userId = _context.PlayerIdToMain(id);
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                if (stats) {
                    player = _readContext
                        .Players
                        .Where(p => p.Id == userId)
                        .Include(p => p.ScoreStats)
                        .Include(p => p.Badges.OrderBy(b => b.Timeset))
                        .Include(p => p.Clans)
                        .Include(p => p.ProfileSettings)
                        .Include(p => p.Socials)
                        .Include(p => p.Changes)
                        .AsSplitQuery()
                        .FirstOrDefault();
                } else {
                    player = await _readContext.Players.FindAsync(userId);
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
                var result = new PlayerResponseFull {
                    Id = player.Id,
                    Name = player.Name,
                    Platform = player.Platform,
                    Avatar = player.Avatar,
                    Country = player.Country,
                    ScoreStats = player.ScoreStats,

                    MapperId = player.MapperId,

                    Banned = player.Banned,
                    Inactive = player.Inactive,
                    Bot = player.Bot,

                    ExternalProfileUrl = player.ExternalProfileUrl,

                    Badges = player.Badges,
                    Changes = player.Changes,

                    Pp = player.Pp,
                    AccPp = player.AccPp,
                    TechPp = player.TechPp,
                    PassPp = player.PassPp,
                    Rank = player.Rank,
                    CountryRank = player.CountryRank,
                    LastWeekPp = player.LastWeekPp,
                    LastWeekRank = player.LastWeekRank,
                    LastWeekCountryRank = player.LastWeekCountryRank,
                    Role = player.Role,
                    Socials = player.Socials,
                    ProfileSettings = player.ProfileSettings,
                    Clans = stats && player.Clans != null
                        ? player
                            .Clans
                            .OrderBy(c => player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id)
                            .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color }) 
                        : null
                };
                if (result.Banned) {
                    result.BanDescription = _context.Bans.OrderByDescending(b => b.Timeset).FirstOrDefault(b => b.PlayerId == player.Id);
                }

                if (keepOriginalId && result.Id != id) {
                    result.Id = id;
                }

                if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                    var contextExtension = _context
                        .PlayerContextExtensions
                        .Include(p => p.ScoreStats)
                        .Where(p => p.PlayerId == userId && p.Context == leaderboardContext)
                        .FirstOrDefault();
                    if (contextExtension != null) {
                        result.ToContext(contextExtension);
                    }
                }

                return PostProcessSettings(result);
            } else {
                return NotFound();
            }
        }

        [HttpGet("~/player/discord/{id}")]
        [SwaggerOperation(Summary = "Get player with Discord", Description = "Retrieves a BeatLeader profile data with linked Discord profile.")]
        [SwaggerResponse(200, "Returns the player's full profile", typeof(PlayerResponseFull))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<PlayerResponseFull>> GetDiscord([FromRoute, SwaggerParameter("Discord profile ID")] string id)
        {
            var social = _context.PlayerSocial.Where(s => s.Service == "Discord" && s.UserId == id && s.PlayerId != null).FirstOrDefault();

            if (social == null || social.PlayerId == null) {
                return NotFound();
            }

            return await Get(social.PlayerId, true);
        }

        [HttpGet("~/player/beatsaver/{id}")]
        [SwaggerOperation(Summary = "Get player with BeatSaver", Description = "Retrieves a BeatLeader profile data with linked BeatSaver profile.")]
        [SwaggerResponse(200, "Returns the player's full profile", typeof(PlayerResponseFull))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<PlayerResponseFull>> GetBeatSaver([FromRoute, SwaggerParameter("BeatSaver profile ID")] string id)
        {
            var social = _context.PlayerSocial.Where(s => s.Service == "BeatSaver" && s.UserId == id && s.PlayerId != null).FirstOrDefault();

            if (social == null || social.PlayerId == null) {
                return NotFound();
            }

            return await Get(social.PlayerId, true);
        }

        [NonAction]
        public async Task<ActionResult<Player>> GetLazy(string id, bool addToBase = true)
        {
            Player? player = _context
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.EventsParticipating)
                .Include(p => p.ProfileSettings)
                .Include(p => p.ContextExtensions)
                .ThenInclude(ce => ce.ScoreStats)
                .FirstOrDefault(p => p.Id == id);

            if (player == null) {
                Int64 userId = long.Parse(id);
                if (userId > 70000000000000000) {
                    player = await PlayerUtils.GetPlayerFromSteam(_configuration.GetValue<string>("SteamApi"), id, _configuration.GetValue<string>("SteamKey"));
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
                        var readStream = new MemoryStream(data);
                        string fileName = player.Id;

                        (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(readStream);
                        fileName += extension;

                        player.Avatar = await _assetsS3Client.UploadAsset(fileName, stream);
                    }
                } else {
                    player = await GetPlayerFromBL(id);
                    if (player == null)
                    {
                        return NotFound();
                    }
                }
                player.Id = id;
                player.ScoreStats = new PlayerScoreStats();
                if (addToBase) {
                    _context.Players.Add(player);
                    
                    await _context.SaveChangesAsync();

                    PlayerSearchService.AddNewPlayer(player);
                }
            }

            return player;
        }

        //[HttpDelete("~/player/{id}")]
        //[Authorize]
        [NonAction]
        public async Task<ActionResult> DeletePlayer(string id)
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            var bslink = _context.BeatSaverLinks.FirstOrDefault(link => link.Id == id);
            if (bslink != null) {
                _context.BeatSaverLinks.Remove(bslink);
            }

            var plink = _context.PatreonLinks.FirstOrDefault(l => l.Id == id);
            if (plink != null) {
                _context.PatreonLinks.Remove(plink);
            }

            var scores = _context.Scores.Where(s => s.PlayerId == id).ToList();
            foreach (var score in scores) {
                string? name = score.Replay.Split("/").LastOrDefault();
                if (name != null) {
                    await _assetsS3Client.DeleteReplay(name);
                }
            }

            Player? player = _context.Players.Where(p => p.Id == id)
                .Include(p => p.Socials)
                .Include(p => p.ProfileSettings)
                .Include(p => p.History)
                .Include(p => p.Changes).FirstOrDefault();

            if (player == null)
            {
                return NotFound();
            }

            player.Socials = null;
            player.ProfileSettings = null;
            player.History = null;
            _context.Players.Remove(player);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/players")]
        [SwaggerOperation(Summary = "Retrieve a list of players (ranking)", Description = "Fetches a paginated and optionally filtered list of players. Filters include sorting by performance points, search, country, maps type, platform, and more.")]
        [SwaggerResponse(200, "List of players retrieved successfully", typeof(ResponseWithMetadata<PlayerResponseWithStats>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Players not found")]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetPlayers(
            [FromQuery, SwaggerParameter("Sorting criteria, default is 'pp' (performance points)")] string sortBy = "pp",
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 50")] int count = 50,
            [FromQuery, SwaggerParameter("Search term for filtering players by username")] string search = "",
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Comma-separated list of countries for filtering")] string countries = "",
            [FromQuery, SwaggerParameter("Type of maps to consider, default is 'ranked'")] string mapsType = "ranked",
            [FromQuery, SwaggerParameter("Type of performance points, default is 'general'")] string ppType = "general",
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Flag to filter only friends, default is false")] bool friends = false,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by amount of pp, default is null")] string? pp_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by total score, default is null")] string? score_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by platform value, default is null")] string? platform = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by role, default is null")] string? role = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by hmd (headset), default is null")] string? hmd = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by clan tags, default is null")] string? clans = null,
            [FromQuery, SwaggerParameter("Value in seconds to filter by the last score time, default is null")] int? activityPeriod = null,
            [FromQuery, SwaggerParameter("Flag to filter only banned players, default is null")] bool? banned = null)
        {
            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                return await GetContextPlayers(sortBy, page, count, search, order, countries, mapsType, ppType, leaderboardContext, friends, pp_range, score_range, platform, role, hmd, clans, activityPeriod, banned);
            }

            IQueryable<Player> request = 
                _readContext
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.Clans)
                .Include(p => p.ProfileSettings);

            string? currentID = HttpContext.CurrentUserID(_context);
            bool showBots = currentID != null ? _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false)
                .FirstOrDefault() : false;

            if (banned != null) {
                var player = await _readContext.Players.FindAsync(currentID);
                if (player == null || !player.Role.Contains("admin"))
                {
                    return NotFound();
                }

                bool bannedUnwrapped = (bool)banned;

                request = request.Where(p => p.Banned == bannedUnwrapped);
            } else {
                request = request.Where(p => !p.Banned || ((showBots || search.Length > 0) && p.Bot));
            }
            if (countries.Length != 0)
            {
                var player = Expression.Parameter(typeof(Player), "p");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in countries.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(player, "Country"), Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<Player, bool>>)Expression.Lambda(exp, player));
            }
            List<string>? ids = null;
            List<PlayerMetadata>? searchMatch = null;
            if (search?.Length > 0) {
                searchMatch = PlayerSearchService.Search(search);
                ids = searchMatch.Select(m => m.Id).ToList();

                request = request.Where(p => ids.Contains(p.Id));
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
                var player = await _readContext.Players.FindAsync(userId);
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
            
            var result = new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = request.Count()
                }
            };

            if (searchMatch?.Count > 0) {
                var matchedAndFiltered = request.Select(p => p.Id).ToList();
                var sorted = matchedAndFiltered
                             .OrderByDescending(p => searchMatch.First(m => m.Id == p).Score)
                             .Skip((page - 1) * count)
                             .Take(count)
                             .ToList();

                request = request.Where(p => sorted.Contains(p.Id));
            } else {
                request = Sorted(request, sortBy, ppType, order, mapsType).Skip((page - 1) * count).Take(count);
            }

            result.Data = request
                .AsSplitQuery()
                .Select(p => new PlayerResponseWithStats
                {
                    Id = p.Id,
                    Name = p.Name,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,
                    ScoreStats = p.ScoreStats,

                    Pp = p.Pp,
                    TechPp= p.TechPp,
                    AccPp = p.AccPp,
                    PassPp = p.PassPp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    LastWeekPp = p.LastWeekPp,
                    LastWeekRank = p.LastWeekRank,
                    LastWeekCountryRank = p.LastWeekCountryRank,
                    Role = p.Role,
                    ProfileSettings = p.ProfileSettings,
                    Clans = p
                            .Clans
                            .OrderBy(c => p.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id)
                            .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                }).ToList().Select(PostProcessSettings);

            if (ids?.Count > 0)
            {
                result.Data = result.Data.OrderBy(e => ids.IndexOf(e.Id));
            }

            return result;
        }

        [NonAction]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetContextPlayers(
            [FromQuery] string sortBy = "pp", 
            [FromQuery] int page = 1, 
            [FromQuery] int count = 50, 
            [FromQuery] string search = "",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string countries = "",
            [FromQuery] string mapsType = "ranked",
            [FromQuery] string ppType = "general",
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] bool friends = false,
            [FromQuery] string? pp_range = null,
            [FromQuery] string? score_range = null,
            [FromQuery] string? platform = null,
            [FromQuery] string? role = null,
            [FromQuery] string? hmd = null,
            [FromQuery] string? clans = null,
            [FromQuery] int? activityPeriod = null,
            [FromQuery] bool? banned = null)
        {
            IQueryable<PlayerContextExtension> request = 
                _context
                .PlayerContextExtensions
                .Where(p => p.Context == leaderboardContext)
                .Include(p => p.ScoreStats)
                .Include(p => p.Player)
                .ThenInclude(p => p.Clans)
                .Include(p => p.Player)
                .ThenInclude(p => p.ProfileSettings);

            string? currentID = HttpContext.CurrentUserID(_context);
            bool showBots = currentID != null ? _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false)
                .FirstOrDefault() : false;

            if (banned != null) {
                var player = await _context.Players.FindAsync(currentID);
                if (player == null || !player.Role.Contains("admin"))
                {
                    return NotFound();
                }

                bool bannedUnwrapped = (bool)banned;

                request = request.Where(p => p.Player.Banned == bannedUnwrapped);
            } else {
                request = request.Where(p => !p.Player.Banned || ((showBots || search.Length > 0) && p.Player.Bot));
            }
            if (countries.Length != 0)
            {
                var player = Expression.Parameter(typeof(PlayerContextExtension), "p");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in countries.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(player, "Country"), Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<PlayerContextExtension, bool>>)Expression.Lambda(exp, player));
            }
            List<string>? ids = null;
            List<PlayerMetadata>? searchMatch = null;
            if (search?.Length > 0) {
                searchMatch = PlayerSearchService.Search(search);
                ids = searchMatch.Select(m => m.Id).ToList();

                request = request.Where(p => ids.Contains(p.PlayerId));
            }

            if (clans != null)
            {
                request = request.Where(p => p.Player.Clans.FirstOrDefault(c => clans.Contains(c.Tag)) != null);
            }
            if (platform != null) {
                var platforms = platform.ToLower().Split(",");
                request = request.Where(p => platforms.Contains(p.ScoreStats.TopPlatform.ToLower()));
            }
            if (role != null)
            {
                var player = Expression.Parameter(typeof(PlayerContextExtension), "p");

                var contains = "".GetType().GetMethod("Contains", new[] { typeof(string) });

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in role.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Call(Expression.Property(Expression.Property(player, "Player"), "Role"), contains, Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<PlayerContextExtension, bool>>)Expression.Lambda(exp, player));
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
                var player = await _readContext.Players.FindAsync(userId);
                if (player == null)
                {
                    return NotFound();
                }
                var friendsContainer = _readContext.Friends.Where(f => f.Id == player.Id).Include(f => f.Friends).FirstOrDefault();
                if (friendsContainer != null)
                {
                    var friendsList = friendsContainer.Friends.Select(f => f.Id).ToList();
                    request = request.Where(p => p.PlayerId == player.Id || friendsList.Contains(p.PlayerId));
                }
                else
                {
                    request = request.Where(p => p.PlayerId == player.Id);
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
            
            var result = new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = request.Count()
                }
            };

            if (searchMatch?.Count > 0) {
                var matchedAndFiltered = request.Select(p => p.PlayerId).ToList();
                var sorted = matchedAndFiltered
                             .OrderByDescending(p => searchMatch.First(m => m.Id == p).Score)
                             .Skip((page - 1) * count)
                             .Take(count)
                             .ToList();

                request = request.Where(p => sorted.Contains(p.PlayerId));
            } else {
                request = Sorted(request, sortBy, ppType, order, mapsType).Skip((page - 1) * count).Take(count);
            }

            result.Data = request.Select(p => new PlayerResponseWithStats
                {
                    Id = p.PlayerId,
                    Name = p.Player.Name,
                    Platform = p.Player.Platform,
                    Avatar = p.Player.Avatar,
                    Country = p.Country,
                    ScoreStats = p.ScoreStats,

                    Pp = p.Pp,
                    TechPp= p.TechPp,
                    AccPp = p.AccPp,
                    PassPp = p.PassPp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    LastWeekPp = p.LastWeekPp,
                    LastWeekRank = p.LastWeekRank,
                    LastWeekCountryRank = p.LastWeekCountryRank,
                    Role = p.Player.Role,
                    EventsParticipating = p.Player.EventsParticipating,
                    PatreonFeatures = p.Player.PatreonFeatures,
                    ProfileSettings = p.Player.ProfileSettings,
                    Clans = p.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                }).ToList().Select(PostProcessSettings);

            if (ids?.Count > 0)
            {
                result.Data = result.Data.OrderBy(e => ids.IndexOf(e.Id));
            }

            return result;
        }

        private IQueryable<T> Sorted<T>(
            IQueryable<T> request, 
            string sortBy, 
            string ppType,
            Order order, 
            string mapsType) where T : IPlayer {
            if (sortBy == "pp") {
                switch (ppType)
                {
                    case "acc":
                        request = request.Order(order, p => p.AccPp);
                        break;
                    case "tech":
                        request = request.Order(order, p => p.TechPp);
                        break;
                    case "pass":
                        request = request.Order(order, p => p.PassPp);
                        break;
                    default:
                        request = request.Order(order, p => p.Pp);
                        break;
                }
            } else if (sortBy == "topPp") {
                switch (ppType)
                {
                    case "acc":
                        request = request.Order(order, p => p.ScoreStats.TopAccPP);
                        break;
                    case "tech":
                        request = request.Order(order, p => p.ScoreStats.TopTechPP);
                        break;
                    case "pass":
                        request = request.Order(order, p => p.ScoreStats.TopPassPP);
                        break;
                    default:
                        request = request.Order(order, p => p.ScoreStats.TopPp);
                        break;
                }
            }

            switch (mapsType)
            {
                case "ranked":
                    switch (sortBy)
                    {
                        case "name":
                            request = request.Order(order, p => p.Name);
                            break;
                        case "rank":
                            request = request
                                .Where(p => p.ScoreStats.AverageRankedRank != 0)
                                .Order(order.Reverse(), p => Math.Round(p.ScoreStats.AverageRankedRank))
                                .ThenOrder(order, p => p.ScoreStats.RankedPlayCount); 
                            break;
                        case "acc":
                            request = request.Order(order, p => p.ScoreStats.AverageRankedAccuracy);
                            break;
                        case "weightedAcc":
                            request = request.Order(order, p => p.ScoreStats.AverageWeightedRankedAccuracy);
                            break;
                        case "top1Count":
                            request = request.Order(order, p => p.ScoreStats.RankedTop1Count);
                            break;
                        case "top1Score":
                            request = request.Order(order, p => p.ScoreStats.RankedTop1Score);
                            break;
                        case "weightedRank":
                            request = request
                                .Where(p => p.ScoreStats != null && p.ScoreStats.AverageWeightedRankedRank != 0)
                                .Order(order.Reverse(), p => p.ScoreStats.AverageWeightedRankedRank);
                            break;
                        case "topAcc":
                            request = request.Order(order, p => p.ScoreStats.TopRankedAccuracy);
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
                        case "maxStreak":
                            request = request.Order(order, p => p.ScoreStats.RankedMaxStreak);
                            break;
                        case "replaysWatched":
                            request = request.Order(order, p => p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched);
                            break;
                        default:
                            break;
                    }
                    break;
                case "unranked":
                    switch (sortBy)
                    {
                        case "name":
                            request = request.Order(order, p => p.Name);
                            break;
                        case "rank":
                            request = request
                                .Where(p => p.ScoreStats.AverageUnrankedRank != 0)
                                .Order(order.Reverse(), p => Math.Round(p.ScoreStats.AverageUnrankedRank))
                                .ThenOrder(order, p => p.ScoreStats.UnrankedPlayCount);
                            break;
                        case "acc":
                            request = request.Order(order, p => p.ScoreStats.AverageUnrankedAccuracy);
                            break;
                        case "weightedAcc":
                            request = request.Order(order, p => p.ScoreStats.AverageUnrankedAccuracy);
                            break;
                        case "top1Count":
                            request = request.Order(order, p => p.ScoreStats.UnrankedTop1Count);
                            break;
                        case "top1Score":
                            request = request.Order(order, p => p.ScoreStats.UnrankedTop1Score);
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
                        case "maxStreak":
                            request = request.Order(order, p => p.ScoreStats.UnrankedMaxStreak);
                            break;
                        case "replaysWatched":
                            request = request.Order(order, p => p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched);
                            break;
                        default:
                            break;
                    }
                    break;
                case "all":
                    switch (sortBy)
                    {
                        case "name":
                            request = request.Order(order, p => p.Name);
                            break;
                        case "rank":
                            request = request
                                .Where(p => p.ScoreStats.AverageRank != 0)
                                .Order(order.Reverse(), p => Math.Round(p.ScoreStats.AverageRank))
                                .ThenOrder(order, p => p.ScoreStats.TotalPlayCount);
                            break;
                        case "top1Count":
                            request = request.Order(order, p => p.ScoreStats.Top1Count);
                            break;
                        case "top1Score":
                            request = request.Order(order, p => p.ScoreStats.Top1Score);
                            break;
                        case "acc":
                            request = request.Order(order, p => p.ScoreStats.AverageAccuracy);
                            break;
                        case "weightedAcc":
                            request = request.Order(order, p => p.ScoreStats.AverageWeightedRankedAccuracy);
                            break;
                        case "topAcc":
                            request = request.Order(order, p => p.ScoreStats.TopAccuracy);
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
                        case "maxStreak":
                            request = request.Order(order, p => p.ScoreStats.MaxStreak);
                            break;
                        case "timing":
                            request = request.Order(order, p => (p.ScoreStats.AverageLeftTiming + p.ScoreStats.AverageRightTiming) / 2);
                            break;
                        case "replaysWatched":
                            request = request.Order(order, p => p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched);
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
