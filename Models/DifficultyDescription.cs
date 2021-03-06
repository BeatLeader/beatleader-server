namespace BeatLeader_Server.Models
{
    public class DifficultyDescription
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public int Mode { get; set; }
        public string DifficultyName { get; set; }
        public string ModeName { get; set; }

        public bool Qualified { get; set; }
        public string QualifiedTime { get; set; } = "";

        public bool Ranked { get; set; }
        public string RankedTime { get; set; } = "";
        public float? Stars { get; set; }

        public float Njs { get; set; }
        public float Nps { get; set; }
        public int Notes { get; set; }
        public int Bombs { get; set; }
        public int Walls { get; set; }
        public int MaxScore { get; set; }

        public int Type { get; set; } = 0;
    }
}
