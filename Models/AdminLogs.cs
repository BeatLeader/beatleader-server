namespace BeatLeader_Server.Models
{
    public class ScoreRemovalLog
    {
        public int Id { get; set; }
        public string Replay { get; set; }
        public string AdminId { get; set; }
        public int Timestamp { get; set; }
    }

    public class PlayerBanLog
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public string AdminId { get; set; }
        public int Timestamp { get; set; }
    }
}
