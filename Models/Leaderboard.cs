namespace BeatLeader_Server.Models
{
    public class Leaderboard
    {
        public string Id { get; set; }
        public Song Song { get; set; }
        public DifficultyDescription Difficulty { get; set; }
        public ICollection<Score> Scores { get; set; }
        public int Plays { get; set; }
    }
}
