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
    }
}
