using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public struct SubScore
    {
        public string PlayerId;
        public string Platform;
        public HMD Hmd;
        public int ModifiedScore ;
        public float Accuracy;
        public float Pp;
        public float BonusPp;
        public float PassPP;
        public float AccPP;
        public float TechPP;
        public int Rank;
        public int Timeset;
        public float Weight;
        public bool Qualification;
        public int? MaxStreak;
        public float LeftTiming { get; set; }
        public float RightTiming { get; set; }
    }

    public class PlayerRefreshControllerHelper {
        public static async Task RefreshStats(
            AppContext dbContext,
            PlayerScoreStats scoreStats, 
            string playerId, 
            int rank, 
            float? percentile,
            float? countryPercentile,
            LeaderboardContexts leaderboardContext,
            List<SubScore>? scores = null)
        {
            var allScores = scores;

            if (scores == null) {
                if (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None) {
                    allScores = await dbContext
                        .Scores
                        .AsNoTracking()
                        .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && (!s.Banned || s.Bot) && s.PlayerId == playerId && !s.IgnoreForStats)
                        .Select(s => new SubScore
                        {
                            PlayerId = s.PlayerId,
                            Platform = s.Platform,
                            Hmd = s.Hmd,
                            ModifiedScore = s.ModifiedScore,
                            Accuracy = s.Accuracy,
                            Pp = s.Pp,
                            BonusPp = s.BonusPp,
                            PassPP = s.PassPP,
                            AccPP = s.AccPP,
                            TechPP = s.TechPP,
                            Rank = s.Rank,
                            Timeset = s.Timepost,
                            Weight = s.Weight,
                            Qualification = s.Qualification,
                            MaxStreak = s.MaxStreak,
                            RightTiming = s.RightTiming,
                            LeftTiming = s.LeftTiming,
                        }).ToListAsync();
                } else {
                    allScores = await dbContext
                        .ScoreContextExtensions
                        .AsNoTracking()
                        .Where(s => s.PlayerId == playerId && s.Context == leaderboardContext && !s.ScoreInstance.IgnoreForStats)
                        .Select(s => new SubScore
                        {
                            PlayerId = s.PlayerId,
                            Platform = s.ScoreInstance.Platform,
                            Hmd = s.ScoreInstance.Hmd,
                            ModifiedScore = s.ModifiedScore,
                            Accuracy = s.Accuracy,
                            Pp = s.Pp,
                            BonusPp = s.BonusPp,
                            PassPP = s.PassPP,
                            AccPP = s.AccPP,
                            TechPP = s.TechPP,
                            Rank = s.Rank,
                            Timeset = s.ScoreInstance.Timepost,
                            Weight = s.Weight,
                            Qualification = s.Qualification,
                            MaxStreak = s.ScoreInstance.MaxStreak,
                            RightTiming = s.ScoreInstance.RightTiming,
                            LeftTiming = s.ScoreInstance.LeftTiming,
                        }).ToListAsync();
                }
            }

            List<SubScore> rankedScores = new();
            List<SubScore> unrankedScores = new();

            if (allScores.Count() > 0) {

                rankedScores = allScores.Where(s => s.Pp != 0 && !s.Qualification).ToList();
                unrankedScores = allScores.Where(s => s.Pp == 0 || s.Qualification).ToList();

                var lastScores = allScores.OrderByDescending(s => s.Timeset).Take(50).ToList();
                Dictionary<string, int> platforms = new Dictionary<string, int>();
                Dictionary<HMD, int> hmds = new Dictionary<HMD, int>();
                foreach (var s in lastScores)
                {
                    string? platform = s.Platform.Split(",").FirstOrDefault();
                    if (platform != null) {
                        if (!platforms.ContainsKey(platform))
                        {
                            platforms[platform] = 1;
                        }
                        else
                        {

                            platforms[platform]++;
                        }
                    }

                    if (!hmds.ContainsKey(s.Hmd))
                    {
                        hmds[s.Hmd] = 1;
                    }
                    else
                    {

                        hmds[s.Hmd]++;
                    }
                }

                scoreStats.TopPlatform = platforms.Count > 0 ? platforms.MaxBy(s => s.Value).Key : "";
                scoreStats.TopHMD = hmds.Count > 0 ? hmds.MaxBy(s => s.Value).Key : HMD.unknown;

                var hmdKeys = allScores
                    .GroupBy(s => s.Hmd)
                    .Select(g => new { 
                        Key = (int)g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Count)
                    .Select(g => g.Key)
                    .ToList();
                
                scoreStats.AllHMDs = string.Join(",", hmdKeys);
                while (scoreStats.AllHMDs.Length > 50) {
                    hmdKeys = hmdKeys.Take(hmdKeys.Count - 1).ToList();
                    scoreStats.AllHMDs = string.Join(",", hmdKeys);
                }

                if (rank < scoreStats.PeakRank || scoreStats.PeakRank == 0) {
                    scoreStats.PeakRank = rank;
                }
            }

            int allScoresCount = allScores.Count();
            int unrankedScoresCount = unrankedScores.Count();
            int rankedScoresCount = rankedScores.Count();

            scoreStats.TotalPlayCount = allScoresCount;
            scoreStats.UnrankedPlayCount = unrankedScoresCount;
            scoreStats.RankedPlayCount = rankedScoresCount;

            if (scoreStats.TotalPlayCount > 0)
            {
                int middle = (int)MathF.Round(allScoresCount / 2f);
                scoreStats.TotalScore = allScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                scoreStats.TopAccuracy = allScores.Max(s => s.Accuracy);
                if (allScoresCount % 2 == 1) {
                    scoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).ElementAt(middle).Accuracy;
                } else {
                    scoreStats.MedianAccuracy = allScores.OrderByDescending(s => s.Accuracy).Skip(middle - 1).Take(2).Average(s => s.Accuracy);
                }
                scoreStats.AverageRank = allScores.Average(s => (float)s.Rank);
                scoreStats.LastScoreTime = allScores.MaxBy(s => s.Timeset).Timeset;

                scoreStats.MaxStreak = allScores.Max(s => s.MaxStreak) ?? 0;
                scoreStats.AverageLeftTiming = allScores.Average(s => s.LeftTiming);
                scoreStats.AverageRightTiming = allScores.Average(s => s.RightTiming);
                scoreStats.Top1Count = allScores.Where(s => s.Rank == 1).Count();
                scoreStats.Top1Score = allScores.Select(s => ReplayUtils.ScoreForRank(s.Rank)).Sum();
            } else {
                scoreStats.TotalScore = 0;
                scoreStats.AverageAccuracy = 0;
                scoreStats.TopAccuracy = 0;
                scoreStats.MedianAccuracy = 0;
                scoreStats.AverageRank = 0;
                scoreStats.LastScoreTime = 0;
            }

            if (scoreStats.UnrankedPlayCount > 0)
            {
                scoreStats.TotalUnrankedScore = unrankedScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageUnrankedAccuracy = unrankedScores.Average(s => s.Accuracy);
                scoreStats.TopUnrankedAccuracy = unrankedScores.Max(s => s.Accuracy);
                scoreStats.AverageUnrankedRank = unrankedScores.Average(s => (float)s.Rank);
                scoreStats.LastUnrankedScoreTime = unrankedScores.MaxBy(s => s.Timeset).Timeset;
                scoreStats.UnrankedMaxStreak = unrankedScores.Max(s => s.MaxStreak) ?? 0;
                scoreStats.UnrankedTop1Count = unrankedScores.Where(s => s.Rank == 1).Count();
                scoreStats.UnrankedTop1Score = unrankedScores.Select(s => ReplayUtils.ScoreForRank(s.Rank)).Sum();
            } else {
                scoreStats.TotalUnrankedScore = 0;
                scoreStats.AverageUnrankedAccuracy = 0;
                scoreStats.TopUnrankedAccuracy = 0;
                scoreStats.AverageUnrankedRank = 0;
                scoreStats.LastUnrankedScoreTime = 0;
            }

            if (scoreStats.RankedPlayCount > 0)
            {
                int middle = (int)MathF.Round(rankedScoresCount / 2f);
                scoreStats.TotalRankedScore = rankedScores.Sum(s => (long)s.ModifiedScore);
                scoreStats.AverageRankedAccuracy = rankedScores.Average(s => s.Accuracy);


                var scoresForWeightedAcc = rankedScores.OrderByDescending(s => s.Accuracy).Take(100).ToList();
                var sum = 0.0f;
                var weights = 0.0f;

                for (int i = 0; i < 100; i++)
                {
                    float weight = MathF.Pow(0.95f, i);
                    if (i < scoresForWeightedAcc.Count) {
                        sum += scoresForWeightedAcc[i].Accuracy * weight;
                    }

                    weights += weight;
                }

                scoreStats.AverageWeightedRankedAccuracy = sum / weights;
                var scoresForWeightedRank = rankedScores.OrderBy(s => s.Rank).Take(100).ToList();
                sum = 0.0f;
                weights = 0.0f;

                for (int i = 0; i < 100; i++)
                {
                    float weight = MathF.Pow(1.05f, i);
                    if (i < scoresForWeightedRank.Count)
                    {
                        sum += scoresForWeightedRank[i].Rank * weight;
                    } else {
                        sum += i * 10 * weight;
                    }

                    weights += weight;
                }
                scoreStats.AverageWeightedRankedRank = sum / weights;

                if (rankedScoresCount % 2 == 1) {
                    scoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).ElementAt(middle).Accuracy;
                } else {
                    scoreStats.MedianRankedAccuracy = rankedScores.OrderByDescending(s => s.Accuracy).Skip(middle - 1).Take(2).Average(s => s.Accuracy);
                }

                scoreStats.TopRankedAccuracy = rankedScores.Max(s => s.Accuracy);
                scoreStats.TopPp = rankedScores.Max(s => s.Pp);
                scoreStats.TopBonusPP = rankedScores.Max(s => s.BonusPp);
                scoreStats.TopPassPP = rankedScores.Max(s => s.PassPP);
                scoreStats.TopAccPP = rankedScores.Max(s => s.AccPP);
                scoreStats.TopTechPP = rankedScores.Max(s => s.TechPP);
                scoreStats.AverageRankedRank = rankedScores.Average(s => (float)s.Rank);
                scoreStats.LastRankedScoreTime = rankedScores.MaxBy(s => s.Timeset).Timeset;
                scoreStats.RankedMaxStreak = rankedScores.Max(s => s.MaxStreak) ?? 0;
                scoreStats.RankedTop1Count = rankedScores.Where(s => s.Rank == 1).Count();
                scoreStats.RankedTop1Score = rankedScores.Select(s => ReplayUtils.ScoreForRank(s.Rank)).Sum();

                scoreStats.SSPPlays = rankedScores.Count(s => s.Accuracy > 0.95);
                scoreStats.SSPlays = rankedScores.Count(s => 0.9 < s.Accuracy && s.Accuracy < 0.95);
                scoreStats.SPPlays = rankedScores.Count(s => 0.85 < s.Accuracy && s.Accuracy < 0.9);
                scoreStats.SPlays = rankedScores.Count(s => 0.8 < s.Accuracy && s.Accuracy < 0.85);
                scoreStats.APlays = rankedScores.Count(s => s.Accuracy < 0.8);
            } else {
                scoreStats.TotalRankedScore = 0;
                scoreStats.AverageRankedAccuracy = 0;
                scoreStats.AverageWeightedRankedAccuracy = 0;
                scoreStats.AverageWeightedRankedRank = 0;
                scoreStats.MedianRankedAccuracy = 0;
                scoreStats.TopRankedAccuracy = 0;
                scoreStats.TopPp = 0;
                scoreStats.TopBonusPP = 0;
                scoreStats.TopPassPP = 0;
                scoreStats.TopAccPP = 0;
                scoreStats.TopTechPP = 0;
                scoreStats.AverageRankedRank = 0;
                scoreStats.LastRankedScoreTime = 0;
                scoreStats.RankedTop1Count = 0;
                scoreStats.RankedTop1Score = 0;
                
                scoreStats.SSPPlays = 0;
                scoreStats.SSPlays = 0;
                scoreStats.SPPlays = 0;
                scoreStats.SPlays = 0;
                scoreStats.APlays = 0;
            }

            if (percentile != null) {
                scoreStats.TopPercentile = (float)percentile;
            }
            if (countryPercentile != null) {
                scoreStats.CountryTopPercentile = (float)countryPercentile;
            }
        }

        public static async Task RefreshAllContextsPp(
            AppContext dbContext) {
            var players = await dbContext.Players.Select(p => new Player { AllContextsPp = p.Pp + p.ContextExtensions.Where(ce => ce.Context != LeaderboardContexts.Funny).Sum(ce => ce.Pp), Id = p.Id }).ToListAsync();
            await dbContext.BulkUpdateAsync(players, options => options.ColumnInputExpression = c => new { c.AllContextsPp });
        }

        public static async Task RefreshRanks(AppContext dbContext) {
            Dictionary<string, int> countries = new Dictionary<string, int>();
            var ranked = await dbContext.Players
                .Where(p => p.Pp > 0 && !p.Banned)
                .AsNoTracking()
                .OrderByDescending(t => t.Pp)
                .Select(p => new Player { Id = p.Id, Country = p.Country })
                .ToListAsync();

            foreach ((int i, var p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;
            }
            await dbContext.BulkUpdateAsync(ranked, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });
            await dbContext.BulkSaveChangesAsync();
        }
    }
}
