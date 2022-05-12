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

    public class ClanReturn
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public string Icon { get; set; }
        public string Tag { get; set; }
        public string LeaderID { get; set; }

        public int PlayersCount { get; set; }
        public float Pp { get; set; }
        public float AverageRank { get; set; }
        public float AverageAccuracy { get; set; }

        public ICollection<string> Players { get; set; } = new List<string>();
        public ICollection<string> PendingInvites { get; set; } = new List<string>();
    }

    public class UserReturn
    {
        public Player Player { get; set; }
        
        public ClanReturn? Clan { get; set; }
        public ICollection<Clan> ClanRequest { get; set; } = new List<Clan>();
        public ICollection<Clan> BannedClans { get; set; } = new List<Clan>();
        public ICollection<Playlist>? Playlists { get; set; }
        public ICollection<string>? Friends { get; set; }

        public bool Migrated { get; set; }
        public bool Patreoned { get; set; }
    }
}
