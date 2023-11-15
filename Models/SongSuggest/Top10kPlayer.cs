namespace BeatLeader_Server.Models.SongSuggest
{
    public class Top10kPlayer
    {
        public String id { get; set; }
        public String name { get; set; }
        public int rank { get; set; }

        public List<Top10kScore> top10kScore { get; set; }
    }
}
