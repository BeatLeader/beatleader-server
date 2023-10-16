using AngleSharp.Common;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using System.Runtime.ConstrainedExecution;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Utils
{
    static class ReplayUtils
    {
        static List<(double, double)> pointList = new List<(double, double)> { 
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.101),
                (0.955, 1.047),
                (0.95, 1.000),
                (0.94, 0.919),
                (0.93, 0.847),
                (0.92, 0.786),
                (0.91, 0.734),
                (0.9, 0.692),
                (0.875, 0.606),
                (0.85, 0.537),
                (0.825, 0.480),
                (0.8, 0.429),
                (0.75, 0.345),
                (0.7, 0.286),
                (0.65, 0.246),
                (0.6, 0.217),
                (0.0, 0.000) };

        static List<(double, double)> pointList2 = new List<(double, double)> { 
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.094),
                (0.955, 1.039),
                (0.95, 1.000),
                (0.94, 0.931),
                (0.93, 0.867),
                (0.92, 0.813),
                (0.91, 0.768),
                (0.9, 0.729),
                (0.875, 0.650),
                (0.85, 0.581),
                (0.825, 0.522),
                (0.8, 0.473),
                (0.75, 0.404),
                (0.7, 0.345),
                (0.65, 0.296),
                (0.6, 0.256),
                (0.0, 0.000), };

        public static float Curve(float acc)
        {
            int i = 0;
            for (; i < pointList.Count; i++)
            {
                if (pointList[i].Item1 <= acc) {
                    break;
                }
            }
    
            if (i == 0) {
                i = 1;
            }
    
            double middle_dis = (acc - pointList[i-1].Item1) / (pointList[i].Item1 - pointList[i-1].Item1);
            return (float)(pointList[i-1].Item2 + middle_dis * (pointList[i].Item2 - pointList[i-1].Item2));
        }

        public static float Curve2(float acc)
        {
            int i = 0;
            for (; i < pointList2.Count; i++)
            {
                if (pointList2[i].Item1 <= acc) {
                    break;
                }
            }
    
            if (i == 0) {
                i = 1;
            }
    
            double middle_dis = (acc - pointList2[i-1].Item1) / (pointList2[i].Item1 - pointList2[i-1].Item1);
            return (float)(pointList2[i-1].Item2 + middle_dis * (pointList2[i].Item2 - pointList2[i-1].Item2));
        }

        public static float AccRating(float? predictedAcc, float? passRating, float? techRating) {
            float difficulty_to_acc;
            if (predictedAcc > 0) {
                difficulty_to_acc = 15f / Curve((predictedAcc ?? 0) + 0.0022f);
            } else {
                float tiny_tech = 0.0208f * (techRating ?? 0) + 1.1284f;
                difficulty_to_acc = (-MathF.Pow(tiny_tech, -(passRating ?? 0)) + 1) * 8 + 2 + 0.01f * (techRating ?? 0) * (passRating ?? 0);
            }
            if (float.IsInfinity(difficulty_to_acc) || float.IsNaN(difficulty_to_acc) || float.IsNegativeInfinity(difficulty_to_acc)) {
                difficulty_to_acc = 0;
            }
            return difficulty_to_acc;
        }

        private static float Inflate(float peepee) {
            return (650f * MathF.Pow(peepee, 1.3f)) / MathF.Pow(650f, 1.3f);
        }

        private static (float, float, float) GetPp(LeaderboardContexts context, float accuracy, float accRating, float passRating, float techRating) {

            float passPP = 15.2f * MathF.Exp(MathF.Pow(passRating, 1 / 2.62f)) - 30f;
            if (float.IsInfinity(passPP) || float.IsNaN(passPP) || float.IsNegativeInfinity(passPP) || passPP < 0)
            {
                passPP = 0;
            }
            float accPP = context == LeaderboardContexts.Golf ? accuracy * accRating * 42f : Curve2(accuracy) * accRating * 34f;
            float techPP = MathF.Exp(1.9f * accuracy) * 1.08f * techRating;
            
            return (passPP, accPP, techPP);
        }

        public static float ToStars(float accRating, float passRating, float techRating) {
            (float passPP, float accPP, float techPP) = GetPp(LeaderboardContexts.General, 0.96f, accRating, passRating, techRating);

            return Inflate(passPP + accPP + techPP) / 52f;
        }

        public static (float, float, float, float, float) PpFromScore(
            float accuracy, 
            LeaderboardContexts context,
            string modifiers, 
            ModifiersMap modifierValues, 
            ModifiersRating? modifiersRating,
            float accRating, 
            float passRating, 
            float techRating, 
            bool timing)
        {
            float mp = modifierValues.GetTotalMultiplier(modifiers, modifiersRating == null);

            float rawPP = 0; float fullPP = 0; float passPP = 0; float accPP = 0; float techPP = 0; float increase = 0; 
            if (!timing) {
                if (!modifiers.Contains("NF"))
                {
                    (passPP, accPP, techPP) = GetPp(context, accuracy, accRating, passRating, techRating);
                        
                    rawPP = Inflate(passPP + accPP + techPP);
                    if (modifiersRating != null) {
                        var modifiersMap = modifiersRating.ToDictionary<float>();
                        foreach (var modifier in modifiers.ToUpper().Split(","))
                        {
                            if (modifiersMap.ContainsKey(modifier + "AccRating")) { 
                                accRating = modifiersMap[modifier + "AccRating"]; 
                                passRating = modifiersMap[modifier + "PassRating"]; 
                                techRating = modifiersMap[modifier + "TechRating"]; 

                                break;
                            }
                        }
                    }
                    (passPP, accPP, techPP) = GetPp(context, accuracy, accRating * mp, passRating * mp, techRating * mp);
                    fullPP = Inflate(passPP + accPP + techPP);
                    if ((passPP + accPP + techPP) > 0) {
                        increase = fullPP / (passPP + accPP + techPP);
                    }
                }
            } else {
                rawPP = accuracy * passRating * 55f;
                fullPP = accuracy * passRating * 55f;
            }

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP)) {
                rawPP = 0;
            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP)) {
                fullPP = 0;
            }

            return (fullPP, fullPP - rawPP, passPP * increase, accPP * increase, techPP * increase);
        }

        public static (float, float, float, float, float) PpFromScore(Score s, DifficultyDescription difficulty) {
            return PpFromScore(
                s.Accuracy, 
                LeaderboardContexts.General,
                s.Modifiers, 
                difficulty.ModifierValues, 
                difficulty.ModifiersRating,
                difficulty.AccRating ?? 0.0f, 
                difficulty.PassRating ?? 0.0f, 
                difficulty.TechRating ?? 0.0f, 
                difficulty.ModeName.ToLower() == "rhythmgamestandard");
        }

        public static (float, float, float, float, float) PpFromScoreResponse(
            ScoreResponse s, 
            float accRating, 
            float passRating, 
            float techRating, 
            ModifiersMap modifiers,
            ModifiersRating? modifiersRating)
        {
            return PpFromScore(s.Accuracy, LeaderboardContexts.General, s.Modifiers, modifiers, modifiersRating, accRating, passRating, techRating, false);
        }

        public static (float, float, float, float, float) PpFromScoreResponse(
            ScoreResponse s, 
            DifficultyDescription diff)
        {
            return PpFromScore(s.Accuracy, LeaderboardContexts.General, s.Modifiers, diff.ModifierValues, diff.ModifiersRating, diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0, false);
        }

        public static (Score, int) ProcessReplay(Replay replay, DifficultyDescription difficulty) {
            ReplayInfo info = replay.info;
            Score score = new Score();
            
            score.BaseScore = info.score;
            score.Modifiers = info.modifiers;
            score.Hmd = HMDFromName(info.hmd);
            score.Controller = ControllerFromName(info.controller);

            var status = difficulty.Status;
            var modifers = difficulty.ModifierValues ?? new ModifiersMap();
            bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
            bool hasPp = status == DifficultyStatus.ranked || qualification;

            int maxScore = difficulty.MaxScore > 0 ? difficulty.MaxScore : MaxScoreForNote(difficulty.Notes);
            if (hasPp)
            {
                score.ModifiedScore = (int)(score.BaseScore * modifers.GetNegativeMultiplier(info.modifiers));
            } else
            {
                score.ModifiedScore = (int)((score.BaseScore + (int)((float)(maxScore - score.BaseScore) * (modifers.GetPositiveMultiplier(info.modifiers) - 1))) * modifers.GetNegativeMultiplier(info.modifiers));
            }
            
            score.Accuracy = (float)score.BaseScore / (float)maxScore;
            score.Modifiers = info.modifiers;

            if (hasPp) {
                (score.Pp, score.BonusPp, score.PassPP, score.AccPP, score.TechPP) = PpFromScore(score, difficulty);
            }

            score.Qualification = qualification;
            score.Platform = info.platform + "," + info.gameVersion + "," + info.version;
            score.Timepost = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            score.Timeset = info.timestamp;
            score.IgnoreForStats = difficulty.ModeName.ToLower() == "rhythmgamestandard" || difficulty.ModeName.ToLower().Contains("controllable") || info.modifiers.Contains("NF");
            score.Migrated = true;

            if (info.modifiers.Contains("NF")) {
                score.Priority = 3;
            } else if (info.modifiers.Contains("NB") || info.modifiers.Contains("NA")) {
                score.Priority = 2;
            } else if (info.modifiers.Contains("NO")) {
                score.Priority = 1;
            }

            score.WallsHit = replay.walls.Count;
            float firstNoteTime = replay.notes.FirstOrDefault()?.eventTime ?? 0.0f;
            float lastNoteTime = replay.notes.LastOrDefault()?.eventTime ?? 0.0f;
            score.Pauses = replay.pauses.Where(p => p.time >= firstNoteTime && p.time <= lastNoteTime).Count();
            foreach (var item in replay.notes)
            {
                switch (item.eventType)
                {
                    case NoteEventType.bad:
                        score.BadCuts++;
                        break;
                    case NoteEventType.miss:
                        score.MissedNotes++;
                        break;
                    case NoteEventType.bomb:
                        score.BombCuts++;
                        break;
                    default:
                        break;
                }
            }
            score.FullCombo = score.BombCuts == 0 && score.MissedNotes == 0 && score.WallsHit == 0 && score.BadCuts == 0;
            score.ContextExtensions = new List<ScoreContextExtension>();
            var noModsExtension = NoModsContextExtension(score, difficulty);
            if (noModsExtension != null) {
                score.ContextExtensions.Add(noModsExtension);
            }
            var noPauseExtenstion = NoPauseContextExtension(score);
            if (noPauseExtenstion != null) {
                score.ContextExtensions.Add(noPauseExtenstion);
            }
            var golfExtension = GolfContextExtension(score, difficulty);
            if (golfExtension != null) {
                score.ContextExtensions.Add(golfExtension);
            }

            return (score, maxScore);
        }

        public static ScoreContextExtension? NoModsContextExtension(Score score, DifficultyDescription difficulty) {
            if (!(score.Modifiers?.Length == 0 || score.Modifiers == "IF" || score.Modifiers == "BE")) {
                return null;
            }

            var result = new ScoreContextExtension {
                Context = LeaderboardContexts.NoMods,
                BaseScore = score.BaseScore,
                ModifiedScore = score.BaseScore,
                Timeset = score.Timepost,
                Modifiers = "",
                Accuracy = score.Accuracy,
                Qualification = score.Qualification
            };

            if (score.Pp > 0) {
                (result.Pp, result.BonusPp, result.PassPP, result.AccPP, result.TechPP) = PpFromScore(score.Accuracy, LeaderboardContexts.NoMods, "", difficulty.ModifierValues, 
                difficulty.ModifiersRating,
                difficulty.AccRating ?? 0.0f, 
                difficulty.PassRating ?? 0.0f, 
                difficulty.TechRating ?? 0.0f, 
                difficulty.ModeName.ToLower() == "rhythmgamestandard");
            }

            return result;
        }

        public static ScoreContextExtension? GolfContextExtension(Score score, DifficultyDescription difficulty) {
            var modifers = difficulty.ModifierValues ?? new ModifiersMap();
            if (modifers.GetNegativeMultiplier(score.Modifiers) < 1 || score.Accuracy > 0.5f) {
                return null;
            }

            var result = new ScoreContextExtension {
                Context = LeaderboardContexts.Golf,
                BaseScore = score.BaseScore,
                ModifiedScore = score.ModifiedScore,
                Timeset = score.Timepost,
                Modifiers = score.Modifiers,
                Accuracy = score.Accuracy,
                Qualification = score.Qualification
            };

            if (score.Pp > 0) {
                (result.Pp, result.BonusPp, result.PassPP, result.AccPP, result.TechPP) = PpFromScore(1 - score.Accuracy, LeaderboardContexts.Golf, score.Modifiers, difficulty.ModifierValues, 
                difficulty.ModifiersRating,
                difficulty.AccRating ?? 0.0f, 
                difficulty.PassRating ?? 0.0f, 
                difficulty.TechRating ?? 0.0f, 
                difficulty.ModeName.ToLower() == "rhythmgamestandard");
            }
            
            return result;
        }

        public static ScoreContextExtension? NoPauseContextExtension(Score score) {
            if (score.Pauses != 0) return null;
            return new ScoreContextExtension {
                Context = LeaderboardContexts.NoPause,
                BaseScore = score.BaseScore,
                ModifiedScore = score.ModifiedScore,
                Timeset = score.Timepost,
                Modifiers = score.Modifiers,
                Accuracy = score.Accuracy,
                Pp = score.Pp,
                AccPP = score.AccPP,
                TechPP = score.TechPP,
                PassPP = score.PassPP,
                Qualification = score.Qualification
            };
        }

        public static HMD HMDFromName(string hmdName) {
            string lowerHmd = hmdName.ToLower();

            if (lowerHmd.Contains("pico") && lowerHmd.Contains("4")) return HMD.picoNeo4;
            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("3")) return HMD.picoNeo3;
            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("2")) return HMD.picoNeo2;
            if (lowerHmd.Contains("vive pro 2")) return HMD.vivePro2;
            if (lowerHmd.Contains("vive elite")) return HMD.viveElite;
            if (lowerHmd.Contains("focus3")) return HMD.viveFocus;
            if (lowerHmd.Contains("miramar")) return HMD.miramar;
            if (lowerHmd.Contains("pimax") && lowerHmd.Contains("8k")) return HMD.pimax8k;
            if (lowerHmd.Contains("pimax") && lowerHmd.Contains("5k")) return HMD.pimax5k;
            if (lowerHmd.Contains("pimax") && lowerHmd.Contains("artisan")) return HMD.pimaxArtisan;
            if (lowerHmd.Contains("pimax") && lowerHmd.Contains("crystal")) return HMD.pimaxCrystal;

            if (lowerHmd.Contains("controllable")) return HMD.controllable;

            if (lowerHmd.Contains("hp reverb")) return HMD.hpReverb;
            if (lowerHmd.Contains("samsung windows")) return HMD.samsungWmr;
            if (lowerHmd.Contains("qiyu dream")) return HMD.qiyuDream;
            if (lowerHmd.Contains("disco")) return HMD.disco;
            if (lowerHmd.Contains("lenovo explorer")) return HMD.lenovoExplorer;
            if (lowerHmd.Contains("acer")) return HMD.acerWmr;
            if (lowerHmd.Contains("arpara")) return HMD.arpara;
            if (lowerHmd.Contains("dell visor")) return HMD.dellVisor;

            if (lowerHmd.Contains("e3")) return HMD.e3;
            if (lowerHmd.Contains("e4")) return HMD.e4;

            if (lowerHmd.Contains("vive dvt")) return HMD.viveDvt;
            if (lowerHmd.Contains("3glasses s20")) return HMD.glasses20;
            if (lowerHmd.Contains("hedy")) return HMD.hedy;
            if (lowerHmd.Contains("vaporeon")) return HMD.vaporeon;
            if (lowerHmd.Contains("huaweivr")) return HMD.huaweivr;
            if (lowerHmd.Contains("asus mr0")) return HMD.asusWmr;
            if (lowerHmd.Contains("cloudxr")) return HMD.cloudxr;
            if (lowerHmd.Contains("vridge")) return HMD.vridge;
            if (lowerHmd.Contains("medion mixed reality")) return HMD.medion;

            if (lowerHmd.Contains("quest") && lowerHmd.Contains("3")) return HMD.quest3;
            if (lowerHmd.Contains("quest") && lowerHmd.Contains("2")) return HMD.quest2;
            if (lowerHmd.Contains("quest") && lowerHmd.Contains("pro")) return HMD.questPro;

            if (lowerHmd.Contains("vive cosmos")) return HMD.viveCosmos;
            if (lowerHmd.Contains("vive_cosmos")) return HMD.viveCosmos;
            if (lowerHmd.Contains("index")) return HMD.index;
            if (lowerHmd.Contains("quest")) return HMD.quest;
            if (lowerHmd.Contains("rift s")) return HMD.riftS;
            if (lowerHmd.Contains("rift_s")) return HMD.riftS;
            if (lowerHmd.Contains("windows")) return HMD.wmr;
            if (lowerHmd.Contains("vive pro")) return HMD.vivePro;
            if (lowerHmd.Contains("vive_pro")) return HMD.vivePro;
            if (lowerHmd.Contains("vive")) return HMD.vive;
            if (lowerHmd.Contains("rift")) return HMD.rift;

            return HMD.unknown;
        }

        public static ControllerEnum ControllerFromName(string controllerName) {
            string lowerController = controllerName.ToLower();

            if (lowerController.Contains("vive tracker") && lowerController.Contains("3")) return ControllerEnum.viveTracker3;
            if (lowerController.Contains("vive tracker") && lowerController.Contains("pro")) return ControllerEnum.viveTracker2;
            if (lowerController.Contains("vive tracker")) return ControllerEnum.viveTracker;

            if (lowerController.Contains("vive") && lowerController.Contains("cosmos")) return ControllerEnum.viveCosmos;
            if (lowerController.Contains("vive") && lowerController.Contains("pro") && lowerController.Contains("2")) return ControllerEnum.vivePro2;
            if (lowerController.Contains("vive") && lowerController.Contains("pro")) return ControllerEnum.vivePro;
            if (lowerController.Contains("vive")) return ControllerEnum.vive;

            if (lowerController.Contains("pico") && lowerController.Contains("phoenix")) return ControllerEnum.picophoenix;
            if (lowerController.Contains("pico neo") && lowerController.Contains("3")) return ControllerEnum.picoNeo3;
            if (lowerController.Contains("pico neo") && lowerController.Contains("2")) return ControllerEnum.picoNeo2;
            if (lowerController.Contains("knuckles")) return ControllerEnum.knuckles;
            if (lowerController.Contains("miramar")) return ControllerEnum.miramar;

            if (lowerController.Contains("gamepad")) return ControllerEnum.gamepad;
            if (lowerController.Contains("joy-con")) return ControllerEnum.joycon;
            if (lowerController.Contains("steam deck")) return ControllerEnum.steamdeck;
            
            if (lowerController.Contains("quest pro")) return ControllerEnum.questPro;
            if (lowerController.Contains("quest 3")) return ControllerEnum.quest3;
            if (lowerController.Contains("quest2")) return ControllerEnum.quest2;
            if (lowerController.Contains("oculus touch") || lowerController.Contains("rift cv1")) return ControllerEnum.oculustouch;
            if (lowerController.Contains("rift s") || lowerController.Contains("quest")) return ControllerEnum.oculustouch2;

            if (lowerController.Contains("066a")) return ControllerEnum.hpMotion;
            if (lowerController.Contains("065d")) return ControllerEnum.odyssey;
            if (lowerController.Contains("windows")) return ControllerEnum.wmr;

            if (lowerController.Contains("nolo")) return ControllerEnum.nolo;
            if (lowerController.Contains("disco")) return ControllerEnum.disco;
            if (lowerController.Contains("hands")) return ControllerEnum.hands;

            if (lowerController.Contains("pimax")) return ControllerEnum.pimax;
            if (lowerController.Contains("huawei")) return ControllerEnum.huawei;
            if (lowerController.Contains("polaris")) return ControllerEnum.polaris;
            if (lowerController.Contains("tundra")) return ControllerEnum.tundra;
            if (lowerController.Contains("cry")) return ControllerEnum.cry;
            if (lowerController.Contains("e4")) return ControllerEnum.e4;
            if (lowerController.Contains("etee")) return ControllerEnum.etee;

            return ControllerEnum.unknown;
        }

        public static int MaxScoreForNote(int count) {
          int note_score = 115;

          if (count <= 1) // x1 (+1 note)
              return note_score * (0 + (count - 0) * 1);
          if (count <= 5) // x2 (+4 notes)
              return note_score * (1 + (count - 1) * 2);
          if (count <= 13) // x4 (+8 notes)
              return note_score * (9 + (count - 5) * 4);
          // x8
          return note_score * (41 + (count - 13) * 8);
        }

        public static float GetTotalMultiplier(this ModifiersMap modifiersObject, string modifiers, bool speedModifiers)
		{
			float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (!speedModifiers && (modifier == "SF" || modifier == "SS" || modifier == "FS")) continue;

                if (modifiers.Contains(modifier)) { multiplier += modifiersMap[modifier]; }
            }
            

			return multiplier;
		}

        public static float GetPositiveMultiplier(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier) && modifiersMap[modifier] > 0) { multiplier += modifiersMap[modifier]; }
            }

            return multiplier;
        }

        public static float GetNegativeMultiplier(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier) && modifiersMap[modifier] < 0) { multiplier += modifiersMap[modifier]; }
            }

            return multiplier;
        }

        public static (string, float) GetNegativeMultipliers(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;
            List<string> modifierArray = new List<string>();

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier)) {
                    if (modifiersMap[modifier] < 0) {
                        multiplier += modifiersMap[modifier]; 
                        modifierArray.Add(modifier);
                    }
                }
            }

            return (String.Join(",", modifierArray), multiplier);
        }

        public static Dictionary<string, float> LegacyModifiers()
        {
            return new Dictionary<string, float>
            {
                ["DA"] = 0.005f,
                ["FS"] = 0.11f,
                ["SS"] = -0.3f,
                ["SF"] = 0.25f,
                ["GN"] = 0.04f,
                ["NA"] = -0.3f,
                ["NB"] = -0.2f,
                ["NF"] = -0.5f,
                ["NO"] = -0.2f,
                ["PM"] = 0.0f,
                ["SC"] = 0.0f
            };
        }

        public static string? RemoveDuplicates(Replay replay, Leaderboard leaderboard) {
            var groups = replay.notes.GroupBy(n => n.noteID + "_" + n.spawnTime).Where(g => g.Count() > 1).ToList();

            if (groups.Count > 0) {
                int sliderCount = 0;
                foreach (var group in groups)
                {
                    bool slider = false;

                    var toRemove = group.OrderByDescending(n => {
                        NoteParams param = new NoteParams(n.noteID);
                        if (param.scoringType != ScoringType.Default && param.scoringType != ScoringType.Normal) {
                            slider = true;
                        }
                        return ReplayStatisticUtils.ScoreForNote(n, param.scoringType);
                    }).Skip(1).ToList();

                    if (slider) {
                        sliderCount++;
                        continue;
                    }

                    foreach (var removal in toRemove)
                    {
                        replay.notes.Remove(removal);
                    }
                }
                if (sliderCount == groups.Count) return null;
                string? error = null;
                ScoreStatistic? statistic = null;
                try
                {
                    (statistic, error) = ReplayStatisticUtils.ProcessReplay(replay, leaderboard);
                } catch (Exception e) {
                    return error = e.ToString();
                }

                if (statistic != null) {
                    replay.info.score = statistic.winTracker.totalScore;
                } else {
                    return error;
                }
            }
            
            return null;
        }

        public static bool IsPlayerCuttingNotesOnPlatform(Replay replay) {
            if (replay.notes.Count < 20) return true;

            int noteIndex = 0;
            int zSum = 0;

            foreach (var frame in replay.frames)
            {
                if (frame.time >= replay.notes[noteIndex].eventTime) {
                    if (frame.head.position.z > 1.05) {
                        zSum++;
                    }
                    if (zSum == (int)Math.Min(50, replay.notes.Count * 0.1f)) return false;

                    if (noteIndex + 1 != replay.notes.Count) {
                        noteIndex++;
                    } else {
                        break;
                    }
                }
            }

            return true;
        }

        public static bool IsNewScoreBetter(Score? oldScore, Score newScore) {
            if (oldScore == null) return true;
            if (oldScore.Modifiers.Contains("OP")) return true;
            if (newScore.Pp != 0) {
                if (newScore.Pp > oldScore.Pp) return true;
            } else {
                if (newScore.ModifiedScore > oldScore.ModifiedScore) return true;

                if (oldScore.Modifiers.Contains("NF") || 
                    oldScore.Modifiers.Contains("NA") ||
                    oldScore.Modifiers.Contains("NB") ||
                    oldScore.Modifiers.Contains("NO")) {
                    if (!newScore.Modifiers.Contains("NF") && 
                    !newScore.Modifiers.Contains("NA") &&
                    !newScore.Modifiers.Contains("NB") &&
                    !newScore.Modifiers.Contains("NO")) return true;
                }
            }

            return false;
        }

        public static bool IsNewScoreExtensionBetter(ScoreContextExtension? oldScore, ScoreContextExtension newScore) {
            if (oldScore == null) return true;
            if (oldScore.Modifiers.Contains("OP")) return true;
            if (newScore.Pp != 0) {
                if (newScore.Pp > oldScore.Pp) return true;
            } else {
                if (newScore.Context == LeaderboardContexts.Golf) {
                    if (newScore.ModifiedScore < oldScore.ModifiedScore) return true;
                } else {
                    if (newScore.ModifiedScore > oldScore.ModifiedScore) return true;
                }

                if (oldScore.Modifiers.Contains("NF") || 
                    oldScore.Modifiers.Contains("NA") ||
                    oldScore.Modifiers.Contains("NB") ||
                    oldScore.Modifiers.Contains("NO")) {
                    if (!newScore.Modifiers.Contains("NF") && 
                    !newScore.Modifiers.Contains("NA") &&
                    !newScore.Modifiers.Contains("NB") &&
                    !newScore.Modifiers.Contains("NO")) return true;
                }
            }

            return false;
        }

        public static int ScoreForRank(int rank) {
            switch (rank) {
                case 1:
                    return 5;
                case 2:
                    return 3;
                case 3:
                    return 1;
                default:
                    return 0;
            }
        }

        public static int UpdateRankScore(int oldScore, int? oldRank, int newRank) {
            int result = oldScore;
            if (oldRank != null) {
                result -= ScoreForRank(oldRank ?? 4);
            }
            result += ScoreForRank(newRank);

            return result; 
        }

        public static string ReplayFilename(Replay replay, Score? score, bool temp = false) {
            string result = "";
            if (score != null) {
                result += score.Id + "-";
            }
            result += replay.info.playerID;
            if (replay.info.speed != 0) {
                result += "-practice";
            }
            if (replay.info.failTime != 0) {
                result += "-fail";
            }

            result += "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash;

            return result + (temp ? ".bsortemp" : ".bsor");  
        }
    }
}
