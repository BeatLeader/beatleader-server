using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class ScoreRefreshControllerHelper {
        public static async Task BulkRefreshScores(AppContext dbContext, string? leaderboardId = null)
        {
            
            var query = dbContext
                .Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.qualified);
            
            query = (leaderboardId != null ? dbContext.Leaderboards.Where(s => s.Id == leaderboardId) : query);

            var allLeaderboards = await query
                .Select(lb => new {
                    lb.Difficulty.Status,
                    lb.Difficulty.MaxScore,
                    lb.Difficulty.Notes,
                    lb.Difficulty.AccRating,
                    lb.Difficulty.PassRating,
                    lb.Difficulty.TechRating,
                    lb.Difficulty.ModifierValues,
                    lb.Difficulty.ModifiersRating,
                    lb.Difficulty.ModeName,
                    Scores = lb.Scores.Select(s => new { s.Id, s.Banned, s.Bot, s.LeaderboardId, s.BaseScore, s.Modifiers, s.FcAccuracy, s.ValidContexts }).ToList()
                })
                .AsNoTracking()
                .ToListAsync();

            var changes = new List<Score>();
            var saveChanges = async () => {
                try {
                    await dbContext.BulkUpdateAsync(changes, options => options.ColumnInputExpression = c => new { 
                        c.Rank, 
                        c.ModifiedScore,
                        c.Accuracy,
                        c.Pp,
                        c.FcPp,
                        c.BonusPp,
                        c.PassPP,
                        c.AccPP,
                        c.TechPP,
                        c.Qualification,
                        c.Priority
                    });
                } catch {
                    try {
                        await dbContext.BulkUpdateAsync(changes, options => options.ColumnInputExpression = c => new { 
                            c.Rank, 
                            c.ModifiedScore,
                            c.Accuracy,
                            c.Pp,
                            c.FcPp,
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
                var allScores = leaderboard.Scores.Where(s => (!s.Banned || s.Bot) && s.ValidContexts.HasFlag(LeaderboardContexts.General)).ToList();

                var status = leaderboard.Status;
                var modifiers = leaderboard.ModifierValues ?? new ModifiersMap();
                bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
                bool hasPp = status == DifficultyStatus.ranked || qualification;
                var newScores = new List<Score>();

                foreach (var s in allScores)
                {
                    var score = new Score() { Id = s.Id, Banned = s.Banned };
                    int maxScore = leaderboard.MaxScore > 0 ? leaderboard.MaxScore : ReplayUtils.MaxScoreForNote(leaderboard.Notes);
                    if (hasPp)
                    {
                        score.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers, true));
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
                        maxScore = ReplayUtils.MaxScoreForNote(leaderboard.Notes);
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
                            leaderboard.ModifierValues,
                            leaderboard.ModifiersRating,
                            leaderboard.AccRating ?? 0,
                            leaderboard.PassRating ?? 0,
                            leaderboard.TechRating ?? 0,
                            leaderboard.ModeName.ToLower() == "rhythmgamestandard");
                        (score.FcPp, _, _, _, _) = ReplayUtils.PpFromScore(
                            s.FcAccuracy, 
                            LeaderboardContexts.General,
                            s.Modifiers, 
                            leaderboard.ModifierValues, 
                            leaderboard.ModifiersRating, 
                            leaderboard.AccRating ?? 0, 
                            leaderboard.PassRating ?? 0, 
                            leaderboard.TechRating ?? 0, 
                            leaderboard.ModeName.ToLower() == "rhythmgamestandard");
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

                    newScores.Add(score);
                }

                var rankedScores = hasPp 
                    ? newScores
                        .Where(s => !s.Banned)
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timeset)
                        .ToList() 
                    : newScores
                        .Where(s => !s.Banned)
                        .OrderBy(el => el.Priority)
                        .ThenByDescending(el => el.ModifiedScore)
                        .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timeset)
                        .ToList();
                foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                }

                changes.AddRange(rankedScores);

                if (rankedScores.Count > 100000) {
                    await saveChanges();
                    changes = new List<Score>();
                }
            }

            await saveChanges();
        }
    }
}
