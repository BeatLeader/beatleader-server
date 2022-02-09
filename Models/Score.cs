namespace BeatLeader_Server.Models
{
    public class Score
    {
        public int Id { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public string PlayerId { get; set; }
        public float Pp { get; set; }
        public float Weight { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public string Replay { get; set; }
        public string Modifiers { get; set; }
        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int BombCuts { get; set; }
        public int WallsHit { get; set; }
        public bool FullCombo { get; set; }
        public int Hmd { get; set; }
        public string Timeset { get; set; }
        public Player player { get; set; }

        public ReplayIdentification Identification { get; set; }
    }
}
