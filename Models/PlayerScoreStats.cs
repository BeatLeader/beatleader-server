using System;
namespace BeatLeader_Server.Models
{
    public class PlayerScoreStats
    {
        public int Id { get; set; }
        public int TotalScore { get; set; }
        public float AverageRankedAccuracy { get; set; }
        public float AverageAccuracy { get; set; }
        public int TotalPlayCount { get; set; }
        public int RankedPlayCount { get; set; }
        public int ReplaysWatched { get; set; }
    }
}

