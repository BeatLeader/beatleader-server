namespace BeatLeader_Server.Models
{
    public class User
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Avatar { get; set; }
        public string Country { get; set; }
        public ICollection<Playlist> Playlists { get; set; }
    }
}
