using System;

namespace ReplayDecoder
{
    public class NoteStructLocal
    {
        public int score;
        public int id;
        public bool isBlock;
        public float time;
        public float spawnTime;
        public ScoringType scoringType;
        public int Color;

        public float multiplier;
        public int totalScore;
        public int maxScore;
        public float accuracy;
        public int combo;
        public float energy;
    }

    public class FunnyReplayStatistic
    {
        public static (ScoreStatistic?, string?) ProcessReplay(Replay replay)
        {
            try {
                ScoreStatistic result = new ScoreStatistic();
                float firstNoteTime = replay.notes.FirstOrDefault()?.eventTime ?? 0.0f;
                float lastNoteTime = replay.notes.LastOrDefault()?.eventTime ?? 0.0f;

                var filteredPauses = replay.pauses.Where(p => p.time >= firstNoteTime && p.time <= lastNoteTime);
                result.winTracker = new WinTracker
                {
                    won = replay.info.failTime < 0.01,
                    failTime = replay.info.failTime,
                    endTime = (replay.frames.LastOrDefault() != null) ? replay.frames.Last().time : 0,
                    nbOfPause = filteredPauses.Count(),
                    totalPauseDuration = filteredPauses.Count() > 0 ? filteredPauses.Sum(p => p.duration) : 0,
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
                    NoteParams param = new NoteParams(item.noteID, item.eventType);
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

                (AccuracyTracker accuracy, List<NoteStructLocal> structs, int maxCombo, int? maxStreak) = Accuracy(replay.notes, replay.walls);
                result.hitTracker.maxCombo = maxCombo;
                result.hitTracker.maxStreak = maxStreak;
                result.winTracker.totalScore = structs.Last().totalScore;
                result.winTracker.maxScore = structs.Last().maxScore;
                result.accuracyTracker = accuracy;
                result.scoreGraphTracker = ScoreGraph(structs, (int)replay.frames.Last().time);

                if (replay.info.modifiers.Contains("NF")) {
                    result.winTracker.failTime = structs.FirstOrDefault(s => s.energy <= 0)?.time ?? 0;
                }

                return (result, null);
            } catch (Exception e) {
                return (null, e.Message);
            }
        }

        public static (AccuracyTracker, List<NoteStructLocal>, int, int?) Accuracy(List<NoteEvent> notes, List<WallEvent> walls)
        {
            AccuracyTracker result = new AccuracyTracker();
            result.gridAcc = new List<float>(new float[12]);
            result.leftAverageCut = new List<float>(new float[3]);
            result.rightAverageCut = new List<float>(new float[3]);

            int[] gridCounts = new int[12];
            int[] leftCuts = new int[3];
            int[] rightCuts = new int[3];

            List<NoteStructLocal> allStructs = new List<NoteStructLocal>();
            foreach (var note in notes)
            {
                NoteParams param = new NoteParams(note.noteID, note.eventType);
                int scoreValue = ScoreForNote(note, param.scoringType);

                if (scoreValue > 0)
                {
                    int index = param.noteLineLayer * 4 + param.lineIndex;
                    if (index > 11 || index < 0) {
                        index = 0;
                    }

                    var scoreDefinition = ScoringExtensions.ScoreDefinitions[param.scoringType];

                    if (scoreDefinition.accApplicable)
                    {
                        gridCounts[index]++;
                        result.gridAcc[index] += (float)scoreValue;
                    }

                    (int before, int after, int acc) = CutScoresForNote(note, param.scoringType);
                    if (param.colorType == 0)
                    {
                        if (scoreDefinition.beforeCutApplicable) {
                            if (note.noteCutInfo.beforeCutRating < 5) {
                                result.leftAverageCut[0] += (float)before;
                                result.leftPreswing += note.noteCutInfo.beforeCutRating;
                                leftCuts[0]++;
                            }
                        }
                        if (scoreDefinition.accApplicable)
                        {
                            result.leftAverageCut[1] += (float)acc;
                            result.accLeft += (float)scoreValue;
                            if (scoreDefinition.maxAfterCutScore == 0) {
                                result.accLeft += (float)ScoringExtensions.NORMAL_MAX_AFTER_CUT;
                            }
                            if (scoreDefinition.maxBeforeCutScore == 0) {
                                result.accLeft += (float)ScoringExtensions.NORMAL_MAX_BEFORE_CUT;
                            }

                            result.leftTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            leftCuts[1]++;
                        }
                        if (scoreDefinition.afterCutApplicable)
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
                        if (scoreDefinition.beforeCutApplicable)
                        {
                            if (note.noteCutInfo.beforeCutRating < 5) {
                                result.rightAverageCut[0] += (float)before;
                                result.rightPreswing += note.noteCutInfo.beforeCutRating;
                                rightCuts[0]++;
                            }
                        }
                        if (scoreDefinition.accApplicable)
                        {
                            result.rightAverageCut[1] += (float)acc;
                            result.rightTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            result.accRight += (float)scoreValue;
                            if (scoreDefinition.maxAfterCutScore == 0) {
                                result.accRight += (float)ScoringExtensions.NORMAL_MAX_AFTER_CUT;
                            }
                            if (scoreDefinition.maxBeforeCutScore == 0) {
                                result.accRight += (float)ScoringExtensions.NORMAL_MAX_BEFORE_CUT;
                            }
                            rightCuts[1]++;
                        }
                        if (scoreDefinition.afterCutApplicable)
                        {
                            if (note.noteCutInfo.afterCutRating < 5) {
                                result.rightAverageCut[2] += (float)after;
                                result.rightPostswing += note.noteCutInfo.afterCutRating;
                                rightCuts[2]++;
                            }
                        }
                    }
                }

                allStructs.Add(new NoteStructLocal
                {
                    time = note.eventTime,
                    id = note.noteID,
                    isBlock = note.eventType != NoteEventType.bomb,
                    score = scoreValue,
                    scoringType = param.scoringType,
                    spawnTime = note.spawnTime,
                    Color = note.noteParams.colorType
                });
            }

            foreach (var wall in walls)
            {
                allStructs.Add(new NoteStructLocal
                {
                    time = wall.spawnTime > wall.time ? wall.spawnTime : wall.time,
                    spawnTime = wall.spawnTime > wall.time ? wall.time : wall.spawnTime,
                    id = wall.wallID,
                    energy = wall.energy,
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
                    if ((ordered[i].time - ordered[i - 1].time) < 0.01 && ordered[i].scoringType != ScoringType.ChainLink && ordered[i - 1].scoringType != ScoringType.ChainLink) {
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
            int fcScore = 0; float currentFcAcc = 1;
            int streak = 0, maxStreak = 0; 
            float energy = 0.5f;
            MultiplierCounter maxCounter = new MultiplierCounter();
            MultiplierCounter normalCounter = new MultiplierCounter();

            for (var i = 0; i < allStructs.Count(); i++)
            {
                var note = allStructs[i];
                var scoreDefinition = ScoringExtensions.ScoreDefinitions[note.scoringType];
                int scoreForMaxScore = note.Color == 1 ? 0 : scoreDefinition.maxCutScore;

                if (note.isBlock) {
                    maxCounter.Increase();
                    maxScore += maxCounter.Multiplier * scoreForMaxScore;
                }

                if (note.score < 0)
                {
                    normalCounter.Decrease();
                    multiplier = normalCounter.Multiplier;
                    combo = 0;
                    if (note.isBlock) {
                        fcScore += (int)MathF.Round((float)(maxCounter.Multiplier * scoreForMaxScore) * currentFcAcc);
                    }
                    switch (note.score) {
					    case -2: // badcut
						    if (note.scoringType == ScoringType.ChainLink) {
							    energy -= 0.025f;
						    } else {
							    energy -= 0.1f;
						    }
						    break;
					    case -3: // miss
					    case -4: // bomb
						    if (note.scoringType == ScoringType.ChainLink) {
							    energy -= 0.03f;
						    } else {
							    energy -= 0.15f;
						    }
						    break;
                        case -5: // wall
                            if (note.energy <= 1.0f) {
                                energy = note.energy;
                            }
                            break;

					    default:
						    break;
				    }
                }
                else
                {
                    normalCounter.Increase();
                    combo++;
                    multiplier = normalCounter.Multiplier;
                    score += multiplier * note.score * (note.Color == 1 ? -1 : 1);
                    fcScore += maxCounter.Multiplier * note.score * (note.Color == 1 ? -1 : 1);

                    if (note.scoringType == ScoringType.ChainLink) {
					    energy += 1 / 500;
				    } else {
					    energy += 0.01f;
				    }
				    if (energy > 1) {
					    energy = 1;
				    }
                }

                if (!potentiallyPoodle && note.scoringType != ScoringType.ChainLink) {
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
                note.maxScore = maxScore;
                note.combo = combo;
                note.energy = energy;

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

            return (result, allStructs, maxCombo, potentiallyPoodle ? null : maxStreak);
        }

        public static ScoreGraphTracker ScoreGraph(List<NoteStructLocal> structs, int replayLength)
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

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            return value > max ? max : value;
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
            
            var scoreDefinition = ScoringExtensions.ScoreDefinitions[scoringType];

            double beforeCutRawScore = Clamp(Math.Round(scoreDefinition.maxBeforeCutScore * cut.beforeCutRating), scoreDefinition.minBeforeCutScore, scoreDefinition.maxBeforeCutScore);
            double afterCutRawScore = Clamp(Math.Round(scoreDefinition.maxAfterCutScore * cut.afterCutRating), scoreDefinition.minAfterCutScore, scoreDefinition.maxAfterCutScore);
            double cutDistanceRawScore = 0;
            if (scoreDefinition.fixedCutScore > 0)
            {
                cutDistanceRawScore = scoreDefinition.fixedCutScore;
            }
            else
            {
                double num = 1 - Clamp(cut.cutDistanceToCenter / 0.3f);
                cutDistanceRawScore = Math.Round(scoreDefinition.maxCenterDistanceCutScore * num);
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