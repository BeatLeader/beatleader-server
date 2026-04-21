using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;

using Newtonsoft.Json.Linq;
using beatleader_parser;
using beatleader_analyzer;
using Parser.Map;
using Parser.Map.Difficulty.V3.Grid;
using Parser.Map.Difficulty.V3.Base;

namespace RatingAPI.Controllers
{
    public class DataProcessing
    {
        public static int preSegmentSize = 12;
        public static int postSegmentSize = 12;
        public static int predictionSize = 8;
        public static int noteSize = 49;
        public static int segmentSize = preSegmentSize + postSegmentSize + predictionSize;

        // Dictionaries for direction to angle and angle to direction
        private static readonly Dictionary<int, int> DirectionToAngle = new Dictionary<int, int>
        {
            {0, 180},
            {1, 0},
            {2, 90},
            {3, 270},
            {4, 135},
            {5, 225},
            {6, 45},
            {7, 315}
        };

        private static readonly Dictionary<int, int> AngleToDirection = new Dictionary<int, int>
        {
            {180, 0},
            {0, 1},
            {90, 2},
            {270, 3},
            {135, 4},
            {225, 5},
            {45, 6},
            {315, 7}
        };

        // Existing methods like PreprocessNote would be here...

        // Method to get the note direction
        public static int GetNoteDirection(int direction, double angle)
        {
            if (direction == 8)
            {
                return 8;
            }

            int noteAngle = (DirectionToAngle[direction] - (int)Math.Round(angle / 45) * 45) % 360;
            if (noteAngle < 0) {
                noteAngle += 360;
            }
            return AngleToDirection[noteAngle];
        }

        // Method to get map notes from json
        public static List<Tuple<double, string, double>> GetMapNotesFromJson(DifficultyV3 mapdata, double bpm)
        {
            List<Tuple<double, string, double>> mapNotes = mapdata.Notes
                    .Where(n => n.x < 1000 && n.x >= 0 && n.y < 1000 && n.y >= 0)
                    .Select(n => Tuple.Create(
                        (double)n.Seconds,
                        $"{n.x}{n.y}{GetNoteDirection(n.CutDirection, n.AngleOffset)}{n.Color}",
                        (double)n.njs
                    ))
                    .OrderBy(x => x.Item1).ThenBy(x => x.Item2)
                    .ToList();
            return mapNotes;
        }

        public static List<double> PreprocessNote(double delta, double deltaOther, int[] noteInfo, double njs, double timeScale)
        {
            delta /= timeScale;
            deltaOther /= timeScale;
            njs *= timeScale;

            double deltaLong = Math.Max(0, 2 - delta) / 2;
            double deltaOtherLong = Math.Max(0, 2 - deltaOther) / 2;
            double deltaShort = Math.Max(0, 0.5 - delta) * 2;
            double deltaOtherShort = Math.Max(0, 0.5 - deltaOther) * 2;

            int colNumber = Math.Clamp(noteInfo[0], 0, 3);
            int rowNumber = Math.Clamp(noteInfo[1], 0, 2);
            int directionNumber = noteInfo[2];
            int color = noteInfo[3];

            double[] rowCol = new double[4 * 3];
            double[] direction = new double[10];

            double[] rowCol2 = new double[4 * 3];
            double[] direction2 = new double[10];

            rowCol[colNumber * 3 + rowNumber] = 1;
            direction[directionNumber] = 1;

            List<double> response = new List<double>();

            if (color == 0)
            {
                response.AddRange(rowCol);
                response.AddRange(direction);
                response.AddRange(rowCol2);
                response.AddRange(direction2);
                response.Add(deltaShort);
                response.Add(deltaLong);
                response.Add(deltaOtherShort);
                response.Add(deltaOtherLong);
            } else if (color == 1)
            {
                response.AddRange(rowCol2);
                response.AddRange(direction2);
                response.AddRange(rowCol);
                response.AddRange(direction);
                response.Add(deltaOtherShort);
                response.Add(deltaOtherLong);
                response.Add(deltaShort);
                response.Add(deltaLong);
            }

            response.Add(njs / 30);

            return response;
        }

