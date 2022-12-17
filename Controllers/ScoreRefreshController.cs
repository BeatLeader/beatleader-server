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
                    var allScores = leaderboard.Scores.Where(s => !s.Banned).ToList();
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

        [HttpGet("~/scores/bulkrefresh")]
        [Authorize]
        public async Task<ActionResult> BulkRefreshScores([FromQuery] string? leaderboardId = null)
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
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            var query = _context.Leaderboards.Where(lb => true);

            var count = await (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query).CountAsync();

            for (int iii = 0; iii < count; iii += 100)
            {
                query = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query);

                var allLeaderboards = query
                    .Skip(iii)
                    .Take(100)
                    .Select(lb => new {
                        Difficulty = lb.Difficulty,
                        ModifierValues = lb.Difficulty.ModifierValues,
                        Scores = lb.Scores.Select(s => new {
                            Id = s.Id,
                            Banned = s.Banned,
                            LeaderboardId = s.LeaderboardId,
                            BaseScore = s.BaseScore,
                            Modifiers = s.Modifiers,
                        })
                    })
                    .ToList();

                foreach (var leaderboard in allLeaderboards)
                {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned).ToList();

                    var status = leaderboard.Difficulty.Status;
                    var modifiers = leaderboard.Difficulty.ModifierValues;
                    bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.nominated || status == DifficultyStatus.inevent;
                    bool hasPp = status == DifficultyStatus.ranked || qualification;
                    var newScores = new List<Score>();

                    foreach (var s in allScores)
                    {
                        var score = new Score() { Id = s.Id };
                        _context.Scores.Attach(score);
                        int maxScore = leaderboard.Difficulty.MaxScore > 0 ? leaderboard.Difficulty.MaxScore : ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        if (hasPp)
                        {
                            score.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }
                        else
                        {
                            score.ModifiedScore = (int)((s.BaseScore + (int)((float)(maxScore - s.BaseScore) * (modifiers.GetPositiveMultiplier(s.Modifiers) - 1))) * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }

                        score.Accuracy = (float)s.BaseScore / (float)maxScore;

                        if (score.Accuracy > 1.29f)
                        {
                            score.Accuracy = 1.29f;
                        }
                        if (hasPp)
                        {
                            (score.Pp, score.BonusPp) = ReplayUtils.PpFromScore(score.Accuracy, s.Modifiers, leaderboard.Difficulty.ModifierValues, leaderboard.Difficulty.Stars ?? 0);
                        }
                        else
                        {
                            score.Pp = 0;
                            score.BonusPp = 0;
                        }

                        score.Qualification = qualification;

                        if (float.IsNaN(score.Pp))
                        {
                            score.Pp = 0.0f;
                        }
                        if (float.IsNaN(score.BonusPp))
                        {
                            score.BonusPp = 0.0f;
                        }
                        if (float.IsNaN(score.Accuracy))
                        {
                            score.Accuracy = 0.0f;
                        }

                        _context.Entry(score).Property(x => x.ModifiedScore).IsModified = true;
                        _context.Entry(score).Property(x => x.Accuracy).IsModified = true;
                        _context.Entry(score).Property(x => x.Pp).IsModified = true;
                        _context.Entry(score).Property(x => x.BonusPp).IsModified = true;
                        _context.Entry(score).Property(x => x.Qualification).IsModified = true;

                        newScores.Add(score);
                    }

                    var rankedScores = hasPp ? newScores.OrderByDescending(el => el.Pp).ToList() : newScores.OrderByDescending(el => el.ModifiedScore).ToList();
                    foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                        _context.Entry(s).Property(x => x.Rank).IsModified = true;
                    }
                }

                try
                {
                    await _context.BulkSaveChangesAsync();
                }
                catch (Exception e)
                {
                    _context.RejectChanges();
                }
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }
    }
}
