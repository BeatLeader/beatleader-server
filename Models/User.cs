using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class User
    {
        public string Id { get; set; }
        public Player Player { get; set; }

        [ForeignKey("ClanRequestId")]
        public ICollection<Clan> ClanRequest { get; set; } = new List<Clan>();
        [ForeignKey("BannedClanId")]
        public ICollection<Clan> BannedClans { get; set; } = new List<Clan>();
        public ICollection<Playlist> Playlists { get; set; }
    }

    public class UserReturn
    {
        public Player Player { get; set; }
        public ICollection<Clan> ClanRequest { get; set; } = new List<Clan>();
        public ICollection<Clan> BannedClans { get; set; } = new List<Clan>();
        public ICollection<Playlist> Playlists { get; set; }
        public ICollection<string> Friends { get; set; }

        public bool Migrated { get; set; }
    }
}
