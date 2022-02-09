namespace BeatLeader_Server.Models
{
    public class User
    {
        public string Id { get; set; }
        public Player Player { get; set; }

        public bool CustomAvatar { get; set; }
        public ICollection<Playlist> Playlists { get; set; }
    }
}
