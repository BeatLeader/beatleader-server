namespace BeatLeader_Server.Models
{
    public class Playlist
    {
        public int Id { get; set; }
        public bool IsShared { get; set; }
        public string Value { get; set; }
        public string OwnerId { get; set; }
    }
}
