using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BeatLeader_Server.Utils
{
    public class ClanRankingData
    {
        public int Id { get; set; }
        public int numberOfScores { get; set; }
        public float weight { get; set; }
        public int lastUpdateTime { get; set; }
        public float Pp { get; set; }
        public float totalAcc { get; set; }
        public int totalRank { get; set; }
        public int totalScore { get; set; }
        public int rank { get; set; }
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

        public static (List<ClanRankingChanges>?, List<Clan>?) UpdateClanRanking(
            this AppContext context, 
            Leaderboard? leaderboard, 
            Score newScore)
        {
            if (leaderboard == null || leaderboard.Difficulty.Status != DifficultyStatus.ranked) return (null, null);

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

            var playerClans = newScore
                .Player?
                .Clans?
                .OrderBy(c => newScore.Player?.ClanOrder.IndexOf(c.Tag) ?? 0)
                .ThenBy(c => c.Id)
                .Take(1)
                .ToList();
            
            if (playerClans == null || playerClans.Count == 0) return (null, null);
            var playerClanIds = playerClans.Select(c => c.Id).ToList();

            var changes = new List<ClanRankingChanges>();
            var clanRankings = context
                    .ClanRanking
                    .Where(cr => cr.LeaderboardId == newScore.LeaderboardId)
                    .ToList();

            var globalTop = clanRankings
                .OrderByDescending(cr => cr.Pp)
                .FirstOrDefault();
            var globalTopPp = globalTop?.Pp ?? 0f;

            foreach (var clan in playerClans)
            {
                var clanRanking = clanRankings
                    .Where(cr => cr.ClanId == clan.Id)
                    .FirstOrDefault();

                var associatedScores = context
                    .Scores
                    .Where(s => 
                        s.ValidContexts.HasFlag(LeaderboardContexts.General) && 
                        s.LeaderboardId == newScore.LeaderboardId && 
                        s.Player.Clans.Contains(clan))
                    .OrderByDescending(a => Math.Round(a.Pp, 2))
                    .ThenByDescending(a => Math.Round(a.Accuracy, 4))
                    .ThenBy(a => a.Timeset)
                    .Select(s => new { s.Pp, s.Rank, s.Accuracy, s.ModifiedScore })
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
                    clanRankings.Add(clanRanking);
                }

                // Update the Pp of the clanRanking
                int weightPower = 0;
                float calculatedPp = 0.0f;
                foreach (var score in associatedScores)
                {
                    calculatedPp += (score.Pp * MathF.Pow(clanRankingWeight, weightPower));
                    weightPower++;
                }
                // Update clanRanking fields
                clanRanking.Pp = calculatedPp;
                clanRanking.LastUpdateTime = newScore.Timepost;
                clanRanking.AverageRank = (float)associatedScores.Average(s => s.Rank);
                clanRanking.AverageAccuracy = (float)associatedScores.Average(s => s.Accuracy);
                clanRanking.TotalScore = associatedScores.Sum(s => s.ModifiedScore);
            }

            var orderedRanking = clanRankings
                .OrderByDescending(c => c.Pp)
                .ToList();
            var localTop = orderedRanking.First();
            
            // Check if we're introducing a tie
            if (globalTop == null || localTop.Pp > globalTopPp)
            {
                // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                if (leaderboard.ClanId == null)
                {
                    // Add captured leaderboard to new owner
                    changes.Add(new ClanRankingChanges(leaderboard, null, localTop.ClanId, context, clanRankings));
                    CaptureLeaderboard(context, localTop.ClanId, leaderboard);
                }
                else if (leaderboard.ClanId != localTop.ClanId)
                {
                    // If the leaderboard was previously won, and now it is won by a different clan,
                    // Remove board from old captor, add board to new captor
                    changes.Add(new ClanRankingChanges(leaderboard, globalTop?.ClanId, localTop.ClanId, context, clanRankings));
                    CaptureLeaderboard(context, localTop.ClanId, leaderboard);
                }
            }
            else if (clanRankings.Count > 1 &&
                     orderedRanking[0].Pp == orderedRanking[1].Pp && 
                     leaderboard.ClanRankingContested == false)
            {
                // If the map was contested before, and its still contested, don't do anything
                changes.Add(new ClanRankingChanges(leaderboard, globalTop?.ClanId, null, context, clanRankings));
                ContestLeaderboard(context, leaderboard);
            }

            var rank = 1;
            foreach (var cr in orderedRanking)
            {
                cr.Rank = rank;
                rank++;
            }

            return (changes, playerClans);
        }

        public static List<ClanRankingChanges>? CalculateClanRankingSlow(this AppContext context, Leaderboard? leaderboard)
        {
            // CalculateClanRankingSlow: Function that calculates the clanRanking given a leaderboard
            // This function is called on relevant leaderboards whenever a user sets a new score, a clan is created, a user leaves a clan,
            // a user joins a clan, a map is ranked, or a clan is deleted.
            if (leaderboard == null)
            {
                return null;
            }

            // Calculate clan captor on this leaderboard
            var clanRankingData = new Dictionary<int, ClanRankingData>();
            var leaderboardScores =
                context
                    .Scores
                    .Where(s => 
                        s.LeaderboardId == leaderboard.Id && 
                        s.ValidContexts.HasFlag(LeaderboardContexts.General) && 
                        !s.Banned && 
                        s.Player.Clans.Count > 0)
                    .Select(s => new {
                        s.Timepost,
                        s.Pp,
                        s.Accuracy,
                        s.Rank,
                        s.ModifiedScore,
                        s.Player.Clans,
                        s.Player.ClanOrder
                        })
                    .ToList()
                    .OrderByDescending(cr => Math.Round(cr.Pp, 2))
                    .ThenByDescending(cr => Math.Round(cr.Accuracy, 4))
                    .ThenBy(cr => cr.Timepost)
                    .ToList();

            var changes = new List<ClanRankingChanges>();

            // Build up a dictionary of the clans on this leaderboard based on scores
            foreach (var score in leaderboardScores)
            {
                if (score.Clans == null)
                {
                    continue;
                }
                foreach (Clan clan in 
                    score
                        .Clans
                        .OrderBy(c => score.ClanOrder.IndexOf(c.Tag))
                        .ThenBy(c => c.Id)
                        .Take(1))
                {
                    if (!clanRankingData.ContainsKey(clan.Id))
                    {
                        // Update the data already in the newClanRankingData dictionary
                        clanRankingData.Add(clan.Id, new ClanRankingData { Id = clan.Id });
                    }

                    clanRankingData[clan.Id].weight = MathF.Pow(clanRankingWeight, clanRankingData[clan.Id].numberOfScores);
                    clanRankingData[clan.Id].numberOfScores++;
                    clanRankingData[clan.Id].lastUpdateTime = Math.Max(score.Timepost, clanRankingData[clan.Id].lastUpdateTime);
                    clanRankingData[clan.Id].Pp += score.Pp * clanRankingData[clan.Id].weight;
                    clanRankingData[clan.Id].totalAcc += score.Accuracy;
                    clanRankingData[clan.Id].totalRank += score.Rank;
                    clanRankingData[clan.Id].totalScore += score.ModifiedScore;
                }
            }

            var rankedData = clanRankingData.Values.OrderByDescending(x => x.Pp).ToList();
            for (int i = 0; i < rankedData.Count; i++)
            {
                rankedData[i].rank = i + 1;
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
            if (!clanRankingData.IsNullOrEmpty())
            {   
                var clanRanking = leaderboard.ClanRanking?.ToList();
                if (clanRanking == null) {
                    clanRanking = new List<ClanRanking>();
                    leaderboard.ClanRanking = clanRanking;
                }

                if (clanRanking.Count != 0)
                {
                    clanRanking = clanRanking.OrderByDescending(cr => cr.Pp).ToList();
                    var prevCaptor = leaderboard.ClanId;

                    // If we are introducing a tie, remove captor from the clanRanking, if there is one
                    if (rankedData.Count > 1 && rankedData.ElementAt(0).Pp == rankedData.ElementAt(1).Pp)
                    {
                        if (!leaderboard.ClanRankingContested) {
                            changes.Add(new ClanRankingChanges(leaderboard, prevCaptor, null, context, clanRanking));
                            ContestLeaderboard(context, leaderboard);
                        }
                    }
                    else
                    {
                        // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                        if (clanRanking.Count > 1 && leaderboard.ClanRankingContested)
                        {
                            changes.Add(new ClanRankingChanges(leaderboard, null, rankedData.First().Id, context, clanRanking));
                            CaptureLeaderboard(context, rankedData.First().Id, leaderboard);
                        }
                        else
                        {
                            // If the leaderboard was previously won, and now it is won by a different clan,
                            // Remove board from old captor, add board to new captor
                            if (prevCaptor != rankedData.First().Id)
                            {
                                changes.Add(new ClanRankingChanges(leaderboard, prevCaptor, rankedData.First().Id, context, clanRanking));
                                CaptureLeaderboard(context, rankedData.First().Id, leaderboard);
                            }
                        }
                    }
                }
                else
                {
                    // If clanRanking was empty:
                    // Empty --> Tie : Set leaderboard as contested
                    if (rankedData.Count > 1 && rankedData.ElementAt(0).Pp == rankedData.ElementAt(1).Pp)
                    {
                        leaderboard.ClanRankingContested = true;
                    }
                    else
                    {
                        // Empty --> Won : Add captured leaderboard
                        changes.Add(new ClanRankingChanges(leaderboard, null, rankedData.First().Id, context, clanRanking));
                        CaptureLeaderboard(context, rankedData.First().Id, leaderboard);
                    }
                }

                foreach (var clan in rankedData)
                {
                    var clanRankings = leaderboard.ClanRanking.Where(cr => cr.ClanId == clan.Id).ToList();
                    if (clanRankings.Count > 1) {
                        var toDelete = clanRankings.Skip(1).ToList();
                        foreach (var item in toDelete)
                        {
                            leaderboard.ClanRanking.Remove(item);
                            context.ClanRanking.Remove(item);
                        }
                    }
                }

                // Recalculate pp on clans
                foreach (var clan in rankedData)
                {
                    var updateClan = leaderboard.ClanRanking.Where(cr => cr.ClanId == clan.Id).FirstOrDefault();
                    if (updateClan != null)
                    {
                        updateClan.LastUpdateTime = clan.lastUpdateTime;
                        updateClan.Pp = clan.Pp;
                        updateClan.Rank = clan.rank;
                        updateClan.AverageRank = clan.totalRank / clan.numberOfScores;
                        updateClan.AverageAccuracy = clan.totalAcc / clan.numberOfScores;
                        updateClan.TotalScore = clan.totalScore;
                    }
                    else
                    {
                        leaderboard.ClanRanking.Add(new ClanRanking
                        {
                            ClanId = clan.Id,
                            LastUpdateTime = clan.lastUpdateTime,
                            Pp = clan.Pp,
                            AverageRank = clan.totalRank / clan.numberOfScores,
                            AverageAccuracy = clan.totalAcc / clan.numberOfScores,
                            Rank = clan.rank,
                            TotalScore = clan.totalScore,
                            LeaderboardId = leaderboard.Id,
                            Leaderboard = leaderboard,
                        });
                    }
                }
            }

            return changes;
        }

        public static async Task<List<ClanRankingChanges>?> RecalculateClanRankingForPlayer(AppContext context, string playerId) {
            var leaderboardsRecalc = context
                .Scores
                .Where(s => s.Pp > 0 && !s.Qualification && s.PlayerId == playerId)
                .Include(s => s.Leaderboard)
                .ThenInclude(lb => lb.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(lb => lb.ClanRanking)
                .Select(s => s.Leaderboard)
                .ToList();
            var result = new List<ClanRankingChanges>(); 
            foreach (var leaderboard in leaderboardsRecalc)
            {
                var changes = context.CalculateClanRankingSlow(leaderboard);
                if (changes != null) {
                    result.AddRange(changes);
                }
            }
            await context.BulkSaveChangesAsync();
            return result;
        }
        public static async Task UpdateClanRankingRanks(AppContext context) {
            var clans = await context
                .Clans
                .Where(c => c.CaptureLeaderboardsCount > 0)
                .OrderByDescending(c => c.CaptureLeaderboardsCount)
                .ToListAsync();

            var rank = 1;
            foreach (var clan in clans)
            {
                clan.Rank = rank;
                rank++;
            }

            await context.BulkSaveChangesAsync();
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

        public static async Task PostChangesWithScore(AppContext context, List<ClanRankingChanges>? changes, Score score, string imagePath, string? hook) {
            if (changes == null || hook == null) return;

            try {
            var dsClient = new DiscordWebhookClient(hook);
            foreach (var change in changes)
            {
                var player = score.Player;
                var currentCaptor = change.CurrentCaptorId != null ? context.Clans.Find(change.CurrentCaptorId) : null;
                var previousCaptor = change.PreviousCaptorId != null ? context.Clans.Find(change.PreviousCaptorId) : null;
                var songName = context.Songs.Where(s => s.Id == change.Leaderboard.SongId).Select(s => s.Name).FirstOrDefault();

                string message = $"**{player.Name}** ";
                if (currentCaptor == null) {
                    message += "introduced a tie on ";
                } else {
                    message += $"**[{currentCaptor.Tag}]** captured ";
                }

                if (songName != null && change.Leaderboard.Difficulty != null) {
                    message += $"[{songName} - {change.Leaderboard.Difficulty.DifficultyName}](https://beatleader.net/leaderboard/clanranking/{change.Leaderboard.Id}) ";
                }

                message += $"by getting {Math.Round(score.Pp, 2)}pp with {Math.Round(score.Accuracy * 100, 2)}% acc{(score.Modifiers.Length > 0 ? (" and " + score.Modifiers) : "")}.\n";

                if (currentCaptor != null) {
                    if (previousCaptor != null) {
                        message += $"Taking over the map from **[{previousCaptor.Tag}]**";
                    }
                    message += $" which brings **[{currentCaptor.Tag}]** to {Math.Round(currentCaptor.RankedPoolPercentCaptured * 100, 2)}% of global dominance!";
                }

                var messageId = await dsClient.SendFileAsync(imagePath, message, flags: MessageFlags.SuppressEmbeds);
                await Bot.BotService.PublishAnnouncement(1195125703830683678, messageId);
            }
            } catch { }
        }

        public static async Task PostChangesWithMessage(AppContext context, List<ClanRankingChanges>? changes, string postMessage, string? hook) {
            if (changes == null || changes.Count == 0 || hook == null) return;

            if (changes.Count > 10) {
                for (int i = 0; i < changes.Count; i += 10)
                {
                    await PostChangesWithMessage(context, changes.Skip(i).Take(10).ToList(), postMessage, hook);
                }
            } else {
                try {
                    var dsClient = new DiscordWebhookClient(hook);
                    string message = "**" + postMessage + "**\n";
                    foreach (var change in changes)
                    {
                        var currentCaptor = change.CurrentCaptorId != null ? context.Clans.Find(change.CurrentCaptorId) : null;
                        var previousCaptor = change.PreviousCaptorId != null ? context.Clans.Find(change.PreviousCaptorId) : null;
                        var songName = context.Songs.Where(s => s.Id == change.Leaderboard.SongId).Select(s => s.Name).FirstOrDefault();
                
                        if (currentCaptor == null) {
                            message += "introduced a tie on ";
                        } else {
                            message += $"captured ";
                        }

                        if (songName != null && change.Leaderboard.Difficulty != null) {
                            message += $"[{songName} - {change.Leaderboard.Difficulty.DifficultyName}](https://beatleader.net/leaderboard/clanranking/{change.Leaderboard.Id})";
                        }

                        if (currentCaptor != null) {
                            message += $" for **[{currentCaptor.Tag}]**";
                            if (previousCaptor != null) {
                                message += $" from **[{previousCaptor.Tag}]**";
                            }
                        }
                        message += "\n";
                    }
                    var messageId = await dsClient.SendMessageAsync(message,
                        embeds: new List<Embed> { new EmbedBuilder()
                            .WithTitle("View changes on a map 🌐")
                            .WithUrl("https://beatleader.net/clansmap")
                            .Build()
                        });
                    await Bot.BotService.PublishAnnouncement(1195125703830683678, messageId);
                } catch { }
            }
        }
    }
}