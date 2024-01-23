using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;
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
                var query = _context
                    .Leaderboards
                    .Include(s => s.Scores)
                    .Include(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating);
                var allLeaderboards = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query)
                    .Select(l => new { l.Scores, l.Difficulty })
                    .ToList(); // .Skip(iii).Take(1000).ToList();

                int counter = 0;
                var transaction = await _context.Database.BeginTransactionAsync();

                foreach (var leaderboard in allLeaderboards)
                {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned && s.ValidContexts.HasFlag(LeaderboardContexts.General)).ToList();
                    var status = leaderboard.Difficulty.Status;
                    var modifiers = leaderboard.Difficulty.ModifierValues ?? new ModifiersMap();
                    bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                    bool hasPp = status == DifficultyStatus.ranked || qualification;

                    foreach (Score s in allScores)
                    {
                        int maxScore = leaderboard.Difficulty.MaxScore > 0 ? leaderboard.Difficulty.MaxScore : ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        if (hasPp)
                        {
                            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers ?? ""));
                        }
                        else
                        {
                            s.ModifiedScore = (int)((s.BaseScore + (int)((float)(maxScore - s.BaseScore) * (modifiers.GetPositiveMultiplier(s.Modifiers) - 1))) * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }

                        if (s.Modifiers != null) {
                            if (s.Modifiers.Contains("NF")) {
                                s.Priority = 3;
                            } else if (s.Modifiers.Contains("NB") || s.Modifiers.Contains("NA")) {
                                s.Priority = 2;
                            } else if (s.Modifiers.Contains("NO")) {
                                s.Priority = 1;
                            }
                        }

                        s.Accuracy = (float)s.BaseScore / (float)maxScore;

                        if (s.Accuracy > 1.29f)
                        {
                            s.Accuracy = 1.29f;
                        }
                        if (hasPp)
                        {
                            (s.Pp, s.BonusPp, s.PassPP, s.AccPP, s.TechPP) = ReplayUtils.PpFromScore(s, leaderboard.Difficulty);
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

                    var rankedScores = hasPp 
                        ? allScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList() 
                        : allScores
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList();
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
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                    return Unauthorized();
                }
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false; 
            
            var query = _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked || lb.Difficulty.Status == DifficultyStatus.nominated || lb.Difficulty.Status == DifficultyStatus.qualified);

            
                query = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query);

                var allLeaderboards = query
                    .Select(lb => new {
                        lb.Difficulty,
                        lb.Difficulty.ModifierValues,
                        lb.Difficulty.ModifiersRating,
                        Scores = lb.Scores.Select(s => new { s.Id, s.Banned, s.LeaderboardId, s.BaseScore, s.Modifiers, s.FcAccuracy, s.ValidContexts })
                    }).ToList();
                await allLeaderboards.ParallelForEachAsync(async leaderboard => {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned && s.ValidContexts.HasFlag(LeaderboardContexts.General)).ToList();

                    var status = leaderboard.Difficulty.Status;
                    var modifiers = leaderboard.Difficulty.ModifierValues;
                    bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                    bool hasPp = status == DifficultyStatus.ranked || qualification;
                    var newScores = new List<Score>();

                    foreach (var s in allScores)
                    {
                        var score = new Score() { Id = s.Id };
                        try {
                            _context.Scores.Attach(score);
                        } catch { }
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
                        if (s.Modifiers.Contains("NF")) {
                            score.Priority = 3;
                        } else if (s.Modifiers.Contains("NB") || s.Modifiers.Contains("NA")) {
                            score.Priority = 2;
                        } else if (s.Modifiers.Contains("NO")) {
                            score.Priority = 1;
                        }

                        if (score.Accuracy > 1f)
                        {
                            maxScore = ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                            if (hasPp)
                            {
                                score.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                            }
                            else
                            {
                                score.ModifiedScore = (int)((s.BaseScore + (int)((float)(maxScore - s.BaseScore) * (modifiers.GetPositiveMultiplier(s.Modifiers) - 1))) * modifiers.GetNegativeMultiplier(s.Modifiers));
                            }

                            score.Accuracy = (float)s.BaseScore / (float)maxScore;
                        }
                        if (hasPp)
                        {
                            (score.Pp, score.BonusPp, score.PassPP, score.AccPP, score.TechPP) = ReplayUtils.PpFromScore(
                                score.Accuracy,
                                LeaderboardContexts.General,
                                s.Modifiers,
                                leaderboard.Difficulty.ModifierValues,
                                leaderboard.Difficulty.ModifiersRating,
                                leaderboard.Difficulty.AccRating ?? 0,
                                leaderboard.Difficulty.PassRating ?? 0,
                                leaderboard.Difficulty.TechRating ?? 0,
                                leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard");
                            (score.FcPp, _, _, _, _) = ReplayUtils.PpFromScore(
                                s.FcAccuracy, 
                                LeaderboardContexts.General,
                                s.Modifiers, 
                                leaderboard.Difficulty.ModifierValues, 
                                leaderboard.Difficulty.ModifiersRating, 
                                leaderboard.Difficulty.AccRating ?? 0, 
                                leaderboard.Difficulty.PassRating ?? 0, 
                                leaderboard.Difficulty.TechRating ?? 0, 
                                leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard");
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
                        _context.Entry(score).Property(x => x.FcPp).IsModified = true;
                        _context.Entry(score).Property(x => x.BonusPp).IsModified = true;
                        _context.Entry(score).Property(x => x.PassPP).IsModified = true;
                        _context.Entry(score).Property(x => x.AccPP).IsModified = true;
                        _context.Entry(score).Property(x => x.TechPP).IsModified = true;
                        _context.Entry(score).Property(x => x.Qualification).IsModified = true;
                        _context.Entry(score).Property(x => x.Priority).IsModified = true;

                        newScores.Add(score);
                    }

                    var rankedScores = hasPp 
                        ? newScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList() 
                        : newScores
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList();
                    foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                        _context.Entry(s).Property(x => x.Rank).IsModified = true;
                    }
                }, maxDegreeOfParallelism: 20);

            await _context.BulkSaveChangesAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }

        [HttpGet("~/scores/bulkrefresh/allContexts")]
        [Authorize]
        public async Task<ActionResult> BulkRefreshScoresAllContexts([FromQuery] string? leaderboardId = null)
        {
            foreach (var context in ContextExtensions.NonGeneral) {
                await BulkRefreshContextScores(context, leaderboardId);
            }

            return Ok();
        }

        [HttpGet("~/scores/{leaderboardContext}/bulkrefresh")]
        [Authorize]
        public async Task<ActionResult> BulkRefreshContextScores(LeaderboardContexts leaderboardContext, [FromQuery] string? leaderboardId = null)
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                    return Unauthorized();
                }
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false; 
            
            var query = _context.Leaderboards.Where(lb => true);

            
                query = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query);

                var allLeaderboards = query
                    .Select(lb => new {
                        lb.Difficulty,
                        lb.Difficulty.ModifierValues,
                        lb.Difficulty.ModifiersRating,
                        Scores = lb.ContextExtensions.Where(s => s.Context == leaderboardContext).Select(s => new { s.Id, s.LeaderboardId, s.BaseScore, s.Modifiers, s.Context })
                    }).ToList();
                await allLeaderboards.ParallelForEachAsync(async leaderboard => {
                    var allScores = leaderboard.Scores.ToList();

                    var status = leaderboard.Difficulty.Status;
                    var modifiers = leaderboard.Difficulty.ModifierValues;
                    bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                    bool hasPp = status == DifficultyStatus.ranked || qualification;
                    var newScores = new List<ScoreContextExtension>();

                    foreach (var s in allScores)
                    {
                        var score = new ScoreContextExtension() { Id = s.Id };
                        try {
                            _context.ScoreContextExtensions.Attach(score);
                        } catch { }

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
                        if (s.Modifiers.Contains("NF")) {
                            score.Priority = 3;
                        } else if (s.Modifiers.Contains("NB") || s.Modifiers.Contains("NA")) {
                            score.Priority = 2;
                        } else if (s.Modifiers.Contains("NO")) {
                            score.Priority = 1;
                        }

                        if (score.Accuracy > 1f)
                        {
                            maxScore = ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                            if (hasPp)
                            {
                                score.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                            }
                            else
                            {
                                score.ModifiedScore = (int)((s.BaseScore + (int)((float)(maxScore - s.BaseScore) * (modifiers.GetPositiveMultiplier(s.Modifiers) - 1))) * modifiers.GetNegativeMultiplier(s.Modifiers));
                            }

                            score.Accuracy = (float)s.BaseScore / (float)maxScore;
                        }
                        if (hasPp)
                        {
                            (score.Pp, score.BonusPp, score.PassPP, score.AccPP, score.TechPP) = ReplayUtils.PpFromScore(
                                leaderboardContext == LeaderboardContexts.Golf ? 1f - score.Accuracy : score.Accuracy,
                                leaderboardContext,
                                s.Modifiers,
                                leaderboard.Difficulty.ModifierValues,
                                leaderboard.Difficulty.ModifiersRating,
                                leaderboard.Difficulty.AccRating ?? 0,
                                leaderboard.Difficulty.PassRating ?? 0,
                                leaderboard.Difficulty.TechRating ?? 0,
                                leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard");
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
                        _context.Entry(score).Property(x => x.PassPP).IsModified = true;
                        _context.Entry(score).Property(x => x.AccPP).IsModified = true;
                        _context.Entry(score).Property(x => x.TechPP).IsModified = true;
                        _context.Entry(score).Property(x => x.Qualification).IsModified = true;
                        _context.Entry(score).Property(x => x.Priority).IsModified = true;

                        newScores.Add(score);
                    }

                    List<ScoreContextExtension> rankedScores;
                    
                    if (leaderboardContext == LeaderboardContexts.Golf) {
                        rankedScores = hasPp 
                        ? newScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenBy(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.ModifiedScore)
                            .ThenBy(el => el.Timeset)
                            .ToList() 
                        : newScores
                            .OrderBy(el => el.Priority)
                            .ThenBy(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.ModifiedScore)
                            .ThenBy(el => el.Timeset)
                            .ToList();
                    } else {
                        rankedScores = hasPp 
                        ? newScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList() 
                        : newScores
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList();
                    }
                    foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                        _context.Entry(s).Property(x => x.Rank).IsModified = true;
                    }
                }, maxDegreeOfParallelism: 20);

            await _context.BulkSaveChangesAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }

        [HttpGet("~/scores/{leaderboardContext}/bulkrankrefresh")]
        [Authorize]
        public async Task<ActionResult> BulkRankRefreshScores(LeaderboardContexts leaderboardContext)
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
            
            var allLeaderboards = _context.Leaderboards
                .Select(lb => new {
                    lb.Difficulty,
                    Scores = lb.ContextExtensions.Where(s => s.Context == leaderboardContext).Select(s => new { s.Id, s.Pp, s.Accuracy, s.Timeset, s.ModifiedScore })
                }).ToList();
                await allLeaderboards.ParallelForEachAsync(async leaderboard => {
                    var allScores = leaderboard.Scores.ToList();

                    var status = leaderboard.Difficulty.Status;
                    bool hasPp = status == DifficultyStatus.ranked || status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                    var newScores = new List<ScoreContextExtension>();

                    foreach (var s in allScores)
                    {
                        var score = new ScoreContextExtension() { Id = s.Id, Pp = s.Pp, Accuracy = s.Accuracy, Timeset = s.Timeset, ModifiedScore = s.ModifiedScore };
                        try {
                            _context.ScoreContextExtensions.Attach(score);
                        } catch { }

                        //score.ModifiedScore = leaderboard.Difficulty.MaxScore - score.ModifiedScore;

                        newScores.Add(score);
                    }

                    List<ScoreContextExtension> rankedScores;
                    if (leaderboardContext != LeaderboardContexts.Golf) {
                        rankedScores = hasPp 
                        ? newScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList() 
                        : newScores
                            .OrderByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList();
                    } else {
                        rankedScores = hasPp 
                        ? newScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList() 
                        : newScores
                            .OrderBy(el => el.ModifiedScore)
                            .ThenBy(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList();
                    }
                    foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                        _context.Entry(s).Property(x => x.Rank).IsModified = true;
                        //_context.Entry(s).Property(x => x.ModifiedScore).IsModified = true;
                    }
                }, maxDegreeOfParallelism: 20);

            await _context.BulkSaveChangesAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }

        [HttpGet("~/scores/bulkrankrefresh")]
        [Authorize]
        public async Task<ActionResult> BulkRankRefreshContextScores()
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
            
            var allLeaderboards = _context.Leaderboards
                .Select(lb => new {
                    lb.Difficulty,
                    Scores = lb.Scores.Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General)).Select(s => new { s.Id, s.Banned, s.Pp, s.Accuracy, s.Timeset, s.ModifiedScore, s.Priority })
                }).ToList();

            for (int i = 0; i < allLeaderboards.Count; i += 1000)
            {
                await allLeaderboards.Skip(i).Take(1000).ParallelForEachAsync(async leaderboard => {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned).ToList();

                    var status = leaderboard.Difficulty.Status;
                    bool hasPp = status == DifficultyStatus.ranked || status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                    var newScores = new List<Score>();

                    foreach (var s in allScores)
                    {
                        var score = new Score() { Id = s.Id, Pp = s.Pp, Accuracy = s.Accuracy, Timeset = s.Timeset, ModifiedScore = s.ModifiedScore, Priority = s.Priority };
                        try {
                            _context.Scores.Attach(score);
                        } catch { }

                        newScores.Add(score);
                    }

                    var rankedScores = hasPp 
                        ? newScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList() 
                        : newScores
                            .OrderBy(el => el.Priority)
                            .OrderByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList();
                    foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                        _context.Entry(s).Property(x => x.Rank).IsModified = true;
                    }
                }, maxDegreeOfParallelism: 20);

                await _context.BulkSaveChangesAsync();
            }
            

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            return Ok();
        }
    }
}
