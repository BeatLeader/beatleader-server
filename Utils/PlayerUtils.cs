using System;
using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    public static class PlayerUtils
    {
        public static void RecalculatePP(this AppContext context, Player player, List<Score>? scores = null)
        {
            var ranked = scores ?? context.Scores.Where(s => s.PlayerId == player.Id && s.Pp != 0 && !s.Banned).OrderByDescending(s => s.Pp).ToList();
            float resultPP = 0f;
            foreach ((int i, Score s) in ranked.Select((value, i) => (i, value)))
            {
                float weight = MathF.Pow(0.965f, i);
                if (s.Weight != weight)
                {
                    s.Weight = weight;
                }

                resultPP += s.Pp * s.Weight;
            }
            player.Pp = resultPP;
        }

        public static void RecalculatePPAndRankFast(this AppContext context, Player player)
        {
            float oldPp = player.Pp;

            var rankedScores = context.Scores.Where(s => s.PlayerId == player.Id && s.Pp != 0 && !s.Banned).OrderByDescending(s => s.Pp).Select(s => new { Pp = s.Pp }).ToList();
            float resultPP = 0f;
            foreach ((int i, float pp) in rankedScores.Select((value, i) => (i, value.Pp)))
            {
                float weight = MathF.Pow(0.965f, i);
                resultPP += pp * weight;
            }
            player.Pp = resultPP;

            var rankedPlayers = context
                .Players
                .Where(t => t.Pp >= oldPp && t.Pp <= resultPP && t.Id != player.Id)
                .OrderByDescending(t => t.Pp)
                .Select(p => new { Pp = p.Pp, Country = p.Country, Rank = p.Rank, CountryRank = p.CountryRank })
                .ToList();

            if (rankedPlayers.Count() > 0)
            {
                player.Rank = rankedPlayers[0].Rank;

                var topCountryPlayer = rankedPlayers.FirstOrDefault(p => p.Country == player.Country);
                if (topCountryPlayer != null)
                {
                    player.CountryRank = topCountryPlayer.CountryRank;
                }
            }
        }

        public static string[] AllowedCountries() {
            return new string[] { "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM", "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW", "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI", "VN", "VU", "WF", "WS", "XK", "YE", "YT", "ZA", "ZM", "ZW" };
        }
    }
}

