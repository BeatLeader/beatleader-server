using System;
using BeatLeader_Server.Models;
public enum ScoringType
{
    Default,
    Ignore,
    NoScore,
    Normal,
    SliderHead,
    SliderTail,
    BurstSliderHead,
    BurstSliderElement
}

public class NoteParams
{
    public ScoringType scoringType;
    public int lineIndex;
    public int noteLineLayer;
    public int colorType;
    public int cutDirection;

    public NoteParams(int noteId)
    {
        int id = noteId;
        if (id < 100000) {
            scoringType = (ScoringType)(id / 10000);
            id -= (int)scoringType * 10000;

            lineIndex = id / 1000;
            id -= lineIndex * 1000;

            noteLineLayer = id / 100;
            id -= noteLineLayer * 100;

            colorType = id / 10;
            cutDirection = id - colorType * 10;
        } else {
            scoringType = (ScoringType)(id / 10000000);
            id -= (int)scoringType * 10000000;

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
        public ScoringType scoringType;

        public float multiplier;
        public int totalScore;
        public float accuracy;
        public int combo;
    }

    class MultiplierCounter
    {
        public int Multiplier { get; private set; } = 1;

        private int _multiplierIncreaseProgress;
        private int _multiplierIncreaseMaxProgress = 2;

        public void Reset()
        {
            Multiplier = 1;
            _multiplierIncreaseProgress = 0;
            _multiplierIncreaseMaxProgress = 2;
        }

        public void Increase()
        {
            if (Multiplier >= 8) return;

            if (_multiplierIncreaseProgress < _multiplierIncreaseMaxProgress)
            {
                ++_multiplierIncreaseProgress;
            }

            if (_multiplierIncreaseProgress >= _multiplierIncreaseMaxProgress)
            {
                Multiplier *= 2;
                _multiplierIncreaseProgress = 0;
                _multiplierIncreaseMaxProgress = Multiplier * 2;
            }
        }

        public void Decrease()
        {
            if (_multiplierIncreaseProgress > 0)
            {
                _multiplierIncreaseProgress = 0;
            }

            if (Multiplier > 1)
            {
                Multiplier /= 2;
                _multiplierIncreaseMaxProgress = Multiplier * 2;
            }
        }
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
            result.ScoreGraphTracker = ScoreGraph(structs, (int)replay.frames.Last().time);

            return result;
        }

        public static (AccuracyTracker, List<NoteStruct>, int) Accuracy(Replay replay)
        {
            AccuracyTracker result = new AccuracyTracker();
            result.GridAcc = new List<float>(new float[12]);
            result.LeftAverageCut = new List<float>(new float[3]);
            result.RightAverageCut = new List<float>(new float[3]);

            int[] gridCounts = new int[12];
            int[] leftCuts = new int[3];
            int[] rightCuts = new int[3];

            List<NoteStruct> allStructs = new List<NoteStruct>();
            foreach (var note in replay.notes)
            {
                NoteParams param = new NoteParams(note.noteID);
                int scoreValue = ScoreForNote(note, param.scoringType);

                if (scoreValue > 0)
                {
                    int index = param.noteLineLayer * 4 + param.lineIndex;
                    if (index > 11 || index < 0) {
                        index = 0;
                    }

                    if (param.scoringType != ScoringType.BurstSliderElement)
                    {
                        gridCounts[index]++;
                        result.GridAcc[index] += (float)scoreValue;
                    }

                    (int before, int after, int acc) = CutScoresForNote(note, param.scoringType);
                    if (param.colorType == 0)
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement) {
                            result.LeftAverageCut[0] += (float)before;
                            result.LeftPreswing += note.noteCutInfo.beforeCutRating;
                            leftCuts[0]++;
                        }
                        if (param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.LeftAverageCut[1] += (float)acc;
                            result.AccLeft += (float)scoreValue;
                            result.LeftTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            leftCuts[1]++;
                        }
                        if (param.scoringType != ScoringType.SliderHead
                            && param.scoringType != ScoringType.BurstSliderHead
                            && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.LeftAverageCut[2] += (float)after;
                            result.LeftPostswing += note.noteCutInfo.afterCutRating;
                            leftCuts[2]++;
                        }
                    }
                    else
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.RightAverageCut[0] += (float)before;
                            result.RightPreswing += note.noteCutInfo.beforeCutRating;
                            rightCuts[0]++;
                        }
                        if (param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.RightAverageCut[1] += (float)acc;
                            result.RightTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            result.AccRight += (float)scoreValue;
                            rightCuts[1]++;
                        }
                        if (param.scoringType != ScoringType.SliderHead
                            && param.scoringType != ScoringType.BurstSliderHead
                            && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.RightAverageCut[2] += (float)after;
                            result.RightPostswing += note.noteCutInfo.afterCutRating;
                            rightCuts[2]++;
                        }
                    }
                }

                allStructs.Add(new NoteStruct
                {
                    time = note.eventTime,
                    isBlock = param.colorType != 2,
                    score = scoreValue,
                    scoringType = param.scoringType,
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

            if (leftCuts[0] > 0)
            {
                result.LeftAverageCut[0] /= (float)leftCuts[0];
                result.LeftPreswing /= (float)leftCuts[0];
            }

            if (leftCuts[1] > 0)
            {
                result.LeftAverageCut[1] /= (float)leftCuts[1];

                result.AccLeft /= (float)leftCuts[1];
                result.LeftTimeDependence /= (float)leftCuts[1];
            }

            if (leftCuts[2] > 0)
            {
                result.LeftAverageCut[2] /= (float)leftCuts[2];

                result.LeftPostswing /= (float)leftCuts[2];
            }

            if (rightCuts[0] > 0)
            {
                result.RightAverageCut[0] /= (float)rightCuts[0];
                result.RightPreswing /= (float)rightCuts[0];
            }

            if (rightCuts[1] > 0)
            {
                result.RightAverageCut[1] /= (float)rightCuts[1];

                result.AccRight /= (float)rightCuts[1];
                result.RightTimeDependence /= (float)rightCuts[1];
            }

            if (rightCuts[2] > 0)
            {
                result.RightAverageCut[2] /= (float)rightCuts[2];

                result.RightPostswing /= (float)rightCuts[2];
            }

            allStructs = allStructs.OrderBy(s => s.time).ToList();

            int multiplier = 1;
            int score = 0, noteIndex = 0;
            int combo = 0, maxCombo = 0;
            int maxScore = 0;
            MultiplierCounter maxCounter = new MultiplierCounter();
            MultiplierCounter normalCounter = new MultiplierCounter();

            for (var i = 0; i < allStructs.Count(); i++)
            {
                var note = allStructs[i];
                int scoreForMaxScore = 115;
                if (note.scoringType == ScoringType.BurstSliderHead) {
                    scoreForMaxScore = 85;
                } else if (note.scoringType == ScoringType.BurstSliderElement) {
                    scoreForMaxScore = 20;
                }
                maxCounter.Increase();
                maxScore += maxCounter.Multiplier * scoreForMaxScore;

                if (note.score < 0)
                {
                    normalCounter.Decrease();
                    multiplier = normalCounter.Multiplier;
                    combo = 0;
                }
                else
                {
                    normalCounter.Increase();
                    combo++;
                    multiplier = normalCounter.Multiplier;
                    score += multiplier * note.score;
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
                    note.accuracy = (float)note.totalScore / maxScore;
                    noteIndex++;
                }
                else
                {
                    note.accuracy = i == 0 ? 0 : allStructs[i - 1].accuracy;
                }
            }

            return (result, allStructs, maxCombo);
        }

        public static ScoreGraphTracker ScoreGraph(List<NoteStruct> structs, int replayLength)
        {
            ScoreGraphTracker scoreGraph = new ScoreGraphTracker();
            scoreGraph.Graph = new List<float>(new float[replayLength]);

            int structIndex = 0;

            for (int i = 0; i < replayLength; i++)
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

        public static float Clamp(float value)
        {
            if (value < 0.0) return 0.0f;
            return value > 1.0f ? 1.0f : value;
        }

        public static int ScoreForNote(NoteEvent note, ScoringType scoringType)
        {
            if (note.eventType == NoteEventType.good)
            {
                (int before, int after, int acc) = CutScoresForNote(note, scoringType);

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

        public static (int, int, int) CutScoresForNote(NoteEvent note, ScoringType scoringType)
        {
            var cut = note.noteCutInfo;
            double beforeCutRawScore = 0;
            if (scoringType != ScoringType.BurstSliderElement)
            {
                if (scoringType == ScoringType.SliderTail)
                {
                    beforeCutRawScore = 70;
                }
                else
                {
                    beforeCutRawScore = Math.Clamp(Math.Round(70 * cut.beforeCutRating), 0, 70);
                }
            }
            double afterCutRawScore = 0;
            if (scoringType != ScoringType.BurstSliderElement)
            {
                if (scoringType == ScoringType.BurstSliderHead)
                {
                    afterCutRawScore = 0;
                }
                else if (scoringType == ScoringType.SliderHead)
                {
                    afterCutRawScore = 30;
                }
                else
                {
                    afterCutRawScore = Math.Clamp(Math.Round(30 * cut.afterCutRating), 0, 30);
                }
            }
            double cutDistanceRawScore = 0;
            if (scoringType == ScoringType.BurstSliderElement)
            {
                cutDistanceRawScore = 20;
            }
            else
            {

                double num = 1 - Clamp(cut.cutDistanceToCenter / 0.3f);
                cutDistanceRawScore = Math.Round(15 * num);

            }

            return ((int)beforeCutRawScore, (int)afterCutRawScore, (int)cutDistanceRawScore);
        }

        public static List<float> AverageList(List<List<float>> total) {
            int length = total.Max(t => t.Count);
            var result = new List<float>(length);
            for (int i = 0; i < length; i++)
            {
                float sum = 0;
                float count = 0;
                for (int j = 0; j < total.Count; j++)
                {
                    if (i < total[j].Count) {
                        sum += total[j][i];
                        count++;
                    }
                }
                result.Add(count > 0 ? sum / count : 0);
            }
            return result;

        }

        public static void AverageStatistic(List<ScoreStatistic> statistics, LeaderboardStatistic leaderboardStatistic) {

            leaderboardStatistic.WinTracker = new WinTracker {
                Won = statistics.Average(st => st.WinTracker.Won ? 1.0 : 0.0) > 0.5,
                EndTime = statistics.Average(st => st.WinTracker.EndTime),
                NbOfPause = (int)Math.Round(statistics.Average(st => st.WinTracker.NbOfPause)),
                JumpDistance = statistics.Average(st => st.WinTracker.JumpDistance),
                TotalScore = (int)statistics.Average(st => st.WinTracker.TotalScore)
            };

            leaderboardStatistic.HitTracker = new HitTracker {
                MaxCombo = (int)Math.Round(statistics.Average(st => st.HitTracker.MaxCombo)),
                LeftMiss = (int)Math.Round(statistics.Average(st => st.HitTracker.LeftMiss)),
                RightMiss = (int)Math.Round(statistics.Average(st => st.HitTracker.RightMiss)),
                LeftBadCuts = (int)Math.Round(statistics.Average(st => st.HitTracker.LeftBadCuts)),
                RightBadCuts = (int)Math.Round(statistics.Average(st => st.HitTracker.RightBadCuts)),
                LeftBombs = (int)Math.Round(statistics.Average(st => st.HitTracker.LeftBombs)),
                RightBombs = (int)Math.Round(statistics.Average(st => st.HitTracker.RightBombs))
            };

            leaderboardStatistic.AccuracyTracker = new AccuracyTracker {
                AccRight = statistics.Average(st => st.AccuracyTracker.AccRight),
                AccLeft = statistics.Average(st => st.AccuracyTracker.AccLeft),
                LeftPreswing = statistics.Average(st => st.AccuracyTracker.LeftPreswing),
                RightPreswing = statistics.Average(st => st.AccuracyTracker.RightPreswing),
                AveragePreswing = statistics.Average(st => st.AccuracyTracker.AveragePreswing),
                LeftPostswing = statistics.Average(st => st.AccuracyTracker.LeftPostswing),
                RightPostswing = statistics.Average(st => st.AccuracyTracker.RightPostswing),
                LeftTimeDependence = statistics.Average(st => st.AccuracyTracker.LeftTimeDependence),
                RightTimeDependence = statistics.Average(st => st.AccuracyTracker.RightTimeDependence),
                LeftAverageCut = AverageList(statistics.Select(st => st.AccuracyTracker.LeftAverageCut).ToList()),
                RightAverageCut = AverageList(statistics.Select(st => st.AccuracyTracker.RightAverageCut).ToList()),
                GridAcc = AverageList(statistics.Select(st => st.AccuracyTracker.GridAcc).ToList())
            };

            leaderboardStatistic.ScoreGraphTracker = new ScoreGraphTracker {
                Graph = AverageList(statistics.Select(st => st.ScoreGraphTracker.Graph).ToList())
            };
        }

        public static void EncodeArrays(ScoreStatistic statistic)
        {
            statistic.AccuracyTracker.LeftAverageCutS = string.Join(",", statistic.AccuracyTracker.LeftAverageCut);
            statistic.AccuracyTracker.RightAverageCutS = string.Join(",", statistic.AccuracyTracker.RightAverageCut);
            statistic.AccuracyTracker.GridAccS = string.Join(",", statistic.AccuracyTracker.GridAcc);

            statistic.ScoreGraphTracker.GraphS = string.Join(",", statistic.ScoreGraphTracker.Graph);
        }

        public static void EncodeArrays(LeaderboardStatistic statistic)
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

        public static void DecodeArrays(LeaderboardStatistic statistic)
        {
            statistic.AccuracyTracker.LeftAverageCut = statistic.AccuracyTracker.LeftAverageCutS.Split(",").Select(x => float.Parse(x)).ToList();
            statistic.AccuracyTracker.RightAverageCut = statistic.AccuracyTracker.RightAverageCutS.Split(",").Select(x => float.Parse(x)).ToList();
            statistic.AccuracyTracker.GridAcc = statistic.AccuracyTracker.GridAccS.Split(",").Select(x => float.Parse(x)).ToList();

            if (statistic.ScoreGraphTracker.GraphS.Length > 0)
            {
                statistic.ScoreGraphTracker.Graph = statistic.ScoreGraphTracker.GraphS.Split(",").Select(x => float.Parse(x)).ToList();
            }
        }
    }
}