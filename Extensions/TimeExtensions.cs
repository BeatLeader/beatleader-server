namespace BeatLeader_Server.Extensions
{
    public static class Time
    {
        public static int UnixNow() {
            return (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }
}
