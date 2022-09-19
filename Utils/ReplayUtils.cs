using AngleSharp.Common;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Migrations;
using BeatLeader_Server.Models;
using System.Runtime.ConstrainedExecution;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Utils
{
    
    static class ReplayUtils
    {
        public static float Curve(float acc, float stars)
        {
            float l = (float)(1f - (0.03f * (stars - 3.0f) / 11.0f));
            float a = 0.96f * l;
            float f = 1.2f - 0.6f * stars / 14.0f;

            return MathF.Pow(MathF.Log10(l / (l - acc)) / MathF.Log10(l / (l - a)), f);
        }

        public static (float, float) PpFromScore(Score s, ModifiersMap modifierValues, float stars)
        {
            var accuracy = s.Accuracy;
            bool negativeAcc = float.IsNegative(accuracy);
            if (negativeAcc)
            {
                accuracy *= -1;
            }

            float mp = modifierValues.GetPositiveMultiplier(s.Modifiers);
            mp = 1 + (mp - 1);

            float rawPP = (float)(Curve(accuracy, (float)stars - 0.5f) * ((float)stars + 0.5f) * 42);
            float fullPP = (float)(Curve(accuracy, (float)stars * mp - 0.5f) * ((float)stars * mp + 0.5f) * 42);

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP))
            {
                rawPP = 1042;

            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP))
            {
                fullPP = 1042;

            }

            if (negativeAcc)
            {

                rawPP *= -1;
                fullPP *= -1;
            }

            return (fullPP, fullPP - rawPP);
        }

        public static (float, float) PpFromScore(Score s, DifficultyDescription difficulty) {
            return PpFromScore(s, difficulty.ModifierValues, (float)difficulty.Stars);
        }

        public static (float, float) PpFromScoreResponse(ScoreResponse s, RankUpdate reweight)
        {
            var accuracy = s.Accuracy;
            bool negativeAcc = float.IsNegative(accuracy);
            if (negativeAcc)
            {
                accuracy *= -1;
            }

            float mp = reweight.Modifiers.GetPositiveMultiplier(s.Modifiers);
            mp = 1 + (mp - 1);

            float rawPP = (float)(Curve(accuracy, (float)reweight.Stars - 0.5f) * ((float)reweight.Stars + 0.5f) * 42);
            float fullPP = (float)(Curve(accuracy, (float)reweight.Stars * mp - 0.5f) * ((float)reweight.Stars * mp + 0.5f) * 42);

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP))
            {
                rawPP = 1042;

            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP))
            {
                fullPP = 1042;

            }

            if (negativeAcc)
            {

                rawPP *= -1;
                fullPP *= -1;
            }

            return (fullPP, fullPP - rawPP);
        }

        public static (Replay, Score, int) ProcessReplay(Replay replay, Leaderboard leaderboard) {
            Score score = new Score();
            
            score.BaseScore = replay.info.score;
            score.Modifiers = replay.info.modifiers;
            score.WallsHit = replay.walls.Count;
            score.Pauses = replay.pauses.Count;
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
            score.Hmd = HMDFromName(replay.info.hmd);

            var status = leaderboard.Difficulty.Status;
            var modifers = leaderboard.Difficulty.ModifierValues;
            bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent || status == DifficultyStatus.nominated;
            bool hasPp = status == DifficultyStatus.ranked || qualification;

            if (hasPp)
            {
                score.ModifiedScore = (int)(score.BaseScore * modifers.GetNegativeMultiplier(replay.info.modifiers));
            } else
            {
                score.ModifiedScore = (int)(score.BaseScore * modifers.GetTotalMultiplier(replay.info.modifiers));
            }
            int maxScore = leaderboard.Difficulty.MaxScore > 0 ? leaderboard.Difficulty.MaxScore : MaxScoreForNote(leaderboard.Difficulty.Notes);
            score.Accuracy = (float)score.ModifiedScore / (float)maxScore;
            score.Modifiers = replay.info.modifiers;

            if (hasPp) {
                (score.Pp, score.BonusPp) = PpFromScore(score, leaderboard.Difficulty);
            }

            score.Qualification = qualification;

            score.Platform = replay.info.platform + "," + replay.info.gameVersion + "," + replay.info.version;

            score.Timeset = replay.info.timestamp;
            
            return (replay, score, maxScore);
        }




 

        private static HMD HMDFromName(string hmdName) {
            string lowerHmd = hmdName.ToLower();

            if (lowerHmd.Contains("pico neo 3")) return HMD.picoNeo3;
            if (lowerHmd.Contains("pico neo3")) return HMD.picoNeo3;
            if (lowerHmd.Contains("pico neo 2")) return HMD.picoNeo2;
            if (lowerHmd.Contains("pico neo2")) return HMD.picoNeo2;
            if (lowerHmd.Contains("vive pro 2")) return HMD.vivePro2;
            if (lowerHmd.Contains("vive elite")) return HMD.viveElite;
            if (lowerHmd.Contains("focus3")) return HMD.viveFocus;
            if (lowerHmd.Contains("miramar")) return HMD.miramar;
            if (lowerHmd.Contains("pimax vision 8k")) return HMD.pimax8k;
            if (lowerHmd.Contains("pimax 5k")) return HMD.pimax5k;
            if (lowerHmd.Contains("pimax artisan")) return HMD.pimaxArtisan;

            if (lowerHmd.Contains("hp reverb")) return HMD.hpReverb;
            if (lowerHmd.Contains("samsung windows")) return HMD.samsungWmr;
            if (lowerHmd.Contains("qiyu dream")) return HMD.qiyuDream;
            if (lowerHmd.Contains("disco")) return HMD.disco;
            if (lowerHmd.Contains("lenovo explorer")) return HMD.lenovoExplorer;
            if (lowerHmd.Contains("acer ah1010")) return HMD.acerWmr;
            if (lowerHmd.Contains("acer ah5010")) return HMD.acerWmr;
            if (lowerHmd.Contains("arpara")) return HMD.arpara;
            if (lowerHmd.Contains("dell visor")) return HMD.dellVisor;

            if (lowerHmd.Contains("e3")) return HMD.e3;
            if (lowerHmd.Contains("vive dvt")) return HMD.viveDvt;
            if (lowerHmd.Contains("3glasses s20")) return HMD.glasses20;
            if (lowerHmd.Contains("hedy")) return HMD.hedy;
            if (lowerHmd.Contains("vaporeon")) return HMD.vaporeon;
            if (lowerHmd.Contains("huaweivr")) return HMD.huaweivr;
            if (lowerHmd.Contains("asus mr0")) return HMD.asusWmr;
            if (lowerHmd.Contains("cloudxr")) return HMD.cloudxr;
            if (lowerHmd.Contains("vridge")) return HMD.vridge;
            if (lowerHmd.Contains("medion mixed reality")) return HMD.medion;

            if (lowerHmd.Contains("quest 2")) return HMD.quest2;
            if (lowerHmd.Contains("quest_2")) return HMD.quest2;
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

            return 0;
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

        public static float GetTotalMultiplier(this ModifiersMap modifiersObject, string modifiers)
		{
			float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
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
    }
}
