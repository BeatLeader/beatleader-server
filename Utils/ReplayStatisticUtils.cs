using System;
using BeatLeader_Server.Models;

public class NoteParams
{
    public int lineIndex;
    public int noteLineLayer;
    public int colorType;
    public int cutDirection;

    public NoteParams(int noteId)
    {
        int id = noteId;
        if (id < 100000) {
            lineIndex = id / 1000;
            id -= lineIndex * 1000;

            noteLineLayer = id / 100;
            id -= noteLineLayer * 100;

            colorType = id / 10;
            cutDirection = id - colorType * 10;
        } else {
            lineIndex = id / 1000000;
            id -= lineIndex * 1000000;

            noteLineLayer = id / 100000;
            id -= noteLineLayer * 100000;

            colorType = id / 10;
            cutDirection = id - colorType * 10;
        }
    }
}

namespace BeatLeader_Server.Utils
{
    class NoteStruct
    {
        public int score;
        public bool isBlock;
        public float time;

        public float multiplier;
        public int totalScore;
        public float accuracy;
        public int combo;
    }

    class ReplayStatisticUtils
    {
        public static ScoreStatistic ProcessReplay(Replay replay, Leaderboard leaderboard)
        {
            ScoreStatistic result = new ScoreStatistic();
            result.WinTracker = new WinTracker
            {
                Won = replay.info.failTime < 0.01,
                EndTime = (replay.frames.LastOrDefault() != null) ? replay.frames.Last().time : 0,
                NbOfPause = replay.pauses.Count(),
                JumpDistance = replay.info.jumpDistance
            };

            HitTracker hitTracker = new HitTracker();
            result.HitTracker = hitTracker;

            List<NoteParams> noteParams = new List<NoteParams>();

            foreach (var item in replay.notes)
            {
                NoteParams param = new NoteParams(item.noteID);
                switch (item.eventType)
                {
                    case NoteEventType.bad:
                        if (item.noteCutInfo.saberType == 0)
                        {
                            hitTracker.LeftBadCuts++;
                        }
                        else
                        {
                            hitTracker.RightBadCuts++;
                        }
                        break;
                    case NoteEventType.miss:
                        if (param.colorType == 0)
                        {
                            hitTracker.LeftMiss++;
                        }
                        else
                        {
                            hitTracker.RightMiss++;
                        }
                        break;
                    case NoteEventType.bomb:
                        if (param.colorType == 0)
                        {
                            hitTracker.LeftBombs++;
                        }
                        else
                        {
                            hitTracker.RightBombs++;
                        }
                        break;
                    default:
                        break;
                }
            }
            (AccuracyTracker accuracy, List<NoteStruct> structs, int maxCombo) = Accuracy(replay);
            result.HitTracker.MaxCombo = maxCombo;
            result.WinTracker.TotalScore = structs.Last().totalScore;
            result.AccuracyTracker = accuracy;
            result.ScoreGraphTracker = ScoreGraph(structs, leaderboard);

            return result;
        }

        public static (AccuracyTracker, List<NoteStruct>, int) Accuracy(Replay replay)
        {
            AccuracyTracker result = new AccuracyTracker();
            result.GridAcc = new List<float>(new float[12]);
            result.LeftAverageCut = new List<float>(new float[3]);
            result.RightAverageCut = new List<float>(new float[3]);

            int[] gridCounts = new int[12];
            int leftCuts = 0;
            int rightCuts = 0;

            List<NoteStruct> allStructs = new List<NoteStruct>();
            foreach (var note in replay.notes)
            {
                NoteParams param = new NoteParams(note.noteID);
                int scoreValue = ScoreForNote(note);

                if (scoreValue > 0)
                {
                    int index = param.noteLineLayer * 4 + param.lineIndex;
                    if (index > 11 || index < 0) {
                        index = 0;
                    }

                    gridCounts[index]++;
                    result.GridAcc[index] += (float)scoreValue;

                    (int before, int after, int acc) = CutScoresForNote(note);
                    if (param.colorType == 0)
                    {
                        result.LeftAverageCut[0] += (float)before;
                        result.LeftAverageCut[1] += (float)acc;
                        result.LeftAverageCut[2] += (float)after;
                        result.LeftPreswing += note.noteCutInfo.beforeCutRating;
                        result.LeftPostswing += note.noteCutInfo.afterCutRating;
                        result.LeftTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                        result.AccLeft += (float)scoreValue;
                        leftCuts++;
                    }
                    else
                    {
                        result.RightAverageCut[0] += (float)before;
                        result.RightAverageCut[1] += (float)acc;
                        result.RightAverageCut[2] += (float)after;
                        result.RightPreswing += note.noteCutInfo.beforeCutRating;
                        result.RightPostswing += note.noteCutInfo.afterCutRating;
                        result.RightTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                        result.AccRight += (float)scoreValue;
                        rightCuts++;
                    }
                }

                allStructs.Add(new NoteStruct
                {
                    time = note.eventTime,
                    isBlock = param.colorType != 2,
                    score = scoreValue
                });
            }

            foreach (var wall in replay.walls)
            {
                allStructs.Add(new NoteStruct
                {
                    time = wall.time,

                    score = -5
                });
            }

            for (int i = 0; i < result.GridAcc.Count(); i++)
            {
                if (gridCounts[i] > 0)
                {
                    result.GridAcc[i] /= (float)gridCounts[i];
                }
            }

            if (leftCuts > 0)
            {
                for (int i = 0; i < result.LeftAverageCut.Count(); i++)
                {
                    result.LeftAverageCut[i] /= (float)leftCuts;
                }

                result.AccLeft /= (float)leftCuts;
                result.LeftPreswing /= (float)leftCuts;
                result.LeftPostswing /= (float)leftCuts;
                result.LeftTimeDependence /= (float)leftCuts;
            }

            if (rightCuts > 0)
            {
                for (int i = 0; i < result.RightAverageCut.Count(); i++)
                {
                    result.RightAverageCut[i] /= (float)rightCuts;
                }

                result.AccRight /= (float)rightCuts;
                result.RightPreswing /= (float)rightCuts;
                result.RightPostswing /= (float)rightCuts;
                result.RightTimeDependence /= (float)rightCuts;
            }

            allStructs = allStructs.OrderBy(s => s.time).ToList();

            int multiplier = 1, lastmultiplier = 1;
            int score = 0, noteIndex = 0;
            int combo = 0, maxCombo = 0;

            for (var i = 0; i < allStructs.Count(); i++)
            {
                var note = allStructs[i];

                if (note.score < 0)
                {
                    multiplier = multiplier > 1 ? multiplier / 2 : 1;
                    lastmultiplier = multiplier;
                    combo = 0;
                }
                else
                {
                    score += multiplier * note.score;
                    combo++;
                    multiplier = multiplierForCombo(comboForMultiplier(lastmultiplier) + combo);
                }

                if (combo > maxCombo)
                {
                    maxCombo = combo;
                }

                note.multiplier = multiplier;
                note.totalScore = score;
                note.combo = combo;

                if (note.isBlock)
                {
                    note.accuracy = (float)note.totalScore / ReplayUtils.MaxScoreForNote(noteIndex + 1);
                    noteIndex++;
                }
                else
                {
                    note.accuracy = i == 0 ? 0 : allStructs[i - 1].accuracy;
                }
            }

            return (result, allStructs, maxCombo);
        }

