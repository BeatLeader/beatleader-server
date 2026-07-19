using ReplayDecoder;
using System.Text;

namespace BeatLeader_RankedPlay_Tester;

public enum LevelEndType {
    Unknown = 0,
    Clear = 1,
    Fail = 2,
    Restart = 3,
    Quit = 4,
    Practice = 5
}

/// <summary>
/// Streams a replay over a WebSocket using the same binary protocol as the mod replay socket.
/// Timing follows the original replay timeline, with optional acceleration.
/// </summary>
public class ReplayStreamer {
    private readonly WebSocketClient _socket;

    public ReplayStreamer(WebSocketClient socket) {
        _socket = socket;
    }

    public async Task StreamReplayAsync(Replay replay, CancellationToken token) {
        SendMapLaunched(replay.info);

        var timeline = BuildTimeline(replay);
        if (timeline.Count == 0) {
            return;
        }

        long startRealTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        float replayStartTime = 0f;

        foreach (var evt in timeline) {
            if (token.IsCancellationRequested) {
                break;
            }

            double elapsedRealMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startRealTimeMs;
            double elapsedReplayMs = elapsedRealMs;
            float targetReplayMs = (evt.Time - replayStartTime) * 1000f;

            if (targetReplayMs > elapsedReplayMs) {
                int delayMs = (int)((targetReplayMs - elapsedReplayMs));
                if (delayMs > 0) {
                    await Task.Delay(Math.Min(delayMs, 5000), token);
                }
            }

            switch (evt.Type) {
                case TimelineEventType.Frame:
                    SendFrame(evt.Frame!);
                    break;
                case TimelineEventType.Note:
                    SendNote(evt.Note!);
                    break;
                case TimelineEventType.Wall:
                    SendWall(evt.Wall!);
                    break;
                case TimelineEventType.Pause:
                    SendPause(evt.Pause!);
                    break;
                case TimelineEventType.Height:
                    SendHeight(evt.Height!);
                    break;
            }
        }

        var endType = replay.info.failTime > 0.01f ? LevelEndType.Fail : LevelEndType.Clear;
        var endTime = replay.info.failTime > 0.01f
            ? replay.info.failTime
            : replay.frames.LastOrDefault()?.time ?? 0;
        SendMapFinished(replay, endType, endTime);
    }

    private void SendMapLaunched(ReplayInfo info) {
        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)StructType.info);
        ReplayEncoder.EncodeInfo(info, writer);
        _socket.QueueBinary(stream.ToArray());
    }

    private void SendFrame(Frame frame) {
        using var stream = new MemoryStream(128);
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)StructType.frames);
        writer.Write((uint)1);
        ReplayEncoder.EncodeFrame(frame, writer);
        _socket.QueueBinary(stream.ToArray());
    }

    private void SendNote(NoteEvent note) {
        using var stream = new MemoryStream(128);
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)StructType.notes);
        writer.Write((uint)1);
        ReplayEncoder.EncodeNote(note, writer);
        _socket.QueueBinary(stream.ToArray());
    }

    private void SendWall(WallEvent wall) {
        using var stream = new MemoryStream(32);
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)StructType.walls);
        writer.Write((uint)1);
        ReplayEncoder.EncodeWall(wall, writer);
        _socket.QueueBinary(stream.ToArray());
    }

    private void SendPause(Pause pause) {
        using var stream = new MemoryStream(32);
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)StructType.pauses);
        writer.Write((uint)1);
        ReplayEncoder.EncodePause(pause, writer);
        _socket.QueueBinary(stream.ToArray());
    }

    private void SendHeight(AutomaticHeight height) {
        using var stream = new MemoryStream(16);
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)StructType.heights);
        writer.Write((uint)1);
        ReplayEncoder.EncodeHeight(height, writer);
        _socket.QueueBinary(stream.ToArray());
    }

    private void SendMapFinished(Replay replay, LevelEndType endType, float endTime) {
        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write((byte)99);
        ReplayEncoder.EncodeInfo(replay.info, writer);
        writer.Write((int)endType);
        writer.Write(endTime);
        writer.Write(true);
        _socket.QueueBinary(stream.ToArray());
    }

    private static List<TimelineEvent> BuildTimeline(Replay replay) {
        var events = new List<TimelineEvent>(
            replay.frames.Count +
            replay.notes.Count +
            replay.walls.Count +
            replay.pauses.Count +
            replay.heights.Count);

        foreach (var frame in replay.frames) {
            events.Add(new TimelineEvent(frame.time, TimelineEventType.Frame) { Frame = frame });
        }

        foreach (var note in replay.notes) {
            events.Add(new TimelineEvent(note.eventTime, TimelineEventType.Note) { Note = note });
        }

        foreach (var wall in replay.walls) {
            events.Add(new TimelineEvent(wall.time, TimelineEventType.Wall) { Wall = wall });
        }

        foreach (var pause in replay.pauses) {
            events.Add(new TimelineEvent(pause.time, TimelineEventType.Pause) { Pause = pause });
        }

        foreach (var height in replay.heights) {
            events.Add(new TimelineEvent(height.time, TimelineEventType.Height) { Height = height });
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        return events;
    }
}

internal enum TimelineEventType {
    Frame,
    Note,
    Wall,
    Pause,
    Height
}

internal class TimelineEvent {
    public float Time { get; }
    public TimelineEventType Type { get; }
    public Frame? Frame { get; init; }
    public NoteEvent? Note { get; init; }
    public WallEvent? Wall { get; init; }
    public Pause? Pause { get; init; }
    public AutomaticHeight? Height { get; init; }

    public TimelineEvent(float time, TimelineEventType type) {
        Time = time;
        Type = type;
    }
}
