namespace BeatLeader_Server.Enums;

public enum MapSortBy
{
    None,
    Timestamp,
    Name,
    Stars,
    PassRating,
    AccRating,
    TechRating,
    ScoreTime,
    PlayCount,
    Voting,
    VoteCount,
    VoteRatio,
    Duration,
    Attempts,
    NJS,
    NPS,
    BPM
}

public enum ScoresSortBy {
    Date = 0,
    Pp = 1,
    AccPP = 2,
    PassPP = 3,
    TechPP = 4,
    Acc = 5,
    Pauses = 6,
    PlayCount = 7,
    LastTryTime = 8,
    Rank = 9,
    MaxStreak = 10,
    Timing = 11,
    Stars = 12,
    Mistakes = 13,
    ReplaysWatched = 14
}

public enum PlayerSortBy {
    Pp,
    TopPp,
    Name,
    Rank,
    Acc,
    WeightedAcc,
    Top1Count,
    Top1Score,
    WeightedRank,
    TopAcc,
    Hmd,
    PlayCount,
    Score,
    Lastplay,
    MaxStreak,
    ReplaysWatched,
    DailyImprovements,
    Timing,
    AllContextsPp,
    Level,
    Prestige,
    Experience,
}

public enum ClanSortBy {
    Name,
    Pp,
    Acc,
    Rank,
    Count,
    Captures
}

public enum MapperSortBy {
    Name,
    Maps,
    RankedMaps
}

public enum PlayedStatus {
    Any,
    Played,
    Unplayed
}

public enum ClanMapsSortBy {
    Pp,
    Acc,
    Rank,
    Date,
    Tohold,
    Toconquer
}

public enum LeaderboardSortBy {
    Date,
    Pp,   
    Acc,
    Pauses,
    Rank,
    MaxStreak,
    Mistakes,
    Weight,
    WeightedPp
}