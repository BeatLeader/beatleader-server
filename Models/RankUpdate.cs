namespace BeatLeader_Server.Models
{
    public class RankUpdate
    {
        public int Id { get; set; }
        public int Timeset { get; set; }
        public string RTMember { get; set; }
        public bool Keep { get; set; }

        public float Stars { get; set; }
        public float PassRating { get; set; }
        public float TechRating { get; set; }
        public float PredictedAcc { get; set; }
        public int Type { get; set; }
        public int CriteriaMet { get; set; }
        public string? CriteriaCommentary { get; set; }
        public bool Finished { get; set; }

        public ModifiersMap Modifiers { get; set; }
        public ModifiersRating? ModifiersRating { get; set; }

        public ICollection<RankUpdateChange>? Changes { get; set; }
    }

    public class RankUpdateChange
    {
        public int Id { get; set; }

        public int Timeset { get; set; } = 0;
        public string PlayerId { get; set; }

        public bool OldKeep { get; set; }
        public float OldStars { get; set; } = 0;
        public int OldType { get; set; } = 0;
        public int OldCriteriaMet { get; set; } = 0;
        public string? OldCriteriaCommentary { get; set; }
        public ModifiersMap? OldModifiers { get; set; }

        public bool NewKeep { get; set; }
        public float NewStars { get; set; } = 0;
        public int NewType { get; set; } = 0;
        public int NewCriteriaMet { get; set; } = 0;
        public string? NewCriteriaCommentary { get; set; }
        public ModifiersMap? NewModifiers { get; set; }
    }
}
