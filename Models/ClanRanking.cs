using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class ClanRanking
    {
        public int Id { get; set; } // Unique ID for this clanRanking
        public Clan Clan { get; set; } // A clan that has at least one score on this leaderboard
        public int LastUpdateTime { get; set; } // The timestamp of the most recently submitted scores associated with this clan.
        public float AverageRank { get; set; } // Average rank of scores from this clan on this leaderboard
        public float Pp { get; set; } // sum pp of all scores from this clan on this leaderboard, weighted.
        public float AverageAccuracy { get; set; } // Average accuracy of scores from this clan on this leaderboard.
        public float TotalScore { get; set; } // Total score of scores from this clan on this leaderboard.
        public string LeaderboardId { get; set; } // ID of the leaderboard, useful for quick filtering.
        public Leaderboard Leaderboard { get; set; } // Leaderboard associated with this clanRanking.
        public ICollection<Score> AssociatedScores { get; set; } // A list of scores associated with this clan
    }

    //public class ClanRankingScore
    //{
    //    public int Id { get; set; } // Unique ID for this clanRankingScore
    //    public Score AssociatedScore { get; set; } // The score associated with this clanRankingScore
    //    public int Rank { get; set; } // The rank of this score in the list of associated scores for a clan Ranking
    //    public float Weight { get; set; } // The weight applied to the PP of this score when calculating ClanRanking PP.
    //}
}