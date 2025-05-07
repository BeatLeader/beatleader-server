using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {

    public class PlayerContextRefreshControllerHelper {
        private class ScoreSelection {
            public int Id { get; set; } 
            public float Accuracy { get; set; } 
            public int Rank { get; set; } 
            public float Pp { get; set; } 
            public float AccPP { get; set; }  
            public float TechPP { get; set; } 
            public float PassPP { get; set; } 
            public float Weight { get; set; } 
            public string PlayerId { get; set; }
        }

        private static async Task CalculateBatch(
            AppContext dbContext,
            List<IGrouping<string, ScoreSelection>> groups, 
            Dictionary<string, int> playerMap,
            Dictionary<int, float> weights)
        {
            var playerUpdates = new List<PlayerContextExtension>();
            var scoreUpates = new List<ScoreContextExtension>();

            foreach (var group in groups)
            {
                if (playerMap.ContainsKey(group.Key)) {
                    var player = new PlayerContextExtension { Id = playerMap[group.Key] };

                    float resultPP = 0f;
                    float accPP = 0f;
                    float techPP = 0f;
                    float passPP = 0f;

                    foreach ((int i, var s) in group.OrderByDescending(s => s.Pp).Select((value, i) => (i, value)))
                    {
                        float weight = weights[i];
                        if (s.Weight != weight)
                        {
                            scoreUpates.Add(new ScoreContextExtension() { Id = s.Id, Weight = weight });
                        }
                        resultPP += s.Pp * weight;
                        accPP += s.AccPP * weight;
                        techPP += s.TechPP * weight;
                        passPP += s.PassPP * weight;
                    }
                    player.Pp = resultPP;
                    player.AccPp = accPP;
                    player.TechPp = techPP;
                    player.PassPp = passPP;
                    playerUpdates.Add(player);
                }
            }
                
            await dbContext.BulkUpdateAsync(playerUpdates, options => options.ColumnInputExpression = c => new { c.Pp, c.AccPp, c.TechPp, c.PassPp });
            await dbContext.BulkUpdateAsync(scoreUpates, options => options.ColumnInputExpression = c => new { c.Weight });
        }

        public static async Task RefreshPlayersContext(
            AppContext dbContext, 
            LeaderboardContexts context,
            List<string>? playerIds = null)
        {
            var weights = new Dictionary<int, float>();
            for (int i = 0; i < 10000; i++)
            {
                weights[i] = MathF.Pow(0.965f, i);
            }

            var scores = await dbContext
                .ScoreContextExtensions
                .Where(s => 
                    s.Pp != 0 && 
                    s.Context == context && 
                    s.ScoreId != null && 
                    !s.Banned && 
                    !s.Qualification &&
                    (playerIds == null || playerIds.Contains(s.PlayerId)))
                .Select(s => new ScoreSelection { 
                    Id = s.Id, 
                    Accuracy = s.Accuracy, 
                    Rank = s.Rank, 
                    Pp = s.Pp, 
                    AccPP = s.AccPP, 
                    TechPP = s.TechPP, 
                    PassPP = s.PassPP, 
                    Weight = s.Weight, 
                    PlayerId = s.PlayerId
                })
                .AsNoTracking()
                .ToListAsync();

            var playerMap = dbContext
                .PlayerContextExtensions
                .Where(ce => ce.Context == context &&
                    (playerIds == null || playerIds.Contains(ce.PlayerId)))
                .AsNoTracking()
                .ToDictionary(ce => ce.PlayerId, ce => ce.Id);

            var scoreGroups = scores.GroupBy(s => s.PlayerId).ToList();
            for (int i = 0; i < scoreGroups.Count; i += 5000)
            {
                await CalculateBatch(dbContext, scoreGroups.Skip(i).Take(5000).ToList(), playerMap, weights);
            }

            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await dbContext.PlayerContextExtensions
                .Where(ce => ce.Context == context && !ce.Banned && ce.Pp > 0)
                .AsNoTracking()
                .OrderByDescending(t => t.Pp)
                .Select(ce => new { ce.Id, ce.Country })
                .ToListAsync();

            var updates = new List<PlayerContextExtension>();
            foreach ((int i, var p) in ranked.Select((value, i) => (i, value)))
            {
                var ce = new PlayerContextExtension { Id = p.Id };
                ce.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                ce.CountryRank = countries[p.Country];
                countries[p.Country]++;

                updates.Add(ce);
            }

            await dbContext.BulkUpdateAsync(updates, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });
            await dbContext.BulkSaveChangesAsync();
        }
    }
}
