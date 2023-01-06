using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public enum ScoreStatus
    {
        normal = 0,
        pinned = 1,
    }

    public class ScoreMetadata 
    {
        public int Id { get; set; }
        public ScoreStatus Status { get; set; }
        public int Priority { get; set; }
        public string? Description { get; set; }

        public string? LinkService { get; set; }
        public string? LinkServiceIcon { get; set; }
        public string? Link { get; set; }
    }

    public class ReplayOffsets
    {
        public int Id { get; set; }
        public int Frames { get; set; }
        public int Notes { get; set; }
        public int Walls { get; set; }
        public int Heights { get; set; }
        public int Pauses { get; set; }
    }

    [Index(nameof(PlayerId))]
    [Index(nameof(PlayerId), nameof(LeaderboardId), IsUnique = true)]
    public class Score {
        [Key]
        public int Id { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public string PlayerId { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public bool Qualification { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public string? Replay { get; set; }
        public string? Modifiers { get; set; }
        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int BombCuts { get; set; }
        public int WallsHit { get; set; }
        public int Pauses { get; set; }
        public bool FullCombo { get; set; }
        public int MaxCombo { get; set; }
        public float FcAccuracy { get; set; }
        public float FcPp { get; set; }
        public HMD Hmd { get; set; }
        public ControllerEnum Controller { get; set; }
        public float AccRight { get; set; }
        public float AccLeft { get; set; }
        public string? Timeset { get; set; }
        public int Timepost { get; set; }
        public string Platform { get; set; } = "";
        public Player Player { get; set; }
        public string LeaderboardId { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public int AuthorizedReplayWatched { get; set; }
        public int AnonimusReplayWatched { get; set; }
        public ReplayOffsets? ReplayOffsets { get; set; }
        public string? Country { get; set; }
        public int MaxStreak { get; set; }
        public float LeftTiming { get; set; }
        public float RightTiming { get; set; }

        public ScoreImprovement? ScoreImprovement { get; set; }
        public bool Banned { get; set; }
        public bool Suspicious { get; set; }
        public bool IgnoreForStats { get; set; }
        public bool Migrated { get; set; }
        public RankVoting? RankVoting { get; set; }
        public ScoreMetadata? Metadata { get; set; }

        public SecondaryScore ToSecondary() {
            return new SecondaryScore {
                 Id = Id,
                 BaseScore = BaseScore,
                 ModifiedScore = ModifiedScore,
                 Accuracy = Accuracy,
                 PlayerId = PlayerId,
                 Pp = Pp,
                 BonusPp = BonusPp,
                 Qualification = Qualification,
                 Weight = Weight,
                 Rank = Rank,
                 CountryRank = CountryRank,
                 Replay = Replay,
                 Modifiers = Modifiers,
                 BadCuts = BadCuts,
                 MissedNotes = MissedNotes,
                 BombCuts = BombCuts,
                 WallsHit = WallsHit,
                 Pauses = Pauses,
                 FullCombo = FullCombo,
                 MaxCombo = MaxCombo,
                 FcAccuracy = FcAccuracy,
                 FcPp = FcPp,
                 Hmd = Hmd,
                 Controller = Controller,
                 AccRight = AccRight,
                 AccLeft = AccLeft,
                 Timeset = Timeset,
                 Timepost = Timepost,
                 Platform = Platform,
                 Player = Player,
                 LeaderboardId = LeaderboardId,
                 Leaderboard = Leaderboard,
                 AuthorizedReplayWatched = AuthorizedReplayWatched,
                 AnonimusReplayWatched = AnonimusReplayWatched,
                 ReplayOffsets = ReplayOffsets,
                 Country = Country,
                 MaxStreak = MaxStreak,
                 LeftTiming = LeftTiming,
                 RightTiming = RightTiming,
                 ScoreImprovement = ScoreImprovement,
                 Banned = Banned,
                 Suspicious = Suspicious,
                 IgnoreForStats = IgnoreForStats,
                 Migrated = Migrated,
                 RankVoting = RankVoting,
                 Metadata = Metadata,
            };
        }
    }

    public class SecondaryScore
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Key]
        public int Id { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public string? PlayerId { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public bool Qualification { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public string? Replay { get; set; }
        public string? Modifiers { get; set; }
        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int BombCuts { get; set; }
        public int WallsHit { get; set; }
        public int Pauses { get; set; }
        public bool FullCombo { get; set; }
        public int MaxCombo { get; set; }
        public float FcAccuracy { get; set; }
        public float FcPp { get; set; }
        public HMD Hmd { get; set; }
        public ControllerEnum Controller { get; set; }
        public float AccRight { get; set; }
        public float AccLeft { get; set; }
        public string? Timeset { get; set; }
        public int Timepost { get; set; }
        public string Platform { get; set; } = "";
        public Player? Player { get; set; }
        public string? LeaderboardId { get; set; }
        public Leaderboard? Leaderboard { get; set; }
        public int AuthorizedReplayWatched { get; set; }
        public int AnonimusReplayWatched { get; set; }
        public ICollection<AltScore>? AltScores { get; set; }
        public ReplayOffsets? ReplayOffsets { get; set; }
        public string? Country { get; set; }
        public int MaxStreak { get; set; }
        public float LeftTiming { get; set; }
        public float RightTiming { get; set; }
        public ScoreImprovement? ScoreImprovement { get; set; }
        public bool Banned { get; set; }
        public bool Suspicious { get; set; }
        public bool IgnoreForStats { get; set; }
        public bool Migrated { get; set; }
        public RankVoting? RankVoting { get; set; }
        public ScoreMetadata? Metadata { get; set; }
    }

    public class ReplayWatchingSession {
        public int Id { get; set; }
        public int ScoreId { get; set; }
        public string? IP { get; set; }
        public string? Player { get; set; }
    }

    [Index(nameof(PlayerId))]
    [Index(nameof(PlayerId), nameof(LeaderboardId), IsUnique = false)]
    public class AltScore 
    {
        [Key]
        public int Id { get; set; }
        public int? ScoreId { get; set; }
        public SecondaryScore? Score { get; set; }

        public ScoreImprovement? ScoreImprovement { get; set; }
        public float Weight { get; set; }
        public string Modifiers { get; set; } = "";
        public int Rank { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public string? Timeset { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public string? LeaderboardId { get; set; }
        public Player? Player { get; set; }
        public string? PlayerId { get; set; }

        public AltPlayer? AltPlayer { get; set; }
        public int? AltPlayerId { get; set; }
        public int AltBoardId { get; set; }
        public AltBoard AltBoard { get; set; }

        public LeaderboardType LeaderboardType { get; set; } = LeaderboardType.standard;
    }

    public class FailedScore
    {
        public int Id { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public string PlayerId { get; set; }
        public float Pp { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public string Replay { get; set; }
        public string Modifiers { get; set; }
        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int BombCuts { get; set; }
        public int WallsHit { get; set; }
        public int Pauses { get; set; }
        public bool FullCombo { get; set; }
        public HMD Hmd { get; set; }
        public string Timeset { get; set; }
        public Player Player { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public string Error { get; set; }
    }
}
