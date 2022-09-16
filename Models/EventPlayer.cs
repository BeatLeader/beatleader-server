namespace BeatLeader_Server.Models
{
    public class EventPlayer
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public string Name { get; set; }
        public string PlayerId { get; set; }
        public string Country { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public float Pp { get; set; }
    }
}
