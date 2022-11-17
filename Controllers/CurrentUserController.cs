using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class CurrentUserController : ControllerBase
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        BlobContainerClient _assetsContainerClient;
        PlayerController _playerController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;
        ScoreController _scoreController;

        public CurrentUserController(
            AppContext context,
            ReadAppContext readContext,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            PlayerController playerController,
            ReplayController replayController,
            ScoreController scoreController,
            IConfiguration configuration)
        {
            _context = context;
            _readContext = readContext;

            _playerController = playerController;
            _replayController = replayController;
            _scoreController = scoreController;
            _configuration = configuration;
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

        [HttpGet("~/user/id")]
        public ActionResult<string> GetIdResult() => GetId();

        [NonAction]
        public string? GetId() => HttpContext.CurrentUserID(_readContext);

        [HttpGet("~/user")]
        public async Task<ActionResult<UserReturn>> GetCurrentUser()
        {
            User? user = await GetCurrentUserLazy();
            if (user == null) {
               return NotFound();
            }
            string id = user.Id;

            PlayerFriends? friends = _readContext.Friends.Include(f => f.Friends).FirstOrDefault(f => f.Id == id);
            Clan? clan = _readContext.Clans.Include(c => c.Players).Where(f => f.LeaderID == id).FirstOrDefault();

            long intId = Int64.Parse(id);
            if (intId > 1000000000000000) {
                var link = _readContext.AccountLinks.FirstOrDefault(el => el.SteamID == id || el.PCOculusID == id);
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
                Players = clan.Players.Select(p => p.Id).ToList(),
                PendingInvites = _readContext.Users.Where(u => u.ClanRequest.Contains(clan)).Select(f => f.Id).ToList(),
            } : null;
            var player = user.Player;

            return new UserReturn {
                Player = PostProcessSettings(ResponseFullFromPlayer(player)),
                Ban = player.Banned ? _readContext
                    .Bans
                    .Where(b => b.PlayerId == player.Id)
                    .Select(b => new BanReturn { Reason = b.BanReason, Duration = b.Duration, Timeset = b.Timeset })
                    .FirstOrDefault() : null,
                ClanRequest = user.ClanRequest,
                BannedClans = user.BannedClans,
                Friends = friends != null ? friends.Friends.Select(ResponseUtils.ResponseFullFromPlayer).ToList() : new List<PlayerResponseFull>(),
                Login = _readContext.Auths.FirstOrDefault(a => a.Id == intId)?.Login,
                
                Migrated = _readContext.AccountLinks.FirstOrDefault(a => a.SteamID == id) != null,
                Patreoned = _readContext.PatreonLinks.Find(id) != null,
                Clan = clanReturn
            };
        }

        [HttpGet("~/user/modinterface")]
        public async Task<ActionResult<PlayerResponseWithFriends>> GetCurrentUserMod() {
            string? id = GetId();
            if (id == null) {
                return NotFound();
            }

            Player? player;

            User? user = _context
                .Users
                .Where(u => u.Id == id)
                .Include(u => u.Player)
                .ThenInclude(p => p.ScoreStats)
                .Include(u => u.Player)
                .ThenInclude(p => p.Socials)
                .Include(u => u.Player)
                .ThenInclude(p => p.ProfileSettings)
                .FirstOrDefault();
            

            if (user == null) {
                player = (await _playerController.GetLazy(id)).Value;
                if (player == null)
                {
                    return NotFound();
                }
                _context.Users.Add(new User
                {
                    Id = id,
                    Player = player
                });
                _context.SaveChanges();
            } else {
                player = user.Player;
            }
            
            var result = ResponseUtils.GeneralResponseFromPlayer<PlayerResponseWithFriends>(player);
            PlayerFriends? friends = _readContext.Friends.Include(f => f.Friends).FirstOrDefault(f => f.Id == id);
            result.Friends = friends != null ? friends.Friends.Select(f => f.Id).ToList() : new List<string>();
            return result;
        }

        [NonAction]
        public async Task<User?> GetCurrentUserLazy() => await GetUserLazy(GetId());

        [NonAction]
        public async Task<User?> GetUserLazy(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            User? user = _context
                .Users
                .Where(u => u.Id == id)
                .Include(u => u.Player)
                .ThenInclude(p => p.Clans)
                .Include(u => u.Player)
                .ThenInclude(p => p.ScoreStats)
                .Include(u => u.Player)
                .ThenInclude(p => p.Socials)
                .Include(u => u.Player)
                .ThenInclude(p => p.ProfileSettings)
                .Include(u => u.ClanRequest)
                .Include(u => u.BannedClans)
                .FirstOrDefault();
            if (user == null)
            {
                Player? player = (await _playerController.GetLazy(id)).Value;
                if (player == null)
                {
                    return null;
                }

                user = new User
                {
                    Id = id,
                    Player = player
                };
                _context.Users.Add(user);
                _context.SaveChanges();
            }

            return user;
        }

        [HttpPost("~/user/friend")]
        public async Task<ActionResult> AddFriend([FromQuery] string playerId)
        {
            string? id = GetId();
            if (playerId == id) {
                return BadRequest("Couldnt add user as a friend to himself");
            }
            Player? player = _context.Players.Where(u => u.Id == id).FirstOrDefault();
            if (player == null)
            {
                return NotFound();
            }
            PlayerFriends? playerFriends = _context.Friends.Where(u => u.Id == id).Include(f => f.Friends).FirstOrDefault();
            if (playerFriends == null) {
                playerFriends = new PlayerFriends();
                playerFriends.Id = id;
                _context.Friends.Add(playerFriends);
                _context.SaveChanges();
            }

            if (playerFriends.Friends.FirstOrDefault(p => p.Id == playerId) != null)
            {
                return BadRequest("Already a friend");
            }

            Player? friend = _context.Players.FirstOrDefault(p => p.Id == playerId);
            if (friend == null)
            {
                return NotFound();
            }

            playerFriends.Friends.Add(friend);
            _context.SaveChanges();

            return Ok();
        }

        [HttpDelete("~/user/friend")]
        public async Task<ActionResult> RemoveFriend([FromQuery] string playerId)
        {
            string? id = GetId();
            if (playerId == id)
            {
                return BadRequest("Couldnt remove user as a friend from himself");
            }
            Player? player = _context.Players.Where(u => u.Id == id).FirstOrDefault();
            if (player == null)
            {
                return NotFound();
            }
            PlayerFriends? playerFriends = _context.Friends.Where(u => u.Id == id).Include(f => f.Friends).FirstOrDefault();
            if (playerFriends == null)
            {
                return NotFound();
            }
            Player? friend = playerFriends.Friends.FirstOrDefault(p => p.Id == playerId);
            if (friend == null)
            {
                return NotFound();
            }

            playerFriends.Friends.Remove(friend);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPatch("~/user")]
        public async Task<ActionResult> ChangeFullName(
            [FromQuery] string? name = null,
            [FromQuery] string? country = null,
            [FromQuery] string? profileAppearance = null,
            [FromQuery] string? message = null,
            [FromQuery] float? hue = null,
            [FromQuery] float? saturation = null,
            [FromQuery] string? effectName = null,
            [FromQuery] string? leftSaberColor = null,
            [FromQuery] string? rightSaberColor = null,
            [FromQuery] string? id = null)
        {
            string userId = GetId();
            var player = _context
                .Players
                .Include(p => p.ProfileSettings)
                .Include(p => p.PatreonFeatures)
                .Include(p => p.Changes)
                .FirstOrDefault(p => p.Id == userId);
            bool adminChange = false;

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                adminChange = true;
                player = _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .Include(p => p.PatreonFeatures)
                    .Include(p => p.Changes)
                    .FirstOrDefault(p => p.Id == id);
            }

            if (player == null)
            {
                return NotFound();
            }
            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }

            if (name != null || country != null)
            {
                var changes = player.Changes;
                if (changes == null) {
                   changes = player.Changes = new List<PlayerChange>();
                }
                PlayerChange? lastChange = changes.Count > 0 ? changes.OrderByDescending(ch => ch.Timestamp).FirstOrDefault() : null;
                int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                PlayerChange newChange = new PlayerChange {
                    OldName = player.Name,
                    OldCountry = player.Country,
                    Timestamp = timestamp,
                    Changer = adminChange ? userId : null,
                };

                if (name != null)
                {
                    if (name.Length < 3 || name.Length > 30)
                    {
                        return BadRequest("Use name between the 3 and 30 symbols");
                    }

                    player.Name = name;
                    newChange.NewName = name;
                }

                if (country != null)
                {
                    if (!PlayerUtils.AllowedCountries().Contains(country))
                    {
                        return BadRequest("This country code is not allowed.");
                    }
                    
                    if (lastChange != null && !adminChange && (timestamp - lastChange.Timestamp) < 60 * 60 * 24 * 30)
                    {
                        return BadRequest("Error. You can change country after " + (int)(30 - (timestamp - lastChange.Timestamp) / (60 * 60 * 24)) + " day(s)");
                    }
                    newChange.NewCountry = country;

                    var oldCountryList = _context.Players.Where(p => p.Country == player.Country && p.Id != player.Id).OrderByDescending(p => p.Pp).ToList();
                    foreach ((int i, Player p) in oldCountryList.Select((value, i) => (i, value)))
                    {
                        p.CountryRank = i + 1;
                    }

                    player.Country = country;

                    var newCountryList = _context.Players.Where(p => p.Country == country || p.Id == player.Id).OrderByDescending(p => p.Pp).ToList();
                    foreach ((int i, Player p) in newCountryList.Select((value, i) => (i, value)))
                    {
                        p.CountryRank = i + 1;
                    }
                }

                if (player.Country != newChange.OldCountry || player.Name != newChange.OldName) {
                    player.Changes.Add(newChange);
                }
            }

            string? fileName = null;
            try
            {
                await _assetsContainerClient.CreateIfNotExistsAsync();

                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                fileName = userId + extension;

                await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                await _assetsContainerClient.UploadBlobAsync(fileName, stream);
            }
            catch {}

            if (fileName != null) {
                player.Avatar = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://cdn.beatleader.xyz/assets/") + fileName;
            }

            PatreonFeatures? features = player.PatreonFeatures;
            ProfileSettings? settings = player.ProfileSettings;
            if (features == null)
            {
                features = new PatreonFeatures();
                player.PatreonFeatures = features;
            }
            if (settings == null)
            {
                settings = new ProfileSettings();
                player.ProfileSettings = settings;
            }

            if (Request.Query.ContainsKey("effectName")) {
                settings.EffectName = effectName;
            }
            if (Request.Query.ContainsKey("profileAppearance"))
            {
                settings.ProfileAppearance = profileAppearance;
            }
            if (Request.Query.ContainsKey("hue"))
            {
                settings.Hue = hue;
            }
            if (Request.Query.ContainsKey("saturation"))
            {
                settings.Saturation = saturation;
            }

            if (Request.Query.ContainsKey("message")) {
                if (message != null && (message.Length < 3 || message.Length > 150))
                {
                    return BadRequest("Use message between the 3 and 150 symbols");
                }

                features.Message = message ?? "";
                settings.Message = message;
            }

            if (Request.Query.ContainsKey("leftSaberColor"))
            {
                if (leftSaberColor != null) {
                    int colorLength = leftSaberColor.Length;
                    try
                    {
                        if (!((colorLength == 7 || colorLength == 9) && Int64.Parse(leftSaberColor.Substring(1), System.Globalization.NumberStyles.HexNumber) != 0))
                        {
                            return BadRequest("leftSaberColor is not valid");
                        }
                    }
                    catch
                    {
                        return BadRequest("leftSaberColor is not valid");
                    }
                }

                settings.LeftSaberColor = leftSaberColor;
            }

            if (Request.Query.ContainsKey("rightSaberColor"))
            {
                if (rightSaberColor != null)
                {
                    int colorLength = rightSaberColor.Length;
                    try
                    {
                        if (!((colorLength == 7 || colorLength == 9) && Int64.Parse(rightSaberColor.Substring(1), System.Globalization.NumberStyles.HexNumber) != 0))
                        {
                            return BadRequest("rightSaberColor is not valid");
                        }
                    }
                    catch
                    {
                        return BadRequest("rightSaberColor is not valid");
                    }
                }
                settings.RightSaberColor = rightSaberColor;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/avatar")]
        public async Task<ActionResult> ChangeAvatar([FromQuery] string? id = null)
        {
            string userId = GetId();
            var player = _context.Players.Find(userId);

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = _context.Players.Find(id);
            }

            if (player == null)
            {
                return NotFound();
            }

            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }

            string fileName = userId;
            try
            {
                await _assetsContainerClient.CreateIfNotExistsAsync();

                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                fileName += extension;

                await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                await _assetsContainerClient.UploadBlobAsync(fileName, stream);
            }
            catch (Exception)
            {
                return BadRequest("Error saving avatar");
            }

            player.Avatar = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://cdn.beatleader.xyz/assets/") + fileName;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/name")]
        public async Task<ActionResult> ChangeName([FromQuery] string newName, [FromQuery] string? id = null)
        {
            string userId = GetId();
            var player = _context.Players.Find(userId);

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = _context.Players.Find(id);
            }

            if (player == null)
            {
                return NotFound();
            }
            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }
            if (newName.Length < 3 || newName.Length > 30)
            {
                return BadRequest("Use name between the 3 and 30 symbols");
            }

            player.Name = newName;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/country")]
        public async Task<ActionResult> ChangeCountry([FromQuery] string newCountry, [FromQuery] string? id = null)
        {
            string userId = GetId();
            var player = _context.Players.Find(userId);
            bool adminChange = false;

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = _context.Players.Find(id);
                adminChange = true;
            }

            if (player == null)
            {
                return NotFound();
            }
            if (!PlayerUtils.AllowedCountries().Contains(newCountry))
            {
                return BadRequest("This country code is not allowed.");
            }
            
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var lastCountryChange = _context.CountryChanges.FirstOrDefault(el => el.Id == player.Id);
            if (lastCountryChange != null && !adminChange && (timestamp - lastCountryChange.Timestamp) < 60 * 60 * 24 * 30)
            {
                return BadRequest("Error. You can change country after " + (int)(30 - (timestamp - lastCountryChange.Timestamp) / (60 * 60 * 24)) + " day(s)");
            }
            if (lastCountryChange == null) {
                lastCountryChange = new CountryChange { Id = player.Id };
                _context.CountryChanges.Add(lastCountryChange);
            }
            lastCountryChange.OldCountry = player.Country;
            lastCountryChange.NewCountry = newCountry;
            lastCountryChange.Timestamp = timestamp;

            var oldCountryList = _context.Players.Where(p => p.Country == player.Country && p.Id != player.Id).OrderByDescending(p => p.Pp).ToList();
            foreach ((int i, Player p) in oldCountryList.Select((value, i) => (i, value)))
            {
                p.CountryRank = i + 1;
            }

            player.Country = newCountry;

            var newCountryList = _context.Players.Where(p => p.Country == newCountry || p.Id == player.Id).OrderByDescending(p => p.Pp).ToList();
            foreach ((int i, Player p) in newCountryList.Select((value, i) => (i, value)))
            {
                p.CountryRank = i + 1;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/changePassword")]
        public ActionResult ChangePassword([FromForm] string login, [FromForm] string oldPassword, [FromForm] string newPassword)
        {
            IPAddress? iPAddress = Request.HttpContext.Connection.RemoteIpAddress;
            if (iPAddress == null)
            {
                return Unauthorized("You don't have an IP adress? Tell #NSGolova how you get this error.");
            }

            LoginAttempt? loginAttempt = _context.LoginAttempts.FirstOrDefault(el => el.IP == iPAddress.ToString());
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (loginAttempt != null && loginAttempt.Count == 10 && (timestamp - loginAttempt.Timestamp) < 60 * 60 * 24)
            {
                return Unauthorized("To much login attempts in one day");
            }

            string? currentID = HttpContext.User.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
            if (currentID == null) return Unauthorized("Login or password is incorrect");

            long intId = Int64.Parse(currentID);
            AuthInfo? authInfo = _context.Auths.FirstOrDefault(a => a.Login == login && a.Password == oldPassword && intId == a.Id);

            if (authInfo == null) {
                if (loginAttempt == null)
                {
                    loginAttempt = new LoginAttempt
                    {
                        IP = iPAddress.ToString(),
                        Timestamp = timestamp,
                    };
                    _context.LoginAttempts.Add(loginAttempt);
                    _context.SaveChanges();
                }
                else if ((timestamp - loginAttempt.Timestamp) >= 60 * 60 * 24)
                {
                    loginAttempt.Timestamp = timestamp;
                    loginAttempt.Count = 0;
                }
                loginAttempt.Count++;
                _context.SaveChanges();

                return Unauthorized("Login or password is incorrect");
            }
            if (newPassword.Trim().Length < 8)
            {
                return Unauthorized("Come on, type at least 8 symbols password");
            }

            authInfo.Password = newPassword;
            _context.Auths.Update(authInfo);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPatch("~/user/resetPassword")]
        public ActionResult ChangePassword([FromForm] string login, [FromForm] string newPassword)
        {
            string userId = GetId();
            AccountLink? link = _context.AccountLinks.FirstOrDefault(a => a.SteamID == userId);
            if (link == null) {
                return Unauthorized("Login is incorrect. Or there is no link");
            }
            AuthInfo? authInfo = _context.Auths.FirstOrDefault(a => a.Login == login);
            if (authInfo == null || authInfo.Id != link.OculusID)
            {
                return Unauthorized("Login is incorrect. Or there is no link");
            }

            if (newPassword.Trim().Length < 8)
            {
                return Unauthorized("Come on, type at least 8 symbols password");
            }

            authInfo.Password = newPassword;
            _context.Auths.Update(authInfo);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPatch("~/user/changeLogin")]
        public ActionResult ChangeLogin([FromForm] string newLogin)
        {
            IPAddress? iPAddress = Request.HttpContext.Connection.RemoteIpAddress;
            if (iPAddress == null)
            {
                return Unauthorized("You don't have an IP adress? Tell #NSGolova how you get this error.");
            }

            LoginAttempt? loginAttempt = _context.LoginAttempts.FirstOrDefault(el => el.IP == iPAddress.ToString());
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (loginAttempt != null && loginAttempt.Count == 10 && (timestamp - loginAttempt.Timestamp) < 60 * 60 * 24)
            {
                return Unauthorized("To much login changes attempts in one day");
            }

            string userId = GetId();

            long intId = Int64.Parse(userId);
            if (intId > 70000000000000000)
            {
                var link = _context.AccountLinks.FirstOrDefault(el => el.SteamID == userId);
                if (link != null)
                {
                    intId = link.OculusID;
                }
            }

            AuthInfo? authInfo = _context.Auths.FirstOrDefault(a => a.Id == intId);
            if (authInfo == null)
            {
                return Unauthorized("Can't find auth info");
            }

            if (_context.Auths.FirstOrDefault(a => a.Login == newLogin) != null) {

                if (loginAttempt == null)
                {
                    loginAttempt = new LoginAttempt
                    {
                        IP = iPAddress.ToString(),
                        Timestamp = timestamp,
                    };
                    _context.LoginAttempts.Add(loginAttempt);
                    _context.SaveChanges();
                }
                else if ((timestamp - loginAttempt.Timestamp) >= 60 * 60 * 24)
                {
                    loginAttempt.Timestamp = timestamp;
                    loginAttempt.Count = 0;
                }
                loginAttempt.Count++;
                _context.SaveChanges();

                return Unauthorized("User with such login already exists");
            }

            if (newLogin.Trim().Length < 2)
            {
                return Unauthorized("Use at least 3 symbols for login.");
            }

            var lastLoginChange = _context.LoginChanges.OrderByDescending(el => el.Timestamp).FirstOrDefault(el => el.PlayerId == authInfo.Id);
            if (lastLoginChange != null && (timestamp - lastLoginChange.Timestamp) < 60 * 60 * 24 * 7)
            {
                return BadRequest("Error. You can change login after " + (int)(7 - (timestamp - lastLoginChange.Timestamp) / (60 * 60 * 24)) + " day(s)");
            }
            if (lastLoginChange == null)
            {
                lastLoginChange = new LoginChange { PlayerId = authInfo.Id };
                _context.LoginChanges.Add(lastLoginChange);
            }
            lastLoginChange.OldLogin = authInfo.Login;
            lastLoginChange.NewLogin = newLogin;
            lastLoginChange.Timestamp = timestamp;

            authInfo.Login = newLogin;
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/user/migrate")]
        public async Task<ActionResult<int>> MigrateToSteam([FromForm] string login, [FromForm] string password)
        {
            string steamID = HttpContext.CurrentUserID();

            AuthInfo? authInfo = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (authInfo == null || authInfo.Password != password)
            {
                return Unauthorized("Login or password is invalid");
            }

            return await MigratePrivate(steamID, authInfo.Id.ToString());
        }

        [HttpGet("~/user/migrateoculuspc")]
        public async Task<ActionResult<int>> MigrateOculusPC([FromQuery] string ReturnUrl, [FromQuery] string Token)
        {
            string currentId = HttpContext.CurrentUserID();
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(Token, _configuration);
            if (id == null)
            {
                return Unauthorized("Token seems to be wrong");
            }
            var player = await _playerController.GetLazy(id, true);
            if (player == null) {
                return BadRequest("Can't find player");
            }

            if (Int64.Parse(currentId) < 1000000000000000) {
                await _playerController.GetLazy(currentId, true);
                await MigratePrivate(id, currentId);
            } else {
                await MigratePrivate(currentId, id);
            }

            return Redirect(ReturnUrl);
        }

        [NonAction]
        public async Task<ActionResult<int>> MigratePrivate(string migrateToId, string migrateFromId)
        {
            if (Int64.Parse(migrateToId) < 1000000000000000)
            {
                return Unauthorized("You need to be logged in with Steam or Oculus");
            }
            long fromIntID = Int64.Parse(migrateFromId);

            var accountLinks = _context.AccountLinks.Where(l => l.OculusID == fromIntID || l.SteamID == migrateToId || l.PCOculusID == migrateFromId || l.PCOculusID == migrateToId).ToList();

            AccountLink? accountLink = null;
            if (accountLinks.Count == 1)
            {
                accountLink = accountLinks[0];
                if (fromIntID < 1000000000000000 && accountLink.PCOculusID.Length > 0)
                {
                    migrateFromId = accountLink.PCOculusID;
                    fromIntID = Int64.Parse(migrateFromId);
                }
            }
            else if (accountLinks.Count > 1)
            {
                if (accountLinks.Count == 2)
                {
                    accountLink = accountLinks.FirstOrDefault(al => al.SteamID.Length > 0);

                    var oculusLink = accountLinks.FirstOrDefault(al => al.PCOculusID.Length > 0);
                    migrateFromId = oculusLink.PCOculusID;
                    fromIntID = Int64.Parse(migrateFromId);

                    _context.AccountLinks.Remove(oculusLink);

                }
                else
                {
                    return BadRequest("Too much migrations to handle");
                }
            }
            Player? currentPlayer = _context.Players.Where(p => p.Id == migrateFromId)
                .Include(p => p.Clans)
                .Include(p => p.PatreonFeatures)
                .Include(p => p.ProfileSettings)
                .Include(p => p.Changes)
                .Include(p => p.History)
                .Include(p => p.Badges)
                .Include(p => p.Socials)
                .FirstOrDefault();
            Player? migratedToPlayer = _context.Players.Where(p => p.Id == migrateToId)
                .Include(p => p.Clans)
                .Include(p => p.PatreonFeatures)
                .Include(p => p.ProfileSettings)
                .Include(p => p.Changes)
                .Include(p => p.History)
                .Include(p => p.Badges)
                .FirstOrDefault();

            if (currentPlayer == null || migratedToPlayer == null)
            {
                return BadRequest("Could not find one of the players =( Make sure you posted at least one score from the mod.");
            }

            if (currentPlayer.Banned || migratedToPlayer.Banned) {
                return BadRequest("Some of the players are banned");
            }

            if (migratedToPlayer.Clans.Select(c => c.Tag).Union(currentPlayer.Clans.Select(c => c.Tag)).Count() > 3) {
                return BadRequest("Leave some clans as there is too many of them for one account.");
            }

            Clan? currentPlayerClan = currentPlayer.Clans.FirstOrDefault(c => c.LeaderID == currentPlayer.Id);

            if (migratedToPlayer.Clans.FirstOrDefault(c => c.LeaderID == migratedToPlayer.Id) != null &&
                currentPlayerClan != null)
            {
                return BadRequest("Both players are clan leaders, delete one of the clans");
            }

            if (accountLink == null) {
               if (fromIntID < 1000000000000000) {
                    if (Int64.Parse(migrateToId) > 70000000000000000) {
                        accountLink = new AccountLink
                        {
                            OculusID = (int)fromIntID,
                            SteamID = migrateToId,
                        };
                    } else {
                        accountLink = new AccountLink
                        {
                            OculusID = (int)fromIntID,
                            PCOculusID = migrateToId,
                        };
                    }
                } else {
                    accountLink = new AccountLink
                    {
                        PCOculusID = migrateFromId,
                        SteamID = migrateToId,
                    };
                }
                _context.AccountLinks.Add(accountLink);
            } else {
                if (fromIntID < 1000000000000000)
                {
                    if (Int64.Parse(migrateToId) > 70000000000000000)
                    {
                        if (accountLink.SteamID.Length > 0 && accountLink.OculusID > 0) return BadRequest("Account already linked");

                        accountLink.OculusID = (int)fromIntID;
                        accountLink.SteamID = migrateToId;
                    }
                    else
                    {
                        if (accountLink.PCOculusID.Length > 0 && accountLink.OculusID > 0) return BadRequest("Account already linked");

                        accountLink.OculusID = (int)fromIntID;
                        accountLink.PCOculusID = migrateToId;
                    }
                }
                else
                {
                    if (accountLink.PCOculusID.Length > 0 && accountLink.SteamID.Length > 0) return BadRequest("Account already linked");

                    accountLink.PCOculusID = migrateFromId;
                    accountLink.SteamID = migrateToId;
                }
            }
            

            if (migratedToPlayer.Country == "Not set" && currentPlayer.Country != "Not set")
            {
                migratedToPlayer.Country = currentPlayer.Country;
            }

            if (currentPlayer.History?.Count >= migratedToPlayer.History?.Count) {
                foreach (var item in currentPlayer.History)
                {
                    item.PlayerId = migrateToId;
                }
            }

            if (currentPlayer.Changes?.Count >= migratedToPlayer.Changes?.Count)
            {
                foreach (var item in currentPlayer.Changes)
                {
                    item.PlayerId = migrateToId;
                }
            }

            PlayerFriends? currentPlayerFriends = _context.Friends.Where(u => u.Id == currentPlayer.Id).Include(f => f.Friends).FirstOrDefault();
            PlayerFriends? playerFriends = _context.Friends.Where(u => u.Id == migratedToPlayer.Id).Include(f => f.Friends).FirstOrDefault();
            if (playerFriends == null && currentPlayerFriends != null)
            {
                playerFriends = new PlayerFriends();
                playerFriends.Id = migratedToPlayer.Id;
                _context.Friends.Add(playerFriends);
            }
            if (currentPlayerFriends != null && playerFriends != null) {
                foreach (var friend in currentPlayerFriends.Friends)
                {
                    if (playerFriends.Friends.FirstOrDefault(p => p.Id == friend.Id) == null) {
                        playerFriends.Friends.Add(friend);
                    }
                }
            }

            if (currentPlayer.Badges != null) {
                if (migratedToPlayer.Badges == null)
                {
                    migratedToPlayer.Badges = new List<Badge>();
                }
                foreach (var badge in currentPlayer.Badges)
                {
                    migratedToPlayer.Badges.Add(badge);
                }
            }

            PatreonLink? link = _context.PatreonLinks.Find(currentPlayer.Id);
            if (link != null) {
                link.Id = migratedToPlayer.Id;
            }

            PatreonFeatures? features = migratedToPlayer.PatreonFeatures;
            if (features == null)
            {
                migratedToPlayer.PatreonFeatures = currentPlayer.PatreonFeatures;
            }

            ProfileSettings? settings = migratedToPlayer.ProfileSettings;
            if (settings == null)
            {
                migratedToPlayer.ProfileSettings = currentPlayer.ProfileSettings;
            }

            List<Score> scores = _context.Scores
                .Include(el => el.Player)
                .Where(el => el.Player.Id == currentPlayer.Id && el.LeaderboardId != null)
                .Include(el => el.Leaderboard)
                .ThenInclude(el => el.Scores).ToList();
            if (scores.Count() > 0) {
                foreach (Score score in scores)
                {
                    Score? migScore = score.Leaderboard.Scores.FirstOrDefault(el => el.PlayerId == migrateToId);
                    if (migScore != null)
                    {
                        if (migScore.ModifiedScore >= score.ModifiedScore)
                        {
                            score.Leaderboard.Scores.Remove(score);
                        }
                        else
                        {
                            score.Leaderboard.Scores.Remove(migScore);
                            score.Player = migratedToPlayer;
                            score.PlayerId = migrateToId;
                        }

                        var rankedScores = score.Leaderboard.Scores.Where(sc => sc != null).OrderByDescending(el => el.ModifiedScore).ToList();
                        foreach ((int i, Score? s) in rankedScores.Select((value, i) => (i, value)))
                        {
                            if (s != null)
                            {
                                s.Rank = i + 1;
                            }
                        }
                    }
                    else
                    {
                        score.Player = migratedToPlayer;
                        score.PlayerId = migrateToId;
                    }
                }
            }

            foreach (var clan in currentPlayer.Clans)
            {
                if (migratedToPlayer.Clans.FirstOrDefault(c => c.Id == clan.Id) == null) {
                    if (currentPlayerClan == clan)
                    {
                        clan.LeaderID = migratedToPlayer.Id;
                    }
                    migratedToPlayer.Clans.Add(clan);
                }
            }

            currentPlayer.History = null;
            currentPlayer.ProfileSettings = null;
            currentPlayer.Socials = null;
            _context.Players.Remove(currentPlayer);

            await _context.SaveChangesAsync();
            await _playerController.RefreshPlayer(migratedToPlayer);
            await _playerController.RefreshRanks();
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/user/failedscores")]
        public async Task<ActionResult<ResponseWithMetadata<FailedScore>>> GetFailedScores(
            [FromQuery] int page = 1,
            [FromQuery] int count = 3)
        {
            string? id = GetId();
            if (id == null) {
                return NotFound();
            }
            Player? currentPlayer = _readContext.Players.Find(id);
            IQueryable<FailedScore> query = _readContext.FailedScores.Include(lb => lb.Player).ThenInclude(p => p.ScoreStats).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                query = query.Where(t => t.PlayerId == id);
            }

            return new ResponseWithMetadata<FailedScore> {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = query.Count()
                },
                Data = query
                        .Skip((page - 1) * count)
                        .Take(count)

                        .ToList()
            };
    }

        [HttpPost("~/user/failedscore/remove")]
        public async Task<ActionResult> RemoveFailedScore([FromQuery] int id)
        {
            string? playerId = GetId();
            if (playerId == null)
            {
                return NotFound();
            }
            Player? currentPlayer = _context.Players.Find(playerId);
            FailedScore? score;
            if (currentPlayer != null && currentPlayer.Role.Contains("admin"))
            {
                score = _context.FailedScores.FirstOrDefault(t => t.Id == id);
            }
            else
            {
                score = _context.FailedScores.FirstOrDefault(t => t.PlayerId == playerId && t.Id == id);
            }
            if (score == null) {
                return NotFound();
            }

            _context.FailedScores.Remove(score);
            _context.SaveChanges();
            return Ok();
        }

        [HttpPost("~/user/failedscore/retry")]
        public async Task<ActionResult> RetryFailedScore([FromQuery] int id)
        {
            string? playerId = GetId();
            if (playerId == null)
            {
                return NotFound();
            }
            Player? currentPlayer = _context.Players.Find(playerId);
            FailedScore? score;
            if (currentPlayer != null && currentPlayer.Role.Contains("admin"))
            {
                score = _context.FailedScores.FirstOrDefault(t => t.Id == id);
            } else {
                score = _context.FailedScores.FirstOrDefault(t => t.PlayerId == playerId && t.Id == id);
            }
            if (score == null)
            {
                return NotFound();
            }

            string? name = score.Replay.Split("/").LastOrDefault();
            if (name == null)
            {
                return Ok();
            }

            await _replayController.PostReplayFromCDN(score.PlayerId, name, HttpContext);

            return Ok();
        }

        [HttpGet("~/players/avatarsrefresh")]
        public async Task<ActionResult> ResizeAvatars([FromQuery] int hundred)
        {
            string userId = GetId();
            var player = _context.Players.Find(userId);

            if (player == null || !player.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var players = _context.Players.Where(p => p.Avatar.Contains("cdn.beatleader.xyz") && !p.Avatar.Contains("avatar.png")).Skip(hundred * 100).Take(100).ToList();

            foreach (var p in players)
            {
                string fileName = p.Id;
                try
                {
                    var ms = new MemoryStream(5);
                    _assetsContainerClient.GetBlobClient(p.Avatar.Split("/").Last()).DownloadTo(ms);

                    ms.Position = 0;

                    (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                    fileName += extension;

                    await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                    await _assetsContainerClient.UploadBlobAsync(fileName, stream);
                }
                catch (Exception)
                {
                    return BadRequest("Error saving avatar");
                }

                p.Avatar = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://cdn.beatleader.xyz/assets/") + fileName;
                _context.Players.Update(p);
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/user/ban")]
        public async Task<ActionResult> Ban([FromQuery] string? id = null, [FromQuery] string? reason = null, [FromQuery] int? duration = null)
        {
            string userId = GetId();

            var player = _context.Players.Find(userId);

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                if (reason == null || duration == null) {
                    return BadRequest("Provide ban reason and duration");
                }
                player = _context.Players.Find(id);
            }

            if (player == null)
            {
                return NotFound();
            }

            if (player.Banned) {
                return BadRequest("Player already banned");
            }

            var leaderboardsToUpdate = new List<string>();
            var scores = _context.Scores.Where(s => s.PlayerId == player.Id).ToList();
            foreach (var score in scores)
            {
                leaderboardsToUpdate.Add(score.LeaderboardId);
                score.Banned = true;
            }

            player.Banned = true;

            Ban ban = new Ban {
                PlayerId = player.Id,
                BannedBy = userId,
                BanReason = reason ?? "Self ban",
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Duration = duration ?? 0,
            };
            _context.Bans.Add(ban);

            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                foreach (var item in leaderboardsToUpdate)
                {
                    await _scoreController.RefreshScores(item);
                }

                await _playerController.RefreshRanks();
            });

            return Ok();
        }

        [HttpPost("~/user/unban")]
        public async Task<ActionResult> Unban([FromQuery] string? id = null)
        {
            string userId = GetId();
            var transaction = _context.Database.BeginTransaction();
            var player = _context.Players.Find(userId);
            bool adminUnban = false;

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                adminUnban = true;
                player = _context.Players.Find(id);
            }

            if (player == null)
            {
                return NotFound();
            }

            if (!player.Banned) { return BadRequest("Player is not banned"); }
            var ban = _context.Bans.OrderByDescending(x => x.Id).Where(x => x.PlayerId == player.Id).FirstOrDefault();
            if (ban != null && ban.BannedBy != userId && !adminUnban) {
                return BadRequest("Good try, but not");
            }

            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            if (ban != null && ban.BannedBy == userId && !adminUnban && (timeset - ban.Timeset) < 60 * 60 * 24 * 7)
            {
                return BadRequest("You will can unban yourself after: " + (24 * 7 - (timeset - ban.Timeset) / (60 * 60)) + "hours");
            }

            var scores = _context.Scores.Where(s => s.PlayerId == player.Id && s.LeaderboardId != null).ToList();
            var leaderboardsToUpdate = new List<string>();
            foreach (var score in scores)
            {
                leaderboardsToUpdate.Add(score.LeaderboardId);
                score.Banned = false;
                
            }
            player.Banned = false;

            await _context.SaveChangesAsync();
            transaction.Commit();

            HttpContext.Response.OnCompleted(async () => {
                foreach (var item in leaderboardsToUpdate)
                {
                    await _scoreController.RefreshScores(item);
                }
                await _playerController.RefreshPlayer(player);
                await _playerController.RefreshRanks();
            });

            return Ok();
        }
    }
}