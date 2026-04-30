using BeatLeader;
using Newtonsoft.Json.Linq;
using ReplayDecoder;

const string ApiBase = "https://api.beatleader.com";
const string ReplayCdn = "https://cdn.replays.beatleader.com";

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("User-Agent", "BeatLeader-Replay-Sockets-Tester/1.0");

Console.WriteLine("Starting replay streaming tester...");

await ReplaySocket.EnsureConnected();
if (ReplaySocket.SocketClient == null || !ReplaySocket.SocketClient.IsAlive()) {
    Console.WriteLine("Failed to connect. Exiting.");
    return;
}

int mapCount = 0;
while (!cts.IsCancellationRequested) {
    try {
        mapCount++;
        Console.WriteLine($"\n=== Map #{mapCount} ===");

        var (replay, songName) = await FetchRandomReplay(cts.Token);
        if (replay == null) {
            Console.WriteLine("Failed to fetch replay, retrying in 5s...");
            await Task.Delay(5000, cts.Token);
            continue;
        }

        Console.WriteLine($"Streaming: {songName}");
        Console.WriteLine($"  Notes: {replay.notes.Count}, Frames: {replay.frames.Count}, " +
                          $"Walls: {replay.walls.Count}, Pauses: {replay.pauses.Count}");

        await StreamReplay(replay, cts.Token);

        Console.WriteLine("Replay finished, moving to next...");
        await Task.Delay(2000, cts.Token);
    } catch (OperationCanceledException) {
        break;
    } catch (Exception ex) {
        Console.WriteLine($"Error: {ex.Message}");
        await Task.Delay(5000, cts.Token);
    }
}

Console.WriteLine("\nShutting down...");
ReplaySocket.SocketClient?.Dispose();

async Task<(Replay? replay, string songName)> FetchRandomReplay(CancellationToken token) {
    var scoreJson = await httpClient.GetStringAsync($"{ApiBase}/score/random", token);
    var score = JObject.Parse(scoreJson);

    var replayUrl = score["replay"]?.ToString();
    var songName = score["song"]?["name"]?.ToString() ?? "Unknown";
    var difficulty = score["difficulty"]?["difficultyName"]?.ToString() ?? "";
    var mode = score["difficulty"]?["modeName"]?.ToString() ?? "";

    Console.WriteLine($"  Score: {songName} [{difficulty} {mode}]");

    if (string.IsNullOrEmpty(replayUrl)) {
        Console.WriteLine("  No replay URL found");
        return (null, songName);
    }

    if (!replayUrl.StartsWith("http")) {
        replayUrl = $"{ReplayCdn}/{replayUrl}";
    }

    Console.WriteLine($"  Downloading replay...");
    var replayData = await httpClient.GetByteArrayAsync(replayUrl, token);
    var (replay, _) = ReplayDecoder.ReplayDecoder.Decode(replayData);

    if (replay == null) {
        Console.WriteLine("  Failed to decode replay");
        return (null, songName);
    }

    return (replay, songName);
}

async Task StreamReplay(Replay replay, CancellationToken token) {
    await ReplaySocket.LaunchedMap(replay.info);

    var timeline = BuildTimeline(replay);
    Console.WriteLine($"  Timeline events: {timeline.Count}");

    if (timeline.Count == 0) return;

    double startRealTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    float replayStartTime = 0;

    int framesSent = 0, notesSent = 0, wallsSent = 0, pausesSent = 0, heightsSent = 0;
    int eventIndex = 0;

    foreach (var evt in timeline) {
        if (token.IsCancellationRequested) break;

        double elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startRealTime;
        float targetTime = (evt.time - replayStartTime) * 1000f;

        if (targetTime > elapsed) {
            int delayMs = (int)((targetTime - elapsed));
            if (delayMs > 0) {
                await Task.Delay(Math.Min(delayMs, 5000), token);
            }
        }

        switch (evt.type) {
            case TimelineEventType.Frame:
                ReplaySocket.SendFrame(evt.frame!);
                framesSent++;
                break;
            case TimelineEventType.Note:
                ReplaySocket.SendNote(evt.note!);
                notesSent++;
                break;
            case TimelineEventType.Wall:
                ReplaySocket.SendWall(evt.wall!);
                wallsSent++;
                break;
            case TimelineEventType.Pause:
                ReplaySocket.SendPause(evt.pause!);
                pausesSent++;
                break;
            case TimelineEventType.Height:
                ReplaySocket.SendHeight(evt.height!);
                heightsSent++;
                break;
        }

        eventIndex++;
        float progress = (float)eventIndex / timeline.Count * 100;
        Console.Write($"\r  Progress: {progress:F0}% | F:{framesSent} N:{notesSent} W:{wallsSent} P:{pausesSent}    ");
    }

    Console.WriteLine($"\r  Done: F:{framesSent} N:{notesSent} W:{wallsSent} P:{pausesSent} H:{heightsSent}          ");

    var endData = new PlayEndData {
        EndType = replay.info.failTime > 0.01f ? LevelEndType.Fail : LevelEndType.Clear,
        Time = replay.info.failTime > 0.01f ? replay.info.failTime : replay.frames.LastOrDefault()?.time ?? 0
    };
    ReplaySocket.FinishedMap(replay, endData, false);
}

List<TimelineEvent> BuildTimeline(Replay replay) {
    var events = new List<TimelineEvent>(
        replay.frames.Count + replay.notes.Count + replay.walls.Count +
        replay.pauses.Count + replay.heights.Count);

    foreach (var f in replay.frames)
        events.Add(new TimelineEvent { time = f.time, type = TimelineEventType.Frame, frame = f });
    foreach (var n in replay.notes)
        events.Add(new TimelineEvent { time = n.eventTime, type = TimelineEventType.Note, note = n });
    foreach (var w in replay.walls)
        events.Add(new TimelineEvent { time = w.time, type = TimelineEventType.Wall, wall = w });
    foreach (var p in replay.pauses)
        events.Add(new TimelineEvent { time = p.time, type = TimelineEventType.Pause, pause = p });
    foreach (var h in replay.heights)
        events.Add(new TimelineEvent { time = h.time, type = TimelineEventType.Height, height = h });

    events.Sort((a, b) => a.time.CompareTo(b.time));
    return events;
}

enum TimelineEventType { Frame, Note, Wall, Pause, Height }

class TimelineEvent {
    public float time;
    public TimelineEventType type;
    public Frame? frame;
    public NoteEvent? note;
    public WallEvent? wall;
    public Pause? pause;
    public AutomaticHeight? height;
}
