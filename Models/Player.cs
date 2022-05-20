namespace BeatLeader_Server.Models
{
    public class Player
    {
        public string Id { get; set; }
        public string Name { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Avatar { get; set; } = "";
        public string Country { get; set; } = "not set";
        public string Histories { get; set; } = "";

        public string Role { get; set; } = "";

        public float Pp { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }

        public bool Banned { get; set; }
        public bool Inactive { get; set; }

        public string ExternalProfileUrl { get; set; } = "";
        public float LastTwoWeeksTime { get; set; }
        public float AllTime { get; set; }

        public PlayerScoreStats ScoreStats { get; set; } = new PlayerScoreStats();
        public PlayerStatsHistory? StatsHistory { get; set; }
        public ICollection<Clan> Clans { get; set; } = new List<Clan>();
        public ICollection<PlayerFriends> Friends { get; set; } = new List<PlayerFriends>();

        public ICollection<Badge>? Badges { get; set; }

        public PatreonFeatures? PatreonFeatures { get; set; }

        public void SetDefaultAvatar()
        {
            this.Avatar = "https://cdn.beatleader.xyz/assets/" + this.Platform + "avatar.png";
        }
    }
}
