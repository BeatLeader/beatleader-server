using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Utils.ResponseUtils;
using BeatLeader_Server.ControllerHelpers;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace BeatLeader_Server.Controllers
{
    public class PlayerController : Controller
    {
        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;
        private readonly IMemoryCache _cache;

        private readonly IServerTiming _serverTiming;

        public PlayerController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            IMemoryCache cache,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;
            _dbFactory = dbFactory;
            _cache = cache;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpGet("~/player/{id}/exists")]
        [SwaggerOperation(Summary = "Check if player with such ID or alias exists", Description = "Retrieves a status of potential Beat Saber profile for a specific player ID.")]
        [SwaggerResponse(200, "Player exists")]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult> Exists(
            [FromRoute, SwaggerParameter("The ID of the player")] string id)
        {
            string userId = await _context.PlayerIdToMain(id);
            
            bool exists = await _context
                        .Players
                        .AsNoTracking()
                        .AnyAsync(p => p.Id == userId);
            if (exists) {
                return Ok();
            } else {
                return NotFound();
            }
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
            string userId = await _context.PlayerIdToMain(id);
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                if (stats) {
                    player = await _context
                        .Players
                        .AsNoTracking()
                        .Where(p => p.Id == userId)
                        .Include(p => p.ScoreStats)
                        .Include(p => p.Badges.OrderBy(b => b.Timeset))
                        .Include(p => p.Clans)
                        .Include(p => p.ProfileSettings)
                        .Include(p => p.Socials)
                        .Include(p => p.Changes)
                        .AsSplitQuery()
                        .FirstOrDefaultAsync();
                } else {
                    player = await _context
                        .Players
                        .AsNoTracking()
                        .Where(p => p.Id == userId)
                        .FirstOrDefaultAsync();
                }
                
            }
            if (player == null)
            {
                using (_serverTiming.TimeAction("lazy"))
                {
                    player = await PlayerControllerHelper.GetLazy(_context, _configuration, id, false);
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
                    Alias = player.Alias,

                    MapperId = player.MapperId != null ? (int)player.MapperId : 0,

                    Banned = player.Banned,
                    Inactive = player.Inactive,
                    Bot = player.Bot,
                    Temporary = player.Temporary,

                    ExternalProfileUrl = player.ExternalProfileUrl,
                    RichBioTimeset = player.RichBioTimeset,
                    SpeedrunStart = player.SpeedrunStart,

                    Badges = player.Badges,
                    Changes = player.Changes,

                    Pp = player.Pp,
                    AccPp = player.AccPp,
                    TechPp = player.TechPp,
                    PassPp = player.PassPp,
                    Rank = player.Rank,
                    CountryRank = player.CountryRank,
                    Level = player.Level,
                    Experience = player.Experience,
                    Prestige = player.Prestige,
                    LastWeekPp = player.LastWeekPp,
                    LastWeekRank = player.LastWeekRank,
                    LastWeekCountryRank = player.LastWeekCountryRank,
                    Role = player.Role,
                    Socials = player.Socials,
                    ProfileSettings = player.ProfileSettings,
                    ClanOrder = player.ClanOrder,
                    Clans = stats && player.Clans != null
                        ? player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color }) 
                        : null
                };
                if (result.Banned) {
                    result.BanDescription = await _context.Bans.OrderByDescending(b => b.Timeset).FirstOrDefaultAsync(b => b.PlayerId == player.Id);
                }

                if (keepOriginalId && result.Id != id) {
                    result.Id = id;
                }

                if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                    var contextExtension = await _context
                        .PlayerContextExtensions
                        .AsNoTracking()
                        .Include(p => p.ScoreStats)
                        .Where(p => p.PlayerId == userId && p.Context == leaderboardContext)
                        .FirstOrDefaultAsync();
                    if (contextExtension != null) {
                        result.ToContext(contextExtension);
                    }
                }

                if (Int64.TryParse(player.Id, out long intId) && intId > 1000000000000000) {
                    var link = await _context
                            .AccountLinks
                            .AsNoTracking()
                            .Where(el => el.SteamID == player.Id || el.PCOculusID == player.Id)
                            .Select(el => new LinkResponse {
                                SteamId = el.SteamID,
                                OculusPCId = el.PCOculusID,
                                QuestId = el.OculusID
                            })
                            .FirstOrDefaultAsync();
                    if (link != null) {
                        result.LinkedIds = link;
                    }
                }

                return PostProcessSettings(result, true);
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
            var social = await _context.PlayerSocial.Where(s => s.Service == "Discord" && s.UserId == id && s.PlayerId != null).FirstOrDefaultAsync();

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
            var social = await _context.PlayerSocial.Where(s => s.Service == "BeatSaver" && s.UserId == id && s.PlayerId != null).FirstOrDefaultAsync();

            if (social == null || social.PlayerId == null) {
                return NotFound();
            }

            return await Get(social.PlayerId, true);
        }

        [HttpGet("~/player/patreon/{id}")]
        [SwaggerOperation(Summary = "Get player with Patreon", Description = "Retrieves a BeatLeader profile data with linked Patreon profile.")]
        [SwaggerResponse(200, "Returns the player's full profile", typeof(PlayerResponseFull))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<PlayerResponseFull>> GetPatreon([FromRoute, SwaggerParameter("Patreon profile ID")] string id)
        {
            var social = await _context.PatreonLinks.Where(s => s.PatreonId == id).FirstOrDefaultAsync();

            if (social == null) {
                return NotFound();
            }

            return await Get(social.Id, true);
        }

        [HttpGet("~/players")]
        [SwaggerOperation(Summary = "Retrieve a list of players (ranking)", Description = "Fetches a paginated and optionally filtered list of players. Filters include sorting by performance points, search, country, maps type, platform, and more.")]
        [SwaggerResponse(200, "List of players retrieved successfully", typeof(ResponseWithMetadata<PlayerResponseWithStats>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Players not found")]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetPlayers(
            [FromQuery, SwaggerParameter("Sorting criteria, default is 'pp' (performance points)")] PlayerSortBy sortBy = PlayerSortBy.Pp,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 50")] int count = 50,
            [FromQuery, SwaggerParameter("Search term for filtering players by username")] string search = "",
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Comma-separated list of countries for filtering")] string countries = "",
            [FromQuery, SwaggerParameter("Type of maps to consider, default is 'ranked'")] MapsType mapsType = MapsType.Ranked,
            [FromQuery, SwaggerParameter("Type of performance points, default is 'general'")] PpType ppType = PpType.General,
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Flag to filter only friends, default is false")] bool friends = false,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by amount of pp, default is null")] string? pp_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by amount of acc pp, default is null")] string? acc_pp_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by amount of pass pp, default is null")] string? pass_pp_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by amount of tech pp, default is null")] string? tech_pp_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by total score count, default is null")] string? score_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by ranked score count, default is null")] string? ranked_score_range = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by platform value, default is null")] string? platform = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by role, default is null")] string? role = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by hmd (headset), default is null")] string? hmd = null,
            [FromQuery, SwaggerParameter("Value in seconds to filter by the last score time, default is null")] int? activityPeriod = null,
            [FromQuery, SwaggerParameter("Unix timestamp to filter by the first score time, default is null")] int? firstScoreTime = null,
            [FromQuery, SwaggerParameter("Unix timestamp to filter by the first score time, default is null")] int? recentScoreTime = null,
            [FromQuery, SwaggerParameter("Mapper status filter, default is null")] MapperStatus? mapperStatus = null,
            [FromQuery, SwaggerParameter("Flag to filter only banned players, default is null")] bool? banned = null)
        {
            if (count < 0 || count > 100) {
                return BadRequest("Please use count between 0 and 100");
            }

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                return await GetContextPlayers(sortBy, page, count, search, order, countries, mapsType, ppType, leaderboardContext, friends, pp_range, acc_pp_range, pass_pp_range, tech_pp_range, score_range, ranked_score_range, platform, role, hmd, activityPeriod, mapperStatus, banned);
            }

            IQueryable<Player> request = 
                _context
                .Players;

            string? currentID = HttpContext.CurrentUserID(_context);
            bool showBots = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false)
                .FirstOrDefaultAsync() : false;

            if (banned != null) {
                var player = await _context.Players.FindAsync(currentID);
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

            
            int? searchIdentifier = null;
            if (search?.Length > 0) {
                var searchMatch = PlayerSearchService.Search(search);

                Random rnd = new Random();
                searchIdentifier = rnd.Next(1, 10000);

                List<string>? startIds = searchMatch.Select(m => m.Id.Split("_").First()).ToList();

                var ids = _context.Players.Where(p => startIds.Contains(p.Id)).Select(p => p.Id).ToList();

                _context.BulkInsert(searchMatch.Where(m => ids.Contains(m.Id)).Select(item => new PlayerSearch {
                    PlayerId = item.Id,
                    Score = item.Score,
                    SearchId = (int)searchIdentifier
                }));

                request = request.Where(p => ids.Contains(p.Id));
            }

            if (platform != null) {
                var player = Expression.Parameter(typeof(Player), "p");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in platform.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(Expression.Property(player, "ScoreStats"), "TopPlatform"), Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<Player, bool>>)Expression.Lambda(exp, player));
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
            if (mapperStatus != null) {
                request = request.Where(p => p.Mapper != null && p.Mapper.Status == mapperStatus);
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
            if (pp_range != null && pp_range.Length > 1)
            {
                try {
                    var array = pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.Pp >= from && p.Pp <= to);
                    }
                } catch { }
            }
            if (acc_pp_range != null && acc_pp_range.Length > 1)
            {
                try {
                    var array = acc_pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.AccPp >= from && p.AccPp <= to);
                    }
                } catch { }
            }
            if (pass_pp_range != null && pass_pp_range.Length > 1)
            {
                try {
                    var array = pass_pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.PassPp >= from && p.PassPp <= to);
                    }
                } catch { }
            }
            if (tech_pp_range != null && tech_pp_range.Length > 1)
            {
                try {
                    var array = tech_pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.TechPp >= from && p.TechPp <= to);
                    }
                } catch { }
            }
            if (score_range != null && score_range.Length > 1) {
                try {
                    var array = score_range.Split(",").Select(s => int.Parse(s)).ToArray();
                    int from = array[0]; int to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.ScoreStats.TotalPlayCount >= from && p.ScoreStats.TotalPlayCount <= to);
                    }
                } catch { }
            }
            if (ranked_score_range != null && ranked_score_range.Length > 1) {
                try {
                    var array = ranked_score_range.Split(",").Select(s => int.Parse(s)).ToArray();
                    int from = array[0]; int to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.ScoreStats.RankedPlayCount >= from && p.ScoreStats.RankedPlayCount <= to);
                    }
                } catch { }
            }
            if (friends) {
                string? userId = HttpContext.CurrentUserID(_context);
                var player = userId != null ? await _context.Players.FindAsync(userId) : null;
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
            if (firstScoreTime != null || activityPeriod != null || recentScoreTime != null) {
                if (recentScoreTime == null && activityPeriod != null) {
                    recentScoreTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - (int)activityPeriod;
                }

                switch (mapsType)
                {
                    case MapsType.Ranked:
                        request = request.Where(p => 
                            (recentScoreTime == null || p.ScoreStats.LastRankedScoreTime >= recentScoreTime) &&
                            (firstScoreTime == null || p.ScoreStats.FirstRankedScoreTime >= firstScoreTime));
                        break;
                    case MapsType.Unranked:
                        request = request.Where(p => 
                            (recentScoreTime == null || p.ScoreStats.LastUnrankedScoreTime >= recentScoreTime) &&
                            (firstScoreTime == null || p.ScoreStats.FirstUnrankedScoreTime >= firstScoreTime));
                        break;
                    case MapsType.All:
                        request = request.Where(p => 
                            (recentScoreTime == null || p.ScoreStats.LastScoreTime >= recentScoreTime) &&
                            (firstScoreTime == null || p.ScoreStats.FirstScoreTime >= firstScoreTime));
                        break;
                }
            }
            
            var result = new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count
                }
            };

            List<string> playerIds = await 
                PlayerControllerHelper.Sorted(leaderboardContext, request, sortBy, ppType, order, mapsType, searchIdentifier)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(p => p.Id)
                .ToListAsync();

            using (var anotherContext = _dbFactory.CreateDbContext()) {
                (result.Metadata.Total, result.Data) = await request.CountAsync().CoundAndResults(
                    anotherContext
                    .Players
                    .TagWithCallerS()
                    .AsNoTracking()
                    .Where(p => playerIds.Contains(p.Id))
                    .Include(p => p.ScoreStats)
                    .Include(p => p.Clans)
                    .Include(p => p.ProfileSettings)
                    .Select(p => new PlayerResponseWithStats
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Platform = p.Platform,
                        Avatar = p.Avatar,
                        Country = p.Country,
                        ScoreStats = p.ScoreStats,
                        Alias = p.Alias,

                        Level = p.Level,
                        Prestige = p.Prestige,
                        Experience = p.Experience,

                        Pp = p.Pp,
                        TechPp= p.TechPp,
                        AccPp = p.AccPp,
                        PassPp = p.PassPp,
                        AllContextsPp = p.AllContextsPp,
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        LastWeekPp = p.LastWeekPp,
                        LastWeekRank = p.LastWeekRank,
                        LastWeekCountryRank = p.LastWeekCountryRank,
                        Role = p.Role,
                        ProfileSettings = p.ProfileSettings,
                        ClanOrder = p.ClanOrder,
                        Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    })
                    .ToListAsync());
            }

            foreach (var item in result.Data)
            {
                PostProcessSettings(item, false);
            }

            if (playerIds.Count > 0) {
                result.Data = result.Data.OrderBy(e => playerIds.IndexOf(e.Id));
            }

            if (searchIdentifier != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = await _context.PlayerSearches.Where(s => s.SearchId == searchIdentifier).ToListAsync();
                    foreach (var item in searchRecords) {
                        _context.PlayerSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            return result;
        }

        // Unfortunately I wasn't able to unify it nicely with General context. So copy-paste we go
        [NonAction]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetContextPlayers(
            PlayerSortBy sortBy = PlayerSortBy.Pp, 
            int page = 1, 
            int count = 50, 
            string search = "",
            Order order = Order.Desc,
            string countries = "",
            MapsType mapsType = MapsType.Ranked,
            PpType ppType = PpType.General,
            LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            bool friends = false,
            string? pp_range = null,
            string? acc_pp_range = null,
            string? pass_pp_range = null,
            string? tech_pp_range = null,
            string? score_range = null,
            string? ranked_score_range = null,
            string? platform = null,
            string? role = null,
            string? hmd = null,
            int? activityPeriod = null,
            MapperStatus? mapperStatus = null,
            bool? banned = null)
        {
            IQueryable<PlayerContextExtension> request = 
                _context
                .PlayerContextExtensions
                .AsNoTracking()
                .Where(p => p.Context == leaderboardContext)
                .Include(p => p.ScoreStats)
                .Include(p => p.PlayerInstance)
                .ThenInclude(p => p.Clans)
                .Include(p => p.PlayerInstance)
                .ThenInclude(p => p.ProfileSettings);

            string? currentID = HttpContext.CurrentUserID(_context);
            bool showBots = currentID != null ? await _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false)
                .FirstOrDefaultAsync() : false;

            if (banned != null) {
                var player = await _context.Players.FindAsync(currentID);
                if (player == null || !player.Role.Contains("admin"))
                {
                    return NotFound();
                }

                bool bannedUnwrapped = (bool)banned;

                request = request.Where(p => p.PlayerInstance.Banned == bannedUnwrapped);
            } else {
                request = request.Where(p => !p.PlayerInstance.Banned || ((showBots || search.Length > 0) && p.PlayerInstance.Bot));
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

            int? searchIdentifier = null;
            if (search?.Length > 0) {
                var searchMatch = PlayerSearchService.Search(search);

                Random rnd = new Random();
                searchIdentifier = rnd.Next(1, 10000);

                foreach (var item in searchMatch) {
                    _context.PlayerSearches.Add(new PlayerSearch {
                        PlayerId = item.Id.Split("_").First(),
                        Score = item.Score,
                        SearchId = (int)searchIdentifier
                    });
                }
                _context.BulkSaveChanges();

                List<string>? ids = searchMatch.Select(m => m.Id).ToList();

                request = request.Where(p => ids.Contains(p.PlayerId));
            }

            if (platform?.Length > 0) {
                var player = Expression.Parameter(typeof(PlayerContextExtension), "p");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in platform.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(Expression.Property(player, "ScoreStats"), "TopPlatform"), Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<PlayerContextExtension, bool>>)Expression.Lambda(exp, player));
            }
            if (role != null)
            {
                var player = Expression.Parameter(typeof(PlayerContextExtension), "p");

                var contains = "".GetType().GetMethod("Contains", new[] { typeof(string) });

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in role.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Call(Expression.Property(Expression.Property(player, "PlayerInstance"), "Role"), contains, Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<PlayerContextExtension, bool>>)Expression.Lambda(exp, player));
            }
            if (mapperStatus != null) {
                request = request.Where(p => p.PlayerInstance.Mapper != null && p.PlayerInstance.Mapper.Status == mapperStatus);
            }
            if (hmd != null) {
                try {
                    var hmds = hmd.ToLower().Split(",").Select(s => (HMD)Int32.Parse(s));
                    request = request.Where(p => hmds.Contains(p.ScoreStats.TopHMD));
                } catch { }
            }
            if (pp_range != null && pp_range.Length > 1)
            {
                try {
                    var array = pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.Pp >= from && p.Pp <= to);
                    }
                } catch { }
            }
            if (acc_pp_range != null && acc_pp_range.Length > 1)
            {
                try {
                    var array = acc_pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.AccPp >= from && p.AccPp <= to);
                    }
                } catch { }
            }
            if (pass_pp_range != null && pass_pp_range.Length > 1)
            {
                try {
                    var array = pass_pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.PassPp >= from && p.PassPp <= to);
                    }
                } catch { }
            }
            if (tech_pp_range != null && tech_pp_range.Length > 1)
            {
                try {
                    var array = tech_pp_range.Split(",").Select(s => float.Parse(s)).ToArray();
                    float from = array[0]; float to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.TechPp >= from && p.TechPp <= to);
                    }
                } catch { }
            }
            if (score_range != null && score_range.Length > 1) {
                try {
                    var array = score_range.Split(",").Select(s => int.Parse(s)).ToArray();
                    int from = array[0]; int to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.ScoreStats.TotalPlayCount >= from && p.ScoreStats.TotalPlayCount <= to);
                    }
                } catch { }
            }
            if (ranked_score_range != null && ranked_score_range.Length > 1) {
                try {
                    var array = ranked_score_range.Split(",").Select(s => int.Parse(s)).ToArray();
                    int from = array[0]; int to = array[1];
                    if (!float.IsNaN(from) && !float.IsNaN(to)) {
                        request = request.Where(p => p.ScoreStats.RankedPlayCount >= from && p.ScoreStats.RankedPlayCount <= to);
                    }
                } catch { }
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
                    request = request.Where(p => p.PlayerId == player.Id || friendsList.Contains(p.PlayerId));
                }
                else
                {
                    request = request.Where(p => p.PlayerId == player.Id);
                }
            }
            if (activityPeriod != null) {
                int timetreshold = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - (int)activityPeriod;

                switch (mapsType) {
                    case MapsType.Ranked:
                        request = request.Where(p => p.ScoreStats.LastRankedScoreTime >= timetreshold);
                        break;
                    case MapsType.Unranked:
                        request = request.Where(p => p.ScoreStats.LastUnrankedScoreTime >= timetreshold);
                        break;
                    case MapsType.All:
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
                    Total = await request.CountAsync()
                }
            };

            List<int> playerIds = await 
                PlayerControllerHelper.Sorted(leaderboardContext, request, sortBy, ppType, order, mapsType, searchIdentifier)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(p => p.Id)
                .ToListAsync();

            using (var anotherContext = _dbFactory.CreateDbContext()) {
                (result.Metadata.Total, result.Data) = await request.CountAsync().CoundAndResults(
                    anotherContext
                    .PlayerContextExtensions
                    .TagWithCaller()
                    .AsNoTracking()
                    .Where(p => playerIds.Contains(p.Id))
                    .AsSplitQuery()
                    .Select(p => new PlayerResponseWithStats
                    {
                        Id = p.PlayerId,
                        ExtensionId = p.Id,
                        Name = p.PlayerInstance.Name,
                        Platform = p.PlayerInstance.Platform,
                        Avatar = p.PlayerInstance.Avatar,
                        Country = p.Country,
                        ScoreStats = p.ScoreStats,
                        Alias = p.PlayerInstance.Alias,

                        Pp = p.Pp,
                        TechPp= p.TechPp,
                        AccPp = p.AccPp,
                        PassPp = p.PassPp,
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        LastWeekPp = p.LastWeekPp,
                        LastWeekRank = p.LastWeekRank,
                        LastWeekCountryRank = p.LastWeekCountryRank,
                        Role = p.PlayerInstance.Role,
                        PatreonFeatures = p.PlayerInstance.PatreonFeatures,
                        ProfileSettings = p.PlayerInstance.ProfileSettings,
                        ClanOrder = p.PlayerInstance.ClanOrder,
                        Clans = p.PlayerInstance.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    })
                    .ToListAsync());
            }

            foreach (var item in result.Data)
            {
                PostProcessSettings(item, false);
            }

            if (playerIds.Count > 0) {
                result.Data = result.Data.OrderBy(e => playerIds.IndexOf(e.ExtensionId));
            }

            if (searchIdentifier != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = await _context.PlayerSearches.Where(s => s.SearchId == searchIdentifier).ToListAsync();
                    foreach (var item in searchRecords) {
                        _context.PlayerSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            return result;
        }

        [HttpGet("~/player/{id}/eventsparticipating")]
        [SwaggerOperation(Summary = "Get events where player participated", Description = "Retrieves a chronological list of events player with such ID took part of.")]
        [SwaggerResponse(200, "Returns list of events player took part", typeof(ParticipatingEventResponse))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<ICollection<ParticipatingEventResponse>>> GetParticipatingEvents(
            [FromRoute, SwaggerParameter("The ID of the player")] string id)
        {
            return await _context
                .EventPlayer
                .AsNoTracking()
                .Where(ep => ep.PlayerId == id)
                .OrderByDescending(ep => ep.Event.EndDate)
                .Select(ep => new ParticipatingEventResponse {
                    Id = ep.EventRankingId,
                    Name = ep.EventName
                }).ToListAsync();
        }

        [HttpGet("~/player/{id}/followersInfo")]
        [SwaggerOperation(Summary = "Get player' followers and players they follow", Description = "Retrieves an info about player' followers such as count and 3 closest followers. Also 3 most followed players this player follows")]
        [SwaggerResponse(200, "Returns brief info about player followers.", typeof(PlayerFollowersInfoResponse))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<PlayerFollowersInfoResponse>> GetFollowersInfo(
            [FromRoute, SwaggerParameter("The ID of the player")] string id) {
            string? currentID = HttpContext.CurrentUserID(_context);
            id = await _context.PlayerIdToMain(id);

            (var allFollowersIds, var followers) = await PlayerControllerHelper.GetPlayerFollowers(_context, id, 1, 3);
            var followersIds = followers.Select(f => f.Id).ToList(); 

            (var allFollowingIds, var following) = await PlayerControllerHelper.GetPlayerFollowing(_context, id, currentID, 1, 3);
            var followingIds = following.Select(f => f.Id).ToList(); 

            return new PlayerFollowersInfoResponse {
                FollowingCount = allFollowingIds.Count,
                MeFollowing = allFollowingIds.FirstOrDefault(f => f == currentID) != null,
                Following = (await _context
                    .Players
                    .Where(p => followingIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Avatar })
                    .ToListAsync())
                    .OrderBy(f => followingIds.IndexOf(f.Id))
                    .Select(f => f.Avatar)
                    .ToList(),

                FollowersCount = allFollowersIds.Count,
                IFollow = allFollowersIds.FirstOrDefault(f => f == currentID) != null,
                Followers = (await _context
                    .Players
                    .Where(p => followersIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Avatar })
                    .ToListAsync())
                    .OrderBy(f => followingIds.IndexOf(f.Id))
                    .Select(f => f.Avatar)
                    .ToList()
            };
        }

        [HttpGet("~/player/{id}/followers")]
        [SwaggerOperation(Summary = "Get player's full follower list", Description = "Retrieves a full list of player' followers and players this player follow.")]
        [SwaggerResponse(200, "Returns list of players", typeof(ICollection<PlayerFollower>))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<ICollection<PlayerFollower>>> GetFollowers(
            [FromRoute, SwaggerParameter("The ID of the player")] string id,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Relationship type: followers or following")] FollowerType type = FollowerType.Followers) {

            string? currentID = HttpContext.CurrentUserID(_context);
            id = await _context.PlayerIdToMain(id);

            (var allFollowersIds, var followers) = 
                        type == FollowerType.Followers 
                        ? await PlayerControllerHelper.GetPlayerFollowers(_context, id, page, count)
                        : await PlayerControllerHelper.GetPlayerFollowing(_context, id, currentID, page, count);
            var followersIds = followers.Select(f => f.Id).ToList(); 

            var result = await _context.Players.Where(p => followersIds.Contains(p.Id)).Select(p => new PlayerFollower {
                Id = p.Id,
                Alias = p.Alias,
                Name = p.Name,
                Avatar = p.Avatar
            }).ToListAsync();
            foreach (var item in result) {
                var follower = followers.FirstOrDefault(f => f.Id == item.Id);
                item.Count = follower?.Count;
                item.Mutual = follower?.Mutual ?? false;
            }

            return result.OrderByDescending(f => f.Count ?? 0).ToList();
        }

        [HttpGet("~/player/{id}/foundedClan")]
        [SwaggerOperation(Summary = "Get info about the clan this player founded", Description = "Retrieves an information about the clan this player created and manage.")]
        [SwaggerResponse(200, "Returns brief info about the clan", typeof(ClanBiggerResponse))]
        [SwaggerResponse(404, "Player not found or player doesn't found any clans")]
        public async Task<ActionResult<ClanBiggerResponse>> GetFoundedClan(
            [FromRoute, SwaggerParameter("The ID of the player")] string id) {
            id = await _context.PlayerIdToMain(id);
            string? currentID = HttpContext.CurrentUserID(_context);
            var result = await _context.Clans.Where(c => c.LeaderID == id).Select(c => new ClanBiggerResponse {
                Id = c.Id,
                Tag = c.Tag,
                Color = c.Color,
                Name = c.Name,
                Icon = c.Icon,
                RankedPoolPercentCaptured = c.RankedPoolPercentCaptured,

                PlayersCount = c.PlayersCount,
                Joined = currentID != null && c.Players.FirstOrDefault(p => p.Id == currentID) != null
            }).FirstOrDefaultAsync();
            if (result == null) {
                return NotFound();
            }
            return result;
        }

        [HttpGet("~/player/{id}/rankedMaps")]
        [SwaggerOperation(Summary = "Get ranked maps this player mapped", Description = "Retrieves a list of maps this player created that later became ranked and give PP now.")]
        [SwaggerResponse(200, "Returns brief stats about maps this player ranked, like count, total PP gained, etc...", typeof(RankedMapperResponse))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<RankedMapperResponse>> GetRankedMaps(
            [FromRoute, SwaggerParameter("The ID of the player")] int id,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? sortBy = null) {

            var lbs = await _context
                .Leaderboards
                .Where(lb =>
                    lb.Difficulty.Status == DifficultyStatus.ranked &&
                    lb.Song.Mappers.FirstOrDefault(m => m.Id == id) != null)
                .Select(lb => new { 
                    Plays = leaderboardContext == LeaderboardContexts.General ? lb.Plays : lb.ContextExtensions.Where(ce => ce.Context == leaderboardContext).Count(),
                    lb.Song.UploadTime,
                    lb.Difficulty.Stars,
                    lb.SongId,
                    Pp = leaderboardContext == LeaderboardContexts.General ? lb.Scores.Sum(s => s.Pp) : lb.ContextExtensions.Where(ce => ce.Context == leaderboardContext).Sum(s => s.Pp) })
                .ToListAsync();

            if (lbs.Count == 0) {
                return NotFound();
            }

            var result = new RankedMapperResponse();
            result.TotalMapCount = lbs.Count;
            result.TotalPp = lbs.Sum(lb => lb.Pp);
            result.PlayersCount = lbs.Sum(lb => lb.Plays);

            switch (sortBy) {
                case "top-stars":
                    lbs = lbs.OrderByDescending(s => s.Stars ?? 0).ToList();
                    break;
                case "top-played":
                    lbs = lbs.OrderByDescending(s => s.Plays).ToList();
                    break;
                case "top-grinded":
                    lbs = lbs.OrderByDescending(s => s.Pp).ToList();
                    break;
                default:
                    lbs = lbs.OrderByDescending(s => s.UploadTime).ToList();
                    break;
            }

            var songIds = lbs.GroupBy(lb => lb.SongId).Take(4).Select(g => g.First().SongId).ToList();

            result.Maps = 
                (await _context
                .Songs
                .Where(s => songIds.Contains(s.Id))
                .Select(s => new RankedMap {
                    Name = s.Name,
                    SongId = s.Id,
                    Stars = s.Difficulties.OrderByDescending(d => d.Stars).Select(d => d.Stars).First(),
                    Cover = s.CoverImage
                })
                .ToListAsync())
                .OrderBy(s => songIds.IndexOf(s.SongId))
                .ToList();

            return result;
        }

        [HttpGet("~/player/{id}/officialmaps")]
        [SwaggerOperation(Summary = "Get ranked maps this player mapped", Description = "Retrieves a list of maps this player created that later became ranked and give PP now.")]
        [SwaggerResponse(200, "Returns brief stats about maps this player ranked, like count, total PP gained, etc...", typeof(RankedMapperResponse))]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult<RankedMapperResponse>> GetOfficialMaps(
            [FromRoute, SwaggerParameter("The ID of the player")] int id,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? sortBy = null) {

            var lbs = await _context
                .Leaderboards
                .Where(lb =>
                    lb.Difficulty.Status == DifficultyStatus.OST &&
                    lb.Song.Mappers.FirstOrDefault(m => m.Id == id) != null)
                .Select(lb => new { 
                    Plays = leaderboardContext == LeaderboardContexts.General ? lb.Plays : lb.ContextExtensions.Where(ce => ce.Context == leaderboardContext).Count(),
                    lb.Song.UploadTime,
                    lb.Difficulty.Stars,
                    lb.SongId })
                .ToListAsync();

            if (lbs.Count == 0) {
                return NotFound();
            }

            var result = new RankedMapperResponse();
            result.TotalMapCount = lbs.Count;
            result.PlayersCount = lbs.Sum(lb => lb.Plays);

            switch (sortBy) {
                case "top-stars":
                    lbs = lbs.OrderByDescending(s => s.Stars ?? 0).ToList();
                    break;
                case "top-played":
                    lbs = lbs.OrderByDescending(s => s.Plays).ToList();
                    break;
                default:
                    lbs = lbs.OrderByDescending(s => s.UploadTime).ToList();
                    break;
            }

            var songIds = lbs.GroupBy(lb => lb.SongId).Take(4).Select(g => g.First().SongId).ToList();

            result.Maps = 
                (await _context
                .Songs
                .Where(s => songIds.Contains(s.Id))
                .Select(s => new RankedMap {
                    Name = s.Name,
                    SongId = s.Id,
                    Stars = s.Difficulties.OrderByDescending(d => d.Stars).Select(d => d.Stars).First(),
                    Cover = s.CoverImage
                })
                .ToListAsync())
                .OrderBy(s => songIds.IndexOf(s.SongId))
                .ToList();

            return result;
        }

        [HttpGet("~/player/{id}/ingameavatar")]
        public async Task<ActionResult<AvatarData>> GetIngameAvatar([FromRoute] string id) {
            AvatarData? result = null;
            var ingameAvatar = await _context.IngameAvatars.FirstOrDefaultAsync(a => a.PlayerID == id);
            if (ingameAvatar == null) {
                if ((await _context.Players.FirstOrDefaultAsync(p => p.Id == id)) == null) {
                    return NotFound();
                }
                result = AvatarData.Random(); 
                _context.IngameAvatars.Add(new IngameAvatar {
                    PlayerID = id,
                    Value = JsonConvert.SerializeObject(result)
                });
                await _context.SaveChangesAsync();
            } else {
                result = JsonConvert.DeserializeObject<AvatarData>(ingameAvatar.Value); 
            }

            return result;
        }

        [HttpPost("~/player/{id}/ingameavatar")]
        public async Task<ActionResult> UpdateIngameAvatar([FromRoute] string id, [FromBody] AvatarData avatarData) {
            string? currentID = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.FindAsync(currentID);
            if (player == null || !(player.Role.Contains("admin") || currentID == id))
            {
                return NotFound();
            }

            var ingameAvatar = await _context.IngameAvatars.FirstOrDefaultAsync(a => a.PlayerID == id);
            if (ingameAvatar == null) {
                ingameAvatar = new IngameAvatar {
                    PlayerID = id
                };
                _context.IngameAvatars.Add(ingameAvatar);
            }

            ingameAvatar.Value = JsonConvert.SerializeObject(avatarData);
            await _context.SaveChangesAsync();

            return Ok();
        }

        public class PlayersTopResponse {
            public float Pp { get; set; }
            public float TechPp { get; set; }
            public float AccPp { get; set; }
            public float PassPp { get; set; }

            public int PlayCount { get; set; }
            public int RankedPlayCount { get; set; }
        }

        [HttpGet("~/players/top")]
        public async Task<ActionResult<PlayersTopResponse>> TopPlayers(
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            var cacheKey = $"TopPlayers_{leaderboardContext}";
            var cachedResponse = _cache.Get<PlayersTopResponse>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            PlayersTopResponse response;

            if (leaderboardContext == LeaderboardContexts.General) {
                response = new PlayersTopResponse { 
                    Pp = await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.Pp).Select(p => p.Pp).FirstOrDefaultAsync(),
                    TechPp = await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.TechPp).Select(p => p.TechPp).FirstOrDefaultAsync(),
                    AccPp = await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.AccPp).Select(p => p.AccPp).FirstOrDefaultAsync(),
                    PassPp = await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.PassPp).Select(p => p.PassPp).FirstOrDefaultAsync(),

                    PlayCount = await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.ScoreStats.TotalPlayCount).Select(p => p.ScoreStats.TotalPlayCount).FirstOrDefaultAsync(),
                    RankedPlayCount = await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.ScoreStats.RankedPlayCount).Select(p => p.ScoreStats.RankedPlayCount).FirstOrDefaultAsync(),
                };
            } else {
                response = new PlayersTopResponse { 
                    Pp = await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.Pp).Select(p => p.Pp).FirstOrDefaultAsync(),
                    TechPp = await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.TechPp).Select(p => p.TechPp).FirstOrDefaultAsync(),
                    AccPp = await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.AccPp).Select(p => p.AccPp).FirstOrDefaultAsync(),
                    PassPp = await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.PassPp).Select(p => p.PassPp).FirstOrDefaultAsync(),

                    PlayCount = await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.ScoreStats.TotalPlayCount).Select(p => p.ScoreStats.TotalPlayCount).FirstOrDefaultAsync(),
                    RankedPlayCount = await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.ScoreStats.RankedPlayCount).Select(p => p.ScoreStats.RankedPlayCount).FirstOrDefaultAsync(),
                };
            }

            _cache.Set(cacheKey, response, TimeSpan.FromDays(1));
            return response;
        }
        
        [HttpGet("~/players/top/pp")]
        public async Task<ActionResult<float>> TopPp(
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            if (leaderboardContext == LeaderboardContexts.General) {
                return await _context.Players.Where(p => !p.Banned).OrderByDescending(p => p.Pp).Select(p => p.Pp).FirstOrDefaultAsync();
            } else {
                return await _context.PlayerContextExtensions.Where(p => !p.Banned && p.Context == leaderboardContext).OrderByDescending(p => p.Pp).Select(p => p.Pp).FirstOrDefaultAsync();
            }
        }
    }
}
