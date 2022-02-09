namespace BeatLeader_Server.Models
{
    public class Song
    {
        public string Id { get; set; }
        public string Hash { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public string? SubName { get; set; }
        public string Author { get; set; }
        public string Mapper { get; set; }
        public string CoverImage { get; set; }
        public string DownloadUrl { get; set; }
        public double Bpm { get; set; }
        public double Duration { get; set; }
        public string? Tags { get; set; }
        public ICollection<DifficultyDescription> Difficulties { get; set; }
    }
}
