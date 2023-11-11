using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BeatLeader_Server.Extensions
{
    public static class JsonExtensions
    {
        static DefaultContractResolver contractResolver = new DefaultContractResolver {
            NamingStrategy = new CamelCaseNamingStrategy()
        };

        public static string SerializeObject(object? value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings
            {
                ContractResolver = contractResolver,
                Formatting = Formatting.Indented
            });
        }
    } 
}
