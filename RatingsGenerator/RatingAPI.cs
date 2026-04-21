using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer;
using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_parser;
using BeatLeader_Server.Models;
using MapPostprocessor;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using RatingAPI.Controllers;
using RatingAPI.Utils;
using System.Diagnostics;

namespace BeatLeader_Server.RatingsGenerator {
    public class RatingAPI {

        public class RatingResult
        {
            public double? PredictedAcc { get; set; }
            public double? AccRating { get; set; }
            public MapTypes MapType { get; set; }

            public Ratings Ratings { get; set; }
            public List<Point> PointList { get; set; }
        }

        public class Point
        {
            public double x { get; set; } = 0;
            public double y { get; set; } = 0;

            public Point()
            {

            }

            public Point(double x, double y)
            {
                this.x = x;
                this.y = y;
            }

            public List<Point> ToPoints(List<(double x, double y)> curve)
            {
                List<Point> points = new();

                foreach (var p in curve)
                {
                    points.Add(new(p.x, p.y));
                }

                return points;
            }
        }

        public static string CustomModeMapping(string mode)
        {
            switch (mode)
            {
                case "InvertedStandard":
                    return "Standard";
                default:
                    break;
            }

            return mode;
        }

        public static DifficultyV3 CustomModeDataMapping(string mode, DifficultyV3 mapdata)
        {
            int numberOfLines = 4;
            bool is_ME = false;
            bool is_ME_or_NE = false;
            DifficultyV3 result = mapdata;
            switch (mode)
            {
                case "VerticalStandard":
                    result = Parser.Utils.ChiralitySupport.Mirror_Vertical(mapdata, false, is_ME_or_NE);
                    break;
                case "HorizontalStandard":
                    result = Parser.Utils.ChiralitySupport.Mirror_Horizontal(mapdata, numberOfLines, false, is_ME_or_NE);
                    break;
                case "InverseStandard":
                    result = Parser.Utils.ChiralitySupport.Mirror_Inverse(mapdata, numberOfLines, true, true, is_ME_or_NE);
                    break;
                case "InvertedStandard":
                    result = Parser.Utils.ChiralitySupport.Mirror_Inverse(mapdata, numberOfLines, false, false, is_ME_or_NE);
                    break;

            }

            return result;
        }

        public static async Task<Dictionary<string, RatingResult?>?> Calculate(BeatmapV3 mapset, InferPublish? aiSession, string mode, int diff)
        {
            var difficulty = FormattingUtils.GetDiffLabel(diff);
            
            var map = mapset.Difficulties.FirstOrDefault(d => d.Characteristic == CustomModeMapping(mode) && (d.BeatMap._difficultyRank == diff || d.BeatMap._difficulty == difficulty));
            if (map == null) return null;

            //Stopwatch sw = Stopwatch.StartNew();
            var modifiers = new List<(string, double)>() {
                ("SS", 0.85),
                ("none", 1),
                ("FS", 1.2),
                ("SFS", 1.5),
                ("BFS", 1.2),
                ("BSF", 1.5),
            };

            var results = new Dictionary<string, RatingResult?>();
            foreach ((var name, var timescale) in modifiers)
            {
                var njsMult = 1.0;
                if (name == "BFS" || name == "BSF")
                {
                    njsMult = ((timescale - 1) / 2 + 1) / timescale;
                }
                results[name] = GetBLRatings(aiSession, map, mode, difficulty, mapset.Info._beatsPerMinute, timescale, njsMult);
            }
            //_logger.LogWarning("Took " + sw.ElapsedMilliseconds);

            return results;
        }

        const float linearPercentThreshold = 0.4f;
        const float dodgeWallValue = 0.3f;
        const float crouchWallValue = 5f;
        const float fitbeatDensityThreshold = 0.15f;
        const int bombAvoidanceTreshold = 20;

        private static RatingResult? GetBLRatings(InferPublish? aiSession, DifficultySet map, string characteristic, string difficulty, double bpm, double timescale, double njsMult = 1)
        {
            var mapdata = CustomModeDataMapping(characteristic, map.Data);
            var ratings = Analyze.GetRating(mapdata, characteristic, difficulty, (float)bpm, (float)timescale);
            if (ratings == null) return null;

            double? predictedAcc = null;
            double? accRating = null;

            if (aiSession != null) {
                predictedAcc = aiSession.GetAIAcc(mapdata, bpm, timescale, njsMult);

                AccRating ar = new();
                accRating = ar.GetRating(predictedAcc, ratings.PassRating, ratings.TechRating);
                accRating *= ratings.LowNoteNerf;
            }

            MapTypes mapTypes = MapTypes.None;
            if (ratings.LinearPercentage >= linearPercentThreshold) {
                mapTypes |= MapTypes.Linear;
            }

            float wallScore = (ratings.DodgeWalls.Count * dodgeWallValue) + (ratings.CrouchWalls.Count * crouchWallValue);
            

            var start = map.Data.Notes.OrderBy(x => x.Seconds).FirstOrDefault()?.Seconds ?? 9999;
            start = Math.Min(start, map.Data.Walls.OrderBy(x => x.Seconds).FirstOrDefault()?.Seconds ?? 9999);
            var end = map.Data.Notes.OrderByDescending(x => x.Seconds).FirstOrDefault()?.Seconds ?? 0;
            end = Math.Max(end, map.Data.Walls.OrderByDescending(x => x.Seconds).FirstOrDefault()?.Seconds ?? 0);
            var length = end - start;
            if (start == 9999 || end == 0) length = 0;

            if (length > 0) {
                float wallDensity = wallScore / length;

                if (wallDensity >= fitbeatDensityThreshold)
                {
                    mapTypes |= MapTypes.Fitbeat;
                }
            }

            if (ratings.Statistics.BombAvoidances > bombAvoidanceTreshold) {
                mapTypes |= MapTypes.BombReset;
            }

            //Curve curve = new();
            //var pointList = curve.GetCurve(lack);
            //var star = curve.ToStars(0.96, accRating, lack, pointList);
            RatingResult result = new()
            {
                PredictedAcc = predictedAcc,
                AccRating = accRating,
                Ratings = ratings,
                MapType = mapTypes
            };
            return result;
        }
    }
}