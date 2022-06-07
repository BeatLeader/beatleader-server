using System.Dynamic;
using System.Net;
using BeatLeader_Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BeatLeader_Server.Utils
{
    public static class PlayerUtils
    {
        public static void RecalculatePP(this AppContext context, Player player, List<Score>? scores = null)
        {
            var ranked = scores ?? context.Scores.Where(s => s.PlayerId == player.Id && s.Pp != 0).OrderByDescending(s => s.Pp).ToList();
            float resultPP = 0f;
            foreach ((int i, Score s) in ranked.Select((value, i) => (i, value)))
            {
                float weight = MathF.Pow(0.965f, i);
                if (s.Weight != weight) {
                    s.Weight = weight;
                }
                
                resultPP += s.Pp * s.Weight;
            }
            player.Pp = resultPP;
        }

        public static string[] AllowedCountries() {
            return new string[] { "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM", "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW", "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI", "VN", "VU", "WF", "WS", "XK", "YE", "YT", "ZA", "ZM", "ZW" };
        }
        public static async Task<Player?> GetPlayerFromSteam(string playerID, string steamKey)
        {
            dynamic? info = await GetPlayer("http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=" + steamKey + "&steamids=" + playerID);

            if (info == null) return null;

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

            dynamic? gamesInfo = await GetPlayer("http://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v0001/?key=" + steamKey + "&steamid=" + playerID);
            if (gamesInfo != null && ExpandantoObject.HasProperty(gamesInfo, "response") && ExpandantoObject.HasProperty(gamesInfo.response, "total_count"))
            {
                dynamic response = gamesInfo.response;
                dynamic? beatSaber = null;

                for (int i = 0; i < response.total_count; i++)
                {
                    if (response.games[i].appid == 620980)
                    {
                        beatSaber = response.games[i];
                    }
                }

                if (beatSaber != null)
                {
                    result.AllTime = beatSaber.playtime_forever / 60.0f;
                    result.LastTwoWeeksTime = beatSaber.playtime_2weeks / 60.0f;
                }
            }

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

