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
            foreach (var context in ContextExtensions.All)
            {
                await RefreshLeaderboardsRank(id, context);
            }

            return Ok();
        }

        [HttpGet("~/leaderboards/refresh")]
        public async Task<ActionResult> RefreshLeaderboardsRank(
            [FromQuery] string? id = null,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            if (HttpContext != null)
            {
                string? currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            var query = _context
                .Leaderboards.Where(lb => true);

            if (id != null)
            {
                query = query.Where(lb => lb.Id == id);
            }

            int count = await query.CountAsync();

            for (int i = 0; i < count; i += 1000)
            {
                var leaderboards =
                await query
                .OrderBy(lb => lb.Id)
                .Skip(i)
                .Take(1000)
                .Select(lb => new
                {
                    lb.Id,
                    lb.Difficulty.Status,
                    Scores = 
                        leaderboardContext == LeaderboardContexts.General 
                        ? lb.Scores
                            .Where(s => !s.Banned && s.ValidContexts.HasFlag(leaderboardContext))
                            .Select(s => new { s.Id, s.Pp, s.Accuracy, s.ModifiedScore, Timeset = s.Timepost, s.Priority })
                        : lb.ContextExtensions
                            .Where(s => !s.Banned && s.Context == leaderboardContext)
                            .Select(s => new { s.Id, s.Pp, s.Accuracy, s.ModifiedScore, s.Timeset, s.Priority })
                })
                .ToArrayAsync();

                foreach (var leaderboard in leaderboards)
                {
                    var status = leaderboard.Status;

                    var rankedScores = status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent
                        ? leaderboard
                            .Scores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList()
                        : leaderboard
                            .Scores
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList();
                    if (rankedScores.Count > 0)
                    {
                        foreach ((int ii, var s) in rankedScores.Select((value, ii) => (ii, value)))
                        {
                            if (leaderboardContext == LeaderboardContexts.General) {
                                var score = new Score() { Id = s.Id };
                                try
                                {
                                    _context.Scores.Attach(score);
                                } catch { }
                                score.Rank = ii + 1;

                                _context.Entry(score).Property(x => x.Rank).IsModified = true;
                            } else {
                                var scoreExtenstion = new ScoreContextExtension() { Id = s.Id };
                                try
                                {
                                    _context.ScoreContextExtensions.Attach(scoreExtenstion);
                                } catch { }
                                scoreExtenstion.Rank = ii + 1;

                                _context.Entry(scoreExtenstion).Property(x => x.Rank).IsModified = true;
                            }
                        }
                    }

                    if (leaderboardContext == LeaderboardContexts.General) {
                        Leaderboard lb = new Leaderboard() { Id = leaderboard.Id };
                        try {
                        _context.Leaderboards.Attach(lb);
                        } catch { }
                        lb.Plays = rankedScores.Count;

                        _context.Entry(lb).Property(x => x.Plays).IsModified = true;
                    }
                }

                try
                {
                    await _context.BulkSaveChangesAsync();
                } catch (Exception e)
                {
                    _context.RejectChanges();
                }
            }

            return Ok();
        }
    }
}
