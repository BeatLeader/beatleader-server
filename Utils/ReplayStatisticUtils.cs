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
        public int id;
        public bool isBlock;
        public float time;
        public float spawnTime;
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
        public static (ScoreStatistic?, string?) ProcessReplay(Replay replay, Leaderboard leaderboard)
        {
            try {
            ScoreStatistic result = new ScoreStatistic();
            float firstNoteTime = replay.notes.FirstOrDefault()?.eventTime ?? 0.0f;
            float lastNoteTime = replay.notes.LastOrDefault()?.eventTime ?? 0.0f;
            result.winTracker = new WinTracker
            {
                won = replay.info.failTime < 0.01,
                endTime = (replay.frames.LastOrDefault() != null) ? replay.frames.Last().time : 0,
                nbOfPause = replay.pauses.Where(p => p.time >= firstNoteTime && p.time <= lastNoteTime).Count(),
                jumpDistance = replay.info.jumpDistance,
                averageHeight = replay.heights.Count() > 0 ? replay.heights.Average(h => h.height) : replay.info.height,
                averageHeadPosition = new AveragePosition {
                    x = replay.frames.Average(f => f.head.position.x),
                    y = replay.frames.Average(f => f.head.position.y),
                    z = replay.frames.Average(f => f.head.position.z),
                }
            };

            HitTracker hitTracker = new HitTracker();
            result.hitTracker = hitTracker;

            int leftGoodCuts = 0, rightGoodCuts = 0;
            float leftTiming = 0, rightTiming = 0;

            foreach (var item in replay.notes)
            {
                NoteParams param = new NoteParams(item.noteID);
                switch (item.eventType)
                {
                    case NoteEventType.bad:
                        if (item.noteCutInfo.saberType == 0)
                        {
                            hitTracker.leftBadCuts++;
                        }
                        else
                        {
                            hitTracker.rightBadCuts++;
                        }
                        break;
                    case NoteEventType.miss:
                        if (param.colorType == 0)
                        {
                            hitTracker.leftMiss++;
                        }
                        else
                        {
                            hitTracker.rightMiss++;
                        }
                        break;
                    case NoteEventType.bomb:
                        if (param.colorType == 0)
                        {
                            hitTracker.leftBombs++;
                        }
                        else
                        {
                            hitTracker.rightBombs++;
                        }
                        break;
                    case NoteEventType.good:
                        if (param.colorType == 0)
                        {
                            leftGoodCuts++;
                            leftTiming += item.noteCutInfo.timeDeviation;
                        }
                        else
                        {
                            rightGoodCuts++;
                            rightTiming += item.noteCutInfo.timeDeviation;
                        }
                        break;
                    default:
                        break;
                }
            }
            if (leftGoodCuts > 0) {
                hitTracker.leftTiming = leftTiming / leftGoodCuts;
            }
            if (rightGoodCuts > 0) {
                hitTracker.rightTiming = rightTiming / rightGoodCuts;
            }

            string? error = CheckReplay(replay, leaderboard);
            if (error != null) {
                return (null, error);
            }

            (AccuracyTracker accuracy, List<NoteStruct> structs, int maxCombo, int maxStreak) = Accuracy(replay);
            result.hitTracker.maxCombo = maxCombo;
            result.hitTracker.maxStreak = maxStreak;
            result.winTracker.totalScore = structs.Last().totalScore;
            result.accuracyTracker = accuracy;
            result.scoreGraphTracker = ScoreGraph(structs, (int)replay.frames.Last().time);

            return (result, null);
            } catch (Exception e) {
                return (null, e.Message);
            }
        }

        public static string? CheckReplay(Replay replay, Leaderboard leaderboard) {
            float endTime = replay.notes.Count > 0 ? replay.notes.Last().eventTime : 0;

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                leaderboard.Difficulty.Status == DifficultyStatus.nominated) {
                if (replay.notes.Count < leaderboard.Difficulty.Notes && (leaderboard.Difficulty.Duration - endTime) > 1)
                {
                    return "Too few notes in the replay";
                }

                foreach (var note in replay.notes)
                {
                    NoteParams param = new NoteParams(note.noteID);
                    if (note.noteID < 100000 && note.noteID > 0 && endTime - note.eventTime > 1)
                    {
                        if (note.eventType == NoteEventType.good && param.colorType != note.noteCutInfo.saberType)
                        {
                            return "Wrong saber type on a good cut note";
                        }
                    }
                }
            }

            return null;
        }

        public static (AccuracyTracker, List<NoteStruct>, int, int) Accuracy(Replay replay)
        {
            AccuracyTracker result = new AccuracyTracker();
            result.gridAcc = new List<float>(new float[12]);
            result.leftAverageCut = new List<float>(new float[3]);
            result.rightAverageCut = new List<float>(new float[3]);

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

                    if (param.scoringType != ScoringType.BurstSliderElement
                     && param.scoringType != ScoringType.BurstSliderHead)
                    {
                        gridCounts[index]++;
                        result.gridAcc[index] += (float)scoreValue;
                    }

                    (int before, int after, int acc) = CutScoresForNote(note, param.scoringType);
                    if (param.colorType == 0)
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement) {
                            result.leftAverageCut[0] += (float)before;
                            result.leftPreswing += note.noteCutInfo.beforeCutRating;
                            leftCuts[0]++;
                        }
                        if (param.scoringType != ScoringType.BurstSliderElement
                         && param.scoringType != ScoringType.BurstSliderHead)
                        {
                            result.leftAverageCut[1] += (float)acc;
                            result.accLeft += (float)scoreValue;
                            result.leftTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            leftCuts[1]++;
                        }
                        if (param.scoringType != ScoringType.SliderHead
                            && param.scoringType != ScoringType.BurstSliderHead
                            && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.leftAverageCut[2] += (float)after;
                            result.leftPostswing += note.noteCutInfo.afterCutRating;
                            leftCuts[2]++;
                        }
                    }
                    else
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.rightAverageCut[0] += (float)before;
                            result.rightPreswing += note.noteCutInfo.beforeCutRating;
                            rightCuts[0]++;
                        }
                        if (param.scoringType != ScoringType.BurstSliderElement 
                         && param.scoringType != ScoringType.BurstSliderHead)
                        {
                            result.rightAverageCut[1] += (float)acc;
                            result.rightTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            result.accRight += (float)scoreValue;
                            rightCuts[1]++;
                        }
                        if (param.scoringType != ScoringType.SliderHead
                            && param.scoringType != ScoringType.BurstSliderHead
                            && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.rightAverageCut[2] += (float)after;
                            result.rightPostswing += note.noteCutInfo.afterCutRating;
                            rightCuts[2]++;
                        }
                    }
                }

                allStructs.Add(new NoteStruct
                {
                    time = note.eventTime,
                    id = note.noteID,
                    isBlock = param.colorType != 2,
                    score = scoreValue,
                    scoringType = param.scoringType,
                    spawnTime = note.spawnTime
                });
            }

            foreach (var wall in replay.walls)
            {
                allStructs.Add(new NoteStruct
                {
                    time = wall.time,
                    id = wall.wallID,
                    score = -5
                });
            }

            for (int i = 0; i < result.gridAcc.Count(); i++)
            {
                if (gridCounts[i] > 0)
                {
                    result.gridAcc[i] /= (float)gridCounts[i];
                }
            }

            if (leftCuts[0] > 0)
            {
                result.leftAverageCut[0] /= (float)leftCuts[0];
                result.leftPreswing /= (float)leftCuts[0];
            }

            if (leftCuts[1] > 0)
            {
                result.leftAverageCut[1] /= (float)leftCuts[1];

                result.accLeft /= (float)leftCuts[1];
                result.leftTimeDependence /= (float)leftCuts[1];
            }

            if (leftCuts[2] > 0)
            {
                result.leftAverageCut[2] /= (float)leftCuts[2];

                result.leftPostswing /= (float)leftCuts[2];
            }

            if (rightCuts[0] > 0)
            {
                result.rightAverageCut[0] /= (float)rightCuts[0];
                result.rightPreswing /= (float)rightCuts[0];
            }

            if (rightCuts[1] > 0)
            {
                result.rightAverageCut[1] /= (float)rightCuts[1];

                result.accRight /= (float)rightCuts[1];
                result.rightTimeDependence /= (float)rightCuts[1];
            }

            if (rightCuts[2] > 0)
            {
                result.rightAverageCut[2] /= (float)rightCuts[2];

                result.rightPostswing /= (float)rightCuts[2];
            }

            allStructs = allStructs.OrderBy(s => s.time).ToList();

            var groupedById = allStructs.GroupBy(s => s.id);
            bool potentiallyPoodle = false;
            foreach (var group in groupedById) {
                var ordered = group.OrderBy(g => g.time).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    if ((ordered[i].time - ordered[i - 1].time) < 0.04) {
                        potentiallyPoodle = true;
                        break;
                    }
                }
                if (potentiallyPoodle) {
                    break;
                }
            }

            int multiplier = 1;
            int score = 0, noteIndex = 0;
            int combo = 0, maxCombo = 0;
            int maxScore = 0;
            int fcScore = 0; float currentFcAcc = 0;
            int streak = 0, maxStreak = 0; 
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
                    if (note.isBlock) {
                        fcScore += (int)MathF.Round((float)(maxCounter.Multiplier * scoreForMaxScore) * currentFcAcc);
                    }
                }
                else
                {
                    normalCounter.Increase();
                    combo++;
                    multiplier = normalCounter.Multiplier;
                    score += multiplier * note.score;
                    fcScore += maxCounter.Multiplier * note.score;
                }

                if (!potentiallyPoodle) {
                    if (note.score == 115) {
                        streak++;
                    } else if (note.isBlock) {
                        if (streak > maxStreak) {
                            maxStreak = streak; 
                        }
                        streak = 0;
                    }
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

                currentFcAcc = (float)fcScore / maxScore;
            }
            if (streak > maxStreak) {
                maxStreak = streak; 
            }
            result.fcAcc = currentFcAcc;

            return (result, allStructs, maxCombo, maxStreak);
        }

        public static ScoreGraphTracker ScoreGraph(List<NoteStruct> structs, int replayLength)
        {
            ScoreGraphTracker scoreGraph = new ScoreGraphTracker();
            scoreGraph.graph = new List<float>(new float[replayLength]);

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
                    scoreGraph.graph[i] = cumulative / (float)delimiter;
                }
                if (scoreGraph.graph[i] == 0)
                {
                    scoreGraph.graph[i] = i == 0 ? 1.0f : scoreGraph.graph[i - 1];
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

        public static async Task AverageStatistic(IEnumerable<Task<ScoreStatistic>> statisticsAsync, ScoreStatistic leaderboardStatistic) {
            var statistics = (await Task.WhenAll(statisticsAsync)).Where(st => st != null).ToList();


            leaderboardStatistic.winTracker = new WinTracker {
                won = statistics.Average(st => st.winTracker.won ? 1.0 : 0.0) > 0.5,
                endTime = statistics.Average(st => st.winTracker.endTime),
                nbOfPause = (int)Math.Round(statistics.Average(st => st.winTracker.nbOfPause)),
                jumpDistance = statistics.Average(st => st.winTracker.jumpDistance),
                averageHeight = statistics.Average(st => st.winTracker.averageHeight),
                totalScore = (int)statistics.Average(st => st.winTracker.totalScore)
            };

            leaderboardStatistic.hitTracker = new HitTracker {
                maxCombo = (int)Math.Round(statistics.Average(st => st.hitTracker.maxCombo)),
                leftMiss = (int)Math.Round(statistics.Average(st => st.hitTracker.leftMiss)),
                rightMiss = (int)Math.Round(statistics.Average(st => st.hitTracker.rightMiss)),
                leftBadCuts = (int)Math.Round(statistics.Average(st => st.hitTracker.leftBadCuts)),
                rightBadCuts = (int)Math.Round(statistics.Average(st => st.hitTracker.rightBadCuts)),
                leftBombs = (int)Math.Round(statistics.Average(st => st.hitTracker.leftBombs)),
                rightBombs = (int)Math.Round(statistics.Average(st => st.hitTracker.rightBombs))
            };

            leaderboardStatistic.accuracyTracker = new AccuracyTracker {
                accRight = statistics.Average(st => st.accuracyTracker.accRight),
                accLeft = statistics.Average(st => st.accuracyTracker.accLeft),
                leftPreswing = statistics.Average(st => st.accuracyTracker.leftPreswing),
                rightPreswing = statistics.Average(st => st.accuracyTracker.rightPreswing),
                averagePreswing = statistics.Average(st => st.accuracyTracker.averagePreswing),
                leftPostswing = statistics.Average(st => st.accuracyTracker.leftPostswing),
                rightPostswing = statistics.Average(st => st.accuracyTracker.rightPostswing),
                leftTimeDependence = statistics.Average(st => st.accuracyTracker.leftTimeDependence),
                rightTimeDependence = statistics.Average(st => st.accuracyTracker.rightTimeDependence),
                leftAverageCut = AverageList(statistics.Select(st => st.accuracyTracker.leftAverageCut).ToList()),
                rightAverageCut = AverageList(statistics.Select(st => st.accuracyTracker.rightAverageCut).ToList()),
                gridAcc = AverageList(statistics.Select(st => st.accuracyTracker.gridAcc).ToList())
            };

            leaderboardStatistic.scoreGraphTracker = new ScoreGraphTracker {
                graph = AverageList(statistics.Select(st => st.scoreGraphTracker.graph).ToList())
            };
        }
    }
}