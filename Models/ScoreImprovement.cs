namespace BeatLeader_Server.Models
{
    public class ScoreImprovement
    {
        public int Id { get; set; }
        public int Score { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public int Rank { get; set; }
        public float AccRight { get; set; }
        public float AccLeft { get; set; }

        public float AverageRankedAccuracy { get; set; }
        public float TotalPp { get; set; }
        public int TotalRank { get; set; }
    }
        
}
