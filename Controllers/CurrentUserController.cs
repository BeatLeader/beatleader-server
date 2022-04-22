using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class CurrentUserController : ControllerBase
    {
        private readonly AppContext _context;
        BlobContainerClient _assetsContainerClient;
        PlayerController _playerController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;

        public CurrentUserController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            PlayerController playerController,
            ReplayController replayController)
        {
            _context = context;
            _playerController = playerController;
            _replayController = replayController;
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
        public ActionResult<string> GetId()
        {
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);
            
            return accountLink != null ? accountLink.SteamID : currentID;
        }

        [HttpGet("~/user")]
        public ActionResult<UserReturn> GetCurrentUser()
        {
            string? id = GetId().Value;
            if (id == null) {
                return NotFound();
            }
            User? user = GetUserLazy(id);
            if (user == null) {
               return NotFound();
            }

            PlayerFriends? friends = _context.Friends.Include(f => f.Friends).FirstOrDefault(f => f.Id == id);

            return new UserReturn {
                Player = user.Player,
                ClanRequest = user.ClanRequest,
                BannedClans = user.BannedClans,
                Friends = friends != null ? friends.Friends.Select(f => f.Id).ToList() : new List<string>(),
                Migrated = _context.AccountLinks.FirstOrDefault(a => a.SteamID == id) != null
            };
        }

        public User? GetUserLazy(string id)
        {
            User? user = _context.Users.Where(u => u.Id == id).Include(u => u.Player).Include(u => u.ClanRequest).Include(u => u.BannedClans).FirstOrDefault();
            if (user == null)
            {
                Player? player = _context.Players.Where(u => u.Id == id).FirstOrDefault();
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
            string? id = GetId().Value;
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
            _context.Friends.Update(playerFriends);
            _context.SaveChanges();

            return Ok();
        }

        [HttpDelete("~/user/friend")]
        public async Task<ActionResult> RemoveFriend([FromQuery] string playerId)
        {
            string? id = GetId().Value;
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
            _context.Friends.Update(playerFriends);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPatch("~/user/avatar")]
        public async Task<ActionResult> ChangeAvatar([FromQuery] string? id = null)
        {
            string userId = GetId().Value;
            var player = await _context.Players.FindAsync(userId);

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = await _context.Players.FindAsync(id);
            }

            if (player == null)
            {
                return NotFound();
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
            _context.Players.Update(player);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/name")]
        public async Task<ActionResult> ChangeName([FromQuery] string newName, [FromQuery] string? id = null)
        {
            string userId = GetId().Value;
            var player = await _context.Players.FindAsync(userId);

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = await _context.Players.FindAsync(id);
            }

            if (player == null)
            {
                return NotFound();
            }
            if (newName.Length < 3 || newName.Length > 30)
            {
                return BadRequest("Use name between the 3 and 30 symbols");
            }

            player.Name = newName;

            _context.Players.Update(player);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/country")]
        public async Task<ActionResult> ChangeCountry([FromQuery] string newCountry, [FromQuery] string? id = null)
        {
            string userId = GetId().Value;
            var player = await _context.Players.FindAsync(userId);
            bool adminChange = false;

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = await _context.Players.FindAsync(id);
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
                return BadRequest("Error. You can change country after " + (int)(30 - (timestamp - lastCountryChange.Timestamp) / 60 * 60 * 24) + " day(s)");
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
            _context.Players.Update(player);

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
            string userId = GetId().Value;
            AuthInfo? authInfo = _context.Auths.FirstOrDefault(a => a.Login == login && a.Password == oldPassword && Int64.Parse(userId) == a.Id);

            if (authInfo == null) {
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
            string userId = GetId().Value;
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

        [HttpPost("~/user/migrate")]
        public async Task<ActionResult<int>> MigrateToSteam([FromForm] string login, [FromForm] string password)
        {
            string steamID = HttpContext.CurrentUserID();

            AuthInfo? authInfo = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (authInfo == null || authInfo.Password != password)
            {
                return Unauthorized("Login or password is invalid");
            }
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.SteamID == steamID || el.OculusID == authInfo.Id);
            if (accountLink != null)
            {
                return Unauthorized("Accounts are already linked");
            }

            return await MigratePrivate(authInfo.Id);
        }

        [NonAction]
        public async Task<ActionResult<int>> MigratePrivate(int id)
        {
            string steamID = HttpContext.CurrentUserID();

            if (Int64.Parse(steamID) < 70000000000000000)
            {
                return Unauthorized("You need to be logged in with Steam");
            }

            AccountLink? accountLink = new AccountLink
            {
                OculusID = id,
                SteamID = steamID
            };

            _context.AccountLinks.Add(accountLink);

            Player? currentPlayer = _context.Players.Find(id.ToString());
            Player? migratedToPlayer = _context.Players.Find(steamID);

            if (currentPlayer == null || migratedToPlayer == null)
            {
                return BadRequest("Could not find one of the players =( Make sure you posted at least one score from the mod.");
            }

            if (migratedToPlayer.Country == "Not set" && currentPlayer.Country != "Not set")
            {
                migratedToPlayer.Country = currentPlayer.Country;
            }

            dynamic scores = _context.Scores.Include(el => el.Player).Where(el => el.Player.Id == currentPlayer.Id).Include(el => el.Leaderboard).ThenInclude(el => el.Scores);
            foreach (Score score in scores)
            {
                Score? migScore = score.Leaderboard.Scores.FirstOrDefault(el => el.PlayerId == steamID);
                if (migScore != null)
                {
                    if (migScore.ModifiedScore >= score.ModifiedScore)
                    {
                        score.Leaderboard.Scores.Remove(score);
                    }
                    else
                    {
                        score.Leaderboard.Scores.Remove(migScore);
                    }

                    var rankedScores = score.Leaderboard.Scores.Where(sc => sc != null).OrderByDescending(el => el.ModifiedScore).ToList();
                    foreach ((int i, Score? s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        if (s != null)
                        {
                            s.Rank = i + 1;
                            _context.Scores.Update(s);
                        }
                    }
                }
                else
                {
                    score.Player = migratedToPlayer;
                    score.PlayerId = steamID;
                }
            }

            _context.Players.Remove(currentPlayer);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/user/failedscores")]
        public async Task<ActionResult<IEnumerable<FailedScore>>> GetFailedScores()
        {
            string? id = GetId().Value;
            if (id == null) {
                return NotFound();
            }
            Player? currentPlayer = _context.Players.Find(id);
            if (currentPlayer != null && currentPlayer.Role.Contains("admin"))
            {
                return _context.FailedScores.Include(lb => lb.Player).Take(3).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties).ToList();
            } else {
                return _context.FailedScores.Where(t => t.PlayerId == id).Take(3).Include(lb => lb.Player).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties).ToList();
            }
        }

        [HttpPost("~/user/failedscore/remove")]
        public async Task<ActionResult> RemoveFailedScore([FromQuery] int id)
        {
            string? playerId = GetId().Value;
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
            string? playerId = GetId().Value;
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
            string userId = GetId().Value;
            var player = await _context.Players.FindAsync(userId);

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

        [HttpGet("~/clan/my")]
        public ActionResult<Clan> MyClan()
        {
            string userId = GetId().Value;
            var clan = _context.Clans.FirstOrDefault(c => c.LeaderID == userId);
            if (clan == null)
            {
                return NotFound();
            }
            return clan;
        }
    }
}