        public static ScoreGraphTracker ScoreGraph(List<NoteStruct> structs, Leaderboard leaderboard)
        {
            ScoreGraphTracker scoreGraph = new ScoreGraphTracker();
            scoreGraph.Graph = new List<float>(new float[(int)leaderboard.Song.Duration]);

            int structIndex = 0;

            for (int i = 0; i < (int)leaderboard.Song.Duration; i++)
            {
                float cumulative = 0.0f;
                int delimiter = 0;
                while (structIndex < structs.Count() && structs[structIndex].time < i + 1)
                {
                    cumulative += structs[structIndex].accuracy;
                    structIndex++;
                    delimiter++;
                }
                if (delimiter > 0)
                {
                    scoreGraph.Graph[i] = cumulative / (float)delimiter;
                }
                if (scoreGraph.Graph[i] == 0)
                {
                    scoreGraph.Graph[i] = i == 0 ? 1.0f : scoreGraph.Graph[i - 1];
                }
            }

            return scoreGraph;
        }

        public static int multiplierForCombo(int combo)
        {
            if (combo < 1)
            {
                return 1;
            }
            if (combo < 5)
            {
                return 2;
            }
            if (combo < 13)
            {
                return 4;
            }
            else
            {
                return 8;
            }
        }

        public static int comboForMultiplier(int multiplier)
        {
            if (multiplier == 1)
            {
                return 0;
            }
            if (multiplier == 2)
            {
                return 1;
            }
            if (multiplier == 4)
            {
                return 6;
            }
            else
            {
                return 13;
            }
        }

        public static float Clamp(float value)
        {
            if (value < 0.0) return 0.0f;
            return value > 1.0f ? 1.0f : value;
        }

        public static int ScoreForNote(NoteEvent note)
        {
            if (note.eventType == NoteEventType.good)
            {
                (int before, int after, int acc) = CutScoresForNote(note);

                return before + after + acc;
            }
            else
            {
                switch (note.eventType)
                {
                    case NoteEventType.bad:
                        return -2;
                    case NoteEventType.miss:
                        return -3;
                    case NoteEventType.bomb:
                        return -4;
                }
            }
            return -1;
        }

        public static (int, int, int) CutScoresForNote(NoteEvent note)
        {
            var cut = note.noteCutInfo;
            double beforeCutRawScore = Math.Round(70 * cut.beforeCutRating);
            double afterCutRawScore = Math.Round(30 * cut.afterCutRating);
            double num = 1 - Clamp(cut.cutDistanceToCenter / 0.3f);
            double cutDistanceRawScore = Math.Round(15 * num);

            return ((int)beforeCutRawScore, (int)afterCutRawScore, (int)cutDistanceRawScore);
        }

        public static void EncodeArrays(ScoreStatistic statistic)
        {
            statistic.AccuracyTracker.LeftAverageCutS = string.Join(",", statistic.AccuracyTracker.LeftAverageCut);
            statistic.AccuracyTracker.RightAverageCutS = string.Join(",", statistic.AccuracyTracker.RightAverageCut);
            statistic.AccuracyTracker.GridAccS = string.Join(",", statistic.AccuracyTracker.GridAcc);

            statistic.ScoreGraphTracker.GraphS = string.Join(",", statistic.ScoreGraphTracker.Graph);
        }

        public static void DecodeArrays(ScoreStatistic statistic)
        {
            statistic.AccuracyTracker.LeftAverageCut = statistic.AccuracyTracker.LeftAverageCutS.Split(",").Select(x => float.Parse(x)).ToList();
            statistic.AccuracyTracker.RightAverageCut = statistic.AccuracyTracker.RightAverageCutS.Split(",").Select(x => float.Parse(x)).ToList();
            statistic.AccuracyTracker.GridAcc = statistic.AccuracyTracker.GridAccS.Split(",").Select(x => float.Parse(x)).ToList();

            if (statistic.ScoreGraphTracker.GraphS.Length > 0) {
                statistic.ScoreGraphTracker.Graph = statistic.ScoreGraphTracker.GraphS.Split(",").Select(x => float.Parse(x)).ToList();
            }
        }
    }
}