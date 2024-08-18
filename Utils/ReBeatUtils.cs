using ReplayDecoder;

namespace BeatLeader_Server.Utils {
    public class ReBeatUtils {
        public static string MODE_IDENTIFIER = "ReBeat_Standard";
        public static int GetScore(Replay replay) {
            return replay.notes.Select(n => ScoreForNote(n, replay.info.modifiers)).Sum();
        }

        public static int MaxScoreForNote(int count) {
            return count * 100;
        }

        public static (ScoreStatistic?, string?) ProcessReplay(Replay replay)
        {
            try {
                (ScoreStatistic? stats, string? error) = ReplayStatistic.ProcessReplay(replay);
                if (stats != null) {
                    (AccuracyTracker accuracy, List<NoteStruct> structs, int maxCombo, int? maxStreak) = Accuracy(replay);
                    stats.hitTracker.maxStreak = maxStreak;
                    stats.winTracker.totalScore = structs.Last().totalScore;
                    stats.winTracker.maxScore = structs.Last().maxScore;
                    stats.accuracyTracker = accuracy;
                    stats.scoreGraphTracker = ScoreGraph(structs, (int)replay.frames.Last().time);
                }
                return (stats, error);
            } catch (Exception e) {
                return (null, e.Message);
            }
        }

        public static int ScoreForNote(NoteEvent note, string modifiers)
        {
            if (note.eventType == NoteEventType.good)
            {
                (int before, int after, int acc) = CutScoresForNote(note, modifiers);

                return before + after + acc;
            }
            return 0;
        }

        public static (int, int, int) CutScoresForNote(NoteEvent note, string modifiers)
        {
            var cut = note.noteCutInfo;
            double beforeCutRawScore = Math.Clamp(Math.Round(30 * cut.beforeCutRating), 0, 30);
            double afterCutRawScore = Math.Clamp(Math.Round(20 * cut.afterCutRating), 0, 20);

            float sectorSize = 0.6f / 29f;
            float cutDistanceToCenter = cut.cutDistanceToCenter;

            float[] sectors = modifiers.Contains("PM") ? [4.5f, 8.5f, 11.5f, 13.5f, 14.5f] : 
                modifiers.Contains("EZ") ? [ 7.5f, 10.5f, 12.5f, 13.5f, 14.5f ] : 
                [ 6.5f, 9.5f, 11.5f, 13.5f, 14.5f ];

            double cutDistanceRawScore = cutDistanceToCenter < sectorSize * sectors[0] ? 50 :
                cutDistanceToCenter < sectorSize * sectors[1] ? 44 :
                cutDistanceToCenter < sectorSize * sectors[2] ? 36 :
                cutDistanceToCenter < sectorSize * sectors[3] ? 22 :
                cutDistanceToCenter < sectorSize * sectors[4] ? 10 : 0;

            return ((int)beforeCutRawScore, (int)afterCutRawScore, (int)cutDistanceRawScore);
        }

        public static (AccuracyTracker, List<NoteStruct>, int, int?) Accuracy(Replay replay)
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
                int scoreValue = ScoreForNote(note, replay.info.modifiers);

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

                    (int before, int after, int acc) = CutScoresForNote(note, replay.info.modifiers);
                    if (param.colorType == 0)
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement) {
                            if (note.noteCutInfo.beforeCutRating < 5) {
                                result.leftAverageCut[0] += (float)before;
                                result.leftPreswing += note.noteCutInfo.beforeCutRating;
                                leftCuts[0]++;
                            }
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
                            if (note.noteCutInfo.afterCutRating < 5) {
                                result.leftAverageCut[2] += (float)after;
                                result.leftPostswing += note.noteCutInfo.afterCutRating;
                                leftCuts[2]++;
                            }
                        }
                    }
                    else
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            if (note.noteCutInfo.beforeCutRating < 5) {
                                result.rightAverageCut[0] += (float)before;
                                result.rightPreswing += note.noteCutInfo.beforeCutRating;
                                rightCuts[0]++;
                            }
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
                            if (note.noteCutInfo.afterCutRating < 5) {
                                result.rightAverageCut[2] += (float)after;
                                result.rightPostswing += note.noteCutInfo.afterCutRating;
                                rightCuts[2]++;
                            }
                        }
                    }
                }

                allStructs.Add(new NoteStruct
                {
                    time = note.eventTime,
                    id = note.noteID,
                    isBlock = note.eventType != NoteEventType.bomb,
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
                    if ((ordered[i].time - ordered[i - 1].time) < 0.01 && ordered[i].scoringType != ScoringType.BurstSliderElement && ordered[i - 1].scoringType != ScoringType.BurstSliderElement) {
                        potentiallyPoodle = true;
                        break;
                    }
                }
                if (potentiallyPoodle) {
                    break;
                }
            }

            int score = 0, noteIndex = 0;
            int maxScore = 0;
            int fcScore = 0; float currentFcAcc = 1;
            int streak = 0, maxStreak = 0;

            for (var i = 0; i < allStructs.Count(); i++)
            {
                var note = allStructs[i];
                int scoreForMaxScore = 100;

                if (note.isBlock) {
                    maxScore += scoreForMaxScore;
                }

                if (note.score < 0)
                {
                    if (note.isBlock) {
                        fcScore += (int)MathF.Round((float)(scoreForMaxScore) * currentFcAcc);
                    }
                }
                else
                {
                    score += note.score;
                    fcScore += note.score;
                }

                if (!potentiallyPoodle && note.scoringType != ScoringType.BurstSliderElement) {
                    if (note.score == scoreForMaxScore) {
                        streak++;
                    } else if (note.isBlock) {
                        if (streak > maxStreak) {
                            maxStreak = streak; 
                        }
                        streak = 0;
                    }
                }

                note.totalScore = score;
                note.maxScore = maxScore;

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

            return (result, allStructs, 0, potentiallyPoodle ? null : maxStreak);
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
    }
}
