using BeatLeader_Server.Migrations.ReadApp;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Numerics;

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

        public static ICollection<ClanRanking> CalculateClanRanking(this AppContext context, Leaderboard leaderboard)
        {
            if (leaderboard == null) 
            {
                return null;
            }

            // Calculate owning clan on this leaderboard
            // Dictionary is: <Clan, (weight, pp-value)>
            var clanPPDict = new Dictionary<Clan, (float, float)>();
            // Why can't I put a Where clause of: ".Where(s => s.LeaderboardId == leaderboardId && !s.Banned && s.Player.Clans != null)"
            // I want to ignore all players who aren't in any clans
            var leaderboardClans =
                context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned)
                    .OrderByDescending(el => el.Pp)
                    .Select(s => new { Pp = s.Pp, Clans = s.Player.Clans })
                    .ToList();

            // Build up a dictionary of the clans on this leaderboard with score pp, weighted by pp
            foreach (var score in leaderboardClans)
            {
                foreach (Clan clan in score.Clans)
                {
                    if (clanPPDict.ContainsKey(clan))
                    {
                        // Just picked a value of .9f, need to balance value based on how many clan members usually play the same map so as to not give advantage
                        // to clans with lots of members
                        float weight = MathF.Pow(0.965f, clanPPDict[clan].Item1);
                        //float weight = clanPPDict[clan].Item1 * 0.9f;
                        float clanPP = clanPPDict[clan].Item2 + (score.Pp * weight);
                        clanPPDict[clan] = (weight, clanPP);
                    }
                    else
                    {
                        clanPPDict.Add(clan, (1.0f, score.Pp));
                    }
                }
            }

            // Cases covered -------
            // Null -> Null : Good
            // Null -> Won : Good
            // Null -> Tied : Good
            // Won -> Null : Impossible
            // Won -> Won same Clan : Good
            // Won -> Won diff Clan : Good
            // Won -> Tied : Good
            // Tied -> Won : Good
            // Tied -> Tied : Good
            // ----------------------
            if (!clanPPDict.IsNullOrEmpty())
            {
                // Sort clanPPDict and clanRanking by PP
                clanPPDict = clanPPDict.OrderBy(x => x.Value.Item2).ToDictionary(x => x.Key, x => x.Value);
                leaderboard.ClanRanking ??= new List<ClanRanking>();
                var clanRanking = leaderboard.ClanRanking;

                if (clanRanking.Count != 0)
                {
                    clanRanking = clanRanking.OrderByDescending(cr => cr.ClanPP).ToList();
                    var prevCaptor = clanRanking.First().Clan;

                    // If we are introducing a tie, remove captor from the clanRanking, if there is one
                    if ((clanPPDict.Count > 1 && clanPPDict.ElementAt(0).Value.Item2 == clanPPDict.ElementAt(1).Value.Item2))
                    {
                        // The reason this has to be in a loop is because of n-way ties.
                        // If an n-way tie existed in the clanRanking, the previous captor could be anywhere in the clanRanking list.
                        foreach (var clan in clanRanking)
                        {
                            RemoveCapturedLeaderboard(ref prevCaptor, leaderboard);
                        }
                    }
                    else
                    {
                        // If the leaderboard was previously tied, and now it is captured, we don't want to remove captor (there wasn't one)
                        if (clanRanking.Count > 1 && clanRanking.ElementAt(0).ClanPP == clanRanking.ElementAt(1).ClanPP)
                        {
                            AddCapturedLeaderboard(ref clanPPDict, leaderboard);
                        } else
                        {
                            // If the leaderboard was previously won, and now it is won by a different clan,
                            // Remove board from old captor, add board to new captor
                            if (prevCaptor != clanPPDict.First().Key)
                            {
                                RemoveCapturedLeaderboard(ref prevCaptor, leaderboard);
                                AddCapturedLeaderboard(ref clanPPDict, leaderboard);
                            }
                        }
                    }
                } else
                {
                    // If clanRanking was empty:
                    // Empty --> Tie : Do nothing
                    // Empty --> Won : Add captured leaderboard
                    if (clanPPDict.Count == 1 || (clanPPDict.Count > 1 && clanPPDict.ElementAt(0).Value.Item2 != clanPPDict.ElementAt(1).Value.Item2))
                    {
                        AddCapturedLeaderboard(ref clanPPDict, leaderboard);
                    }
                }

                // Recalculate pp on clans
                // TODO : Maybe have function that only needs to do these operations for the clans affected by the score set
                foreach (var clan in clanPPDict)
                {
                    var updateClan = clanRanking.Where(cr => cr.Clan == clan.Key).FirstOrDefault();
                    if (updateClan != null)
                    {
                        updateClan.ClanPP = clan.Value.Item2;
                    }
                    else
                    {
                        clanRanking.Add(new ClanRanking { Clan = clan.Key, ClanPP = clan.Value.Item2 });
                    }
                }
            }

            return leaderboard.ClanRanking;
        }

        private static void AddCapturedLeaderboard(ref Dictionary<Clan, (float, float)> clanPPDict, Leaderboard leaderboard)
        {
            // Add leaderboard to new captor
            if (clanPPDict.First().Key.CapturedLeaderboards == null)
            {
                clanPPDict.First().Key.CapturedLeaderboards = new List<Leaderboard>
                {
                    leaderboard
                };
            }
            else
            {
                // Check to make sure the clan hasn't already captured this map
                if (!clanPPDict.First().Key.CapturedLeaderboards.Contains(leaderboard))
                {
                    clanPPDict.First().Key.CapturedLeaderboards.Add(leaderboard);
                }
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
