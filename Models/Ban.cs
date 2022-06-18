namespace BeatLeader_Server.Models
{
    public class Ban
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public string BannedBy { get; set; }
        public string BanReason { get; set; }
        public int Timeset { get; set; }
        public int Duration { get; set; }
    }
}
