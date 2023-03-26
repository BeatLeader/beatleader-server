namespace BeatLeader_Server.Models
{
    public class QualificationCommentary 
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public int Timeset { get; set; }
        public string Value { get; set; }

        public int? EditTimeset { get; set; }
        public bool Edited { get; set; }

        public int? RankQualificationId { get; set; }
        public RankQualification? RankQualification { get; set; }

        public string DiscordMessageId { get; set; } = "";
    }

    public class CriteriaCommentary 
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public int Timeset { get; set; }
        public string Value { get; set; }

        public int? EditTimeset { get; set; }
        public bool Edited { get; set; }

        public int? RankQualificationId { get; set; }
        public RankQualification? RankQualification { get; set; }

        public string DiscordMessageId { get; set; } = "";
    }

    public enum MapQuality
    {
        Good = 1,
        Ok = 2,
        Bad = 3
    }

    public class QualificationVote 
    {
        public int Id { get; set; }
        public string PlayerId { get; set; }
        public int Timeset { get; set; }
        public MapQuality Value { get; set; }

        public int? EditTimeset { get; set; }
        public bool Edited { get; set; }

        public int? RankQualificationId { get; set; }
        public RankQualification? RankQualification { get; set; }

        public string? DiscordRTMessageId { get; set; }
    }

    public class RankQualification
    {
        public int Id { get; set; }
        public int Timeset { get; set; }
        public string RTMember { get; set; }

        public int CriteriaTimeset { get; set; }
        public int CriteriaMet { get; set; }
        public string? CriteriaChecker { get; set; }
        public string? CriteriaCommentary { get; set; }

        public bool MapperAllowed { get; set; }
        public string? MapperId { get; set; }

        public bool MapperQualification { get; set; }

        public int ApprovalTimeset { get; set; }
        public bool Approved { get; set; }
        public string? Approvers { get; set; }

        public string? CriteriaCheck { get; set; }

        public ModifiersMap? Modifiers { get; set; }
        public ModifiersRating? ModifiersRating { get; set; }

        public ICollection<QualificationChange>? Changes { get; set; }
        public ICollection<QualificationCommentary>? Comments { get; set; }
        public ICollection<CriteriaCommentary>? CriteriaComments { get; set; }

        public int QualityVote { get; set; }
        public ICollection<QualificationVote>? Votes { get; set; }

        public string DiscordChannelId { get; set; } = "";
        public string DiscordRTChannelId { get; set; } = "";
    }
}
