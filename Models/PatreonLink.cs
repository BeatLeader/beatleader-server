namespace BeatLeader_Server.Models
{
    public class PatreonLink
    {
        public string Id { get; set; }
        public string PatreonId { get; set; }
        public string Token { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Tier { get; set; } = "";
    }

    public class TwitchLink
    {
        public string Id { get; set; }
        public string TwitchId { get; set; }
        public string Token { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public class TwitterLink
    {
        public string Id { get; set; }
        public string TwitterId { get; set; }
        public string Token { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public class DiscordLink
    {
        public string Id { get; set; }
        public string DiscordId { get; set; }
        public string Token { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public class YouTubeLink
    {
        public string Id { get; set; }
        public string GoogleId { get; set; }
        public string Token { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }
}
