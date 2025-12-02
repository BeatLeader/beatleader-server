using BeatLeader_Server.ControllerHelpers;
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
                var query = _context
                    .Leaderboards
                    .Include(s => s.Scores)
                    .Include(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating);
                var allLeaderboards = await (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query)
                    .Select(l => new { l.Scores, l.Difficulty })
                    .ToListAsync(); // .Skip(iii).Take(1000).ToListAsync();

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

            await ScoreRefreshControllerHelper.BulkRefreshScores(_context, leaderboardId);

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
            
            var diffStatus = DifficultyStatus.ranked;
            var query = _context.Leaderboards.Where(lb => true);
            
            query = (leaderboardId != null ? query.Where(lb => lb.Id == leaderboardId) : query.Where(lb => lb.Difficulty.Status == diffStatus));

            var allLeaderboards = await query
                .AsNoTracking()
                .Select(lb => new {
                    lb.Difficulty,
                    lb.Difficulty.ModifierValues,
                    lb.Difficulty.ModifiersRating,
                    lb.Id
                }).ToListAsync();

            var allScores = (await _context
                .ScoreContextExtensions
                .AsNoTracking()
                .Where(s => s.Context == leaderboardContext && !s.Banned && ((leaderboardId == null && s.Leaderboard.Difficulty.Status == diffStatus) || (leaderboardId != null && s.LeaderboardId == leaderboardId)))
                .Select(s => new ScoreContextExtension { 
                    Id = s.Id, 
                    Timepost = s.Timepost, 
                    LeaderboardId = s.LeaderboardId, 
                    BaseScore = s.BaseScore, 
                    Modifiers = s.Modifiers, 
                    Context = s.Context 
                }).ToListAsync())
                .GroupBy(s => s.LeaderboardId)
                .ToDictionary(s => s.Key, s => s.ToList());

            var changes = new List<ScoreContextExtension>();
            var saveChanges = async () => {
                try {
                    await _context.BulkUpdateAsync(changes, options => options.ColumnInputExpression = c => new { 
                        c.Rank, 
                        c.ModifiedScore,
                        c.Accuracy,
                        c.Pp,
                        c.BonusPp,
                        c.PassPP,
                        c.AccPP,
                        c.TechPP,
                        c.Qualification,
                        c.Priority
                    });
                } catch (Exception e) {
                    try {
                        await _context.BulkUpdateAsync(changes, options => options.ColumnInputExpression = c => new { 
                            c.Rank, 
                            c.ModifiedScore,
                            c.Accuracy,
                            c.Pp,
                            c.BonusPp,
                            c.PassPP,
                            c.AccPP,
                            c.TechPP,
                            c.Qualification,
                            c.Priority
                        });
                    } catch {}
                }
            };

            foreach (var leaderboard in allLeaderboards) {
                if (!allScores.ContainsKey(leaderboard.Id)) continue;
                var scores = allScores[leaderboard.Id];

                var status = leaderboard.Difficulty.Status;
                var modifiers = leaderboard.ModifierValues ?? new ModifiersMap();
                bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                bool hasPp = status == DifficultyStatus.ranked || qualification;

                foreach (var s in scores)
                {
                    int maxScore = leaderboard.Difficulty.MaxScore > 0 ? leaderboard.Difficulty.MaxScore : ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);

                    if (hasPp)
                    {
                        s.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                    }
                    else
                    {
                        s.ModifiedScore = (int)((s.BaseScore + (int)((float)(maxScore - s.BaseScore) * (modifiers.GetPositiveMultiplier(s.Modifiers) - 1))) * modifiers.GetNegativeMultiplier(s.Modifiers));
                    }

                    s.Accuracy = (float)s.BaseScore / (float)maxScore;
                    if (s.Modifiers.Contains("NF")) {
                        s.Priority = 3;
                    } else if (s.Modifiers.Contains("NB") || s.Modifiers.Contains("NA")) {
                        s.Priority = 2;
                    } else if (s.Modifiers.Contains("NO")) {
                        s.Priority = 1;
                    }

                    if (s.Accuracy > 1f)
                    {
                        maxScore = ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        if (hasPp)
                        {
                            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }
                        else
                        {
                            s.ModifiedScore = (int)((s.BaseScore + (int)((float)(maxScore - s.BaseScore) * (modifiers.GetPositiveMultiplier(s.Modifiers) - 1))) * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }

                        s.Accuracy = (float)s.BaseScore / (float)maxScore;
                    }
                    if (hasPp)
                    {
                        (s.Pp, s.BonusPp, s.PassPP, s.AccPP, s.TechPP) = ReplayUtils.PpFromScore(
                            leaderboardContext == LeaderboardContexts.Golf ? 1f - s.Accuracy : s.Accuracy,
                            leaderboardContext,
                            s.Modifiers,
                            leaderboard.ModifierValues,
                            leaderboard.ModifiersRating,
                            leaderboard.Difficulty.AccRating ?? 0,
                            leaderboard.Difficulty.PassRating ?? 0,
                            leaderboard.Difficulty.TechRating ?? 0,
                            leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard");
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
                }

                List<ScoreContextExtension> rankedScores;
                    
                if (leaderboardContext == LeaderboardContexts.Golf) {
                    rankedScores = hasPp 
                    ? scores
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenBy(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.ModifiedScore)
                        .ThenBy(el => el.Timepost)
                        .ToList() 
                    : scores
                        .OrderBy(el => el.Priority)
                        .ThenBy(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.ModifiedScore)
                        .ThenBy(el => el.Timepost)
                        .ToList();
                } else {
                    rankedScores = hasPp 
                    ? scores
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timepost)
                        .ToList() 
                    : scores
                        .OrderBy(el => el.Priority)
                        .ThenByDescending(el => el.ModifiedScore)
                        .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timepost)
                        .ToList();
                }
                foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                }
                changes.AddRange(scores);

                if (changes.Count >= 200000) {
                    await saveChanges();
                    await _context.BulkSaveChangesAsync();
                    changes = new List<ScoreContextExtension>();
                }
            }

            await saveChanges();
            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/scores/bulkrankrefresh/allContexts")]
        [Authorize]
        public async Task<ActionResult> BulkRankRefreshScoresAllContexts()
        {
            foreach (var context in ContextExtensions.NonGeneral) {
                await BulkRankRefreshScores(context);
            }

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
            
            var scores = await _context
                .ScoreContextExtensions
                .Where(s => s.Context == leaderboardContext && !s.Banned)
                .AsNoTracking()
                .Select(s => new ScoreContextExtension { Id = s.Id, LeaderboardId = s.LeaderboardId, Pp = s.Pp, Accuracy = s.Accuracy, Timepost = s.Timepost, ModifiedScore = s.ModifiedScore })
                .ToListAsync();
            var allLeaderboards = scores
                .GroupBy(s => s.LeaderboardId)
                .ToList();

            foreach (var allScores in allLeaderboards) {

                bool hasPp = allScores.Count(s => s.Pp > 0) > 2;

                List<ScoreContextExtension> rankedScores;
                if (leaderboardContext != LeaderboardContexts.Golf) {
                    rankedScores = hasPp 
                    ? allScores
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timepost).ToList() 
                    : allScores
                        .OrderByDescending(el => el.ModifiedScore)
                        .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timepost).ToList();
                } else {
                    rankedScores = hasPp 
                    ? allScores
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenBy(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timepost).ToList() 
                    : allScores
                        .OrderBy(el => el.ModifiedScore)
                        .ThenBy(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timepost).ToList();
                }
                foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                }
            }

            await _context.BulkUpdateAsync(scores, options => options.ColumnInputExpression = c => new { c.Rank });

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

            var lbsCount = await _context.Leaderboards.CountAsync();

            for (int i = 0; i < lbsCount; i += 10000) {
                var allLeaderboards = await _context
                    .Leaderboards
                    .AsNoTracking()
                    .OrderBy(lb => lb.Id)
                    .Skip(i)
                    .Take(10000)
                    .Select(lb => new {
                        lb.Difficulty,
                        Scores = lb.Scores
                        .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General))
                        .Select(s => new Score { 
                            Id = s.Id, 
                            Banned = s.Banned, 
                            Pp = s.Pp, 
                            Accuracy = s.Accuracy, 
                            Timeset = s.Timeset, 
                            ModifiedScore = s.ModifiedScore, 
                            Priority = s.Priority 
                        })
                    }).ToListAsync();

                await Task.WhenAll(allLeaderboards.Select(async leaderboard => {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned).ToList();

                    var status = leaderboard.Difficulty.Status;
                    bool hasPp = status == DifficultyStatus.ranked || status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;

                    var rankedScores = hasPp 
                        ? allScores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList() 
                        : allScores
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList();
                    foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                    }
                }));

                await _context.BulkUpdateAsync(allLeaderboards.SelectMany(l => l.Scores), options => options.ColumnInputExpression = c => new { c.Rank });
            }

            return Ok();
        }
    }
}
