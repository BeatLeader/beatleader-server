using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class LeaderboardRefreshControllerHelper {
        public static async Task RefreshLeaderboardsRankAllContexts(AppContext dbContext, string? id = null)
        {
            foreach (var context in ContextExtensions.All)
            {
                await RefreshLeaderboardsRank(dbContext, id, context);
            }
        }

        public static async Task RefreshLeaderboardsRank(AppContext dbContext, string? id = null, LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            var query = dbContext
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
                                    dbContext.Scores.Attach(score);
                                } catch { }
                                score.Rank = ii + 1;

                                dbContext.Entry(score).Property(x => x.Rank).IsModified = true;
                            } else {
                                var scoreExtenstion = new ScoreContextExtension() { Id = s.Id };
                                try
                                {
                                    dbContext.ScoreContextExtensions.Attach(scoreExtenstion);
                                } catch { }
                                scoreExtenstion.Rank = ii + 1;

                                dbContext.Entry(scoreExtenstion).Property(x => x.Rank).IsModified = true;
                            }
                        }
                    }

                    if (leaderboardContext == LeaderboardContexts.General) {
                        Leaderboard lb = new Leaderboard() { Id = leaderboard.Id };
                        try {
                        dbContext.Leaderboards.Attach(lb);
                        } catch { }
                        lb.Plays = rankedScores.Count;

                        dbContext.Entry(lb).Property(x => x.Plays).IsModified = true;
                    }
                }

                try
                {
                    await dbContext.BulkSaveChangesAsync();
                } catch (Exception e)
                {
                    dbContext.RejectChanges();
                }
            }
        }
    }
}
