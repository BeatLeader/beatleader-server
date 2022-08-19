namespace BeatLeader_Server.Models
{
    public class EventRanking
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int EndDate { get; set; }
        public int PlaylistId { get; set; }
        public string Image { get; set; }

        public ICollection<Leaderboard> Leaderboards { get; set; }
        public ICollection<EventPlayer> Players { get; set; }
    }
}
