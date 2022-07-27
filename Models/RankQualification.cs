namespace BeatLeader_Server.Models
{
    public class RankQualification
    {
        public int Id { get; set; }
        public int Timeset { get; set; }

        public bool MapperAllowed { get; set; }

        public string RTMember { get; set; }
    }
}
