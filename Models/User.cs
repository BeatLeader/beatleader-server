using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class User
    {
        public string Id { get; set; }
        public Player Player { get; set; }

       
        public ICollection<Clan> ClanRequest { get; set; } = new List<Clan>();
        public ICollection<Clan> BannedClans { get; set; } = new List<Clan>();
        public ICollection<Playlist> Playlists { get; set; }
    }

    public class OculusUser {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Avatar { get; set; }

        public bool Migrated { get; set; }
        public string? MigratedId { get; set; }
    }
}
