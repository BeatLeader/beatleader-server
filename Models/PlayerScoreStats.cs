using System;
namespace BeatLeader_Server.Models
{
    public class PlayerScoreStats
    {
        public int Id { get; set; }
        public int TotalScore { get; set; }

        public float AverageRankedAccuracy { get; set; }
        public float AverageAccuracy { get; set; }

        public float MedianRankedAccuracy { get; set; }
        public float MedianAccuracy { get; set; }

        public float TopAccuracy { get; set; }
        public float TopPp { get; set; }
        public int TotalPlayCount { get; set; }
        public int RankedPlayCount { get; set; }
        public int ReplaysWatched { get; set; }

        public int SSPPlays { get; set; }
        public int SSPlays { get; set; }
        public int SPPlays { get; set; }
        public int SPlays { get; set; }
        public int APlays { get; set; }

        public string TopPlatform { get; set; } = "";
        public int TopHMD { get ; set; }

        public int DailyImprovements { get; set; }
    }
}

