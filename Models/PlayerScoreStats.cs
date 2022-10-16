using System;
namespace BeatLeader_Server.Models
{
    public class PlayerScoreStats
    {
        public int Id { get; set; }

        public int TotalScore { get; set; }
        public int TotalUnrankedScore { get; set; }
        public int TotalRankedScore { get; set; }

        public int LastScoreTime { get; set; }
        public int LastUnrankedScoreTime { get; set; }
        public int LastRankedScoreTime { get; set; }

        public float AverageRankedAccuracy { get; set; }
        public float AverageWeightedRankedAccuracy { get; set; }
        public float AverageUnrankedAccuracy { get; set; }
        public float AverageAccuracy { get; set; }

        public float MedianRankedAccuracy { get; set; }
        public float MedianAccuracy { get; set; }

        public float TopRankedAccuracy { get; set; }
        public float TopUnrankedAccuracy { get; set; }
        public float TopAccuracy { get; set; }

        public float TopPp { get; set; }
        public float TopBonusPP { get; set; }

        public float PeakRank { get; set; }

        public int RankedPlayCount { get; set; }
        public int UnrankedPlayCount { get; set; }
        public int TotalPlayCount { get; set; }

        public float AverageRankedRank { get; set; }
        public float AverageWeightedRankedRank { get; set; }
        public float AverageUnrankedRank { get; set; }
        public float AverageRank { get; set; }

        public int SSPPlays { get; set; }
        public int SSPlays { get; set; }
        public int SPPlays { get; set; }
        public int SPlays { get; set; }
        public int APlays { get; set; }

        public string TopPlatform { get; set; } = "";
        public HMD TopHMD { get ; set; }

        public int DailyImprovements { get; set; }
        public int AuthorizedReplayWatched { get; set; }
        public int AnonimusReplayWatched { get; set; }
        public int WatchedReplays { get; set; }
    }

    public class PlayerVoteStats
    {
        public int Id { get; set; }
    }

    public class PlayerRankStats
    {
        public int Id { get; set; }
    }
}

