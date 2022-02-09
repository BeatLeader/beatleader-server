using System.Dynamic;

namespace BeatLeader_Server.Utils
{
    public static class ExpandantoObject
    {
        public static bool HasProperty(ExpandoObject obj, string propertyName)
        {
            return obj != null && ((IDictionary<String, object>)obj).ContainsKey(propertyName);
        }
    }
}
