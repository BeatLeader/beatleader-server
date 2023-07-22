using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class ClanRanking
    {
        public int Id { get; set; }
        public Clan Clan { get; set; }
        public string LastUpdateTime { get; set; }
        public int ClanRank { get; set; }
        public float ClanAverageRank { get; set; }
        public float ClanPP { get; set; }
        public float ClanAverageAccuracy { get; set; }
        public float ClanTotalScore { get; set; }
        public string LeaderboardId { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public ICollection<Score> AssociatedScores { get; set; }
    }
}