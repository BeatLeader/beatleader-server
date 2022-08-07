namespace BeatLeader_Server.Models
{
    public class ScoreRedirect
    {
        public int Id { get; set; }

        public int OldScoreId { get; set; }
        public int NewScoreId { get; set; }
    }
}
