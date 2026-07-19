using System.Net.WebSockets;

namespace BeatLeader_RankedPlay_Sockets.Models {
    public class QueueEntry {
        public string PlayerId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public float MMR { get; set; }
        public string Tier { get; set; } = "";
        public int TierDivision { get; set; }
        public bool IsCalibrated { get; set; }
        public int CalibrationMatchesPlayed { get; set; }
        public int GlobalRank { get; set; }
        public WebSocket Socket { get; set; } = null!;
        public int JoinedAt { get; set; }
        // Test-bot flag from the main-server bridge response. Bots can never
        // pair with other bots; real players are paired real-vs-real first
        // and only fall back to a bot opponent if no real partner is found.
        public bool IsBot { get; set; }

        // Expanding match window per docs/RankedPlay.md §6.1. Uncalibrated players go
        // through the same window progression as everyone else — the matchmaker uses
        // their hidden internal MMR so we still pair them against opponents of plausibly
        // similar skill rather than throwing them into the widest bucket immediately.
        private float SearchWindow {
            get {
                int secondsInQueue = Math.Max(0, Time.UnixNow() - JoinedAt);
                if (secondsInQueue < 30) return 100f;
                if (secondsInQueue < 60) return 200f;
                if (secondsInQueue < 120) return 400f;
                return 800f;
            }
        }

        public float EffectiveMinMMR => MMR - SearchWindow;
        public float EffectiveMaxMMR => MMR + SearchWindow;
    }
}
