using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Extensions
{
    public static class HttpWebRequestExtensions
    {
        public static Task<dynamic?> DynamicResponse(this HttpWebRequest request)
        {
            WebResponse? response = null;
            dynamic? song = null;
            var stream =
            Task<(WebResponse?, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                } catch (Exception e)
                {
                    song = null;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadObjectFromResponse(t.Result));
        }

        private static dynamic? ReadObjectFromResponse((WebResponse?, dynamic?) response)
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
            } else
            {
                return response.Item2;
            }
        }

        public static Task<T?> DynamicResponse<T>(this HttpWebRequest request)
        {
            WebResponse? response = null;
            T? song = default;
            var stream =
            Task<(WebResponse?, T?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                } catch (Exception e)
                {
                    song = default;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadObjectFromResponse(t.Result));
        }

        private static T? ReadObjectFromResponse<T>((WebResponse?, T?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return default(T);
                    }

                    return JsonConvert.DeserializeObject<T>(results);
                }
            } else
            {
                return response.Item2;
            }
        }
    }


}
