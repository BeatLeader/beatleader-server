using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BeatLeader_Server.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppContext _context;
        BlobContainerClient _assetsContainerClient;
        CurrentUserController _currentUserController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;

        public AdminController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            CurrentUserController currentUserController,
            ReplayController replayController)
        {
            _context = context;
            _currentUserController = currentUserController;
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

        [HttpPost("~/admin/role")]
        public async Task<ActionResult> AddRole([FromQuery] string playerId, [FromQuery] string role)
        {
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

            string userId = accountLink != null ? accountLink.SteamID : currentID;
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin") || role == "admin")
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Find(playerId);
            if (player != null) {
                player.Role = string.Join(",", player.Role.Split(",").Append(role));
                _context.Players.Update(player);
            }
            _context.SaveChanges();

            return Ok();
        }

        public static string GolovaID = "76561198059961776";
    }
}
