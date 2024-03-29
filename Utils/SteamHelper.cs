﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Utils
{
    public class SteamHelper
    {
        public static Task<(string?, string?)> GetPlayerIDFromTicket(string ticket, IConfiguration configuration)
        {
            if (ticket.Length > 200) {
                return GetSteamPlayerIDFromTicket(ticket, configuration);
            } else {
                return GetOculusPlayerIDFromTicket(ticket, configuration);
            }
        }

        public static Task<(string?, string?)> GetSteamPlayerIDFromTicket(string ticket, IConfiguration configuration)
        {
            string steamKey = configuration.GetValue<string>("SteamKey");
            string steamApi = configuration.GetValue<string>("SteamApi");

            string url = steamApi + "/ISteamUserAuth/AuthenticateUserTicket/v1?appid=620980&key=" + steamKey + "&ticket=" + ticket;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse response = null;
            string? error = null;
            var stream =
            Task<(WebResponse, string)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    error = e.ToString();
                }

                return (response, error);
            }, request);

            return stream.ContinueWith(t => ReadSteamStreamFromResponse(t.Result));
        }

        private static (string?, string?) ReadSteamStreamFromResponse((WebResponse, string?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();

                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    if (info != null 
                        && ExpandantoObject.HasProperty(info, "response") 
                        && ExpandantoObject.HasProperty(info.response, "params")) {
                        return (info.response.@params.steamid, null);
                    } else if (info != null
                        && ExpandantoObject.HasProperty(info, "response")
                        && ExpandantoObject.HasProperty(info.response, "error"))
                    {
                        return (null, info.response.error.errordesc);
                    }

                    return (null, "Could not parse response");
                }
            }
            else
            {
                return (null, response.Item2);
            }
        }

        public static Task<(string?, string?)> GetOculusPlayerIDFromTicket(string ticket, IConfiguration configuration)
        {
            string url = "https://graph.oculus.com/me?fields=id";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Headers.Add("Authorization", "Bearer " + ticket);
            request.Proxy = null;

            WebResponse response = null;
            string? error = null;
            var stream =
            Task<(WebResponse, string)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    error = e.ToString();
                }

                return (response, error);
            }, request);

            return stream.ContinueWith(t => ReadOculusStreamFromResponse(t.Result));
        }

        private static (string?, string?) ReadOculusStreamFromResponse((WebResponse, string?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();

                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    if (info != null
                        && ExpandantoObject.HasProperty(info, "id"))
                    {
                        return (info.id, null);
                    }
                    else if (info != null
                      && ExpandantoObject.HasProperty(info, "message"))
                    {
                        return (null, info.message);
                    }

                    return (null, "Could not parse response");
                }
            }
            else
            {
                return (null, response.Item2);
            }
        }
    }
}
