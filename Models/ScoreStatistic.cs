using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class HitTracker
    {
        public int Id { get; set; }
        public int MaxCombo { get; set; }
        public int LeftMiss { get; set; }
        public int RightMiss { get; set; }
        public int LeftBadCuts { get; set; }
        public int RightBadCuts { get; set; }
        public int LeftBombs { get; set; }
        public int RightBombs { get; set; }
    }

    public class AccuracyTracker
    {
        public int Id { get; set; }
        public float AccRight { get; set; }
        public float AccLeft { get; set; }
        public float LeftPreswing { get; set; }
        public float RightPreswing { get; set; }
        public float AveragePreswing { get; set; }
        public float LeftPostswing { get; set; }
        public float RightPostswing { get; set; }
        public float LeftTimeDependence { get; set; }
        public float RightTimeDependence { get; set; }

        [NotMapped]
        public List<float> LeftAverageCut { get; set; }
        public string LeftAverageCutS { get; set; }

        [NotMapped]
        public List<float> RightAverageCut { get; set; }
        public string RightAverageCutS { get; set; }

        [NotMapped]
        public List<float> GridAcc { get; set; }
        public string GridAccS { get; set; }
    }

    public class WinTracker
    {
        public int Id { get; set; }
        public bool Won { get; set; }
        public float EndTime { get; set; }
        public int NbOfPause { get; set; }
        public float JumpDistance { get; set; }
        public int TotalScore { get; set; }
    }
    public class ScoreGraphTracker {
        public int Id { get; set; }

        [NotMapped]
        public List<float> Graph { get; set; }
        public string GraphS { get; set; }
    }

    public class ScoreStatistic
    {
        public int Id { get; set; }
        public int ScoreId { get; set; }

        public HitTracker HitTracker { get; set; }
        public AccuracyTracker AccuracyTracker { get; set; }
        public WinTracker WinTracker { get; set; }
        public ScoreGraphTracker ScoreGraphTracker { get; set; }
    }

    public class LeaderboardStatistic
    {
        public int Id { get; set; }

        public bool Relevant { get; set; }

        public string LeaderboardId { get; set; }

        public HitTracker HitTracker { get; set; }
        public AccuracyTracker AccuracyTracker { get; set; }
        public WinTracker WinTracker { get; set; }
        public ScoreGraphTracker ScoreGraphTracker { get; set; }
    }
}

