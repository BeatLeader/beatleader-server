using BeatLeader_Server.Migrations;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using J2N.Collections.Generic.Extensions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Reflection.Metadata;
using static Lucene.Net.Queries.Function.ValueSources.MultiFunction;

namespace BeatLeader_Server.Utils
{
    public class ClanRankingData
    {
        public int numberOfScores;
        public float weight;
        public int lastUpdateTime;
        public float Pp;
        public float totalAcc;
        public int totalRank;
        public int totalScore;
        public List<Score> Scores;

        public ClanRankingData(
            int numberOfScores,
            float weight,
            int lastUpdateTime,
            float Pp,
            float totalAcc,
            int totalRank,
            int totalScore,
            List<Score> Scores)
        {
            this.numberOfScores = numberOfScores;
            this.weight = weight;
            this.lastUpdateTime = lastUpdateTime;
            this.Pp = Pp;
            this.totalAcc = totalAcc;
            this.totalRank = totalRank;
            this.totalScore = totalScore;
            this.Scores = Scores;
        }
    };

    public class ClanRankingComparer : IComparer<Tuple<int, float>>
    {
        public int Compare(Tuple<int, float> f1, Tuple<int, float> f2)
        {
            int test = Comparer<float>.Default.Compare(f1.Item2, f2.Item2);
            return (-1)*test;
        }
    }

    public static class ClanUtils
    {
        public const float clanRankingWeight = 0.8f;

