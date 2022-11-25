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
                string containerEndpoint = $"https://{config.Value.AccountName}.blob.core.windows.net/{config.Value.AssetsContainerName}";

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

            Player? player = await _context.Players.FindAsync(playerId);
            if (player != null) {
                player.Role = string.Join(",", player.Role.Split(",").Append(role));
            }
            await _context.SaveChangesAsync();

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

            Player? player = await _context.Players.FindAsync(playerId);
            if (player != null)
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(r => r != role));
            }
            await _context.SaveChangesAsync();

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

            Player? player = await _context.Players.FindAsync(newLeader);
            var clan = _context.Clans.FirstOrDefault(c => c.Id == id);
            if (player != null)
            {
                clan.LeaderID = newLeader;

                player.Clans.Add(clan);

                await _context.SaveChangesAsync();
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

            Player? player = await _context.Players.FindAsync(newLeader);
            var clan = _context.Clans.FirstOrDefault(c => c.Id == id);
            if (player != null)
            {
                player.Clans.Add(clan);
                clan.PlayersCount++;
                clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, player.ScoreStats.AverageRankedAccuracy);
                clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, player.Rank);
                await _context.SaveChangesAsync();

                clan.Pp = _context.RecalculateClanPP(clan.Id);

                await _context.SaveChangesAsync();
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

        #region RecalculateLeaderboardTimestamps

        public class LeaderboardTimestampsRecalculationResult {
            public int Total { get; set; }
            public int Failed { get; set; }
        }

        [HttpGet("~/admin/recalculateLeaderboardTimestamps")]
        public async Task<ActionResult<LeaderboardTimestampsRecalculationResult>> RecalculateLeaderboardTimestamps() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false;

            var allLeaderboards = _context.Leaderboards.Select(l => new { Id = l.Id, ScoreTimesets = l.Scores.Select(s => s.Timeset).ToList() }).ToList();


            var result = new LeaderboardTimestampsRecalculationResult();

            foreach (var leaderboard in allLeaderboards) {
                var firstScoreTimestamp = long.MaxValue;
                var timeset = leaderboard.ScoreTimesets.OrderBy(s => s).FirstOrDefault();
                if (timeset != null) {
                    long.TryParse(timeset, out firstScoreTimestamp);
                }

                result.Total += 1;
                if (firstScoreTimestamp == long.MaxValue) {
                    result.Failed += 1;
                    continue;
                }

                var lb = new Leaderboard() { Id = leaderboard.Id, Timestamp = firstScoreTimestamp };
                _context.Leaderboards.Attach(lb);
                _context.Entry(lb).Property(x => x.Timestamp).IsModified = true;
            }

            await _context.BulkSaveChangesAsync();

            return result;
        }

        #endregion

        #region RecalculateLeaderboardGroups

        public class LeaderboardGroupsRecalculationResult {
            public int Total { get; set; }
        }

        [HttpGet("~/admin/recalculateLeaderboardGroups")]
        public async Task<ActionResult<LeaderboardGroupsRecalculationResult>> RecalculateLeaderboardGroups() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var allLeaderboards = _context.Leaderboards
                .ToList();

            var map = new Dictionary<string, List<Leaderboard>>();
            
            foreach (var leaderboard in allLeaderboards) {
                var baseId = leaderboard.Id.Replace("x", "");
                List<Leaderboard> entry;
                if (map.ContainsKey(baseId)) {
                    entry = map[baseId];
                } else {
                    entry = new List<Leaderboard>();
                    map[baseId] = entry;
                }
                entry.Add(leaderboard);
            }

            foreach (var (_, leaderboards) in map) {
                if (leaderboards.Count == 1) continue;

                var group = new LeaderboardGroup {
                    Leaderboards = leaderboards
                };
                foreach (var leaderboard in leaderboards) {
                    leaderboard.LeaderboardGroup = group;
                }
            }

            await _context.BulkSaveChangesAsync();
            
            return new LeaderboardGroupsRecalculationResult {
                Total = map.Count
            };
        }

        #endregion

        public static string GolovaID = "76561198059961776";
    }
}
