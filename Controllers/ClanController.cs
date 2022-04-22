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
    public class ClanController : Controller
    {
        private readonly AppContext _context;
        BlobContainerClient _assetsContainerClient;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;

        public ClanController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            CurrentUserController userController)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            if (env.IsDevelopment())
            {
                _assetsContainerClient = new BlobContainerClient(config.Value.AccountName, config.Value.AssetsContainerName);
                _assetsContainerClient.SetPublicContainerPermissions();
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.AssetsContainerName);

                _assetsContainerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
        }

        [HttpGet("~/clan/{id}")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<Player, Clan>>> GetClan(int id)
        {
            var clan = await _context.Clans.FindAsync(id);
            if (clan == null)
            {
                return NotFound();
            }

            var players = _context.Players.Where(p => p.Clans.Contains(clan)).ToList();
            return new ResponseWithMetadataAndContainer<Player, Clan> {
                Container = clan,
                Data = players,
                Metadata = new Metadata {
                    Page = 1,
                    ItemsPerPage = 10,
                    Total = players.Count
                }
            };
        }

        [HttpPost("~/clan/create")]
        [Authorize]
        public async Task<ActionResult<Clan>> CreateClan(
            [FromQuery] string name,
            [FromQuery] string tag,
            [FromQuery] string color)
        {
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

            string userId = accountLink != null ? accountLink.SteamID : currentID;

            if (_context.Clans.FirstOrDefault(c => c.Name == name || c.Tag == tag) != null)
            {
                return BadRequest("Clan with such name or tag is already exists");
            }

            if (_context.Clans.FirstOrDefault(c => c.LeaderID == userId) != null)
            {
                return BadRequest("You already have clan");
            }

            var player = await _context.Players.FindAsync(userId);

            if (player == null)
            {
                return NotFound("wHAT?");
            }

            string fileName = tag;
            string? icon;
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

                icon = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://cdn.beatleader.xyz/assets/") + fileName;
            }
            catch (Exception e)
            {
                return BadRequest("Error saving avatar");
            }

            Clan newClan = new Clan
            {
                Name = name,
                Tag = tag,
                Color = color,
                LeaderID = userId,
                Icon = icon ?? "https://cdn.beatleader.xyz/assets/clan.png"
            };
            _context.Clans.Add(newClan);
            _context.SaveChanges();

            player.Clans.Add(newClan);
            _context.Players.Update(player);
            _context.SaveChanges();

            return newClan;
        }

        [HttpGet("~/clans/")]
        public async Task<ActionResult<ResponseWithMetadata<Clan>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "stars",
            [FromQuery] string order = "desc",
            [FromQuery] string? search = null,
            [FromQuery] string? type = null)
        {
            var sequence = _context.Clans.AsQueryable();
            switch (sortBy)
            {
                case "pp":
                    sequence = sequence.Order(order, t => t.Pp);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Where(p => p.Name.ToLower().Contains(lowSearch) ||
                                p.Tag.ToLower().Contains(lowSearch));
            }

            return new ResponseWithMetadata<Clan>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = await sequence.Skip((page - 1) * count).Take(count).ToListAsync()
            };
        }

        [HttpPost("~/clan/invite")]
        [Authorize]
        public async Task<ActionResult> InviteToClan([FromQuery] string player)
        {
            string userId = _userController.GetId().Value;

            Clan? clan = _context.Clans.FirstOrDefault(c => c.LeaderID == userId);
            if (clan == null) {
                return NotFound("Current user is not leader of any clan");
            }

            User? user = _userController.GetUserLazy(player);
            if (user == null) {
                return NotFound("No such player");
            }

            if (user.BannedClans.FirstOrDefault(c => c.Id == clan.Id) != null) {
                return BadRequest("This clan was banned by player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null) {
                return BadRequest("Player already in this clan");
            }

            user.ClanRequest.Add(clan);
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/clan/cancelinvite")]
        [Authorize]
        public async Task<ActionResult> CancelinviteToClan([FromQuery] string player)
        {
            string userId = _userController.GetId().Value;

            Clan? clan = _context.Clans.FirstOrDefault(c => c.LeaderID == userId);
            if (clan == null)
            {
                return NotFound("Current user is not leader of any clan");
            }

            User? user = _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.BannedClans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                return BadRequest("This clan was banned by player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                return BadRequest("Player already in this clan");
            }

            Clan? clanRequest = user.ClanRequest.FirstOrDefault(c => c.Id == clan.Id);
            if (clanRequest == null)
            {
                return NotFound("Player did not have request to this clan");
            }

            user.ClanRequest.Remove(clanRequest);
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/clan/kickplayer")]
        [Authorize]
        public async Task<ActionResult> KickPlayer([FromQuery] string player)
        {
            string userId = _userController.GetId().Value;

            Clan? clan = _context.Clans.FirstOrDefault(c => c.LeaderID == userId);
            if (clan == null)
            {
                return NotFound("Current user is not leader of any clan");
            }

            User? user = _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) == null)
            {
                return NotFound("Player did not belong to this clan");
            }

            user.Player.Clans.Remove(clan);
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/clan/accept")]
        [Authorize]
        public async Task<ActionResult> AcceptRequest([FromQuery] int id)
        {
            string userId = _userController.GetId().Value;

            User? user = _userController.GetUserLazy(userId);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.ClanRequest.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User did not received request to this clan");
            }

            user.ClanRequest.Remove(clan);
            _context.Users.Update(user);

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                _context.SaveChanges();
                return BadRequest("Player already in this clan");
            }

            user.Player.Clans.Add(clan);
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/clan/reject")]
        [Authorize]
        public async Task<ActionResult> RejectRequest([FromQuery] int id, bool ban = false)
        {
            string userId = _userController.GetId().Value;

            User? user = _userController.GetUserLazy(userId);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.ClanRequest.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User did not received request to this clan");
            }

            user.ClanRequest.Remove(clan);
            if (ban) {
                user.BannedClans.Add(clan);
            }
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/clan/unban")]
        [Authorize]
        public async Task<ActionResult> UnbanClan([FromQuery] int id)
        {
            string userId = _userController.GetId().Value;

            User? user = _userController.GetUserLazy(userId);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.BannedClans.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User did not ban this clan");
            }

            user.BannedClans.Remove(clan);
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/clan/leave")]
        [Authorize]
        public async Task<ActionResult> leaveClan([FromQuery] int id)
        {
            string userId = _userController.GetId().Value;

            User? user = _userController.GetUserLazy(userId);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.Player.Clans.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User is not member of this clan");
            }

            user.Player.Clans.Remove(clan);
            _context.Users.Update(user);
            _context.SaveChanges();

            return Ok();
        }
    }
}
