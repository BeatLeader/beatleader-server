using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
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
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin") || role == "admin")
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Find(playerId);
            if (player != null) {
                player.Role = string.Join(",", player.Role.Split(",").Append(role));
            }
            _context.SaveChanges();

            return Ok();
        }

        [HttpDelete("~/admin/role")]
        public async Task<ActionResult> RemoveRole([FromQuery] string playerId, [FromQuery] string role)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin") || role == "admin")
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Find(playerId);
            if (player != null)
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(r => r != role));
            }
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("~/admin/clan/{id}/setLeader")]
        public async Task<ActionResult> SetLeader(int id, [FromQuery] string newLeader)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Find(newLeader);
            var clan = _context.Clans.FirstOrDefault(c => c.Id == id);
            if (player != null)
            {
                clan.LeaderID = newLeader;

                player.Clans.Add(clan);

                _context.SaveChanges();
            } else {
                return NotFound();
            }

            return Ok();
        }

        [HttpPost("~/admin/clan/{id}/addMember")]
        public async Task<ActionResult> AddMember(int id, [FromQuery] string newLeader)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = _context.Players.Find(newLeader);
            var clan = _context.Clans.FirstOrDefault(c => c.Id == id);
            if (player != null)
            {
                player.Clans.Add(clan);
                clan.PlayersCount++;
                clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, player.ScoreStats.AverageRankedAccuracy);
                clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, player.Rank);
                _context.SaveChanges();

                clan.Pp = _context.RecalculateClanPP(clan.Id);

                _context.SaveChanges();
            }
            else
            {
                return NotFound();
            }

            return Ok();
        }

        [HttpGet("~/admin/allScores")]
        public async Task<ActionResult<List<Score>>> GetAllScores([FromQuery] int from, [FromQuery] int to)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            return _context.Scores.Where(s => s.Timepost >= from && s.Timepost <= to).ToList();
        }

        public static string GolovaID = "76561198059961776";
    }
}
