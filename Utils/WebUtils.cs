using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Utils
{
    public class WebUtils
    {
        public static string? GetCountryByIp(string ip)
        {
            string? result = null;
            try
            {
                string jsonResult = new WebClient().DownloadString("http://ipinfo.io/" + ip);
                dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(jsonResult, new ExpandoObjectConverter());
                if (info != null)
                {
                    result = info.country;
                }
            }
            catch { }

            if (result == null) {
                try
                {
                    string jsonResult = new WebClient().DownloadString("https://api.iplocation.net/?ip=" + ip);
                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(jsonResult, new ExpandoObjectConverter());
                    if (info != null)
                    {
                        result = info.country_code2;
                    }
                
                }
                catch{ }
            }

            return result;
        }
    }
}
