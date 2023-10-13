using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Models {

    [Index(nameof(PlayerId), nameof(LeaderboardId), nameof(Context), IsUnique = true)]
    public class ScoreContextExtension
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public Player Player { get; set; }
        public string LeaderboardId { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public float PassPP { get; set; }
        public float AccPP { get; set; }
        public float TechPP { get; set; }
        public float BonusPp { get; set; }
        public string? Modifiers { get; set; }
        
        public int Timeset { get; set; }
        public int Priority { get; set; }

        public int? ScoreId { get; set; }
        public Score? Score { get; set; }
        public bool Qualification { get; set; }
        public bool Banned { get; set; }

        public LeaderboardContexts Context { get; set; }
        public ScoreImprovement? ScoreImprovement { get; set; }
    }
}
