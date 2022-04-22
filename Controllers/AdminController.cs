using Azure.Identity;
using Azure.Storage.Blobs;
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

        //[HttpPost("~/admin/ban")]
        //public async Task<ActionResult> Ban([FromQuery] string playerId)
        //{
        //    string? userId = _currentUserController.GetId().Value;
        //    var currentPlayer = await _context.Players.FindAsync(userId);

        //    if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
        //    {
        //        return Unauthorized();
        //    }


        //}
    }
}
