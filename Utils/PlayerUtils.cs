using System.Dynamic;
using System.Net;
using BeatLeader_Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Utils
{
    public static class PlayerUtils
    {
        public static void RecalculatePP(this AppContext context, Player player, List<Score>? scores = null)
        {
            var ranked = scores ?? context
                .Scores
                .Where(s => s.PlayerId == player.Id && s.Pp != 0 && !s.Banned && !s.Qualification)
                .OrderByDescending(s => s.Pp)
                .ToList();
            float resultPP = 0f;

            float weightedAccSum = 0f;
            float weightsSum = 0f;
            foreach ((int i, Score s) in ranked.Select((value, i) => (i, value)))
            {
                float weight = MathF.Pow(0.965f, i);
                if (s.Weight != weight)
                {
                    s.Weight = weight;
                }

                resultPP += s.Pp * weight;
                weightedAccSum += s.Accuracy * weight;
                weightsSum += weight;
            }
            player.Pp = resultPP;
            if (weightsSum > 0) {
                player.ScoreStats.AverageWeightedRankedAccuracy = weightedAccSum / weightsSum;
            }
        }

        public static void RecalculatePPAndRankFast(this AppContext context, Player player)
        {
            float oldPp = player.Pp;

            var rankedScores = context
                .Scores
                .Where(s => s.PlayerId == player.Id && s.Pp != 0 && !s.Banned && !s.Qualification)
                .OrderByDescending(s => s.Pp)
                .Select(s => new { Pp = s.Pp })
                .ToList();
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

        public static void RecalculateEventsPP(this AppContext context, Player player, Leaderboard leaderboard)
        {
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var events = context.EventRankings
                .Where(ev => ev.EndDate >= timestamp && ev.Leaderboards.Contains(leaderboard))
                .Include(ev => ev.Players)
                .ToList();
            if (events.Count() == 0) {
                return;
            }

            foreach (var eventRanking in events)
            {
                var ranked = context.Leaderboards.Where(lb => lb.Events.FirstOrDefault(e => e.Id == eventRanking.Id) != null)
                    .Select(lb => new { Pp = lb.Scores.Where(s =>
                        s.PlayerId == player.Id &&
                        s.Pp != 0 &&
                        !s.Banned).Count() > 0 ? lb.Scores.Where(s =>
                        s.PlayerId == player.Id &&
                        s.Pp != 0 &&
                        !s.Banned).First().Pp : 0 } )
                    .OrderByDescending(s => s.Pp).ToList();
                if (eventRanking.Players == null)
                {
                    eventRanking.Players = new List<EventPlayer>();
                }

                var eventPlayer = eventRanking.Players.Where(p => p.PlayerId == player.Id).FirstOrDefault();

                if (eventPlayer == null)
                {
                    eventPlayer = new EventPlayer
                    {
                        PlayerId = player.Id,
                        Country = player.Country,
                        EventId = eventRanking.Id,
                        Name = eventRanking.Name
                    };
                    eventRanking.Players.Add(eventPlayer);
                    context.SaveChanges();
                }

                if (player.EventsParticipating == null || player.EventsParticipating.FirstOrDefault(e => e.EventId == eventRanking.Id) == null)
                {
                    if (player.EventsParticipating == null)
                    {
                        player.EventsParticipating = new List<EventPlayer> { eventPlayer };
                    }
                    else
                    {
                        player.EventsParticipating.Add(eventPlayer);
                    }
                }

                float resultPP = 0f;
                foreach ((int i, var s) in ranked.Select((value, i) => (i, value)))
                {
                    resultPP += s.Pp * MathF.Pow(0.965f, i);
                }

                eventPlayer.Pp = resultPP;

                var players = eventRanking.Players.OrderByDescending(t => t.Pp).ToList();
                Dictionary<string, int> countries = new Dictionary<string, int>();

                foreach ((int i, EventPlayer p) in players.Select((value, i) => (i, value)))
                {
                    p.Rank = i + 1;
                    if (!countries.ContainsKey(p.Country))
                    {
                        countries[p.Country] = 1;
                    }

                    p.CountryRank = countries[p.Country];
                    countries[p.Country]++;
                }
            }
        }

        public static string[] AllowedCountries() {
            return new string[] { "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM", "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW", "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI", "VN", "VU", "WF", "WS", "XK", "YE", "YT", "ZA", "ZM", "ZW" };
        }
        public static async Task<Player?> GetPlayerFromSteam(string playerID, string steamKey)
        {
            dynamic? info = await GetPlayer("http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=" + steamKey + "&steamids=" + playerID);

            if (info == null || info.response.players.Count == 0) return null;

            dynamic playerInfo = info.response.players[0];
            Player result = new Player();
            result.Name = playerInfo.personaname;
            result.Avatar = playerInfo.avatarfull;
            result.Platform = "steam";
            if (ExpandantoObject.HasProperty(playerInfo, "loccountrycode"))
            {
                result.Country = playerInfo.loccountrycode;
            }
            else
            {
                result.Country = "not set";
            }
            result.ExternalProfileUrl = playerInfo.profileurl;

            return result;
        }
        public static async Task<Player?> GetPlayerFromOculus(string playerID, string token)
        {
            dynamic? info = await GetPlayer("https://graph.oculus.com/" + playerID + "?fields=id,alias,avatar_v2{avatar_image{uri}}", token);

            if (info == null) return null;

            Player result = new Player();
            result.Name = info.alias;
            result.Id = info.id;
            result.Platform = "oculuspc";
            result.Country = "not set";
            if (ExpandantoObject.HasProperty(info, "avatar_v2") && ExpandantoObject.HasProperty(info.avatar_v2, "avatar_image"))
            {
                result.Avatar = info.avatar_v2.avatar_image.uri;
            }
            else
            {
                result.SetDefaultAvatar();
            }

            return result;
        }

        public static async Task<Player?> GetPlayerFromBeatSaver(string playerID)
        {
            string bslink = "https://beatsaver.com/";
            dynamic? info = await GetPlayer(bslink + "api/users/id/" + playerID);

            if (info == null) return null;

            Player result = new Player();
            result.Name = info.name;
            result.Id = (30000000 + info.id) + "";
            result.MapperId = (int)info.id;
            result.Platform = "beatsaver";
            result.Country = "not set";
            result.Avatar = info.avatar;
            result.ExternalProfileUrl = bslink + "profile/" + playerID;

            return result;
        }

        public static Task<dynamic?> GetPlayer(string url, string? token = null)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            if (token != null) {
                request.Headers.Add("Authorization", "Bearer " + token);
            }
            request.Proxy = null;

            WebResponse? response = null;
            dynamic? song = null;
            var stream =
            Task<(WebResponse?, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadPlayerFromResponse(t.Result));
        }

        private static dynamic? ReadPlayerFromResponse((WebResponse?, dynamic?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return null;
                    }

                    return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                }
            }
            else
            {
                return response.Item2;
            }
        }
    }
}

