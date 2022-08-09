using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    class ReplayUtils
    {
        public static float Curve(float acc, float stars)
        {
            float l = (float)(1f - (0.03f * (stars - 3.0f) / 11.0f));
            float a = 0.96f * l;
            float f = 1.2f - 0.6f * stars / 14.0f;

            return MathF.Pow(MathF.Log10(l / (l - acc)) / MathF.Log10(l / (l - a)), f);
        }

        public static (float, float) PpFromScore(Score s, DifficultyDescription difficulty) {
            float mp = GetPositiveMultiplier(s.Modifiers);
            mp = 1 + (mp - 1) * 2f;

            float rawPP = (float)(Curve(s.Accuracy, (float)difficulty.Stars - 0.5f) * ((float)difficulty.Stars + 0.5f) * 42);
            float fullPP = (float)(Curve(s.Accuracy, (float)difficulty.Stars * mp - 0.5f) * ((float)difficulty.Stars * mp + 0.5f) * 42);

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
            score.Hmd = HMD(replay.info.hmd);

            if (leaderboard.Difficulty.Ranked)
            {
                score.ModifiedScore = (int)(score.BaseScore * GetNegativeMultiplier(replay.info.modifiers));
            } else
            {
                score.ModifiedScore = (int)(score.BaseScore * GetTotalMultiplier(replay.info.modifiers));
            }
            int maxScore = leaderboard.Difficulty.MaxScore > 0 ? leaderboard.Difficulty.MaxScore : MaxScoreForNote(leaderboard.Difficulty.Notes);
            score.Accuracy = (float)score.ModifiedScore / (float)maxScore;
            score.Modifiers = replay.info.modifiers;

            if (leaderboard.Difficulty.Ranked || leaderboard.Difficulty.Qualified || leaderboard.Difficulty.Nominated) {
                (score.Pp, score.BonusPp) = PpFromScore(score, leaderboard.Difficulty);
            }

            score.Qualification = leaderboard.Difficulty.Qualified || leaderboard.Difficulty.Nominated;

            score.Platform = replay.info.platform + "," + replay.info.gameVersion + "," + replay.info.version;

            score.Timeset = replay.info.timestamp;
            
            return (replay, score, maxScore);
        }

        private static int HMD(string hmdName) {
            string lowerHmd = hmdName.ToLower();

            if (lowerHmd.Contains("quest 2")) return 256;
            if (lowerHmd.Contains("quest_2")) return 256;
            if (lowerHmd.Contains("vive cosmos")) return 128;
            if (lowerHmd.Contains("vive_cosmos")) return 128;
            if (lowerHmd.Contains("index")) return 64;
            if (lowerHmd.Contains("quest")) return 32;
            if (lowerHmd.Contains("rift s")) return 16;
            if (lowerHmd.Contains("rift_s")) return 16;
            if (lowerHmd.Contains("windows")) return 8;
            if (lowerHmd.Contains("vive pro")) return 4;
            if (lowerHmd.Contains("vive_pro")) return 4;
            if (lowerHmd.Contains("vive")) return 2;
            if (lowerHmd.Contains("rift")) return 1;

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

        public static float GetTotalMultiplier(string modifiers)
		{
			float multiplier = 1;

            var modifiersMap = Modifiers();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier)) { multiplier += modifiersMap[modifier]; }
            }
            

			return multiplier;
		}

        public static float GetPositiveMultiplier(string modifiers)
        {
            float multiplier = 1;

            var modifiersMap = Modifiers();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier) && modifiersMap[modifier] > 0) { multiplier += modifiersMap[modifier]; }
            }

            return multiplier;
        }

        public static float GetNegativeMultiplier(string modifiers)
        {
            float multiplier = 1;

            var modifiersMap = Modifiers();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier) && modifiersMap[modifier] < 0) { multiplier += modifiersMap[modifier]; }
            }

            return multiplier;
        }

        public static (string, float) GetNegativeMultipliers(string modifiers)
        {
            float multiplier = 1;
            List<string> modifierArray = new List<string>();

            var modifiersMap = Modifiers();
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

        public static Dictionary<string, float> Modifiers()
        {
            return new Dictionary<string, float>
            {
                ["DA"] = 0.01f,
                ["FS"] = 0.04f,
                ["SS"] = -0.3f,
                ["SF"] = 0.08f,
                ["GN"] = 0.05f,
                ["NA"] = -0.3f,
                ["NB"] = -0.2f,
                ["NF"] = -0.5f,
                ["NO"] = -0.2f,
                ["PM"] = 0.0f,
                ["SC"] = 0.0f
            };
        }

        private static byte[] OrderToBlob(int[] order) {
            Stream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            writer.Write(order.Length);
            for (int i = 0; i < order.Length; i++) {
                writer.Write(order[i]);
            }

            stream.Position = 0;

            byte[] result = new byte[stream.Length];
            stream.Read(result, 0, result.Length);
            return result;
        }

        private static int[] BlobToOrder(byte[] blob) {
            int pointer = 0;
            int length = DecodeInt(blob, ref pointer);
            int[] result = new int[length];

            for (int i = 0; i < length; i++) {
                result[i] = DecodeInt(blob, ref pointer);
            }
            return result;
        }

        private static int DecodeInt(byte[] buffer, ref int pointer)
        {
            int result = BitConverter.ToInt32(buffer, pointer);
            pointer += 4;
            return result;
        }
    }
}
