using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Dynamic;

namespace BeatLeader_Server.Extensions
{
    public static class StreamExtentions
    {
        public static ExpandoObject? ObjectFromStream(this Stream ms)
        {
            using (StreamReader reader = new StreamReader(ms))
            {
                string results = reader.ReadToEnd();
                if (string.IsNullOrEmpty(results))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
            }
        }

        public static T? ObjectFromStream<T>(this Stream ms)
        {
            using (StreamReader reader = new StreamReader(ms))
            {
                string results = reader.ReadToEnd();
                if (string.IsNullOrEmpty(results))
                {
                    return default(T);
                }

                return JsonConvert.DeserializeObject<T>(results);
            }
        }
    }
}
