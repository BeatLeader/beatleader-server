using System;
namespace BeatLeader_Server.Models
{
    public class PlayerScoreStats
    {
        public int Id { get; set; }

        public long TotalScore { get; set; }
        public long TotalUnrankedScore { get; set; }
        public long TotalRankedScore { get; set; }

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
        public float TopPassPP { get; set; }
        public float TopAccPP { get; set; }
        public float TopTechPP { get; set; }

        public float PeakRank { get; set; }
        public int RankedMaxStreak { get; set; }
        public int UnrankedMaxStreak { get; set; }
        public int MaxStreak { get; set; }
        public float AverageLeftTiming { get; set; }
        public float AverageRightTiming { get; set; }

        public int RankedPlayCount { get; set; }
        public int UnrankedPlayCount { get; set; }
        public int TotalPlayCount { get; set; }

        public int RankedImprovementsCount { get; set; }
        public int UnrankedImprovementsCount { get; set; }
        public int TotalImprovementsCount { get; set; }

        public int RankedTop1Count { get; set; }
        public int UnrankedTop1Count { get; set; }
        public int Top1Count { get; set; }

        public int RankedTop1Score { get; set; }
        public int UnrankedTop1Score { get; set; }
        public int Top1Score { get; set; }

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

    public class PlayerScoreStatsHistory
    {
        public int Id { get; set; }
        public LeaderboardContexts Context { get; set; }
        public int Timestamp { get; set; }

        public string? PlayerId { get; set; }

        public float Pp { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }

        public long TotalScore { get; set; }
        public long TotalUnrankedScore { get; set; }
        public long TotalRankedScore { get; set; }

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
        public int MaxStreak { get; set; }
        public float AverageLeftTiming { get; set; }
        public float AverageRightTiming { get; set; }

        public int RankedPlayCount { get; set; }
        public int UnrankedPlayCount { get; set; }
        public int TotalPlayCount { get; set; }

        public int RankedImprovementsCount { get; set; }
        public int UnrankedImprovementsCount { get; set; }
        public int TotalImprovementsCount { get; set; }

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
        public HMD TopHMD { get; set; }

        public int DailyImprovements { get; set; }
        public int ReplaysWatched { get; set; }
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

