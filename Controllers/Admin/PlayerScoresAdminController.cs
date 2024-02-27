using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers.Admin
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class PlayerScoresAdminController : Controller
    {
        private readonly AppContext _context;
        CurrentUserController _currentUserController;
        ScoreRefreshController _scoreRefreshController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;

        public PlayerScoresAdminController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController currentUserController,
            ScoreRefreshController scoreRefreshController,
            ReplayController replayController,
            IConfiguration configuration)
        {
            _context = context;
            _currentUserController = currentUserController;
            _scoreRefreshController = scoreRefreshController;
            _replayController = replayController;
            _environment = env;
        }

        [HttpDelete("~/player/{id}/score/{leaderboardID}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(string id, string leaderboardID) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }
            Leaderboard? leaderboard = await _context.Leaderboards.Where(l => l.Id == leaderboardID).Include(l => l.Scores).FirstOrDefaultAsync();
            if (leaderboard == null) {
                return NotFound();
            }
            Score? scoreToDelete = leaderboard.Scores.FirstOrDefault(t => t.PlayerId == id);

            if (scoreToDelete == null) {
                return NotFound();
            }

            _context.Scores.Remove(scoreToDelete);
            await _context.SaveChangesAsync();
            return Ok();

        }
    }
}
