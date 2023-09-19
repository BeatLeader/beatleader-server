using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class ClanRanking
    {
        public int Id { get; set; } // Unique ID for this clanRanking

        public int? ClanId { get; set; }
        public Clan? Clan { get; set; } // A clan that has at least one score on this leaderboard
        public int LastUpdateTime { get; set; } // The timestamp of the most recently submitted scores associated with this clan.
        public float AverageRank { get; set; } // Average rank of scores from this clan on this leaderboard
        public float Pp { get; set; } // sum pp of all scores from this clan on this leaderboard, weighted.
        public float AverageAccuracy { get; set; } // Average accuracy of scores from this clan on this leaderboard.
        public float TotalScore { get; set; } // Total score of scores from this clan on this leaderboard.
        public string? LeaderboardId { get; set; } // ID of the leaderboard, useful for quick filtering.
        public Leaderboard? Leaderboard { get; set; } // Leaderboard associated with this clanRanking.
    }
}