namespace BeatLeader_Server.Models {
    public enum EndType {
        Unknown = 0,
        Clear = 1,
        Fail = 2,
        Restart = 3,
        Quit = 4
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
    }

    public class Leaderboard {
        public string Id { get; set; }
        public string? SongId { get; set; }
        public Song Song { get; set; }
        public DifficultyDescription Difficulty { get; set; }
        public ICollection<Score> Scores { get; set; }
        public RankQualification? Qualification { get; set; }
        public RankUpdate? Reweight { get; set; }

        public long Timestamp { get; set; }

        public LeaderboardGroup? LeaderboardGroup { get; set; }
        public ICollection<LeaderboardChange>? Changes { get; set; }
        public ICollection<PlayerLeaderboardStats>? PlayerStats { get; set; }

        public ICollection<EventRanking>? Events { get; set; }
        //public ICollection<AltBoard>? AltBoards { get; set; }
        public int Plays { get; set; }
        public int PlayCount { get; set; }

        public int PositiveVotes { get; set; }
        public int StarVotes { get; set; }
        public int NegativeVotes { get; set; }
        public float VoteStars { get; set; }

        public void HideRatings() {
            this.Difficulty.AccRating = null;
            this.Difficulty.TechRating = null;
            this.Difficulty.PassRating = null;
            this.Difficulty.Stars = null;

            this.Difficulty.ModifiersRating = null;
        }
    }

    public enum LeaderboardType {
        standard = 1,
        nomodifiers = 2,
        nopause = 3,
        golf = 4,
        precision = 5
    }

    public class AltBoard {
        public int Id { get; set; }
        public LeaderboardType LeaderboardType { get; set; } = LeaderboardType.standard;
        public ICollection<AltScore> Scores { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public int Plays { get; set; }
    }

    public class LeaderboardGroup {
        public int Id { get; set; }
        public ICollection<Leaderboard> Leaderboards { get; set; }
    }
}
