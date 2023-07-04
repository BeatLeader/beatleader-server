namespace BeatLeader_Server.Models
{
    public class LeaderboardChange
    {
        public int Id { get; set; }

        public int Timeset { get; set; } = 0;
        public string PlayerId { get; set; }

        public float OldRankability { get; set; } = 0;
        public float OldStars { get; set; } = 0;

        public float OldAccRating { get; set; } = 0;
        public float OldPassRating { get; set; } = 0;
        public float OldTechRating { get; set; } = 0;
        public int OldType { get; set; } = 0;
        public int OldCriteriaMet { get; set; } = 0;
        public ModifiersMap? OldModifiers { get; set; }
        public ModifiersRating? OldModifiersRating { get; set; }


        public float NewRankability { get; set; } = 0;
        public float NewStars { get; set; } = 0;
        public float NewAccRating { get; set; } = 0;
        public float NewPassRating { get; set; } = 0;
        public float NewTechRating { get; set; } = 0;
        public int NewType { get; set; } = 0;
        public int NewCriteriaMet { get; set; } = 0;
        public ModifiersMap? NewModifiers { get; set; }
        public ModifiersRating? NewModifiersRating { get; set; }
    }

    public class QualificationChange
    {
        public int Id { get; set; }

        public int Timeset { get; set; } = 0;
        public string PlayerId { get; set; }

        public float OldRankability { get; set; } = 0;
        public float OldStars { get; set; } = 0;
        public float OldAccRating { get; set; } = 0;
        public float OldPassRating { get; set; } = 0;
        public float OldTechRating { get; set; } = 0;
        public int OldType { get; set; } = 0;
        public int OldCriteriaMet { get; set; } = 0;
        public string? OldCriteriaCommentary { get; set; }
        public ModifiersMap? OldModifiers { get; set; }

        public float NewRankability { get; set; } = 0;
        public float NewStars { get; set; } = 0;
        public float NewAccRating { get; set; } = 0;
        public float NewPassRating { get; set; } = 0;
        public float NewTechRating { get; set; } = 0;
        public int NewType { get; set; } = 0;
        public int NewCriteriaMet { get; set; } = 0;
        public string? NewCriteriaCommentary { get; set; }
        public ModifiersMap? NewModifiers { get; set; }
    }
}
