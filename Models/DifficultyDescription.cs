namespace BeatLeader_Server.Models
{
    public enum DifficultyStatus
    {
        unranked = 0,
        nominated = 1,
        qualified = 2,
        ranked = 3,
        unrankable = 4,
        outdated = 5,
    }

    public class DifficultyDescription
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public int Mode { get; set; }
        public string DifficultyName { get; set; }
        public string ModeName { get; set; }
        public DifficultyStatus Status { get; set; }

        public bool Nominated { get; set; }
        public int NominatedTime { get; set; }

        public bool Qualified { get; set; }
        public int QualifiedTime { get; set; }

        public bool Ranked { get; set; }
        public int RankedTime { get; set; }

        public float? Stars { get; set; }
        public int Type { get; set; }

        public float Njs { get; set; }
        public float Nps { get; set; }
        public int Notes { get; set; }
        public int Bombs { get; set; }
        public int Walls { get; set; }
        public int MaxScore { get; set; }
    }
}
