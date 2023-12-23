using ReplayDecoder;

namespace BeatLeader_Server.Models {
    public enum EndType {
        Unknown = 0,
        Clear = 1,
        Fail = 2,
        Restart = 3,
        Quit = 4,
        Practice = 5
    }

    public class PlayerLeaderboardStats {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public EndType Type { get; set; }
        public int Timeset { get; set; }
        public float Time { get; set; }
        public int Score { get; set; }
        public string? Replay { get; set; }

        public string? LeaderboardId { get; set; }
        public Leaderboard? Leaderboard { get; set; }

        public int? ScoreId { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public float PassPP { get; set; }
        public float AccPP { get; set; }
        public float TechPP { get; set; }
        public bool Qualification { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
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
        public int Timepost { get; set; }
        public string Platform { get; set; } = "";
        public int AuthorizedReplayWatched { get; set; }
        public int AnonimusReplayWatched { get; set; }
        public int? ReplayOffsetsId { get; set; }
        public ReplayOffsets? ReplayOffsets { get; set; }
        public string? Country { get; set; }
        public int? MaxStreak { get; set; } = null;
        public float LeftTiming { get; set; }
        public float RightTiming { get; set; }
        public int Priority { get; set; } = 0;

        public int? ScoreImprovementId { get; set; }
        public ScoreImprovement? ScoreImprovement { get; set; }

        public void FromScore(Score score) {
            ScoreId = score.Id;
            BaseScore = score.BaseScore;
            ModifiedScore = score.ModifiedScore;
            Accuracy = score.Accuracy;
            Pp = score.Pp;
            BonusPp = score.BonusPp;
            PassPP = score.PassPP;
            AccPP = score.AccPP;
            TechPP = score.TechPP;
            Qualification = score.Qualification;
            Weight = score.Weight;
            Rank = score.Rank;
            CountryRank = score.CountryRank;
            Modifiers = score.Modifiers;
            BadCuts = score.BadCuts;
            MissedNotes = score.MissedNotes;
            BombCuts = score.BombCuts;
            WallsHit = score.WallsHit;
            Pauses = score.Pauses;
            FullCombo = score.FullCombo;
            MaxCombo = score.MaxCombo;
            FcAccuracy = score.FcAccuracy;
            FcPp = score.FcPp;
            Hmd = score.Hmd;
            Controller = score.Controller;
            AccRight = score.AccRight;
            AccLeft = score.AccLeft;
            Timepost = score.Timepost;
            Platform = score.Platform;
            AuthorizedReplayWatched = score.AuthorizedReplayWatched;
            AnonimusReplayWatched = score.AnonimusReplayWatched;
            ReplayOffsetsId = score.ReplayOffsetsId;
            Country = score.Country;
            MaxStreak = score.MaxStreak;
            LeftTiming = score.LeftTiming;
            RightTiming = score.RightTiming;
            Priority = score.Priority;

            ScoreImprovementId = score.ScoreImprovementId;
        }
    }
}
