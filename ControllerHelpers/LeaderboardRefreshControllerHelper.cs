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
                .AsNoTracking()
                .OrderBy(lb => lb.Id)
                .Skip(i)
                .Take(1000)
                .Select(lb => new
                {
                    lb.Id,
                    lb.Difficulty.Status,
                    Scores = 
                        (IEnumerable<IScore>)(leaderboardContext == LeaderboardContexts.General 
                        ? lb.Scores
                            .Where(s => !s.Banned && s.ValidContexts.HasFlag(leaderboardContext))
                            .Select(s => new Score { Id = s.Id, Pp = s.Pp, Accuracy = s.Accuracy, ModifiedScore = s.ModifiedScore, Timepost = s.Timepost, Priority = s.Priority })
                        : lb.ContextExtensions
                            .Where(s => !s.Banned && s.Context == leaderboardContext)
                            .Select(s => new ScoreContextExtension { Id = s.Id, Pp = s.Pp, Accuracy = s.Accuracy, ModifiedScore = s.ModifiedScore, Timepost = s.Timepost, Priority = s.Priority }))
                })
                .ToArrayAsync();

                var leaderboardsToUpdate = new List<Leaderboard>();

                foreach (var leaderboard in leaderboards)
                {
                    var status = leaderboard.Status;

                    var rankedScores = status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent
                        ? leaderboard
                            .Scores
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timepost)
                            .ToList()
                        : leaderboard
                            .Scores
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timepost)
                            .ToList();
                    if (rankedScores.Count > 0)
                    {
                        foreach ((int ii, var s) in rankedScores.Select((value, ii) => (ii, value)))
                        {
                            s.Rank = ii + 1;
                        }
                    }

                    if (leaderboardContext == LeaderboardContexts.General) {
                        leaderboardsToUpdate.Add(new Leaderboard() { Id = leaderboard.Id, Plays = rankedScores.Count });
                    }
                }

                await dbContext.BulkUpdateAsync(leaderboardsToUpdate, options => options.ColumnInputExpression = c => new { c.Plays });
                await dbContext.BulkUpdateAsync(leaderboards.SelectMany(l => l.Scores), options => options.ColumnInputExpression = c => new { c.Rank });
            }
        }
    }
}
