namespace BeatLeader_Server.Models
{
    public class ScoreImprovement
    {
        public int Id { get; set; }
        public string Timeset { get; set; } = "";
        public int Score { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public int Rank { get; set; }
        public float AccRight { get; set; }
        public float AccLeft { get; set; }

        public float AverageRankedAccuracy { get; set; }
        public float TotalPp { get; set; }
        public int TotalRank { get; set; }

        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int BombCuts { get; set; }
        public int WallsHit { get; set; }
        public int Pauses { get; set; }
    }
        
}
