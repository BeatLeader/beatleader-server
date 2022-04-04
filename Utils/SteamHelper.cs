using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Utils
{
    public class SteamHelper
    {
        public static Task<string?> GetPlayerIDFromTicket(string ticket)
        {
            string url = "https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v0001?appid=620980&key=B0A7AF33E804D0ABBDE43BA9DD5DAB48&ticket=" + ticket;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse response = null;
            string? playerID = null;
            var stream =
            Task<(WebResponse, string)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    playerID = null;
                }

                return (response, playerID);
            }, request);

            return stream.ContinueWith(t => ReadStreamFromResponse(t.Result));
        }

        private static string? ReadStreamFromResponse((WebResponse, string?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();

                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    if (info == null || !ExpandantoObject.HasProperty(info, "response") || !ExpandantoObject.HasProperty(info.response, "params")) return null;

                    return info.response.@params.steamid;
                }
            }
            else
            {
                return response.Item2;
            }
        }
    }
}
