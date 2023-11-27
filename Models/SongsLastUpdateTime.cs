namespace BeatLeader_Server.Models
{
    public class SongsLastUpdateTime
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public SongStatus Status { get; set; }
    }
}
