namespace BeatLeader_Server.Models
{
    public class LeaderboardChange
    {
        public int Id { get; set; }

        public int Timeset { get; set; } = 0;
        public string PlayerId { get; set; }

        public float OldRankability { get; set; } = 0;
        public float OldStars { get; set; } = 0;
        public int OldType { get; set; } = 0;
        public int OldCriteriaMet { get; set; } = 0;
        public ModifiersMap? OldModifiers { get; set; }


        public float NewRankability { get; set; } = 0;
        public float NewStars { get; set; } = 0;
        public int NewType { get; set; } = 0;
        public int NewCriteriaMet { get; set; } = 0;
        public ModifiersMap? NewModifiers { get; set; }
    }

    public class QualificationChange
    {
        public int Id { get; set; }

        public int Timeset { get; set; } = 0;
        public string PlayerId { get; set; }

        public float OldRankability { get; set; } = 0;
        public float OldStars { get; set; } = 0;
        public int OldType { get; set; } = 0;
        public int OldCriteriaMet { get; set; } = 0;
        public string? OldCriteriaCommentary { get; set; }

        public float NewRankability { get; set; } = 0;
        public float NewStars { get; set; } = 0;
        public int NewType { get; set; } = 0;
        public int NewCriteriaMet { get; set; } = 0;
        public string? NewCriteriaCommentary { get; set; }
    }
}
