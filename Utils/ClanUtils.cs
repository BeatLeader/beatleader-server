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
            Clan? clan = context.Clans.Where(c => c.Id == clanId).Include(c => c.Players).FirstOrDefault();
            float resultPP = 0f;
            if (clan != null) {
                var ranked = clan.Players.OrderByDescending(s => s.Pp).ToList();
            
                foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
                {
                    float weight = MathF.Pow(0.965f, i);
                    resultPP += p.Pp * weight;
                }
            }
            return resultPP;
        }

        public static void UpdateClanRanking(this AppContext context, Leaderboard? leaderboard, Score? currentScore, Score newScore)
        {
            if (leaderboard == null || leaderboard.Difficulty.Status != DifficultyStatus.ranked) return;

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

            var playerClans = context
                .Players
                .Where(p => p.Id == newScore.PlayerId && p.Clans != null)
                .Select(p => p.Clans.ToList())
                .FirstOrDefault();
            if (playerClans == null || playerClans.Count() == 0) return;

            var clanRankings =
                context
                .ClanRanking
                .Where(cr => cr.LeaderboardId == newScore.LeaderboardId && cr.Clan != null && !playerClans.Contains(cr.Clan))
                .OrderByDescending(cr => Math.Round(cr.Pp, 2))
                .ThenByDescending(cr => Math.Round(cr.AverageAccuracy, 4))
                .ThenByDescending(cr => cr.LastUpdateTime)
                .ToList();

            var topClanRanking = clanRankings.FirstOrDefault();
            var newCRCaptorId = topClanRanking?.Id ?? null;
            var newCRCaptorPp = topClanRanking?.Pp ?? 0;

            foreach (var clan in playerClans)
            {
                var clanRanking = clanRankings
                    .Where(cr => cr.ClanId == clan.Id)
                    .FirstOrDefault();

                var associatedScores = context
                    .Scores
                    .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && s.LeaderboardId == newScore.LeaderboardId && s.Player.Clans.Contains(clan))
                    .OrderBy(a => Math.Round(a.Pp, 2))
                    .ThenBy(a => Math.Round(a.Accuracy, 4))
                    .ThenBy(a => a.Timeset)
                    .Select(s => s.Pp)
                    .ToList();

                if (clanRanking == null)
                {
                    clanRanking = new ClanRanking
                    {
                        ClanId = clan.Id,
                        LastUpdateTime = 0,
                        Pp = 0,
                        AverageRank = 0,
                        AverageAccuracy = 0,
                        TotalScore = 0,
                        LeaderboardId = newScore.LeaderboardId,
                    };
                    context.ClanRanking.Add(clanRanking);
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
                    calculatedPp += (score * MathF.Pow(clanRankingWeight, weightPower));
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
                if (topClanRanking.Id != newCRCaptorId && topClanRanking.Pp == newCRCaptorPp)
                {
                    // If the map was contested before, and its still contested, don't do anything
                    if (leaderboard.ClanRankingContested == false)
                    {
                        ContestLeaderboard(context, leaderboard);
                    }
                }
                else
                {
                    // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                    if (leaderboard.ClanRankingContested)
                    {
                        // Add captured leaderboard to new owner
                        CaptureLeaderboard(context, newCRCaptorId, leaderboard);
                    }
                    else
                    {
                        // If the leaderboard was previously won, and now it is won by a different clan,
                        // Remove board from old captor, add board to new captor
                        if (topClanRanking.Id != newCRCaptorId)
                        {
                            CaptureLeaderboard(context, newCRCaptorId, leaderboard);
                        }
                    }
                }
            }
            else
            {
                // If clanRanking was empty:
                if (playerClans.Count() > 1)
                {
                    // Empty --> Tie : Set leaderboard as contested; If the clanRankings were empty,
                    // then any new singular score with more than 1 clan will be contested immediately.
                    leaderboard.ClanRankingContested = true;
                }
                else
                {
                    CaptureLeaderboard(context, playerClans.First().Id, leaderboard);
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
            var newClanRankingData = new Dictionary<int, ClanRankingData>();
            var leaderboardClans =
                context
                    .Scores
                    .Where(s => 
                        s.LeaderboardId == leaderboard.Id && 
                        s.ValidContexts.HasFlag(LeaderboardContexts.General) && 
                        !s.Banned && 
                        s.Player.Clans.Count > 0)
                    .OrderByDescending(cr => Math.Round(cr.Pp, 2))
                    .ThenByDescending(cr => Math.Round(cr.Accuracy, 4))
                    .ThenByDescending(cr => cr.Timepost)
                    .Select(s => new {
                        s.Timepost,
                        s.Pp,
                        s.Accuracy,
                        s.Rank,
                        s.ModifiedScore,
                        s.Player.Clans
                        })
                    .ToList();

            // Build up a dictionary of the clans on this leaderboard based on scores
            foreach (var score in leaderboardClans)
            {
                if (score.Clans == null)
                {
                    continue;
                }
                foreach (Clan clan in score.Clans)
                {
                    if (newClanRankingData.ContainsKey(clan.Id))
                    {
                        // Update the data already in the newClanRankingData dictionary
                        newClanRankingData[clan.Id].numberOfScores = newClanRankingData[clan.Id].numberOfScores + 1;
                        newClanRankingData[clan.Id].weight = MathF.Pow(0.8f, newClanRankingData[clan.Id].weight);
                        newClanRankingData[clan.Id].lastUpdateTime = Math.Max(score.Timepost, newClanRankingData[clan.Id].lastUpdateTime);
                        newClanRankingData[clan.Id].Pp = newClanRankingData[clan.Id].Pp + (score.Pp * newClanRankingData[clan.Id].weight);
                        newClanRankingData[clan.Id].totalAcc = newClanRankingData[clan.Id].totalAcc + score.Accuracy;
                        newClanRankingData[clan.Id].totalRank = newClanRankingData[clan.Id].totalRank + score.Rank;
                        newClanRankingData[clan.Id].totalScore = newClanRankingData[clan.Id].totalScore + score.ModifiedScore;
                    }
                    else
                    {
                        newClanRankingData.Add(clan.Id, new ClanRankingData(
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
                leaderboard.ClanRanking = new List<ClanRanking>();
                var clanRanking = leaderboard.ClanRanking;

                if (clanRanking.Count != 0)
                {
                    clanRanking = clanRanking.OrderByDescending(cr => cr.Pp).ToList();
                    var prevCaptor = clanRanking.First().Clan.Id;

                    // If we are introducing a tie, remove captor from the clanRanking, if there is one
                    if (newClanRankingData.Count > 1 && newClanRankingData.ElementAt(0).Value.Pp == newClanRankingData.ElementAt(1).Value.Pp)
                    {
                        ContestLeaderboard(context, leaderboard);
                    }
                    else
                    {
                        // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                        if (clanRanking.Count > 1 && leaderboard.ClanRankingContested)
                        {
                            CaptureLeaderboard(context, newClanRankingData.Keys.First(), leaderboard);
                        }
                        else
                        {
                            // If the leaderboard was previously won, and now it is won by a different clan,
                            // Remove board from old captor, add board to new captor
                            if (prevCaptor != newClanRankingData.First().Key)
                            {
                                CaptureLeaderboard(context, newClanRankingData.Keys.First(), leaderboard);
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
                        CaptureLeaderboard(context, newClanRankingData.Keys.First(), leaderboard);
                    }
                }

                // Recalculate pp on clans
                foreach (var clan in newClanRankingData)
                {
                    var updateClan = clanRanking.Where(cr => cr.ClanId == clan.Key).FirstOrDefault();
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
                            ClanId = clan.Key,
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

        private static void CaptureLeaderboard(AppContext context, int? newCaptor, Leaderboard leaderboard)
        {
            if (newCaptor == null) return;

            if (leaderboard.ClanId != null) {
                var losingClan = context.Clans.Find(leaderboard.ClanId);

                if (losingClan != null) {
                    losingClan.CaptureLeaderboardsCount--;
                    losingClan.RankedPoolPercentCaptured = (float)(losingClan.CaptureLeaderboardsCount) / RankingService.RankedMapCount;
                }
            }

            // Add leaderboard to new captor
            leaderboard.ClanId = newCaptor;
            leaderboard.ClanRankingContested = false;

            var clan = context.Clans.Find(newCaptor);
            if (clan != null) {
                clan.CaptureLeaderboardsCount++;
                clan.RankedPoolPercentCaptured = (float)(clan.CaptureLeaderboardsCount) / RankingService.RankedMapCount;
            }
        }

        private static void ContestLeaderboard(AppContext context, Leaderboard leaderboard)
        {
            leaderboard.ClanRankingContested = true;

            if (leaderboard.ClanId != null) {
                var clan = context.Clans.Find(leaderboard.ClanId);

                if (clan != null) {
                    clan.CaptureLeaderboardsCount--;
                    clan.RankedPoolPercentCaptured = (float)(clan.CaptureLeaderboardsCount) / RankingService.RankedMapCount;
                }
            }

            leaderboard.ClanId = null;
        }
    }
}