using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Utils
{
    public static class ClanUtils
    {
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

        public static Clan CalculateOwningClan(this AppContext context, string leaderboardId)
        {
            // Calculate owning clan on this leaderboard
            var clanPPDict = new Dictionary<string, (float, float)>();
            var leaderboardClans =
                context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboardId && !s.Banned && s.Player.Clans != null)
                    .OrderByDescending(el => el.Pp)
                    .Select(s => new { Pp = s.Pp, Clans = s.Player.Clans })
                    .ToList();

            // Build up a dictionary of the clans on this leaderboard with pp, weighted by the pp
            // Any way to utilize linq?
            foreach (var score in leaderboardClans)
            {
                // score.Clans can't be null, because leaderboardClans has s.Player.Clans != null in "Where" statement
                foreach (Clan clan in score.Clans)
                {
                    if (clanPPDict.ContainsKey(clan.Tag))
                    {
                        // Just picked a value of .9f, need to balance value based on how many clan members usually play the same map so as to not give advantage
                        // to clans with lots of members
                        float weight = clanPPDict[clan.Tag].Item2 * 0.9f;
                        float clanPP = clanPPDict[clan.Tag].Item1 + (score.Pp * weight);
                        clanPPDict[clan.Tag] = (clanPP, weight);
                    }
                    else
                    {
                        clanPPDict.Add(clan.Tag, (score.Pp, 1.0f));
                    }
                }
            }

            // Get the clan with the most weighted pp on the map
            bool unclaimed = true;
            bool contested = false;
            string owningClanTag = "";
            float maxPP = 0;
            foreach (var clanWeightedPP in clanPPDict)
            {
                if (clanWeightedPP.Value.Item1 > maxPP)
                {
                    maxPP = clanWeightedPP.Value.Item1;
                    owningClanTag = clanWeightedPP.Key;
                    contested = false;
                    unclaimed = false;
                } else
                {
                    // There are multiple clans with the same weighted pp on this leaderboard
                    if (clanWeightedPP.Value.Item1 == maxPP)
                    {
                        contested = true;
                    }
                }
            }

            if (unclaimed)
            {
                // Reserve clan name - Unclaimed
                Clan unclaimedClan = new Clan();
                unclaimedClan.Name = "Unclaimed";
                unclaimedClan.Color = "#000000";
                return unclaimedClan;
            }
            else
            {
                if (contested)
                {
                    // Reserve clan name - Contested
                    Clan contestedClan = new Clan();
                    contestedClan.Name = "Contested";
                    contestedClan.Color = "#ff0000";
                    return contestedClan;
                }
                else
                {
                    return context.Clans.FirstOrDefault(c => c.Tag == owningClanTag);
                }
            }
        }
    }
}
