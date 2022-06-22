using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    class ReplayUtils
    {
        public static (bool, string?) CheckReplay(byte[] replay, ICollection<Score?> scores, Score? currentScore) {
            float similarityRate = 0.6f;

            foreach (Score? item in scores)
            {
                if (item == null) { continue; }
                if (currentScore != null && currentScore.Player.Id == item.Player.Id)
                {
                    continue;
                }

                if (item.Identification.Value.Length == 0 || item.Identification.Order.Length == 0) {
                    continue;
                }

                byte[] data = item.Identification.Value;
                int[] order = BlobToOrder(item.Identification.Order);

                int treshold = (int)((float)order.Length * similarityRate);
                int sameBits = 0;

                int byteIndex = 0;
                int bitIndex = 3;
                for (int i = 0; i < order.Length; i++)
                {
                    int index = order[i];
                    if (index >= replay.Length || byteIndex >= data.Length) break;

                    byte current = replay[index];

                    if ((current & (1 << (index % 4))) == (data[byteIndex] & (1 << bitIndex))) {
                        sameBits++;
                    }

                    if (sameBits > treshold) {
                        return (false, item.PlayerId);
                    }
                
                    if (bitIndex == 0) {
                        byteIndex++;
                        bitIndex = 3;
                    } else {
                        bitIndex--;
                    }
                }
            }
            return (true, null);
        }

        public static (Replay, Score) ProcessReplay(Replay replay, Leaderboard leaderboard) {
            Score score = new Score();
            
            score.BaseScore = replay.info.score;
            score.Modifiers = replay.info.modifiers;
            score.WallsHit = replay.walls.Count;
            score.Pauses = replay.pauses.Count;
            score.Identification = new ReplayIdentification {
                Order = new byte[0],
                Value = new byte[0]
            };
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
            score.ModifiedScore = (int)(score.BaseScore * GetTotalMultiplier(replay.info.modifiers));
            if (leaderboard.Difficulty.MaxScore > 0) {
                score.Accuracy = (float)score.ModifiedScore / (float)leaderboard.Difficulty.MaxScore;
            } else {
                score.Accuracy = (float)score.ModifiedScore / (float)MaxScoreForNote(leaderboard.Difficulty.Notes);
            }
            
            score.Modifiers = replay.info.modifiers;
            score.Platform = replay.info.platform + "," + replay.info.gameVersion + "," + replay.info.version;
            score.Timeset = replay.info.timestamp;
            
            return (replay, score);
        }

        public static ReplayIdentification ReplayIdentificationForReplay(byte[] replayData) {
            int dataLength = replayData.Length;
            int length = dataLength / 200;
            int[] randomList = new int[length];

            Random rnd = new Random();
            int counter = 0;
            try {
                while (counter < length) {
                    int random = rnd.Next(1, dataLength);
                    if (Array.IndexOf(randomList, random) <= 0) {
                        randomList[counter] = random;
                        counter++;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("Error in generating an Array of Random Numbers", ex);
            }

            byte[] data = new byte[length / 4 + 4];
            int byteIndex = 0;
            int bitIndex = 3;
            for (int i = 0; i < length; i++)
            {
                int index = randomList[i];
                byte current = replayData[index];
                if ((current & (1 << (index % 4))) != 0) {
                    data[byteIndex] |= (byte)(1 << bitIndex);
                }
                
                if (bitIndex == 0) {
                    byteIndex++;
                    bitIndex = 3;
                } else {
                    bitIndex--;
                }
            }

            ReplayIdentification result = new ReplayIdentification();
            result.Order = OrderToBlob(randomList);
            result.Value = data;
            return result;
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
