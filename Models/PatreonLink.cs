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
}
