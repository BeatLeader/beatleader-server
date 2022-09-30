using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BeatLeader_Server.Controllers.RankController;

namespace BeatLeader_Server.Controllers
{
    public class StatsController : Controller
    {
        private readonly AppContext _context;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public StatsController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;

            _serverTiming = serverTiming;
            _configuration = configuration;
        }

        [HttpPost("~/played/{hash}/{diff}/{mode}/")]
        [Authorize]
        public async Task<ActionResult> Played(
            string hash,
            string diff,
            string mode,
            [FromQuery] float time = 0,
            [FromQuery] int score = 0,
            [FromQuery] EndType type = 0)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null)
            {
                return Unauthorized();
            }

            var leaderboard = _context
                .Leaderboards
                .Include(s => s.PlayerStats)
                .FirstOrDefault(l => 
                    l.Song.Hash == hash && 
                    l.Difficulty.DifficultyName == diff && 
                    l.Difficulty.ModeName == mode);

            if (leaderboard == null) {
                return NotFound();
            }

            if (leaderboard.PlayerStats == null) {
                leaderboard.PlayerStats = new List<PlayerLeaderboardStats>();
            }

            leaderboard.PlayerStats.Add(new PlayerLeaderboardStats {
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Time = time,
                Score = score,
                Type = type,
                PlayerId = currentID
            });
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/watched/{scoreId}/")]
        public async Task<ActionResult<VoteStatus>> Played(
            int scoreId)
        {
            var ip = HttpContext.Request.HttpContext.Connection.RemoteIpAddress;

            if (ip == null) return BadRequest();

            int ipHash = ip.GetHashCode();

            if ((await _context.WatchingSessions.FirstOrDefaultAsync(ws => ws.ScoreId == scoreId && ws.IPHash == ipHash)) != null) return Ok(); 

            Score? score = await _context.Scores.FindAsync(scoreId);
            if (score == null) return NotFound();

            score.ReplayWatched++;

            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID != null)
            {
                var player = await _context.Players.Where(p => p.Id == currentID).Include(p => p.ScoreStats).FirstOrDefaultAsync();
                if (player != null) {
                    player.ScoreStats.WatchedReplays++;
                }
            }

            _context.WatchingSessions.Add(new ReplayWatchingSession {
                ScoreId = scoreId,
                IPHash = ipHash
            });
            _context.SaveChanges();

            return Ok();
        }
    }
}
