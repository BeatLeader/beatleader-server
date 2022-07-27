using System.ComponentModel.DataAnnotations;

namespace BeatLeader_Server.Models
{
    public class RankVoting
    {
        [Key]
        public int ScoreId { get; set; }
        public string PlayerId { get; set; }
        public string Hash { get; set; } = "";
        public string Diff { get; set; } = "";
        public string Mode { get; set; } = "";
        public float Rankability { get; set; } = 0;
        public float Stars { get; set; } = 0;
        public int Type { get; set; } = 0;
        public int Timeset { get; set; } = 0;

        public ICollection<VoterFeedback>? Feedbacks { get; set; }
    }
}
