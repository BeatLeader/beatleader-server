using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class ScoreRefreshController : Controller
    {
        private readonly AppContext _context;

        public ScoreRefreshController(
            AppContext context)
        {
            _context = context;
        }

        [HttpGet("~/scores/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshScores([FromQuery] string? leaderboardId = null)
        {
            if (HttpContext != null)
            {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            //var count = await _context.Leaderboards.CountAsync();

            //for (int iii = 0; iii < count; iii += 1000) 
            //{
                var query = _context.Leaderboards.Include(s => s.Scores).Include(l => l.Difficulty).ThenInclude(d => d.ModifierValues);
                var allLeaderboards = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query).Select(l => new { Scores = l.Scores, Difficulty = l.Difficulty }).ToList(); // .Skip(iii).Take(1000).ToList();

                int counter = 0;
                var transaction = await _context.Database.BeginTransactionAsync();

                foreach (var leaderboard in allLeaderboards)
                {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned && s.LeaderboardId != null).ToList();
                    var status = leaderboard.Difficulty.Status;
                    var modifiers = leaderboard.Difficulty.ModifierValues;
                    bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.nominated || status == DifficultyStatus.inevent;
                    bool hasPp = status == DifficultyStatus.ranked || qualification;

                    foreach (Score s in allScores)
                    {
                        if (hasPp)
                        {
                            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }
                        else
                        {
                            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetTotalMultiplier(s.Modifiers));
                        }

                        if (leaderboard.Difficulty.MaxScore > 0)
                        {
                            s.Accuracy = (float)s.BaseScore / (float)leaderboard.Difficulty.MaxScore;
                        }
                        else
                        {
                            s.Accuracy = (float)s.BaseScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        }

                        if (s.Accuracy > 1.29f)
                        {
                            s.Accuracy = 1.29f;
                        }
                        if (hasPp)
                        {
                            (s.Pp, s.BonusPp) = ReplayUtils.PpFromScore(s, leaderboard.Difficulty);
                        }
                        else
                        {
                            s.Pp = 0;
                            s.BonusPp = 0;
                        }

                        s.Qualification = qualification;

                        if (float.IsNaN(s.Pp))
                        {
                            s.Pp = 0.0f;
                        }
                        if (float.IsNaN(s.BonusPp))
                        {
                            s.BonusPp = 0.0f;
                        }
                        if (float.IsNaN(s.Accuracy))
                        {
                            s.Accuracy = 0.0f;
                        }
                        counter++;
                    }

                    var rankedScores = hasPp ? allScores.OrderByDescending(el => el.Pp).ToList() : allScores.OrderByDescending(el => el.ModifiedScore).ToList();
                    foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                    }

                    if (counter >= 5000)
                    {
                        counter = 0;
                        try
                        {
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception e)
                        {

                            _context.RejectChanges();
                            await transaction.RollbackAsync();
                            transaction = await _context.Database.BeginTransactionAsync();
                            continue;
                        }
                        await transaction.CommitAsync();
                        transaction = await _context.Database.BeginTransactionAsync();
                    }
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    _context.RejectChanges();
                }
                await transaction.CommitAsync();
            //}

            return Ok();
        }
    }
}
