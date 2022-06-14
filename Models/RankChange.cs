namespace BeatLeader_Server.Models
{
    public class RankChange
    {
        public int Id { get; set; }

        public int Timeset { get; set; } = 0;
        public string PlayerId { get; set; }
        public string Hash { get; set; } = "";
        public string Diff { get; set; } = "";
        public string Mode { get; set; } = "";

        public float OldRankability { get; set; } = 0;
        public float OldStars { get; set; } = 0;
        public int OldType { get; set; } = 0;
        public float NewRankability { get; set; } = 0;
        public float NewStars { get; set; } = 0;
        public int NewType { get; set; } = 0;
    }
}