        public static Tuple<List<double[]>, List<double>> PreprocessMapNotes(List<Tuple<double, string, double>> mapNotes, double timeScale, double njsMult = 1)
        {
            List<List<double>> notes = new();
            List<double> noteTimes = new();

            double prevZeroNoteTime = 0;
            double prevOneNoteTime = 0;

            foreach (var note in mapNotes)
            {
                double noteTime = note.Item1;
                int[] noteInfo = note.Item2.Select(s => s-'0').ToArray();
                int type = noteInfo.Last();
                double njs = note.Item3 * njsMult;

                double deltaToZero = noteTime - prevZeroNoteTime;
                double deltaToOne = noteTime - prevOneNoteTime;

                if (deltaToZero < 0 || deltaToOne < 0)
                {
                    Console.WriteLine($"{deltaToZero} {deltaToOne}");
                }

                if (type == 0)
                {
                    prevZeroNoteTime = noteTime;
                    List<double> noteProcessed = PreprocessNote(deltaToZero, deltaToOne, noteInfo, njs, timeScale);
                    notes.Add(noteProcessed);
                    noteTimes.Add(noteTime);
                }
                if (type == 1)
                {
                    prevOneNoteTime = noteTime;
                    List<double> noteProcessed = PreprocessNote(deltaToOne, deltaToZero, noteInfo, njs, timeScale);
                    notes.Add(noteProcessed);
                    noteTimes.Add(noteTime);
                }
            }

            return new Tuple<List<double[]>, List<double>>(notes.Select(s => s.ToArray()).ToList(), noteTimes);
        }

        public List<List<double[]>> CreateSegments(List<double[]> notes)
        {
            var emptyRes = new List<List<double[]>> { new List<double[]>(), new List<double[]>() };
            if (notes.Count < predictionSize)
            {
                return emptyRes;
            }

            var segments = new List<List<double[]>>();
            for (int i = 0; i <= notes.Count - predictionSize; i++)
            {
                if (i % predictionSize != 0)
                {
                    continue;
                }

                var preSlice = notes.GetRange(Math.Max(0, i - preSegmentSize), Math.Min(preSegmentSize, i));
                var slice = notes.GetRange(i, predictionSize);
                var postSlice = notes.GetRange(i + predictionSize, Math.Min(postSegmentSize, notes.Count - (i + predictionSize)));

                var preSegment = preSlice.Select(note => note.ToArray()).ToList();
                while (preSegment.Count < preSegmentSize)
                {
                    preSegment.Insert(0, new double[noteSize]);
                }

                var segment = slice.Select(note => note.ToArray()).ToList();

                var postSegment = postSlice.Select(note => note.ToArray()).ToList();
                while (postSegment.Count < postSegmentSize)
                {
                    postSegment.Add(new double[noteSize]);
                }

                var finalSegment = new List<double[]>();
                finalSegment.AddRange(preSegment);
                finalSegment.AddRange(segment);
                finalSegment.AddRange(postSegment);
                segments.Add(finalSegment);
            }

            return segments;
        }

        public int GetFreePointsForMap(DifficultyV3 mapdata)
        {
            if (mapdata.Chains.Count == 0) return 0;

            int segmentCount = 0;
            foreach (var burstSlider in mapdata.Chains)
            {
                segmentCount += burstSlider.SliceCount;
            }
            return segmentCount * 20 * 8;
        }

        public (List<Tuple<double, string, double>> mapNotes, int freePoints) GetMapData(DifficultyV3 mapdata, double bpm)
        {
            var mapNotes = GetMapNotesFromJson(mapdata, bpm);
            var freePoints = GetFreePointsForMap(mapdata);
            return (mapNotes, freePoints);
        }

        public (List<List<double[]>> segments, List<double> noteTimes, int freePoints) PreprocessMap(DifficultyV3 mapdata, double bpm, double timescale, double njsMult = 1)
        {
            var emptyResponse = (new List<List<double[]>>(), new List<double>(), 0);
            var (mapNotes, freePoints) = GetMapData(mapdata, bpm);
            if (mapNotes == null)
            {
                return emptyResponse;
            }

            var (notes, noteTimes) = PreprocessMapNotes(mapNotes, timescale, njsMult);
            List<List<double[]>> segments = CreateSegments(notes);
            return (segments, noteTimes, freePoints);
        }
    }
}
