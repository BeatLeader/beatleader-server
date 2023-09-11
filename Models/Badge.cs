namespace BeatLeader_Server.Models
{
    public class Badge
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public string Image { get; set; }
        public string? Link { get; set; }
        public int Timeset { get; set; }
        public bool Hidden { get; set; }
    }
}
