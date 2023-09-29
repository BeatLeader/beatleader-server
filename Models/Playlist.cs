namespace BeatLeader_Server.Models
{
    public class Playlist
    {
        public int Id { get; set; }
        public bool IsShared { get; set; }
        public string Link { get; set; }
        public string OwnerId { get; set; }
        public string? Hash { get; set; }
    }
}
