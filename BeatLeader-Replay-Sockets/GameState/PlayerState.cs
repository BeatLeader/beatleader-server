using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Replay_Sockets {

    public class Playing {
        public float Time { get; set; }
        public float Accuracy { get; set; }
        public float Pp { get; set; }
        public int Score { get; set; }
    }

    public class PlayingWithMap : Playing {
        public CompactLeaderboardResponse Map { get; set; }
    }

    public enum EventType {
        Miss,
        BadCut,
        BombHit,
        WallHit,
        Pause
    }

    public class StreamEvent {
        public float AtTime { get; set; }
        public EventType Event { get; set; }
    }

    public class PlayerState {
        public string Id { get; set; }

        public string Status { get; set; }
        public object? Context { get; set; }
        public StreamEvent? Event { get; set; }
    }
}
