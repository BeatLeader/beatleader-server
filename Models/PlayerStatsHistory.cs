namespace BeatLeader_Server.Models
{
    public class PlayerStatsHistory
    {
        public int Id { get; set; }
        public string Pp { get; set; } = "";
        public string Rank { get; set; } = "";
        public string CountryRank { get; set; } = "";
        public string TotalScore { get; set; } = "";
        public string AverageRankedAccuracy { get; set; } = "";
        public string AverageWeightedRankedAccuracy { get; set; } = "";
        public string AverageWeightedRankedRank { get; set; } = "";
        public string TopAccuracy { get; set; } = "";
        public string TopPp { get; set; } = "";
        public string AverageAccuracy { get; set; } = "";
        public string MedianAccuracy { get; set; } = "";
        public string MedianRankedAccuracy { get; set; } = "";
        public string TotalPlayCount { get; set; } = "";
        public string RankedPlayCount { get; set; } = "";
        public string ReplaysWatched { get; set; } = "";
    }
}
