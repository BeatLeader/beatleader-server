using System;
using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    public static class PlayerUtils
    {
        public static void RecalculatePP(this AppContext context, Player player)
        {
            var ranked = context.Scores.Where(s => s.PlayerId == player.Id && s.Pp != 0).OrderByDescending(s => s.Pp).ToList();
            float resultPP = 0f;
            foreach ((int i, Score s) in ranked.Select((value, i) => (i, value)))
            {
                s.Weight = MathF.Pow(0.965f, i);
                context.Scores.Update(s);
                resultPP += s.Pp * s.Weight;
            }
            player.Pp = resultPP;
            context.Players.Update(player);
        }
    }
}

