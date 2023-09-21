using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models {
    [Index(nameof(PlayerId), nameof(Context), IsUnique = true)]
    public class PlayerContextExtension : IPlayer {
        public int Id { get; set; }
        public LeaderboardContexts Context { get; set; }
        public float Pp { get; set; }
        public float AccPp { get; set; }
        public float TechPp { get; set; }
        public float PassPp { get; set; }

        public int Rank { get; set; }
        public string Country { get; set; }
        public int CountryRank { get; set; }

        public float LastWeekPp { get; set; }
        public int LastWeekRank { get; set; }
        public int LastWeekCountryRank { get; set; }

        public string PlayerId { get; set; }
        public Player Player { get; set; }
        public PlayerScoreStats? ScoreStats { get; set; }
        public bool Banned { get; set; }

        [NotMapped]
        [JsonIgnore]
        public string Name { get => Player != null ? Player.Name : ""; set => Player.Name = value; }
    }
}
