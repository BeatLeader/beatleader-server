using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class ClanRanking
    {
        public int Id { get; set; } // Unique ID for this clanRanking
        public Clan Clan { get; set; } // A clan that has at least one score on this leaderboard
        public string LastUpdateTime { get; set; } // The timestamp of the most recently submitted scores associated with this clan.
        public int ClanRank { get; set; } // This was intended to be used as an index into the list in leaderboard, but this is can be removed I think, because I just sort the leaderboard clanRanking list by clanPP. Whoops.
        public float ClanAverageRank { get; set; } // Average rank of scores from this clan on this leaderboard
        public float ClanPP { get; set; } // sum pp of all scores from this clan on this leaderboard, weighted.
        public float ClanAverageAccuracy { get; set; } // Average accuracy of scores from this clan on this leaderboard.
        public float ClanTotalScore { get; set; } // Total score of scores from this clan on this leaderboard.
        public string LeaderboardId { get; set; } // ID of the leaderboard. (Maybe this or the Leaderboard could be removed?)
        public Leaderboard Leaderboard { get; set; } // Leaderboard associated with this clanRanking.
        public ICollection<Score> AssociatedScores { get; set; } // A list of scores associated with this clan
    }
}