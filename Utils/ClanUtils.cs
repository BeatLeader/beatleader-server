using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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

        public ClanRankingData(
            int numberOfScores,
            float weight,
            int lastUpdateTime,
            float Pp,
            float totalAcc,
            int totalRank,
            int totalScore)
        {
            this.numberOfScores = numberOfScores;
            this.weight = weight;
            this.lastUpdateTime = lastUpdateTime;
            this.Pp = Pp;
            this.totalAcc = totalAcc;
            this.totalRank = totalRank;
            this.totalScore = totalScore;
        }
    };

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
                 * Therefore, any new score that is posted where the player is in clans will have some kind of change to the
                 * pp of each clan's clanRanking on that leaderboard. (because a leaderboard has a list of clanRankings, and each clanRanking maps to a single clan)
                 * We need to compare the new score to all the 'associated scores' on each clanRanking the player is affecting by posting this new score
                 * and recaculate the weighted clanRanking pp from that list.
                 *
                 * Procure list of clanRankings not affected by this new score (The player who posted the score is not in the clan)
                 * Binary search and place all the Affected ClanRankings in the right spot in the list of nonAffectedClanRankings with their
                 * new pp values.
                 * 
                 * Update the map status and captured leaderboard status for whatever clan owns the leaderboard now
                 */

                if (newScore.Player.Clans.Count() != 0)
                {
                    if (leaderboard.ClanRanking == null)
                    {
                        leaderboard.ClanRanking = new List<ClanRanking>();
                    }

                    var topClanRanking =
                        context
                        .ClanRanking
                        .Where(cr => cr.LeaderboardId == leaderboard.Id && !newScore.Player.Clans.Contains(cr.Clan))
                        .OrderByDescending(cr => Math.Round(cr.Pp, 2))
                        .ThenByDescending(cr => Math.Round(cr.AverageAccuracy, 4))
                        .ThenByDescending(cr => cr.LastUpdateTime)
                        .Select(cr => new Tuple<int, float>(cr.Id, cr.Pp))
                        .FirstOrDefault();

                    var newCRCaptorId = topClanRanking?.Item1 ?? null;
                    var newCRCaptorPp = topClanRanking?.Item2 ?? 0;

                    foreach (var clan in newScore.Player.Clans)
                    {
                        var clanRanking = context
                            .ClanRanking
                            .Where(cr => cr.LeaderboardId == leaderboard.Id && cr.Clan.Id == clan.Id)
                            .FirstOrDefault();

                        var associatedScores = context
                            .Scores
                            .Where(s => s.LeaderboardId == leaderboard.Id && s.Player.Clans.Contains(clan))
                            .Include(s => s.Player)
                            .ThenInclude(s => s.Clans)
                            .OrderBy(a => Math.Round(a.Pp, 2))
                            .ThenBy(a => Math.Round(a.Accuracy, 4))
                            .ThenBy(a => a.Timeset)
                            .ToList();

                        if (clanRanking == null)
                        {
                            clanRanking = new ClanRanking
                            {
                                Clan = clan,
                                LastUpdateTime = 0,
                                Pp = 0,
                                AverageRank = 0,
                                AverageAccuracy = 0,
                                TotalScore = 0,
                                LeaderboardId = leaderboard.Id,
                                Leaderboard = leaderboard,
                            };
                            leaderboard.ClanRanking.Add(clanRanking);
                        }

                        // Remove current score from clanRanking if the currentScore was in the clanRanking
                        // so we can later update the clanRanking with the data from newScore
                        if (currentScore != null)
                        {
                            clanRanking.AverageAccuracy = MathUtils.RemoveFromAverage(clanRanking.AverageAccuracy, associatedScores.Count(), currentScore.Accuracy);
                            clanRanking.AverageRank = MathUtils.RemoveFromAverage(clanRanking.AverageRank, associatedScores.Count(), currentScore.Rank);
                            clanRanking.TotalScore -= currentScore.ModifiedScore;
                        }

                        // Update the Pp of the clanRanking
                        int weightPower = 0;
                        float calculatedPp = 0.0f;
                        foreach(var score in associatedScores)
                        {
                            calculatedPp += (score.Pp * MathF.Pow(clanRankingWeight, weightPower));
                            weightPower++;
                        }
                        // Update clanRanking fields
                        clanRanking.Pp = calculatedPp;
                        clanRanking.LastUpdateTime = newScore.Timepost;
                        clanRanking.AverageRank = MathUtils.AddToAverage(clanRanking.AverageRank, associatedScores.Count(), newScore.Rank);
                        clanRanking.AverageAccuracy = MathUtils.AddToAverage(clanRanking.AverageAccuracy, associatedScores.Count(), newScore.Accuracy);
                        clanRanking.TotalScore += newScore.ModifiedScore;

                        if (topClanRanking == null || clanRanking.Pp >= newCRCaptorPp)
                        {
                            newCRCaptorId = clanRanking.Id;
                            newCRCaptorPp = clanRanking.Pp;
                        }
                    }


                    // Captured leaderboard cases covered -------
                    // Null -> Null : Impossible
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
                    if (topClanRanking != null)
                    {
                        // Check if we're introducing a tie
                        if (topClanRanking.Item1 != newCRCaptorId && topClanRanking.Item2 == newCRCaptorPp)
                        {
                            // If the map was contested before, and its still contested, don't do anything
                            if (leaderboard.ClanRankingContested == false)
                            {
                                // Remove captured Leaderboard from previous owner because now leaderboard is tied.
                                var prevCaptor = context.ClanRanking.Where(c => c.Id == topClanRanking.Item1).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                Clan captor = prevCaptor.Clan;
                                RemoveCapturedLeaderboard(ref captor, leaderboard);
                                leaderboard.ClanRankingContested = true;
                            }
                        }
                        else
                        {
                            // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                            if (leaderboard.ClanRankingContested)
                            {
                                // Add captured leaderboard to new owner
                                var newCaptor = context.ClanRanking.Where(c => c.Id == newCRCaptorId).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                Clan captor = newCaptor.Clan;
                                AddCapturedLeaderboard(ref captor, leaderboard);
                                leaderboard.ClanRankingContested = false;
                            }
                            else
                            {
                                // If the leaderboard was previously won, and now it is won by a different clan,
                                // Remove board from old captor, add board to new captor
                                if (topClanRanking.Item1 != newCRCaptorId)
                                {
                                    // Remove captured Leaderboard from previous owner.
                                    var prevCaptor = context.ClanRanking.Where(c => c.Id == topClanRanking.Item1).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                    Clan captor = prevCaptor.Clan;
                                    RemoveCapturedLeaderboard(ref captor, leaderboard);

                                    // Add captured leaderboard to new owner
                                    var newCaptor = context.ClanRanking.Where(c => c.Id == newCRCaptorId).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                                    captor = newCaptor.Clan;
                                    AddCapturedLeaderboard(ref captor, leaderboard);
                                }
                            }
                        }
                    }
                    else
                    {
                        // If clanRanking was empty:
                        if (newScore.Player.Clans.Count() > 1)
                        {
                            // Empty --> Tie : Set leaderboard as contested; If the clanRankings were empty,
                            // then any new singular score with more than 1 clan will be contested immediately.
                            leaderboard.ClanRankingContested = true;
                        }
                        else
                        {
                            var newCaptor = context.ClanRanking.Where(c => c.Clan.Id == newScore.Player.Clans.First().Id).Include(cl => cl.Clan).ThenInclude(cl => cl.CapturedLeaderboards).FirstOrDefault();
                            Clan captor = newCaptor.Clan;
                            AddCapturedLeaderboard(ref captor, leaderboard);
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
                            score.ModifiedScore
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
                            Clan newCaptor = newClanRankingData.Keys.First();
                            AddCapturedLeaderboard(ref newCaptor, leaderboard);
                            leaderboard.ClanRankingContested = false;
                        }
                        else
                        {
                            // If the leaderboard was previously won, and now it is won by a different clan,
                            // Remove board from old captor, add board to new captor
                            if (prevCaptor != newClanRankingData.First().Key)
                            {
                                RemoveCapturedLeaderboard(ref prevCaptor, leaderboard);
                                Clan newCaptor = newClanRankingData.Keys.First();
                                AddCapturedLeaderboard(ref newCaptor, leaderboard);
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
                        Clan newCaptor = newClanRankingData.Keys.First();
                        AddCapturedLeaderboard(ref newCaptor, leaderboard);
                    }
                }

                // Recalculate pp on clans
                foreach (var clan in newClanRankingData)
                {
                    var updateClan = clanRanking.Where(cr => cr.Clan == clan.Key).FirstOrDefault();
                    if (updateClan != null)
                    {
                        updateClan.LastUpdateTime = clan.Value.lastUpdateTime;
                        updateClan.Pp = clan.Value.Pp;
                        updateClan.AverageRank = clan.Value.totalRank / clan.Value.numberOfScores;
                        updateClan.AverageAccuracy = clan.Value.totalAcc / clan.Value.numberOfScores;
                        updateClan.TotalScore = clan.Value.totalScore;
                    }
                    else
                    {
                        clanRanking.Add(new ClanRanking
                        {
                            Clan = clan.Key,
                            LastUpdateTime = clan.Value.lastUpdateTime,
                            Pp = clan.Value.Pp,
                            AverageRank = clan.Value.totalRank / clan.Value.numberOfScores,
                            AverageAccuracy = clan.Value.totalAcc / clan.Value.numberOfScores,
                            TotalScore = clan.Value.totalScore,
                            LeaderboardId = leaderboard.Id,
                            Leaderboard = leaderboard,
                        });
                    }
                }
            }

            return leaderboard.ClanRanking;
        }

        private static void AddCapturedLeaderboard(ref Clan newCaptor, Leaderboard leaderboard)
        {
            if (newCaptor != null)
            {
                // Add leaderboard to new captor
                if (newCaptor.CapturedLeaderboards == null)
                {
                    newCaptor.CapturedLeaderboards = new List<Leaderboard>
                    {
                        leaderboard
                    };
                }
                else
                {
                    // Check to make sure the clan hasn't already captured this map
                    if (!newCaptor.CapturedLeaderboards.Contains(leaderboard))
                    {
                        newCaptor.CapturedLeaderboards.Add(leaderboard);
                    }
                }

                // Determine what % of the map pool this clan now owns
                if (RankingService.RankedMapCount != 0)
                {
                    newCaptor.RankedPoolPercentCaptured = (float)(newCaptor.CapturedLeaderboards?.Count ?? 0) / RankingService.RankedMapCount;
                }
            }
        }

        private static void RemoveCapturedLeaderboard(ref Clan prevCaptor, Leaderboard leaderboard)
        {
            // Remove leaderboard from old captor
            if (prevCaptor?.CapturedLeaderboards != null && prevCaptor.CapturedLeaderboards.Contains(leaderboard))
            {
                prevCaptor.CapturedLeaderboards.Remove(leaderboard);
            }
        }
    }
}