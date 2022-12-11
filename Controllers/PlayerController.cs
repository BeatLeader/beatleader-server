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
            catch { 
                return BadRequest("Id should be a number");
            }
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
                        .Include(p => p.Clans)
                        .Include(p => p.PatreonFeatures)
                        .Include(p => p.ProfileSettings)
                        .Include(p => p.Socials)
                        .Include(p => p.EventsParticipating)
                        .Include(p => p.Changes)
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
                var result = ResponseFullFromPlayer(player);
                if (result.Banned) {
                    result.BanDescription = _context.Bans.OrderByDescending(b => b.Timeset).FirstOrDefault(b => b.PlayerId == player.Id);
                }

                return PostProcessSettings(result);
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
                Int64 userId = long.Parse(id);
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
                        var readStream = new MemoryStream(data);
                        string fileName = player.Id;

                        (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(readStream);
                        fileName += extension;

                        await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                        await _assetsContainerClient.UploadBlobAsync(fileName, stream);

                        player.Avatar = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://beatleadercdn.blob.core.windows.net/assets/") + fileName;
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
                }
            }

            return player;
        }

        [HttpDelete("~/player/{id}")]
        [Authorize]
        public async Task<ActionResult> DeletePlayer(string id)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var bslink = _context.BeatSaverLinks.FirstOrDefault(link => link.Id == id);
            if (bslink != null) {
                _context.BeatSaverLinks.Remove(bslink);
            }

            var plink = _context.PatreonLinks.FirstOrDefault(l => l.Id == id);
            if (plink != null) {
                _context.PatreonLinks.Remove(plink);
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

        [HttpGet("~/playerlink/{login}")]
        [Authorize]
        public async Task<ActionResult<AccountLink>> GetPlayerLink(string login)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
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
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
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
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
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
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
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
            IQueryable<Player> request = 
                _readContext
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.Clans)
                .Include(p => p.ProfileSettings)
                .Where(p => !p.Banned);
            if (countries.Length != 0)
            {
                request = request.Where(p => countries.Contains(p.Country));
            }
            if (search.Length != 0)
            {
                var player = Expression.Parameter(typeof(Player), "p");
                
                var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) });

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
            request = Sorted(request, sortBy, order, mapsType);
            
            return new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = request.Count()
                },
                Data = request.Skip((page - 1) * count).Take(count).Select(p => new PlayerResponseWithStats
                {
                    Id = p.Id,
                    Name = p.Name,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,
                    ScoreStats = p.ScoreStats,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    LastWeekPp = p.LastWeekPp,
                    LastWeekRank = p.LastWeekRank,
                    LastWeekCountryRank = p.LastWeekCountryRank,
                    Role = p.Role,
                    EventsParticipating = p.EventsParticipating,
                    PatreonFeatures = p.PatreonFeatures,
                    ProfileSettings = p.ProfileSettings,
                    Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                }).ToList().Select(PostProcessSettings)
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
                        case "weightedRank":
                            request = request
                                .Where(p => p.ScoreStats != null && p.ScoreStats.AverageWeightedRankedRank != 0)
                                .Order(order == "desc" ? "asc" : "desc", p => p.ScoreStats.AverageWeightedRankedRank);
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
            IQueryable<Player> request = _readContext
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.EventsParticipating)
                .Include(p => p.ProfileSettings)
                .Where(p => !p.Banned);
            
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
                .AsEnumerable()
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
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Include(p => p.Badges).FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound("Player not found");
            }

            Badge? badge = await _context.Badges.FindAsync(badgeId);
            if (badge == null)
            {
                return NotFound("Badge not found");
            }
            if (player.Badges == null) {
                player.Badges = new List<Badge>();
            }

            player.Badges.Add(badge);
            await _context.SaveChangesAsync();

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

            var link = _readContext.AccountLinks.FirstOrDefault(l => l.PCOculusID == id);
            if (link != null)
            {
                string playerId = link.SteamID.Length > 0 ? link.SteamID : id;

                var player = await _readContext.Players.FindAsync(playerId);

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
