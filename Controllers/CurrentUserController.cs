using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
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
            if (intId == 1 || (intId > 2050 && intId < 2100)) {
                AuthID? authID = _context.AuthIDs.FirstOrDefault(a => a.Id == currentID);
                if (authID == null) {
                    return Redirect("/signout");
                }
            }
            return accountLink != null ? accountLink.SteamID : currentID;
        }

        [HttpGet("~/user/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> GetAllPlaylists()
        {
            string currentId = HttpContext.CurrentUserID();
            return await _context.Playlists.Where(t => t.OwnerId == currentId).ToListAsync();
        }

        [HttpGet("~/user/playlist")]
        public async Task<ActionResult<Playlist>> Get([FromQuery]int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);

            if (playlist == null)
            {
                return NotFound();
            }

            if (!playlist.IsShared && playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }

            return playlist;
        }

        [HttpPatch("~/user/playlist")]
        public async Task<ActionResult<Playlist>> SharePlaylist([FromBody] dynamic content, [FromQuery] bool shared, [FromQuery] int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);

            if (playlist == null)
            {
                return NotFound();
            }

            if (playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }

            //playlist.Value = content.GetRawText();
            playlist.IsShared = shared;

            _context.Playlists.Update(playlist);

            await _context.SaveChangesAsync();

            return playlist;
        }

        [HttpPatch("~/user/avatar")]
        public async Task<ActionResult> ChangeAvatar()
        {
            string userId = GetId().Value;
            var player = await _context.Players.FindAsync(userId);

            if (player == null)
            {
                return NotFound();
            }

            string fileName = userId + ".png";
            try
            {
                await _assetsContainerClient.CreateIfNotExistsAsync();

                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                await _assetsContainerClient.UploadBlobAsync(fileName, ms);
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
        public async Task<ActionResult> ChangeName([FromQuery] string newName)
        {
            string userId = GetId().Value;
            var player = await _context.Players.FindAsync(userId);

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
                return BadRequest("Could not find one of the players =(");
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
                    } else
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
            await _playerController.RefreshPlayers();

            return Ok();
        }

        [HttpPost("~/user/playlist")]
        public async Task<ActionResult<int>> PostPlaylist([FromBody] dynamic content, [FromQuery] bool shared)
        {
            Playlist newPlaylist = new Playlist();
            //newPlaylist.Value = content.GetRawText();
            newPlaylist.OwnerId = HttpContext.CurrentUserID();
            newPlaylist.IsShared = shared;
            _context.Playlists.Add(newPlaylist);
            
            await _context.SaveChangesAsync();

            return CreatedAtAction("PostPlaylist", new { id = newPlaylist.Id }, newPlaylist);
        }

        [HttpDelete("~/user/playlist")]
        public async Task<ActionResult<int>> DeletePlaylist([FromQuery] int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);
            if (playlist == null)
            {
                return NotFound();
            }

            if (playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }
            _context.Playlists.Remove(playlist);

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
            return _context.FailedScores.Where(t => t.PlayerId == id).Include(lb => lb.Player).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties).ToList();
        }

        [HttpPost("~/user/failedscore/remove")]
        public async Task<ActionResult<IEnumerable<FailedScore>>> RemoveFailedScore([FromQuery] int id)
        {
            string? playerId = GetId().Value;
            if (playerId == null)
            {
                return NotFound();
            }
            var score = _context.FailedScores.FirstOrDefault(t => t.PlayerId == playerId && t.Id == id);
            if (score == null) {
                return NotFound();
            }

            _context.FailedScores.Remove(score);
            _context.SaveChanges();
            return Ok();
        }

        [HttpPost("~/user/failedscore/retry")]
        public async Task<ActionResult<IEnumerable<FailedScore>>> RetryFailedScore([FromQuery] int id)
        {
            string? playerId = GetId().Value;
            if (playerId == null)
            {
                return NotFound();
            }
            var score = _context.FailedScores.FirstOrDefault(t => t.PlayerId == playerId && t.Id == id);
            if (score == null)
            {
                return NotFound();
            }

            _context.FailedScores.Remove(score);
            _context.SaveChanges();

            string? name = score.Replay.Split("/").LastOrDefault();
            if (name == null)
            {
                return Ok();
            }

            await _replayController.PostReplayFromCDN(playerId, name);

            return Ok();
        }
    }
}