        public static float RecalculateClanPP(this AppContext context, int clanId)
        {
            Clan clan = context.Clans.Where(c => c.Id == clanId).Include(c => c.Players).FirstOrDefault();
            var ranked = clan.Players.OrderByDescending(s => s.Pp).ToList();
            float resultPP = 0f;
            foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
            {
                float weight = MathF.Pow(0.965f, i);
                resultPP += p.Pp * weight;
            }
            return resultPP;
        }
        public static void UpdateClanRanking(this AppContext context, Leaderboard leaderboard, Score currentScore, Score newScore)
        {
            if (leaderboard != null && 
               (newScore.Player.Clans != null) &&
               (newScore.Player.Clans.Count > 0) &&
               (leaderboard.Difficulty.Status is DifficultyStatus.ranked))
            {
                /*
                 * Any new score can have multiple clans associated with it
                 * Therefore, any new score that is posted where the player is in clans will have some kind of impact to the
                 * pp of each clan's clanRanking on that leaderboard.
                 * We need to compare the new score to all the 'associated scores' on each clan the player is in and recaculate the clanPP from that.
                 * With that new clanPP calculation, we can then recalculate clanRankings
                 * for each clan in the score:
                 * calculate the pp increase of the clan, take that new clanpp value, and update the clanRanking ranks
                 *
                 * Procure list of clanRankings not affected by this new score (The player who posted the score is not in the clan)
                 * Binary search and place all the Affected ClanRankings in the right spot in the list of nonAffectedClanRankings with their
                 * new pp values.
                 * Starting from the best index that was inserted, update all lower ranks to match the indexes
                 */
                var affectedClanRankings =
                    context
                    .ClanRanking
                    .Where(s => s.LeaderboardId == leaderboard.Id && newScore.Player.Clans.Contains(s.Clan))
                    .Include(s => s.AssociatedScores
                             .OrderByDescending(a => Math.Round(a.Pp, 2))
                             .ThenByDescending(a => Math.Round(a.Accuracy, 4))
                             .ThenBy(a => a.Timeset))
                    .ToList();

                if (affectedClanRankings.Count() != 0)
                {
                    var nonAffectedClanRankings =
                        context
                        .ClanRanking
                        .Where(s => s.LeaderboardId == leaderboard.Id && !newScore.Player.Clans.Contains(s.Clan))
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenByDescending(el => Math.Round(el.AverageAccuracy, 4))
                        .ThenByDescending(el => el.LastUpdateTime)
                        .Select(s => new { Id = s.Id, Pp = s.Pp, AverageAccuracy = s.AverageAccuracy })
                        .ToList();

                    var crList = nonAffectedClanRankings.Select(cr => new Tuple<int, float>(cr.Id, cr.Pp)).ToList();
                    var prevCRCaptorId = crList[0].Item1;

                    foreach (var clanRanking in affectedClanRankings)
                    {
                        var cr = context.ClanRanking.Local.FirstOrDefault(c => c.Id == clanRanking.Id);
                        if (cr == null)
                        {
                            cr = new ClanRanking() { Id = clanRanking.Id };
                            context.ClanRanking.Attach(cr);
                        }
                        // Remove current score from clanRanking
                        if (currentScore != null)
                        {
                            clanRanking.AverageAccuracy = MathUtils.RemoveFromAverage(clanRanking.AverageAccuracy, clanRanking.AssociatedScores.Count(), currentScore.Accuracy);
                            clanRanking.AverageRank = MathUtils.RemoveFromAverage(clanRanking.AverageRank, clanRanking.AssociatedScores.Count(), currentScore.Rank);
                            cr.TotalScore -= currentScore.ModifiedScore;
                            clanRanking.AssociatedScores.Remove(currentScore);
                            context.Entry(cr).Property(x => x.AverageAccuracy).IsModified = true;
                            context.Entry(cr).Property(x => x.AverageRank).IsModified = true;
                            context.Entry(cr).Property(x => x.TotalScore).IsModified = true;
                            //context.Entry(cr).Property(x => x.AssociatedScores).IsModified = true;
                        }

                        // Binary search to insert new score into the sorted list
                        List<float> ppValues = clanRanking.AssociatedScores.Select(a => a.Pp).ToList();
                        int index = ppValues.BinarySearch(newScore.Pp);
                        if (index >= 0 && index != ~ppValues.Count())
                        {
                            // Found a score with the exact same pp value, compare accuracies instead
                            if (newScore.Accuracy >= ppValues[index])
                            {
                                ppValues.Insert(index + 1, newScore.Pp);
                            }
                            else
                            {
                                ppValues.Insert(index, newScore.Pp);
                            }
                        }
                        else
                        {
                            // newScore has the highest Pp of all the existing scores
                            if (~index == ppValues.Count())
                            {
                                ppValues.Insert(0, newScore.Pp);
                            }
                            else
                            {
                                // newScore is a totally unique pp value
                                ppValues.Insert(~index, newScore.Pp);
                            }
                        }

                        // Update the Pp of the clanRanking
                        int weightPower = 1;
                        float calculatedPp = 0.0f;
                        foreach (var ppValue in ppValues)
                        {
                            calculatedPp += (ppValue * MathF.Pow(clanRankingWeight, weightPower));
                            weightPower++;
                        }
                        // Update clanRanking fields
                        cr.Pp = calculatedPp;
                        cr.LastUpdateTime = newScore.Timepost;
                        cr.AverageAccuracy = MathUtils.AddToAverage(clanRanking.AverageAccuracy, clanRanking.AssociatedScores.Count(), newScore.Accuracy);
                        cr.AverageRank = MathUtils.AddToAverage(clanRanking.AverageRank, clanRanking.AssociatedScores.Count(), newScore.Rank);
                        cr.AssociatedScores.Add(newScore);
                        context.Entry(cr).Property(x => x.Pp).IsModified = true;
                        context.Entry(cr).Property(x => x.LastUpdateTime).IsModified = true;

                        if (cr.Pp >= crList[0].Item2)
                        {
                            crList.Insert(0, new Tuple<int, float>(clanRanking.Id, clanRanking.Pp));
                        }
                        else if (newScore.Pp == 0.0f)
                        {
                            // Scores with zero pp mess up the binary search because there are duplicate zeros.
                            // Zeros contribute nothing for this pp calculation, so just throw it at the botom of the pile.
                            crList.Add(new Tuple<int, float>(clanRanking.Id, clanRanking.Pp));
                        } else
                        {
                            // Calculate new ClanRanking ranks with the new clan updated
                            index = crList.BinarySearch(new Tuple<int, float>(clanRanking.Id, clanRanking.Pp), new ClanRankingComparer());
                            if (index >= 0 && index != ~crList.Count())
                            {
                                // Found a score with the exact same pp value, should be fine, just add it
                                // either before or after for the purposes of the pp calculation
                                crList.Insert(index, new Tuple<int, float>(clanRanking.Id, clanRanking.Pp));
                            }
                            else
                            {
                                // newScore has the highest Pp of all the existing clanRankings
                                if (index == ~crList.Count())
                                {
                                    crList.Insert(0, new Tuple<int, float>(clanRanking.Id, clanRanking.Pp));
                                }
                                else
                                {
                                    // newScore is a totally unique pp value
                                    crList.Insert(~index, new Tuple<int, float>(clanRanking.Id, clanRanking.Pp));
                                }
                            }
                        }
                    }


                    // Captured leaderboard cases covered -------
                    // Null -> Null : Good
                    // Null -> Won : Good
                    // Null -> Tied : Good
                    // Won -> Null : Impossible (unranked ranked map?)
                    // Won -> Won same Clan : Good
                    // Won -> Won diff Clan : Good
                    // Won -> Tied : Good
                    // Tied -> Won : Good
                    // Tied -> Tied : Good
                    // Tied -> Null : Impossible
                    // ------------------------------------------
                    if (!crList.IsNullOrEmpty())
                    {
                        if (nonAffectedClanRankings.Count() != 0)
                        {
                            // Check if we're introducing a tie
                            if (crList.Count() > 1 && crList[0].Item2 == crList[1].Item2)
                            {
                                // If the map was contested before, and its still contested, don't do anything
                                if (leaderboard.ClanRankingContested == false)
                                {
                                    // Remove captured Leaderboard from previous owner because now leaderboard is tied.
                                    var prevCaptor = context.ClanRanking.Where(c => c.Id == prevCRCaptorId).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                    prevCaptor.Clan.CapturedLeaderboards.Remove(leaderboard);
                                    leaderboard.ClanRankingContested = true;
                                    context.Entry(leaderboard).Property(x => x.ClanRankingContested).IsModified = true;
                                }
                            }
                            else
                            {
                                // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                                if (crList.Count() > 1 && leaderboard.ClanRankingContested)
                                {
                                    // Add captured leaderboard to new owner
                                    var newCaptor = context.ClanRanking.Where(c => c.Id == crList[0].Item1).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                    newCaptor.Clan.CapturedLeaderboards.Add(leaderboard);
                                    newCaptor.Clan.RankedPoolPercentCaptured =
                                        RankingService.RankedMapCount != 0 ? (float)(newCaptor.Clan.CapturedLeaderboards?.Count() ?? 0) / RankingService.RankedMapCount : 0;
                                    leaderboard.ClanRankingContested = false;
                                    context.Entry(leaderboard).Property(x => x.ClanRankingContested).IsModified = true;
                                    context.Entry(newCaptor.Clan).Property(x => x.RankedPoolPercentCaptured).IsModified = true;
                                }
                                else
                                {
                                    // If the leaderboard was previously won, and now it is won by a different clan,
                                    // Remove board from old captor, add board to new captor
                                    if (prevCRCaptorId != crList[0].Item1)
                                    {
                                        // Remove captured Leaderboard from previous owner because now leaderboard is tied.
                                        var prevCaptor = context.ClanRanking.Where(c => c.Id == prevCRCaptorId).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                        prevCaptor.Clan.CapturedLeaderboards.Remove(leaderboard);
                                        // Add captured leaderboard to new owner
                                        var newCaptor = context.ClanRanking.Where(c => c.Id == crList[0].Item1).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                        newCaptor.Clan.CapturedLeaderboards.Add(leaderboard);
                                        newCaptor.Clan.RankedPoolPercentCaptured =
                                            RankingService.RankedMapCount != 0 ? (float)(newCaptor.Clan.CapturedLeaderboards?.Count() ?? 0) / RankingService.RankedMapCount : 0;
                                        context.Entry(newCaptor.Clan).Property(x => x.RankedPoolPercentCaptured).IsModified = true;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // If clanRanking was empty:
                            // Empty --> Tie : Set leaderboard as contested
                            if (crList.Count() > 1 && crList[0].Item2 == crList[1].Item2)
                            {
                                leaderboard.ClanRankingContested = true;
                                context.Entry(leaderboard).Property(x => x.ClanRankingContested).IsModified = true;
                            }
                            else
                            {
                                var newCaptor = context.ClanRanking.Where(c => c.Id == crList[0].Item1).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                newCaptor.Clan.CapturedLeaderboards.Add(leaderboard);
                            }
                        }
                    }
                }
            }
        }

        public static ICollection<ClanRanking>? CalculateClanRankingSlow(this AppContext context, Leaderboard? leaderboard)
        {
            // CalculateClanRankingSlow: Function that calculates the clanRanking given a leaderboard
            // This function is called on relevant leaderboards whenever a user sets a new score, a clan is created, a user leaves a clan,
            // a user joins a clan, a map is ranked, or a clan is deleted.
            if (leaderboard == null)
            {
                return null;
            }

            // Calculate clan captor on this leaderboard
            var newClanRankingData = new Dictionary<Clan, ClanRankingData>();
            var leaderboardClans =
                context
                    .Scores
                    .Include(s => s.Player)
                    .ThenInclude(p => p.Clans)
                    .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned && s.Player.Clans.Count != 0)
                    .OrderByDescending(el => el.Pp)
                    .ToList();

            // Build up a dictionary of the clans on this leaderboard based on scores
            foreach (Score score in leaderboardClans)
            {
                if (score.Player.Clans == null)
                {
                    continue;
                }
                foreach (Clan clan in score.Player.Clans)
                {
                    if (newClanRankingData.ContainsKey(clan))
                    {
                        // Update the data already in the newClanRankingData dictionary
                        newClanRankingData[clan].numberOfScores = newClanRankingData[clan].numberOfScores + 1;
                        newClanRankingData[clan].weight = MathF.Pow(0.8f, newClanRankingData[clan].weight);
                        newClanRankingData[clan].lastUpdateTime = Math.Max(score.Timepost, newClanRankingData[clan].lastUpdateTime);
                        newClanRankingData[clan].Pp = newClanRankingData[clan].Pp + (score.Pp * newClanRankingData[clan].weight);
                        newClanRankingData[clan].totalAcc = newClanRankingData[clan].totalAcc + score.Accuracy;
                        newClanRankingData[clan].totalRank = newClanRankingData[clan].totalRank + score.Rank;
                        newClanRankingData[clan].totalScore = newClanRankingData[clan].totalScore + score.ModifiedScore;
                        newClanRankingData[clan].Scores.Add(score);
                    }
                    else
                    {
                        newClanRankingData.Add(clan, new ClanRankingData(
                            1,
                            1.0f,
                            score.Timepost,
                            score.Pp,
                            score.Accuracy,
                            score.Rank,
                            score.ModifiedScore,
                            new List<Score> { score }
                            ));
                    }
                }
            }

            // Captured leaderboard cases covered -------
            // Null -> Null : Good
            // Null -> Won : Good
            // Null -> Tied : Good
            // Won -> Null : Impossible (unranked ranked map?)
            // Won -> Won same Clan : Good
            // Won -> Won diff Clan : Good
            // Won -> Tied : Good
            // Tied -> Won : Good
            // Tied -> Tied : Good
            // Tied -> Null : Impossible
            // ------------------------------------------
            if (!newClanRankingData.IsNullOrEmpty())
            {
                // Sort newClanRankingData and clanRanking by PP
                newClanRankingData = newClanRankingData.OrderByDescending(x => x.Value.Pp).ToDictionary(x => x.Key, x => x.Value);
                leaderboard.ClanRanking ??= new List<ClanRanking>();
                var clanRanking = leaderboard.ClanRanking;

                if (clanRanking.Count != 0)
                {
                    clanRanking = clanRanking.OrderByDescending(cr => cr.Pp).ToList();
                    var prevCaptor = clanRanking.First().Clan;

                    // If we are introducing a tie, remove captor from the clanRanking, if there is one
                    if (newClanRankingData.Count > 1 && newClanRankingData.ElementAt(0).Value.Pp == newClanRankingData.ElementAt(1).Value.Pp)
                    {
                        // The reason this has to be in a loop is because of n-way ties.
                        // If an n-way tie existed in the clanRanking, the previous captor could be anywhere in the clanRanking list.
                        foreach (var clan in clanRanking)
                        {
                            RemoveCapturedLeaderboard(ref prevCaptor, leaderboard);
                        }
                        leaderboard.ClanRankingContested = true;
                    }
                    else
                    {
                        // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                        if (clanRanking.Count > 1 && leaderboard.ClanRankingContested)
                        {
                            AddCapturedLeaderboard(ref newClanRankingData, leaderboard, context);
                            leaderboard.ClanRankingContested = false;
                        }
                        else
                        {
                            // If the leaderboard was previously won, and now it is won by a different clan,
                            // Remove board from old captor, add board to new captor
                            if (prevCaptor != newClanRankingData.First().Key)
                            {
                                RemoveCapturedLeaderboard(ref prevCaptor, leaderboard);
                                AddCapturedLeaderboard(ref newClanRankingData, leaderboard, context);
                            }
                        }
                    }
                }
                else
                {
                    // If clanRanking was empty:
                    // Empty --> Tie : Set leaderboard as contested
                    if (newClanRankingData.Count > 1 && newClanRankingData.ElementAt(0).Value.Pp == newClanRankingData.ElementAt(1).Value.Pp)
                    {
                        leaderboard.ClanRankingContested = true;
                    }
                    else
                    {
                        // Empty --> Won : Add captured leaderboard
                        AddCapturedLeaderboard(ref newClanRankingData, leaderboard, context);
                    }
                }

                // Recalculate pp on clans
                // TODO : Maybe have function that only needs to do these operations for the clans affected by a new score set?
                foreach (var clan in newClanRankingData.Select((data, index) => new { index, data }))
                {
                    var updateClan = clanRanking.Where(cr => cr.Clan == clan.data.Key).FirstOrDefault();
                    if (updateClan != null)
                    {
                        updateClan.LastUpdateTime = clan.data.Value.lastUpdateTime;
                        updateClan.Pp = clan.data.Value.Pp;
                        updateClan.AverageRank = clan.data.Value.totalRank / clan.data.Value.numberOfScores;
                        updateClan.AverageAccuracy = clan.data.Value.totalAcc / clan.data.Value.numberOfScores;
                        updateClan.TotalScore = clan.data.Value.totalScore;
                        updateClan.AssociatedScores = clan.data.Value.Scores;
                    }
                    else
                    {
                        clanRanking.Add(new ClanRanking
                        {
                            Clan = clan.data.Key,
                            LastUpdateTime = clan.data.Value.lastUpdateTime,
                            Pp = clan.data.Value.Pp,
                            AverageRank = clan.data.Value.totalRank / clan.data.Value.numberOfScores,
                            AverageAccuracy = clan.data.Value.totalAcc / clan.data.Value.numberOfScores,
                            TotalScore = clan.data.Value.totalScore,
                            LeaderboardId = leaderboard.Id,
                            Leaderboard = leaderboard,
                            AssociatedScores = clan.data.Value.Scores
                        });
                    }
                }
            }

            return leaderboard.ClanRanking;
        }

        private static void AddCapturedLeaderboard(
            ref Dictionary<Clan, ClanRankingData> newClanRankingData,
            Leaderboard leaderboard,
            AppContext context)
        {
            // Add leaderboard to new captor
            if (newClanRankingData.First().Key.CapturedLeaderboards == null)
            {
                newClanRankingData.First().Key.CapturedLeaderboards = new List<Leaderboard>
                {
                    leaderboard
                };
            }
            else
            {
                // Check to make sure the clan hasn't already captured this map
                if (!newClanRankingData.First().Key.CapturedLeaderboards.Contains(leaderboard))
                {
                    newClanRankingData.First().Key.CapturedLeaderboards.Add(leaderboard);
                }
            }

            // Determine what % of the map pool this clan now owns
            if (RankingService.RankedMapCount != 0) 
            {
                newClanRankingData.First().Key.RankedPoolPercentCaptured = (float)(newClanRankingData.First().Key.CapturedLeaderboards?.Count ?? 0) / RankingService.RankedMapCount;
            }
        }

        private static void RemoveCapturedLeaderboard(ref Clan prevCaptor, Leaderboard leaderboard)
        {
            // Remove leaderboard from old captor
            if (prevCaptor.CapturedLeaderboards != null && prevCaptor.CapturedLeaderboards.Contains(leaderboard))
            {
                prevCaptor.CapturedLeaderboards.Remove(leaderboard);
            }
        }
    }
}
