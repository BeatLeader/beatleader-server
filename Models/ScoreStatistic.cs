using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class HitTracker
    {
        public int maxCombo { get; set; }
        public int maxStreak { get; set; }
        public float leftTiming { get; set; }
        public float rightTiming { get; set; }
        public int leftMiss { get; set; }
        public int rightMiss { get; set; }
        public int leftBadCuts { get; set; }
        public int rightBadCuts { get; set; }
        public int leftBombs { get; set; }
        public int rightBombs { get; set; }
    }

    public class AveragePosition 
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
    }

    public class WinTracker
    {
        public bool won { get; set; }
        public float endTime { get; set; }
        public int nbOfPause { get; set; }
        public float totalPauseDuration { get; set; }
        public float jumpDistance { get; set; }
        public float averageHeight { get; set; }
        public AveragePosition? averageHeadPosition { get; set; }    
        public int totalScore { get; set; }
    }

    public class ScoreStatistic
    {
        public HitTracker hitTracker { get; set; }
        public AccuracyTracker accuracyTracker { get; set; }
        public WinTracker winTracker { get; set; }
        public ScoreGraphTracker scoreGraphTracker { get; set; }
    }

    public class AccuracyTracker
    {
        public float accRight { get; set; }
        public float accLeft { get; set; }
        public float leftPreswing { get; set; }
        public float rightPreswing { get; set; }
        public float averagePreswing { get; set; }
        public float leftPostswing { get; set; }
        public float rightPostswing { get; set; }
        public float leftTimeDependence { get; set; }
        public float rightTimeDependence { get; set; }
        public List<float> leftAverageCut { get; set; }
        public List<float> rightAverageCut { get; set; }
        public List<float> gridAcc { get; set; }

        public float fcAcc { get; set; }
    }

    public class ScoreGraphTracker
    {
        public List<float> graph { get; set; }
    }
}

