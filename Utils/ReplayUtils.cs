using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    class ReplayUtils
    {
        public static bool CheckReplay(byte[] replay, ICollection<Score> scores, Score? currentScore) {
            float similarityRate = 0.6f;

            foreach (var item in scores)
            {
                if (currentScore != null && currentScore.Player.Id == item.Player.Id)
                {
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
                        return false;
                    }
                
                    if (bitIndex == 0) {
                        byteIndex++;
                        bitIndex = 3;
                    } else {
                        bitIndex--;
                    }
                }
            }
            return true;
        }

        public static (Replay, Score) ProcessReplay(Replay replay, byte[] replayData, Leaderboard leaderboard) {
            Score score = new Score();
            score.Identification = ReplayIdentificationForReplay(replayData);
            score.BaseScore = replay.info.score; // TODO: recalculate score based on note info
            score.Modifiers = replay.info.modifiers;
            score.Timeset = replay.info.timestamp;
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
            score.ModifiedScore = (int)(score.BaseScore * GetTotalMultiplier(replay.info.modifiers));
            score.Accuracy = (float)score.ModifiedScore / (float)MaxScoreForNote(leaderboard.Difficulty.Notes);
            score.Modifiers = replay.info.modifiers;
            
            return (replay, score);
        }

        private static ReplayIdentification ReplayIdentificationForReplay(byte[] replayData) {
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
            if (hmdName.Contains("Quest 2")) return 256;
            if (hmdName.Contains("Vive Cosmos")) return 128;
            if (hmdName.Contains("Index")) return 64;
            if (hmdName.Contains("Quest")) return 32;
            if (hmdName.Contains("Rift S")) return 16;
            if (hmdName.Contains("Windows")) return 8;
            if (hmdName.Contains("Vive Pro")) return 4;
            if (hmdName.Contains("Vive")) return 2;
            if (hmdName.Contains("Rift")) return 1;

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

			if (modifiers.Contains("DA")) { multiplier += 0.07f; }
			if (modifiers.Contains("FS")) { multiplier += 0.08f; }
			if (modifiers.Contains("SS")) { multiplier -= 0.3f; }
			if (modifiers.Contains("SF")) { multiplier += 0.1f; }
			if (modifiers.Contains("GN")) { multiplier += 0.11f; }
			if (modifiers.Contains("NA")) { multiplier -= 0.3f; }
			if (modifiers.Contains("NB")) { multiplier -= 0.1f; }
			if (modifiers.Contains("NF")) { multiplier -= 0.5f; }
			if (modifiers.Contains("NO")) { multiplier -= 0.05f; }

			return multiplier;
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
