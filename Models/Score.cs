using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using ReplayDecoder;

namespace BeatLeader_Server.Models
{
    [Flags]
    public enum LeaderboardContexts
    {
        None = 0,
        General = 1 << 1,
        NoMods = 1 << 2,
        NoPause = 1 << 3,
        Golf = 1 << 4,
        SCPM = 1 << 5
    }

    public static class ContextExtensions {
        public static List<LeaderboardContexts> All =  new List<LeaderboardContexts> { 
            LeaderboardContexts.General,
            LeaderboardContexts.NoMods,
            LeaderboardContexts.NoPause,
            LeaderboardContexts.Golf,
            LeaderboardContexts.SCPM
        };

        public static List<LeaderboardContexts> NonGeneral = new List<LeaderboardContexts> { 
            LeaderboardContexts.NoMods,
            LeaderboardContexts.NoPause,
            LeaderboardContexts.Golf,
            LeaderboardContexts.SCPM
        };
    }

    public class ScoreMetadata 
    {
        public int Id { get; set; }
        public LeaderboardContexts PinnedContexts { get; set; }
        public int Priority { get; set; }
        public string? Description { get; set; }

        public string? LinkService { get; set; }
        public string? LinkServiceIcon { get; set; }
        public string? Link { get; set; }
    }

    [Index(nameof(PlayerId))]
    [Index(nameof(PlayerId), nameof(LeaderboardId), nameof(ValidContexts), IsUnique = true)]
    [Index(nameof(Banned), nameof(Qualification), nameof(Pp), IsUnique = false)]
    [Index(nameof(Timepost), nameof(Replay))]
    [Index(nameof(PlayerId), nameof(Banned), nameof(Qualification), nameof(Pp), IsUnique = false)]
    public class Score
    {
        [Key]
        public int Id { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public string PlayerId { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public float PassPP { get; set; }
        public float AccPP { get; set; }
        public float TechPP { get; set; }
        public bool Qualification { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public string? Replay { get; set; } = "";
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
        public LeaderboardContexts ValidContexts { get; set; }
        public ICollection<ScoreContextExtension> ContextExtensions { get; set; }
        public string LeaderboardId { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public int AuthorizedReplayWatched { get; set; }
        public int AnonimusReplayWatched { get; set; }
        public int? ReplayOffsetsId { get; set; }
        public ReplayOffsets? ReplayOffsets { get; set; }
        public string? Country { get; set; }
        public int? MaxStreak { get; set; } = null;
        public int PlayCount { get; set; } = 1;
        public float LeftTiming { get; set; }
        public float RightTiming { get; set; }
        public int Priority { get; set; } = 0;
        public int? ScoreImprovementId { get; set; }
        public ScoreImprovement? ScoreImprovement { get; set; }
        public bool Banned { get; set; } = false;
        public bool Suspicious { get; set; } = false;
        public bool Bot { get; set; } = false;
        public bool IgnoreForStats { get; set; } = false;
        public bool Migrated { get; set; } = false;
        public RankVoting? RankVoting { get; set; }
        public ScoreMetadata? Metadata { get; set; }

        public void ToContext(ScoreContextExtension? extension) {
            if (extension == null) return;

            Weight = extension.Weight;
            Rank = extension.Rank;
            BaseScore = extension.BaseScore;
            ModifiedScore = extension.ModifiedScore;
            Accuracy = extension.Accuracy;
            Pp = extension.Pp;
            AccPP = extension.AccPP;
            TechPP = extension.TechPP;
            PassPP = extension.PassPP;
            BonusPp = extension.BonusPp;
            Modifiers = extension.Modifiers;
        }
    }

    public class ReplayWatchingSession {
        public int Id { get; set; }
        public int ScoreId { get; set; }
        public string? IP { get; set; }
        public string? Player { get; set; }
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
        public bool FalsePositive { get; set; }
    }
}
