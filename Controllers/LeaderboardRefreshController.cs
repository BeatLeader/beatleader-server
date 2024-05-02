using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class LeaderboardRefreshController : Controller
    {
        private readonly AppContext _context;

        public LeaderboardRefreshController(
            AppContext context)
        {
            _context = context;
        }

        [HttpGet("~/leaderboards/refresh/allContexts")]
        public async Task<ActionResult> RefreshLeaderboardsRankAllContexts([FromQuery] string? id = null)
        {
            await LeaderboardRefreshControllerHelper.RefreshLeaderboardsRankAllContexts(_context, id);

            return Ok();
        }

        [HttpGet("~/leaderboards/refresh")]
        public async Task<ActionResult> RefreshLeaderboardsRank(
            [FromQuery] string? id = null,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            string? currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            await LeaderboardRefreshControllerHelper.RefreshLeaderboardsRank(_context, id, leaderboardContext);

            return Ok();
        }
    }
}
