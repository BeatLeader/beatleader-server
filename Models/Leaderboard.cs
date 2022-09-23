namespace BeatLeader_Server.Models
{
    public enum LeaderboardType
    {
        standard = 1,
        nopause = 2,
        nomodifiers = 3,
    }
    public class Leaderboard
    {
        public string Id { get; set; }
        //public LeaderboardType LeaderboardType { get; set; }
        public string? SongId { get; set; }
        public Song Song { get; set; }
        public DifficultyDescription Difficulty { get; set; }
        public ICollection<Score> Scores { get; set; }
        public RankQualification? Qualification { get; set; }
        public RankUpdate? Reweight { get; set; }

        public ICollection<LeaderboardChange>? Changes { get; set; }

        public ICollection<EventRanking>? Events { get; set; }
        public int Plays { get; set; }
    }
}
