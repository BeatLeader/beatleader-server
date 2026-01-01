using Amazon.S3;
using AngleSharp;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Z.BulkOperations;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class CurrentUserController : ControllerBase {
        private readonly AppContext _context;

        PlayerRefreshController _playerRefreshController;
        PlayerContextRefreshController _playerContextRefreshController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;
        Microsoft.Extensions.Configuration.IConfiguration _configuration;
        ScoreRefreshController _scoreRefreshController;
        IAmazonS3 _s3Client;

        public CurrentUserController(
            AppContext context,
            IWebHostEnvironment env,
            PlayerRefreshController playerRefreshController,
            PlayerContextRefreshController playerContextRefreshController,
            ReplayController replayController,
            ScoreRefreshController scoreRefreshController,
            Microsoft.Extensions.Configuration.IConfiguration configuration) {
            _context = context;

            _playerRefreshController = playerRefreshController;
            _playerContextRefreshController = playerContextRefreshController;
            _replayController = replayController;
            _scoreRefreshController = scoreRefreshController;
            _configuration = configuration;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/user/id")]
        public ActionResult<string> GetIdResult() => GetId();

        [NonAction]
        public string? GetId() => HttpContext.CurrentUserID(_context);

        [HttpGet("~/user")]
        public async Task<ActionResult<UserReturn>> GetCurrentUser() {
            User? user = await GetUserLazy(GetId(), true);
            if (user == null) {
                return Unauthorized();
            }
            string id = user.Id;

            PlayerFriends? friends = await _context.Friends.AsNoTracking().Include(f => f.Friends).ThenInclude(f => f.ProfileSettings).FirstOrDefaultAsync(f => f.Id == id);
            Clan? clan = await _context.Clans.AsNoTracking().Include(c => c.Players).FirstOrDefaultAsync(f => f.LeaderID == id);

            long intId = Int64.Parse(id);
            AccountLink? link = null;
            if (intId > 1000000000000000) {
                link = await _context.AccountLinks.AsNoTracking().FirstOrDefaultAsync(el => el.SteamID == id || el.PCOculusID == id);
                if (link != null) {
                    intId = link.OculusID;
                }
            }

            ClanReturn? clanReturn =
            clan != null ? new ClanReturn {
                Id = clan.Id,
                Name = clan.Name,
                Color = clan.Color,
                Icon = clan.Icon,
                Tag = clan.Tag,
                LeaderID = clan.LeaderID,

                PlayersCount = clan.PlayersCount,
                Pp = clan.Pp,
                AverageRank = clan.AverageRank,
                AverageAccuracy = clan.AverageAccuracy,
                RankedPoolPercentCaptured = clan.RankedPoolPercentCaptured,
                CaptureLeaderboardsCount = clan.CaptureLeaderboardsCount,
                Players = clan.Players.Select(p => p.Id).ToList(),
                PendingInvites = await _context.Users.AsNoTracking().Where(u => u.ClanRequest.Contains(clan)).Select(f => f.Id).ToListAsync(),
            } : null;
            var player = user.Player;

            var playerResponse = ResponseFullFromPlayer(player);
            if (playerResponse != null) {
                PostProcessSettings(playerResponse.Role, playerResponse.ProfileSettings, playerResponse.PatreonFeatures, false);
            } 

            int timeset = Time.UnixNow();

            return new UserReturn {
                Player = playerResponse,
                Ban = player.Banned ? (await _context
                    .Bans
                    .AsNoTracking()
                    .Where(b => b.PlayerId == player.Id)
                    .OrderByDescending(b => b.Timeset)
                    .Select(b => new BanReturn { Reason = b.BanReason, Duration = b.Duration, Timeset = b.Timeset })
                    .FirstOrDefaultAsync()) : null,
                ClanRequest = user.ClanRequest,
                BannedClans = user.BannedClans,
                Friends = friends != null ? friends.Friends.Select(ResponseFullFromPlayer).Select(p => PostProcessSettings(p, true)).ToList() : new List<PlayerResponseFull>(),
                HideFriends = friends?.HideFriends ?? false,
                AliasRequest = await _context
                    .AliasRequests
                    .Where(ar => ar.PlayerId == id && (ar.Status == AliasRequestStatus.open || (timeset - ar.Timeset) < 60 * 60 * 24 * 7))
                    .FirstOrDefaultAsync(),
                Login = (await _context.Auths.AsNoTracking().FirstOrDefaultAsync(a => a.Id == intId))?.Login,
                PlaylistsToInstall = user.PlaylistsToInstall,
                Ids = link == null ? null : new List<string?> { link.OculusID.ToString(), link.PCOculusID, link.SteamID },

                Migrated = (await _context.AccountLinks.AsNoTracking().FirstOrDefaultAsync(a => a.SteamID == id)) != null,
                Patreoned = await _context.PatreonLinks.FindAsync(id) != null,
                Clan = clanReturn
            };
        }

        [HttpGet("~/user/modinterface")]
        public async Task<ActionResult<PlayerResponseWithFriends>> GetCurrentUserMod() {
            string? id = GetId();
            if (id == null) {
                return Unauthorized();
            }

            PlayerResponseWithFriends? result = await _context
                .Players
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(p => new PlayerResponseWithFriends {
                    Id = p.Id,
                    Name = p.Name,
                    Alias = p.Alias,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,

                    Level = p.Level,
                    Experience = p.Experience,
                    Prestige = p.Prestige,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    Role = p.Role,
                    Socials = p.Socials,
                    PatreonFeatures = p.PatreonFeatures,
                    ProfileSettings = p.ProfileSettings,
                    ContextExtensions = p.ContextExtensions,
                    Clans = p.Clans.OrderBy(c => ("," + p.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + p.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                                .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color, Name = c.Name }),
                    GlobalWatermarkPermissions = p.DeveloperProfile != null ? p.DeveloperProfile.GlobalWatermarkPermissions : false
                })
                .AsSplitQuery()
                .FirstOrDefaultAsync();
            if (result == null) {
                var player = await PlayerControllerHelper.GetLazy(_context, _configuration, id);
                if (player == null) {
                    return Unauthorized();
                }
                _context.Users.Add(new User {
                    Id = id,
                    Player = player
                });
                await _context.SaveChangesAsync();
                result = GeneralResponseFromPlayer<PlayerResponseWithFriends>(player);
            }
            
            var friends = await _context.Friends.Where(f => f.Id == id).Select(f => new { Friends = f.Friends.Select(f => f.Id) }).FirstOrDefaultAsync();
            result.Friends = friends?.Friends.ToList() ?? new List<string>();
            result.QuestId = (await _context.AccountLinks.AsNoTracking().Where(al => al.SteamID == id).FirstOrDefaultAsync())?.OculusID.ToString();
            result.PlaylistsToInstall = await _context.Users.AsNoTracking().Where(al => al.Id == id).Select(u => u.PlaylistsToInstall).FirstOrDefaultAsync();
            return result;
        }

        [HttpPost("~/oculususer")]
        public async Task<ActionResult<OculusUser>> PostOculusUser([FromForm] string token) {
            return await GetOculusUser(token);
        }

        [HttpGet("~/oculususer")]
        public async Task<ActionResult<OculusUser>> GetOculusUser([FromQuery] string? token = null)
        {
            string? id = token == null ? GetId() : null;

            if (id == null && token != null) {
                (id, string? error) = await SteamHelper.GetPlayerIDFromTicket(token, _configuration);
            }
            
            if (id == null)
            {
                return NotFound();
            }

            var link = await _context.AccountLinks.FirstOrDefaultAsync(l => l.PCOculusID == id);
            if (link != null)
            {
                string playerId = link.SteamID.Length > 0 ? link.SteamID : id;

                var player = await _context.Players.FindAsync(playerId);

                return new OculusUser
                {
                    Id = id,
                    Migrated = true,
                    MigratedId = playerId,
                    Name = player.Name,
                    Avatar = player.Avatar,
                };
            }

            if (token != null) {
                var oculusPlayer = await PlayerUtils.GetPlayerFromOculus(id, token);

                return new OculusUser
                {
                    Id = id,
                    Name = oculusPlayer.Name,
                    Avatar = oculusPlayer.Avatar,
                };
            } else {
                return new OculusUser
                {
                    Id = id,
                    Name = "",
                    Avatar = "",
                };
            }
        }

        [NonAction]
        public async Task<User?> GetUserLazy(string? id, bool notracking = false) {
            if (string.IsNullOrEmpty(id)) {
                return null;
            }

            User? user =  await (notracking 
                ? _context.Users.AsNoTracking() 
                : _context.Users)
                .Where(u => u.Id == id)
                .Include(u => u.Player)
                .ThenInclude(p => p.Clans)
                .Include(u => u.Player)
                .ThenInclude(p => p.ScoreStats)
                .Include(u => u.Player)
                .ThenInclude(p => p.Socials)
                .Include(u => u.Player)
                .ThenInclude(p => p.ContextExtensions)
                .Include(u => u.Player)
                .ThenInclude(p => p.ProfileSettings)
                .Include(u => u.ClanRequest)
                .Include(u => u.BannedClans)
                .AsSplitQuery()
                .FirstOrDefaultAsync();
            if (user == null) {
                var player = await PlayerControllerHelper.GetLazy(_context, _configuration, id);
                if (player == null) {
                    return null;
                }

                user = new User {
                    Id = id,
                    Player = player
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }

            return user;
        }

        [HttpPost("~/user/friend")]
        public async Task<ActionResult> AddFriend([FromQuery] string playerId) {
            string? id = GetId();
            if (id == null) {
                return Unauthorized();
            }

            if (playerId == id) {
                return BadRequest("Couldnt add user as a friend to himself");
            }
            PlayerFriends? playerFriends = await _context.Friends.Where(u => u.Id == id).Include(p => p.Friends).FirstOrDefaultAsync();
            if (playerFriends == null) {
                playerFriends = new PlayerFriends { Id = id, Friends = new List<Player>() };
                _context.Friends.Add(playerFriends);
            }

            if (playerFriends.Friends.Count >= 250) {
                return BadRequest("You hit the limit of friends, please remove someone first.");
            }

            if (playerFriends.Friends.Any(p => p.Id == playerId)) {
                return Ok();
            }

            Player? friend = await _context.Players.FirstOrDefaultAsync(p => p.Id == playerId);
            if (friend == null) {
                return NotFound();
            }

            playerFriends.Friends.Add(friend);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/user/friend")]
        public async Task<ActionResult> RemoveFriend([FromQuery] string playerId) {
            string? id = GetId();
            if (id == null) {
                return Unauthorized();
            }
            PlayerFriends? playerFriends = await _context.Friends.Where(u => u.Id == id).Include(p => p.Friends).FirstOrDefaultAsync();
            if (playerFriends == null) {
                return NotFound();
            }
            Player? friend = playerFriends.Friends.FirstOrDefault(p => p.Id == playerId);
            if (friend == null) {
                return NotFound();
            }

            playerFriends.Friends.Remove(friend);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/friends")]
        public async Task<ActionResult> ChangeFriendsSettings([FromQuery] bool is_public) {
            string? id = GetId();
            if (id == null) {
                return Unauthorized();
            }

            PlayerFriends? playerFriends = await _context.Friends.Where(u => u.Id == id).FirstOrDefaultAsync();
            if (playerFriends == null) {
                playerFriends = new PlayerFriends { Id = id, Friends = new List<Player>() };
                _context.Friends.Add(playerFriends);
            }

            playerFriends.HideFriends = !is_public;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user")]
        public async Task<ActionResult> UpdatePlayerAll(
            [FromQuery] string? name = null,
            [FromQuery] string? country = null,
            [FromQuery] string? profileAppearance = null,
            [FromQuery] string? message = null,
            [FromQuery] float? hue = null,
            [FromQuery] float? saturation = null,
            [FromQuery] string? effectName = null,
            [FromQuery] string? leftSaberColor = null,
            [FromQuery] string? rightSaberColor = null,
            [FromQuery] string? starredFriends = null,
            [FromQuery] string? clanOrder = null,
            [FromQuery] bool? showBots = null,
            [FromQuery] bool? showAllRatings = null,
            [FromQuery] bool? showStatsPublic = null,
            [FromQuery] bool? showExplicitCovers = null,
            [FromQuery] bool? showStatsPublicPinned = null,
            [FromQuery] bool? horizontalRichBio = null,
            [FromQuery] string? rankedMapperSort = null,
            [FromQuery] string? hiddenSocials = null,
            [FromQuery] string? id = null) {
            string userId = GetId();
            var player = await _context
                .Players
                .Where(p => p.Id == userId)
                .Include(p => p.ProfileSettings)
                .Include(p => p.PatreonFeatures)
                .Include(p => p.Changes)
                .Include(p => p.Clans)
                .FirstOrDefaultAsync();
            bool adminChange = false;

            if (id != null && player != null && player.Role.Contains("admin")) {
                adminChange = true;
                player = await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .Include(p => p.PatreonFeatures)
                    .Include(p => p.Changes)
                    .Include(p => p.Clans)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player == null) {
                return NotFound();
            }
            if (player.Banned) {
                return BadRequest("You are banned!");
            }

            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (name != null || country != null) {
                var changes = player.Changes;
                if (changes == null) {
                    changes = player.Changes = new List<PlayerChange>();
                }
                PlayerChange? lastChange = changes.Count > 0 ? changes.Where(ch => ch.NewCountry != null).MaxBy(ch => ch.Timestamp) : null;
                PlayerChange newChange = new PlayerChange {
                    OldName = player.Name,
                    OldCountry = player.Country,
                    Timestamp = timestamp,
                    Changer = adminChange ? userId : null,
                };

                if (name != null) {
                    name = Player.SanitizeName(name);

                    if (name.Replace(" ", "").Length is < 3 or > 30) {
                        return BadRequest("Use name between the 3 and 30 symbols");
                    }

                    if (name != player.Name) {
                        var changeBan = await _context.UsernamePfpChangeBans.FirstOrDefaultAsync(b => b.PlayerId == player.Id);
                        if (changeBan != null) {
                            return BadRequest("Username/profile picture change is banned for you!");
                        }

                        player.Name = name;
                        newChange.NewName = name;
                    }
                }

                if (country != null) {
                    var changeBan = await _context.CountryChangeBans.FirstOrDefaultAsync(b => b.PlayerId == player.Id);
                    if (changeBan != null) {
                        return BadRequest("Country change is banned for you!");
                    }

                    if (!PlayerUtils.AllowedCountries().Contains(country)) {
                        return BadRequest("This country code is not allowed.");
                    }

                    if (lastChange != null && !adminChange && (timestamp - lastChange.Timestamp) < 60 * 60 * 24 * 30) {
                        return BadRequest("Error. You can change country after " + (int)(30 - (timestamp - lastChange.Timestamp) / (60 * 60 * 24)) + " day(s)");
                    }
                    newChange.NewCountry = country;

                    var countryList = await _context.Players
                        .Where(p => p.Country == player.Country && p.Id != player.Id)
                        .OrderByDescending(p => p.Pp)
                        .Select(p => new { p.Id })
                        .AsNoTracking()
                        .ToListAsync();
                    var updates = countryList.Select((p, i) => new Player { Id = p.Id, CountryRank = i + 1 }).ToList();
                    await _context.BulkUpdateAsync(updates, options => options.ColumnInputExpression = c => new { c.CountryRank });

                    foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                        var contextCountryList = await _context.PlayerContextExtensions
                        .Where(p => p.Country == player.Country && p.PlayerId != player.Id && p.Context == leaderboardContext)
                        .OrderByDescending(p => p.Pp)
                        .Select(p => new { p.Id })
                        .AsNoTracking()
                        .ToListAsync();

                        var contextUpdates = contextCountryList.Select((p, i) => new PlayerContextExtension { Id = p.Id, CountryRank = i + 1 }).ToList();
                        await _context.BulkUpdateAsync(contextUpdates, options => options.ColumnInputExpression = c => new { c.CountryRank });
                    }

                    player.Country = country;

                    var ces = await _context.PlayerContextExtensions.Where(ce => ce.PlayerId == player.Id).Select(ce => ce.Id).ToListAsync();
                    var cesUpdates = ces.Select(ce => new PlayerContextExtension { Id = ce, Country = country });
                    await _context.BulkUpdateAsync(cesUpdates, options => options.ColumnInputExpression = c => new { c.Country });
                    await _context.BulkSaveChangesAsync();

                    countryList = await _context.Players
                        .Where(p => !p.Banned && (p.Country == country || p.Id == player.Id))
                        .OrderByDescending(p => p.Pp)
                        .Select(p => new { p.Id })
                        .AsNoTracking()
                        .ToListAsync();
                    updates = countryList.Select((p, i) => new Player { Id = p.Id, CountryRank = i + 1 }).ToList();
                    await _context.BulkUpdateAsync(updates, options => options.ColumnInputExpression = c => new { c.CountryRank });

                    foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                        var contextCountryList = await _context.PlayerContextExtensions
                        .Where(p => (p.Country == country || p.PlayerId == player.Id) && p.Context == leaderboardContext)
                        .OrderByDescending(p => p.Pp)
                        .Select(p => new { p.Id })
                        .AsNoTracking()
                        .ToListAsync();

                        var contextUpdates = contextCountryList.Select((p, i) => new PlayerContextExtension { Id = p.Id, CountryRank = i + 1 }).ToList();
                        await _context.BulkUpdateAsync(contextUpdates, options => options.ColumnInputExpression = c => new { c.CountryRank });
                    }
                }

                if (player.Country != newChange.OldCountry || player.Name != newChange.OldName) {
                    player.Changes.Add(newChange);
                    PlayerSearchService.PlayerChangedName(player);
                }
            }

            var ip = Request.HttpContext.GetIpAddress();
            Console.WriteLine($"UPDATE_USER {userId} {ip}");

            string? fileName = null;
            try {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                Random rnd = new Random();
                fileName = userId + "R" + rnd.Next(1, 50) + extension;

                var changeBan = await _context.UsernamePfpChangeBans.FirstOrDefaultAsync(b => b.PlayerId == player.Id);
                if (changeBan != null) {
                    return BadRequest("Username/profile picture change is banned for you!");
                }

                player.Avatar = await _s3Client.UploadAsset(fileName, stream);
                ms.Position = 0;
                MemoryStream webpstream = ImageUtils.ResizeToWebp(ms, 40);
                player.WebAvatar = await _s3Client.UploadAsset(fileName.Replace(extension, ".webp"), webpstream);
            } catch {
            }

            PatreonFeatures? features = player.PatreonFeatures;
            ProfileSettings? settings = player.ProfileSettings;
            if (features == null) {
                features = new PatreonFeatures();
                player.PatreonFeatures = features;
            }
            if (settings == null) {
                settings = new ProfileSettings();
                player.ProfileSettings = settings;
            }
            if (Request.Query.ContainsKey("clanOrder")) {
                var newClanOrder = clanOrder ?? "";

                if (player.ClanOrder != null) {
                    var lastChange = await _context
                        .ClanOrderChanges
                        .Where(p => p.PlayerId == userId)
                        .OrderByDescending(ch => ch.Timestamp)
                        .FirstOrDefaultAsync();

                    if (!adminChange && lastChange != null && !adminChange && (timestamp - lastChange.Timestamp) < 60 * 60 * 24 * 7) {
                        return BadRequest("Error. You can change clan order after " + (int)(7 - (timestamp - lastChange.Timestamp) / (60 * 60 * 24)) + " day(s)");
                    }

                    var clansInOrder = player.Clans
                        .OrderBy(c => ("," + newClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + newClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                        .ToList();

                    newClanOrder = string.Join(",", clansInOrder.Select(c => c.Tag));

                    _context.ClanOrderChanges.Add(new ClanOrderChange {
                        PlayerId = userId,
                        Timestamp = timestamp,
                        OldOrder = player.ClanOrder,
                        NewOrder = newClanOrder
                    });

                    player.ClanOrder = newClanOrder;
                    player.TopClan = clansInOrder.FirstOrDefault();

                    ClanTaskService.AddJob(new ClanRankingChangesDescription {
                        GlobalMapEvent = GlobalMapEvent.priorityChange,
                        PlayerId = player.Id
                    });
                }
            }

            if (Request.Query.ContainsKey("profileAppearance")) {
                settings.ProfileAppearance = profileAppearance;
            }
            if (Request.Query.ContainsKey("hue")) {
                settings.Hue = hue;
            }
            if (Request.Query.ContainsKey("saturation")) {
                settings.Saturation = saturation;
            }
            if (Request.Query.ContainsKey("effectName")) {
                settings.EffectName = effectName;
                if (effectName?.Contains("Booster") == true) {
                    settings.Hue = 0;
                    settings.Saturation = 1;
                }
            }
            if (Request.Query.ContainsKey("starredFriends")) {
                settings.StarredFriends = starredFriends;
            }

            if (Request.Query.ContainsKey("message")) {
                if (message != null && (message.Length < 3 || message.Length > 150)) {
                    return BadRequest("Use message between the 3 and 150 symbols");
                }

                features.Message = message ?? "";

                var parts = features.Message.Split(new char[] { '<', '>', '=', '%' });
                for (int i = 0; i < parts.Length - 1; i++) {
                    if (parts[i] == "size") {
                        if (float.TryParse(parts[i + 1], out float size) && size > 120) {
                            return BadRequest("Size tags bigger than 120% are forbidden");
                        }
                    }
                }

                settings.Message = message;
            }

            if (Request.Query.ContainsKey("leftSaberColor")) {
                if (leftSaberColor != null) {
                    int colorLength = leftSaberColor.Length;
                    try {
                        if (!(colorLength is 7 or 9 && long.Parse(leftSaberColor[1..], System.Globalization.NumberStyles.HexNumber) != 0)) {
                            return BadRequest("leftSaberColor is not valid");
                        }
                    } catch {
                        return BadRequest("leftSaberColor is not valid");
                    }
                }

                settings.LeftSaberColor = leftSaberColor;
            }

            if (Request.Query.ContainsKey("rightSaberColor")) {
                if (rightSaberColor != null) {
                    int colorLength = rightSaberColor.Length;
                    try {
                        if (!(colorLength is 7 or 9 && long.Parse(rightSaberColor[1..], System.Globalization.NumberStyles.HexNumber) != 0)) {
                            return BadRequest("rightSaberColor is not valid");
                        }
                    } catch {
                        return BadRequest("rightSaberColor is not valid");
                    }
                }
                settings.RightSaberColor = rightSaberColor;
            }
            if (Request.Query.ContainsKey("showBots")) {
                settings.ShowBots = showBots ?? false;
            }
            if (Request.Query.ContainsKey("showAllRatings")) {
                settings.ShowAllRatings = showAllRatings ?? false;
            }
            if (!player.AnySupporter()) {
                settings.ShowAllRatings = false;
            }
            if (Request.Query.ContainsKey("showStatsPublic")) {
                settings.ShowStatsPublic = showStatsPublic ?? false;
            }
            if (Request.Query.ContainsKey("showStatsPublicPinned")) {
                settings.ShowStatsPublicPinned = showStatsPublicPinned ?? false;
            }
            if (Request.Query.ContainsKey("showExplicitCovers")) {
                settings.ShowExplicitCovers = showExplicitCovers ?? false;
            }

            if (Request.Query.ContainsKey("horizontalRichBio")) {
                settings.HorizontalRichBio = horizontalRichBio ?? false;
            }

            if (Request.Query.ContainsKey("rankedMapperSort")) {
                settings.RankedMapperSort = rankedMapperSort ?? null;
            }

            if (Request.Query.ContainsKey("hiddenSocials")) {
                var socialsToHide = (hiddenSocials ?? "").ToLower().Split(",");
                var socials = _context.PlayerSocial.Where(s => s.PlayerId == player.Id).ToList();
                foreach (var social in socials) {
                    social.Hidden = socialsToHide.Contains(social.Service.ToLower());
                }
            }

            await _context.SaveChangesAsync();

            if (Request.Query.ContainsKey("clanOrder")) {
                await ClanUtils.RecalculateMainCountForPlayer(_context, player.Id);
            }

            return Ok();
        }

        [HttpPatch("~/user/cover")]
        public async Task<ActionResult> UpdateCover(
            [FromQuery] string? id = null) {
            string userId = GetId();
            var player = await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == userId);

            if (id != null && player != null && player.Role.Contains("admin")) {
                player = await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player == null) {
                return NotFound();
            }
            if (player.Banned) {
                return BadRequest("You are banned!");
            }

            ProfileSettings? settings = player.ProfileSettings;
            if (settings == null) {
                settings = new ProfileSettings();
                player.ProfileSettings = settings;
            }

            string? fileName = null;
            try {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormat(ms);
                Random rnd = new Random();
                fileName = "cover-" + userId + "R" + rnd.Next(1, 50) + extension;

                player.ProfileSettings.ProfileCover = await _s3Client.UploadAsset(fileName, stream);
            } catch (Exception e)
            {
                Console.WriteLine($"EXCEPTION: {e}");
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/user/cover")]
        public async Task<ActionResult> RemoveCover(
            [FromQuery] string? id = null) {
            string userId = GetId();
            var player = await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == userId);

            if (id != null && player != null && player.Role.Contains("admin")) {
                player = await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player == null) {
                return NotFound();
            }
            if (player.Banned) {
                return BadRequest("You are banned!");
            }

            ProfileSettings? settings = player.ProfileSettings;
            if (settings == null) {
                settings = new ProfileSettings();
                player.ProfileSettings = settings;
            }

            settings.ProfileCover = null;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("~/user/richbio")]
        public async Task<ActionResult> UpdatePlayerRichBio(
            [FromQuery] string? id = null) {
            var currentID = HttpContext.CurrentUserID(_context);

            var player = await _context.Players.FindAsync(currentID);

            if (player == null) {
                return NotFound();
            }

            if (player.Banned) {
                return BadRequest("You are banned!");
            }

            if (id != null && player != null && player.Role.Contains("admin")) {
                player = await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (!player.AnySupporter()) {
                return BadRequest("Please support on Patreon and link your profile");
            }

            var timeset = Time.UnixNow();
            try {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                if (ms.Length > 10000000)
                {
                    return BadRequest("Bio is too big to save, sorry");
                }

                if (ms.Length > 0) {
                    ms.Position = 0;
                    using var sr = new StreamReader(ms);
                    var content = sr.ReadToEnd();

                    //var parser = new HtmlParser(new HtmlParserOptions { IsScripting = true }, BrowsingContext.New(Configuration.Default.WithCss(new CssParserOptions
                    //{
                    //    IsIncludingUnknownDeclarations = true,
                    //    IsIncludingUnknownRules = true,
                    //    IsToleratingInvalidSelectors = true,
                    //})));
                    //var dom = parser.ParseDocument("<!doctype html><html><body>" + content);

                    var sanitizer = new HtmlSanitizer();
                    var exceptions = _context.SanitizerConfigs.AsNoTracking().ToList();

                    foreach (var item in exceptions) {
                        switch (item.Type) {
                            case SanitizerElement.Attribute:
                                sanitizer.AllowedAttributes.Add(item.Value);
                                break;
                            case SanitizerElement.Tag:
                                sanitizer.AllowedTags.Add(item.Value);
                                break;
                            case SanitizerElement.Scheme:
                                sanitizer.AllowedSchemes.Add(item.Value);
                                break;
                            case SanitizerElement.CssProperty:
                                sanitizer.AllowedCssProperties.Add(item.Value);
                                break;
                            default:
                                break;
                        }
                    }

                    sanitizer.PostProcessNode += (sender, e) => {
                        if (e.Node is IHtmlInlineFrameElement iframe)
                        {
                            string? src = iframe.Source;
                            if (src == null || exceptions.FirstOrDefault(e => e.Type == SanitizerElement.IframeUrl && src.StartsWith("https://" + e.Value)) == null)
                            {
                                iframe.Source = "https://www.youtube.com/embed/dQw4w9WgXcQ";
                            }
                        }
                    };

                    sanitizer.AllowedAtRules.Add(CssRuleType.Keyframe);
                    sanitizer.AllowedAtRules.Add(CssRuleType.Keyframes);
                    sanitizer.AllowedAtRules.Add(CssRuleType.Media);
                    sanitizer.AllowedAtRules.Add(CssRuleType.Import);

                    sanitizer.RemovingStyle += OnRemovingStyle;
                    sanitizer.RemovingTag += OnRemovingTag;
                    sanitizer.RemovingAttribute += OnRemovingAttribute;

                    Regex cssVarRegex = new Regex(@"--\w[\w-]*\s*:", RegexOptions.Compiled);

                    foreach (Match match in cssVarRegex.Matches(content)) {
                        string cssVarName = match.Value.Trim().TrimEnd(':').Trim();
                        sanitizer.AllowedCssProperties.Add(cssVarName);
                    }
                    
                    var newBio = sanitizer.Sanitize(content);
                    newBio = newBio.Replace("<iframe", "<iframe allow=\"fullscreen;\"");

                    if (player.RichBioTimeset > 0) {
                        await _s3Client.DeleteAsset($"player-{player.Id}-richbio-{player.RichBioTimeset}.html");
                    }

                    
                    var fileName = $"player-{player.Id}-richbio-{timeset}.html";

                    player.RichBioTimeset = timeset;
                    _ = await _s3Client.UploadAsset(fileName, newBio);
                }
            } catch (Exception e) {
                Console.WriteLine($"EXCEPTION: {e}");
                return BadRequest("Failed to save rich bio");
            }

            await _context.SaveChangesAsync();

            return Ok(timeset);
        }

        private void OnRemovingAttribute(object? sender, RemovingAttributeEventArgs e) {
            Console.WriteLine($"SANIT OnRemovingAttribute: {e.Attribute.Name}");
        }

        private void OnRemovingTag(object? sender, RemovingTagEventArgs e) {
            Console.WriteLine($"SANIT OnRemovingTag: {e.Tag.TagName}");
        }

        private void OnRemovingStyle(object? sender, RemovingStyleEventArgs e) {
            Console.WriteLine($"SANIT OnRemovingStyle: {e.Style.Name}");
        }

        [HttpDelete("~/user/richbio")]
        public async Task<ActionResult> DeletePlayerRichBio(
            [FromQuery] string? id = null) {
            var currentID = HttpContext.CurrentUserID(_context);

            var player = await _context.Players.FindAsync(currentID);

            if (player == null) {
                return NotFound();
            }

            if (player.Banned) {
                return BadRequest("You are banned!");
            }

            if (id != null && player != null && player.Role.Contains("admin")) {
                player = await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player.RichBioTimeset > 0) {
                await _s3Client.DeleteAsset($"player-{player.Id}-richbio-{player.RichBioTimeset}.html");
            }

            player.RichBioTimeset = 0;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/changePassword")]
        public async Task<ActionResult> ChangePassword([FromForm] string login, [FromForm] string oldPassword, [FromForm] string newPassword) {
            string? iPAddress = Request.HttpContext.GetIpAddress();
            if (iPAddress == null) {
                return Unauthorized("You don't have an IP address? Tell #NSGolova how you get this error.");
            }

            LoginAttempt? loginAttempt = await _context.LoginAttempts.FirstOrDefaultAsync(el => el.IP == iPAddress);
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (loginAttempt is { Count: 10 } && (timestamp - loginAttempt.Timestamp) < 60 * 60 * 24) {
                return Unauthorized("Too many login attempts in one day");
            }

            string? currentID = HttpContext.User.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
            if (currentID == null) return Unauthorized("Login or password is incorrect");

            long intId = Int64.Parse(currentID);
            AuthInfo? authInfo = await _context.Auths.FirstOrDefaultAsync(a => a.Login == login && intId == a.Id);

            if (authInfo == null || authInfo.Password != AuthUtils.HashPasswordWithSalt(oldPassword, authInfo.Salt)) {
                if (loginAttempt == null) {
                    loginAttempt = new LoginAttempt {
                        IP = iPAddress,
                        Timestamp = timestamp,
                    };
                    _context.LoginAttempts.Add(loginAttempt);
                    await _context.SaveChangesAsync();
                } else if ((timestamp - loginAttempt.Timestamp) >= 60 * 60 * 24) {
                    loginAttempt.Timestamp = timestamp;
                    loginAttempt.Count = 0;
                }
                loginAttempt.Count++;
                await _context.SaveChangesAsync();

                return Unauthorized("Login or password is incorrect");
            }
            if (newPassword.Trim().Length < 8) {
                return Unauthorized("Come on, type at least 8 symbols password");
            }

            authInfo.Salt = AuthUtils.GenerateSalt();
            authInfo.Password = AuthUtils.HashPasswordWithSalt(newPassword, authInfo.Salt);
            authInfo.Hint = new string(newPassword.TakeLast(2).ToArray());

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/resetPassword")]
        public async Task<ActionResult> ResetPassword([FromForm] string login, [FromForm] string newPassword) {
            string userId = GetId();
            AccountLink? link = await _context.AccountLinks.FirstOrDefaultAsync(a => a.SteamID == userId);
            if (link == null) {
                return Unauthorized("Login is incorrect. Or there is no link");
            }
            AuthInfo? authInfo = await _context.Auths.FirstOrDefaultAsync(a => a.Login == login);
            if (authInfo == null || authInfo.Id != link.OculusID) {
                return Unauthorized("Login is incorrect. Or there is no link");
            }

            if (newPassword.Trim().Length < 8) {
                return Unauthorized("Come on, type at least 8 symbols password");
            }

            authInfo.Salt = AuthUtils.GenerateSalt();
            authInfo.Password = AuthUtils.HashPasswordWithSalt(newPassword, authInfo.Salt);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/changeLogin")]
        public async Task<ActionResult> ChangeLogin([FromForm] string newLogin) {
            string? iPAddress = Request.HttpContext.GetIpAddress();
            if (iPAddress == null) {
                return Unauthorized("You don't have an IP address? Tell #NSGolova how you got this error.");
            }

            LoginAttempt? loginAttempt = await _context.LoginAttempts.FirstOrDefaultAsync(el => el.IP == iPAddress);
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (loginAttempt is { Count: 10 } && (timestamp - loginAttempt.Timestamp) < 60 * 60 * 24) {
                return Unauthorized("Too many login changes attempts in one day");
            }

            string userId = GetId();

            long intId = long.Parse(userId);
            if (intId > 70000000000000000) {
                var link = await _context.AccountLinks.FirstOrDefaultAsync(el => el.SteamID == userId);
                if (link != null) {
                    intId = link.OculusID;
                }
            } else if (intId > 1000000000000000) {
                var link = await _context.AccountLinks.FirstOrDefaultAsync(el => el.PCOculusID == userId);
                if (link != null) {
                    intId = link.OculusID;
                }
            }

            AuthInfo? authInfo = await _context.Auths.FirstOrDefaultAsync(a => a.Id == intId);
            if (authInfo == null) {
                return Unauthorized("Can't find auth info");
            }

            if ((await _context.Auths.FirstOrDefaultAsync(a => a.Login == newLogin)) != null) {

                if (loginAttempt == null) {
                    loginAttempt = new LoginAttempt {
                        IP = iPAddress,
                        Timestamp = timestamp,
                    };
                    _context.LoginAttempts.Add(loginAttempt);
                    await _context.SaveChangesAsync();
                } else if ((timestamp - loginAttempt.Timestamp) >= 60 * 60 * 24) {
                    loginAttempt.Timestamp = timestamp;
                    loginAttempt.Count = 0;
                }
                loginAttempt.Count++;
                await _context.SaveChangesAsync();

                return Unauthorized("User with such login already exists");
            }

            if (newLogin.Trim().Length < 2) {
                return Unauthorized("Use at least 3 symbols for login.");
            }

            var lastLoginChange = await _context.LoginChanges.OrderByDescending(el => el.Timestamp).FirstOrDefaultAsync(el => el.PlayerId == authInfo.Id);
            if (lastLoginChange != null && (timestamp - lastLoginChange.Timestamp) < 60 * 60 * 24 * 7) {
                return BadRequest("Error. You can change login after " + (int)(7 - (timestamp - lastLoginChange.Timestamp) / (60 * 60 * 24)) + " day(s)");
            }
            if (lastLoginChange == null) {
                lastLoginChange = new LoginChange { PlayerId = authInfo.Id };
                _context.LoginChanges.Add(lastLoginChange);
            }
            lastLoginChange.OldLogin = authInfo.Login;
            lastLoginChange.NewLogin = newLogin;
            lastLoginChange.Timestamp = timestamp;

            authInfo.Login = newLogin;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/user/migrate")]
        public async Task<ActionResult<int>> MigrateToSteam([FromForm] string login, [FromForm] string password) {
            string steamID = HttpContext.CurrentUserID();

            AuthInfo? authInfo = await _context.Auths.FirstOrDefaultAsync(el => el.Login == login);
            if (authInfo == null || authInfo.Password != AuthUtils.HashPasswordWithSalt(password, authInfo.Salt)) {
                return Unauthorized("Login or password is invalid");
            }

            return await MigratePrivate(steamID, authInfo.Id.ToString());
        }

        [HttpGet("~/user/migrateoculuspc")]
        public async Task<ActionResult<int>> MigrateOculusPC([FromQuery] string ReturnUrl, [FromQuery] string Token) {
            string? currentId = HttpContext.CurrentUserID(_context);
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(Token, _configuration);
            if (id == null) {
                return Unauthorized("Token seems to be wrong");
            }
            if (currentId == null) {
                return Unauthorized("Something went wrong with original auth");
            }
            var player = await PlayerControllerHelper.GetLazy(_context, _configuration, id, true);
            if (player == null) {
                return BadRequest("Can't find player");
            }

            if (long.Parse(currentId) < 1000000000000000) {
                await PlayerControllerHelper.GetLazy(_context, _configuration, currentId, true);
                await MigratePrivate(id, currentId);
            } else {
                await MigratePrivate(currentId, id);
            }

            return Redirect(ReturnUrl);
        }

        [NonAction]
        public async Task<ActionResult<int>> MigratePrivate(string migrateToId, string migrateFromId) {
            if (migrateToId == migrateFromId) {
                return Unauthorized("Something went completely wrong");
            }
            if (long.Parse(migrateToId) < 1000000000000000) {
                return Unauthorized("You need to be logged in with Steam or Oculus");
            }
            long fromIntID = long.Parse(migrateFromId);

            var accountLinks = await _context.AccountLinks.Where(l => l.OculusID == fromIntID || l.SteamID == migrateToId || l.PCOculusID == migrateFromId || l.PCOculusID == migrateToId).ToListAsync();

            AccountLink? accountLink = null;
            if (accountLinks.Count == 1) {
                accountLink = accountLinks[0];
                if (fromIntID < 1000000000000000 && accountLink.PCOculusID.Length > 0 && accountLink.OculusID != 0) {
                    migrateFromId = accountLink.PCOculusID;
                    fromIntID = long.Parse(migrateFromId);
                }
            } else if (accountLinks.Count > 1) {
                if (accountLinks.Count == 2) {
                    accountLink = accountLinks.FirstOrDefault(al => al.SteamID.Length > 0);

                    var oculusLink = accountLinks.FirstOrDefault(al => al.PCOculusID.Length > 0);
                    migrateFromId = oculusLink.PCOculusID;
                    fromIntID = long.Parse(migrateFromId);

                    _context.AccountLinks.Remove(oculusLink);

                } else {
                    return BadRequest("Too much migrations to handle");
                }
            }
            Player? currentPlayer = await _context.Players.Where(p => p.Id == migrateFromId)
                .Include(p => p.Clans)
                .Include(p => p.PatreonFeatures)
                .Include(p => p.ProfileSettings)
                .Include(p => p.Changes)
                .Include(p => p.Badges)
                .Include(p => p.Achievements)
                .Include(p => p.Socials)
                .Include(p => p.Mapper)
                .FirstOrDefaultAsync();
            Player? migrateToPlayer = await _context.Players.Where(p => p.Id == migrateToId)
                .Include(p => p.Clans)
                .Include(p => p.PatreonFeatures)
                .Include(p => p.ProfileSettings)
                .Include(p => p.Changes)
                .Include(p => p.Badges)
                .Include(p => p.ScoreStats)
                .Include(p => p.Achievements)
                .Include(p => p.Socials)
                .Include(p => p.ContextExtensions)
                .FirstOrDefaultAsync();

            if (currentPlayer == null || migrateToPlayer == null) {
                return BadRequest("Could not find one of the players =( Make sure you posted at least one score from the mod.");
            }

            if (currentPlayer.Banned || migrateToPlayer.Banned) {
                return BadRequest("Some of the players are banned");
            }

            Clan? currentPlayerClan = currentPlayer.Clans.FirstOrDefault(c => c.LeaderID == currentPlayer.Id);

            if (migrateToPlayer.Clans.FirstOrDefault(c => c.LeaderID == migrateToPlayer.Id) != null &&
                currentPlayerClan != null) {
                return BadRequest("Both players are clan leaders, delete one of the clans");
            }

            if (accountLink == null) {
                if (fromIntID < 1000000000000000) {
                    if (long.Parse(migrateToId) > 70000000000000000) {
                        accountLink = new AccountLink {
                            OculusID = (int)fromIntID,
                            SteamID = migrateToId,
                        };
                    } else {
                        accountLink = new AccountLink {
                            OculusID = (int)fromIntID,
                            PCOculusID = migrateToId,
                        };
                    }
                } else {
                    accountLink = new AccountLink {
                        PCOculusID = migrateFromId,
                        SteamID = migrateToId,
                    };
                }
                _context.AccountLinks.Add(accountLink);
            } else {
                if (fromIntID < 1000000000000000) {
                    if (long.Parse(migrateToId) > 70000000000000000) {
                        if (accountLink.SteamID.Length > 0 && accountLink.OculusID > 0) return BadRequest("Account already linked");

                        accountLink.OculusID = (int)fromIntID;
                        accountLink.SteamID = migrateToId;
                    } else {
                        if (accountLink.PCOculusID.Length > 0 && accountLink.OculusID > 0) return BadRequest("Account already linked");

                        accountLink.OculusID = (int)fromIntID;
                        accountLink.PCOculusID = migrateToId;
                    }
                } else {
                    if (accountLink.PCOculusID.Length > 0 && accountLink.SteamID.Length > 0) return BadRequest("Account already linked");

                    accountLink.PCOculusID = migrateFromId;
                    accountLink.SteamID = migrateToId;
                }
            }


            if (migrateToPlayer.Country == "not set" && currentPlayer.Country != "not set") {
                migrateToPlayer.Country = currentPlayer.Country;
                foreach (var item in migrateToPlayer.ContextExtensions) {
                    item.Country = currentPlayer.Country;
                }
            }

            if (migrateToPlayer.Alias == null && currentPlayer.Alias != null) {
                migrateToPlayer.Alias = currentPlayer.Alias;
                migrateToPlayer.OldAlias = currentPlayer.OldAlias;
            }

            if (currentPlayer.Changes?.Count >= 0) {
                foreach (var item in currentPlayer.Changes) {
                    item.PlayerId = migrateToId;
                }
            }

            if (currentPlayer.Socials?.Count >= 0) {
                foreach (var item in currentPlayer.Socials) {
                    item.PlayerId = migrateToId;
                }
            }

            if (currentPlayer.Achievements?.Count >= 0) {
                foreach (var item in currentPlayer.Achievements) {
                    var existing = migrateToPlayer.Achievements?.FirstOrDefault(a => a.AchievementDescriptionId == item.AchievementDescriptionId);
                    if (existing == null) {
                        item.PlayerId = migrateToId;
                    }
                }
            }

            if (currentPlayer.Mapper != null) {
                currentPlayer.Mapper.Player = migrateToPlayer;
            }

            if ((migrateToPlayer.RichBioTimeset == 0 && currentPlayer.RichBioTimeset != 0) || currentPlayer.RichBioTimeset > migrateToPlayer.RichBioTimeset) {
                try {
                    using (var stream = await _s3Client.DownloadAsset($"player-{currentPlayer.Id}-richbio-{currentPlayer.RichBioTimeset}.html"))
                    using (var memoryStream = new MemoryStream(5)) {
                        await stream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;

                        var fileName = $"player-{migrateToPlayer.Id}-richbio-{currentPlayer.RichBioTimeset}.html";
                        _ = await _s3Client.UploadAsset(fileName, memoryStream);
                        migrateToPlayer.RichBioTimeset = currentPlayer.RichBioTimeset;
                    }
                } catch (Exception e) {
                    Console.WriteLine($"Bio migration exception {e}");
                }
            }

            PlayerFriends? currentPlayerFriends = await _context.Friends.Where(u => u.Id == currentPlayer.Id).Include(f => f.Friends).FirstOrDefaultAsync();
            PlayerFriends? migrateToPlayerFriends = await _context.Friends.Where(u => u.Id == migrateToPlayer.Id).Include(f => f.Friends).FirstOrDefaultAsync();
            
            if (currentPlayerFriends != null && migrateToPlayerFriends != null) {
                foreach (var friend in currentPlayerFriends.Friends) {
                    if (friend.Id == currentPlayer.Id || friend.Id == migrateToPlayer.Id) continue;

                    if (migrateToPlayerFriends.Friends.FirstOrDefault(p => p.Id == friend.Id) == null) {
                        migrateToPlayerFriends.Friends.Add(friend);
                    }
                }
                _context.Friends.Remove(currentPlayerFriends);
            } else if (migrateToPlayerFriends == null && currentPlayerFriends != null) {
                migrateToPlayerFriends = new PlayerFriends();
                migrateToPlayerFriends.Id = migrateToPlayer.Id;
                _context.Friends.Add(migrateToPlayerFriends);
                foreach (var friend in currentPlayerFriends.Friends) {
                    if (friend.Id == currentPlayer.Id || friend.Id == migrateToPlayer.Id) continue;

                    migrateToPlayerFriends.Friends.Add(friend);
                }
            }

            var currentPlayerFollowers = await _context.Friends.Where(u => u.Friends.FirstOrDefault(f => f.Id == currentPlayer.Id) != null).Include(f => f.Friends).ToListAsync();
            foreach (var item in currentPlayerFollowers) {
                item.Friends.Remove(currentPlayer);
                item.Friends.Add(migrateToPlayer);
            }

            if (currentPlayer.Badges != null) {
                if (migrateToPlayer.Badges == null) {
                    migrateToPlayer.Badges = new List<Badge>();
                }
                foreach (var badge in currentPlayer.Badges) {
                    migrateToPlayer.Badges.Add(badge);
                }
            }

            var patreonLink = await _context.PatreonLinks.FindAsync(currentPlayer.Id);
            if (patreonLink != null) {
                var newLink = new PatreonLink {
                    Id = patreonLink.Id,
                    PatreonId = patreonLink.PatreonId,
                    Token = patreonLink.Token,
                    RefreshToken = patreonLink.RefreshToken,
                    Timestamp = patreonLink.Timestamp,
                    Tier = patreonLink.Tier,
                };
                _context.PatreonLinks.Remove(patreonLink);
                _context.PatreonLinks.Add(newLink);
            }

            var saverLink = await _context.BeatSaverLinks.FindAsync(currentPlayer.Id);
            if (saverLink != null) {
                var newLink = new BeatSaverLink {
                    Id = migrateToPlayer.Id,
                    BeatSaverId = saverLink.BeatSaverId,
                    Token = saverLink.Token,
                    RefreshToken = saverLink.RefreshToken,
                    Timestamp = saverLink.Timestamp,
                };
                currentPlayer.MapperId = null;
                migrateToPlayer.MapperId = int.Parse(saverLink.BeatSaverId);
                _context.BeatSaverLinks.Remove(saverLink);
                _context.BeatSaverLinks.Add(newLink);
            }

            PatreonFeatures? features = migrateToPlayer.PatreonFeatures;
            if (features == null) {
                migrateToPlayer.PatreonFeatures = currentPlayer.PatreonFeatures;
            }

            ProfileSettings? settings = migrateToPlayer.ProfileSettings;
            if (settings == null) {
                migrateToPlayer.ProfileSettings = currentPlayer.ProfileSettings;
            }

            migrateToPlayer.Role += currentPlayer.Role;
            await _context.BulkSaveChangesAsync();

            var scoresGroups = (await _context.Scores
                .Include(el => el.ContextExtensions)
                .Where(el => el.Player.Id == currentPlayer.Id || el.Player.Id == migrateToId)
                .Include(el => el.Leaderboard)
                .ThenInclude(el => el.Difficulty)
                .ToListAsync())
                .GroupBy(el => el.LeaderboardId)
                .ToList();

            if (scoresGroups.Count() > 0) {
                foreach (var group in scoresGroups) {
                    var scores = group.ToList();
                    foreach (var score in scores) {
                        if (score.ContextExtensions != null) {
                            foreach (var ce in score.ContextExtensions) {
                                _context.ScoreContextExtensions.Remove(ce);
                            }
                        }
                    }
                }
                await _context.BulkSaveChangesAsync();
                foreach (var group in scoresGroups) {
                    var scores = group.ToList();
                    foreach (var score in scores) {
                        var difficulty = score.Leaderboard.Difficulty;
                        score.ContextExtensions = new List<ScoreContextExtension>();
                        score.ValidContexts = LeaderboardContexts.None;
                        score.ValidForGeneral = false;
                        var noModsExtension = ReplayUtils.NoModsContextExtension(score, difficulty);
                        if (noModsExtension != null) {
                            noModsExtension.LeaderboardId = score.LeaderboardId;
                            noModsExtension.PlayerId = migrateToId;
                            score.ContextExtensions.Add(noModsExtension);
                        }
                        var noPauseExtenstion = ReplayUtils.NoPauseContextExtension(score);
                        if (noPauseExtenstion != null) {
                            noPauseExtenstion.LeaderboardId = score.LeaderboardId;
                            noPauseExtenstion.PlayerId = migrateToId;
                            score.ContextExtensions.Add(noPauseExtenstion);
                        }
                        var golfExtension = ReplayUtils.GolfContextExtension(score, difficulty);
                        if (golfExtension != null) {
                            golfExtension.LeaderboardId = score.LeaderboardId;
                            golfExtension.PlayerId = migrateToId;
                            score.ContextExtensions.Add(golfExtension);
                        }
                        var scpmExtension = ReplayUtils.SCPMContextExtension(score, difficulty);
                        if (scpmExtension != null) {
                            scpmExtension.LeaderboardId = score.LeaderboardId;
                            scpmExtension.PlayerId = migrateToId;
                            score.ContextExtensions.Add(scpmExtension);
                        }
                    }

                    var bestScore = scores[0];
                    foreach (var score in scores) {
                        if (ReplayUtils.IsNewScoreBetter(bestScore, score)) {
                            bestScore = score;
                        }
                    }

                    bestScore.ValidContexts |= LeaderboardContexts.General;
                    bestScore.ValidForGeneral = true;

                    foreach (var context in ContextExtensions.NonGeneral) {
                        var extensions = scores
                            .Select(s => new {
                                Context = s.ContextExtensions.FirstOrDefault(ce => ce.Context == context),
                                Score = s
                            })
                            .Where(e => e.Context != null)
                            .ToList();
                        if (extensions.Count == 0) {
                            continue;
                        }

                        var bestExtension = extensions[0];
                        foreach (var extension in extensions) {
                            if (ReplayUtils.IsNewScoreExtensionBetter(bestExtension.Context, extension.Context)) {
                                bestExtension = extension;
                            }
                        }

                        bestExtension.Score.ValidContexts |= context;
                    }
                    foreach (var score in scores) {
                        var extensionsToRemove = new List<ScoreContextExtension>();
                        foreach (var ce in score.ContextExtensions) {
                            if (!score.ValidContexts.HasFlag(ce.Context)) {
                                extensionsToRemove.Add(ce);
                            }
                        }
                        foreach (var ce in extensionsToRemove) {
                            score.ContextExtensions.Remove(ce);
                        }
                    }

                    foreach (var score in scores) {

                        if (score.ValidContexts == LeaderboardContexts.None) {
                            _context.Scores.Remove(score);
                        }
                    }

                    var valid = scores.Where(s => s.ValidContexts != LeaderboardContexts.None).ToList();
                    var firstValid = valid.First();
                    var save = firstValid.ValidContexts;
                    firstValid.ValidContexts = LeaderboardContexts.None;
                    firstValid.ValidForGeneral = false;

                    await _context.SaveChangesAsync();

                    firstValid.ValidContexts = save;
                    await _context.SaveChangesAsync();

                    foreach (var score in scores) {

                        if (score.ValidContexts != LeaderboardContexts.None) {
                            score.PlayerId = migrateToId;
                            if (score.ValidContexts.HasFlag(LeaderboardContexts.General)) {
                                score.ValidForGeneral = true;
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                }
            }

            foreach (var clan in currentPlayer.Clans) {
                if (migrateToPlayer.Clans.FirstOrDefault(c => c.Id == clan.Id) == null) {
                    if (currentPlayerClan == clan) {
                        clan.LeaderID = migrateToPlayer.Id;
                    }
                    if (migrateToPlayer.Clans.Count < 5) {
                        migrateToPlayer.Clans.Add(clan);
                    }
                }
            }

            var clanUpdates = _context.ClanUpdates.Where(cu => cu.Player == currentPlayer).ToList();
            foreach (var cu in clanUpdates) {
                _context.ClanUpdates.Remove(cu);
            }

            var predictedScores = _context.PredictedScores.Where(s => s.PlayerId == currentPlayer.Id).ToList();
            foreach (var score in predictedScores) {
                _context.PredictedScores.Remove(score);
            }

            currentPlayer.ProfileSettings = null;
            currentPlayer.Socials = null;
            currentPlayer.Achievements = null;
            currentPlayer.ContextExtensions = null;

            await _context.SaveChangesAsync();

            if (scoresGroups.Count() > 0) {
                
                RefreshTaskService.AddJob(new MigrationJob {
                    PlayerId = migrateToPlayer.Id,
                    Leaderboards = scoresGroups.Select(g => g.Key).ToList()
                });
                RefreshTaskService.AddHistoryJob(new HistoryMigrationJob {
                    ToPlayerId = migrateToPlayer.Id,
                    FromPlayerId = currentPlayer.Id
                });
            }

            return Ok();
        }

        [HttpGet("~/user/failedscores")]
        public async Task<ActionResult<ResponseWithMetadata<FailedScore>>> GetFailedScores(
            [FromQuery] int page = 1,
            [FromQuery] int count = 3,
            [FromQuery] string? id = null) {
            string? playerId = GetId();
            if (playerId == null) {
                return NotFound();
            }
            Player? currentPlayer = await _context.Players.FindAsync(playerId);
            if (currentPlayer == null) {
                return Unauthorized();
            }

            IQueryable<FailedScore> query = _context.FailedScores.Include(lb => lb.Player).ThenInclude(p => p.ScoreStats).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                query = query.Where(t => t.PlayerId == id);
            } else {
                query = query.OrderByDescending(s => s.FalsePositive ? 1 : 0).ThenBy(s => s.Timeset);
            }

            return new ResponseWithMetadata<FailedScore> {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await query.CountAsync()
                },
                Data = await query
                        .Skip((page - 1) * count)
                        .Take(count)

                        .ToListAsync()
            };
        }

        [HttpPost("~/user/failedscore/falsepositive")]
        public async Task<ActionResult> MarkFailedScore([FromQuery] int id) {
            string? playerId = GetId();
            if (playerId == null) {
                return NotFound();
            }
            Player? currentPlayer = await _context.Players.FindAsync(playerId);
            FailedScore? score;
            if (currentPlayer != null && currentPlayer.Role.Contains("admin")) {
                score = await _context.FailedScores.FirstOrDefaultAsync(t => t.Id == id);
            } else {
                score = await _context.FailedScores.FirstOrDefaultAsync(t => t.PlayerId == playerId && t.Id == id);
            }
            if (score == null) {
                return NotFound();
            }

            score.FalsePositive = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("~/user/failedscore/remove")]
        public async Task<ActionResult> RemoveFailedScore([FromQuery] int id) {
            string? playerId = GetId();
            if (playerId == null) {
                return NotFound();
            }
            Player? currentPlayer = await _context.Players.FindAsync(playerId);
            FailedScore? score;
            if (currentPlayer != null && currentPlayer.Role.Contains("admin")) {
                score = await _context.FailedScores.FirstOrDefaultAsync(t => t.Id == id);
            } else {
                score = await _context.FailedScores.FirstOrDefaultAsync(t => t.PlayerId == playerId && t.Id == id);
            }
            if (score == null) {
                return NotFound();
            }

            _context.FailedScores.Remove(score);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("~/user/failedscore/retry")]
        public async Task<ActionResult<ScoreUploadResponse>> RetryFailedScore([FromQuery] int id, [FromQuery] bool allow = false) {
            string? playerId = GetId();
            if (playerId == null) {
                return NotFound();
            }
            Player? currentPlayer = await _context.Players.FindAsync(playerId);
            FailedScore? score;
            if (currentPlayer != null && currentPlayer.Role.Contains("admin")) {
                score = await _context.FailedScores.FirstOrDefaultAsync(t => t.Id == id);
            } else {
                score = await _context.FailedScores.FirstOrDefaultAsync(t => t.PlayerId == playerId && t.Id == id);
                allow = false;
            }
            if (score == null) {
                return NotFound();
            }

            string? name = score.Replay.Split("/").LastOrDefault();
            if (name == null) {
                return Ok();
            }
            var result = await _replayController.PostReplayFromCDN(score.PlayerId, name, score.Replay.Contains("/backup/file"), allow, score.Timeset, HttpContext);
            _context.FailedScores.Remove(score);
            await _context.SaveChangesAsync();

            return result;
        }

        [HttpGet("~/user/config/link")]
        public async Task<ActionResult<string>> GetConfigLink() {
            string? userId = GetId();
            if (userId == null) {
                return Unauthorized();
            }

            var configUrl = await _s3Client.GetPresignedUrl(userId + "-config.json", S3Container.configs);
            if (configUrl == null) {
                return NotFound();
            }
            return configUrl;
        }

        [HttpGet("~/user/config")]
        public async Task<ActionResult> GetConfig() {
            string? userId = GetId();
            if (userId == null) {
                return Unauthorized();
            }

            var stream = await _s3Client.DownloadStream(userId + "-config.json", S3Container.configs);
            if (stream == null) {
                return NotFound();
            }

            Response.Headers.Add("Content-Type", "application/json");
            return Ok(stream);
        }

        [HttpPost("~/user/config")]
        public async Task<ActionResult> PostConfig() {
            string? userId = GetId();
            if (userId == null) {
                return Unauthorized();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var ms2 = new MemoryStream(5);
            await ms.CopyToAsync(ms2);
            ms.Position = 0;

            dynamic? mapscontainer = ms.ObjectFromStream();
            if (mapscontainer == null) {
                return BadRequest("Can't decode config");
            }
            ms2.Position = 0;

            await _s3Client.UploadStream(userId + "-config.json", S3Container.configs, ms2);

            return Ok();
        }

        [HttpPost("~/user/ban")]
        public async Task<ActionResult> Ban(
            [FromQuery] string? id = null,
            [FromQuery] string? reason = null,
            [FromQuery] int? duration = null,
            [FromQuery] bool? bot = null) {
            string userId = GetId();

            var ip = Request.HttpContext.GetIpAddress();
            Console.WriteLine($"UPDATE_USER {userId} {ip}");

            var player = await _context
                .Players
                .Include(p => p.ContextExtensions)
                .FirstOrDefaultAsync(p => p.Id == userId);

            if (id != null && player != null && player.Role.Contains("admin")) {
                if (reason == null || duration == null) {
                    return BadRequest("Provide ban reason and duration");
                }
                player = await _context
                    .Players
                    .Include(p => p.ContextExtensions)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player == null) {
                return NotFound();
            }

            if (player.Banned) {
                return BadRequest("Player already banned");
            }

            var leaderboardsToUpdate = new List<string>();
            var scores = await _context.Scores.Where(s => s.PlayerId == player.Id).Include(s => s.ContextExtensions).ToListAsync();
            foreach (var score in scores) {
                leaderboardsToUpdate.Add(score.LeaderboardId);
                score.Banned = true;
                foreach (var ce in score.ContextExtensions)
                {
                    ce.Banned = true;
                }

                await SocketController.ScoreWasRejected(score, _context);
            }

            player.Banned = true;
            foreach (var ce in player.ContextExtensions)
            {
                ce.Banned = true;
            }
            if (bot != null) {
                player.Bot = bot ?? false;
            }

            Ban ban = new Ban {
                PlayerId = player.Id,
                BannedBy = userId,
                BanReason = reason ?? "Self ban",
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Duration = duration ?? 0,
            };
            _context.Bans.Add(ban);

            await _context.SaveChangesAsync();

            ClanTaskService.AddJob(new ClanRankingChangesDescription {
                GlobalMapEvent = player.Id == userId ? GlobalMapEvent.suspend : GlobalMapEvent.ban,
                PlayerId = player.Id
            });

            HttpContext.Response.OnCompleted(async () => {
                foreach (var item in leaderboardsToUpdate) {
                    await _scoreRefreshController.BulkRefreshScores(item);
                    await _scoreRefreshController.BulkRefreshScoresAllContexts(item);
                }

                await _playerRefreshController.RefreshRanks();
                await _playerContextRefreshController.RefreshRanksAllContexts();
            });

            return Ok();
        }

        [HttpPost("~/user/unban")]
        public async Task<ActionResult> Unban([FromQuery] string? id = null) {
            string userId = GetId();
            var transaction = await _context.Database.BeginTransactionAsync();
            var player = await _context
                .Players
                .Include(p => p.ContextExtensions)
                .FirstOrDefaultAsync(p => p.Id == userId);
            bool adminUnban = false;

            if ((id != null && player != null && player.Role.Contains("admin")) || HttpContext == null) {
                adminUnban = true;
                player = await _context
                    .Players
                    .Include(p => p.ContextExtensions)
                    .FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player == null) {
                return NotFound();
            }

            //if (!player.Banned) { return BadRequest("Player is not banned"); }
            var ban = await _context.Bans.OrderByDescending(x => x.Id).FirstOrDefaultAsync(x => x.PlayerId == player.Id);
            if (ban != null && ban.BannedBy != userId && !adminUnban) {
                return BadRequest("Good try, but not");
            }

            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            if (ban != null && ban.BannedBy == userId && !adminUnban && (timeset - ban.Timeset) < 60 * 60 * 24 * 7) {
                return BadRequest("You can unban yourself after: " + (24 * 7 - (timeset - ban.Timeset) / (60 * 60)) + "hours");
            }

            var scores = await _context.Scores.Include(s => s.ContextExtensions).Where(s => s.PlayerId == player.Id).ToListAsync();
            var leaderboardsToUpdate = new List<string>();
            foreach (var score in scores) {
                leaderboardsToUpdate.Add(score.LeaderboardId);
                if (score.Banned) {
                    await SocketController.ScoreWasAccepted(score, _context);
                }
                score.Banned = false;
                foreach (var ce in score.ContextExtensions)
                {
                    ce.Banned = false;
                }

            }
            player.Banned = false;
            foreach (var ce in player.ContextExtensions)
            {
                ce.Banned = false;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            ClanTaskService.AddJob(new ClanRankingChangesDescription {
                GlobalMapEvent = player.Id == userId ? GlobalMapEvent.unsuspend : GlobalMapEvent.unban,
                PlayerId = player.Id
            });

            var refreshBlock = async () => {
                foreach (var item in leaderboardsToUpdate) {
                    await _scoreRefreshController.BulkRefreshScores(item);
                    await _scoreRefreshController.BulkRefreshScoresAllContexts(item);
                }
                await _playerRefreshController.RefreshPlayer(player);
                await _playerRefreshController.RefreshRanks();

                await _playerContextRefreshController.RefreshPlayerAllContexts(player.Id);
                await _playerContextRefreshController.RefreshRanksAllContexts();
            };

            if (HttpContext != null) {
                HttpContext.Response.OnCompleted(refreshBlock);
            } else {
                await refreshBlock();
            }

            return Ok();
        }

        [HttpPost("~/user/hideopscores")]
        public async Task<ActionResult> HideOPScores([FromQuery] string? id = null) {
            string userId = GetId();

            var player = await _context.Players.Include(p => p.ScoreStats).FirstOrDefaultAsync(p => p.Id == userId);

            if (player != null && id == null && player.Role.Contains("admin")) {
                return BadRequest("");
            }

            if (id != null && player != null && player.Role.Contains("admin")) {
                player = await _context.Players.Include(p => p.ScoreStats).FirstOrDefaultAsync(p => p.Id == id);
            }

            if (player == null) {
                return NotFound();
            }

            var leaderboardsToUpdate = new List<string>();
            var scores = await _context.Scores.Include(s => s.Leaderboard.Song).Where(s => s.PlayerId == player.Id && s.Modifiers.Contains("OP")).ToListAsync();
            foreach (var score in scores) {
                leaderboardsToUpdate.Add(score.LeaderboardId);
                score.Banned = true;

                await SocketController.ScoreWasRejected(score, _context);
            }

            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                foreach (var item in leaderboardsToUpdate) {
                    await _scoreRefreshController.BulkRefreshScores(item);
                }
                await _playerRefreshController.RefreshPlayer(player);

                await _playerRefreshController.RefreshRanks();
            });

            return Ok();
        }
    }
}