using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;

namespace BeatLeader_RankedPlay_Tester;

/// <summary>
/// Best-of-3 protocol client for a single bot player connecting to
/// BeatLeader-RankedPlay-Sockets via WebSocket. Mirrors the message set
/// defined in BeatLeader-RankedPlay-Sockets/Models/ProtocolMessages.cs.
///
/// Bots only exist to fill the queue for real players: they log in, queue,
/// negotiate and play whatever map the human picks (or pick one themselves
/// when it's their turn), and re-queue forever. They never test the system —
/// the matchmaker on the server side is what keeps them from pairing with
/// each other (§4.2 — see RankedPlayBotUtils on the main server).
/// </summary>
public class RankedPlayClient : IDisposable {
    public string Login { get; }
    public string PlayerId { get; private set; } = "";
    public string PlayerName { get; private set; } = "";
    public float MMR { get; private set; }

    private string? _cookie;
    private WebSocketClient? _rankedPlaySocket;
    private WebSocketClient? _replaySocket;

    private readonly string _apiBase;
    private readonly string _rankedPlayWsUrl;
    private readonly string _replayWsUrl;
    private readonly HttpClient _http;

    private TaskCompletionSource<MatchFoundData>? _matchFoundTcs;
    private TaskCompletionSource<HandDealtData>? _handDealtTcs;
    private TaskCompletionSource<DiscardPhaseData>? _discardPhaseTcs;
    private TaskCompletionSource<DiscardRevealedData>? _discardRevealedTcs;
    private TaskCompletionSource<PickPhaseData>? _pickPhaseTcs;
    private TaskCompletionSource<MapDecidedData>? _mapDecidedTcs;
    private TaskCompletionSource<CountdownData>? _countdownTcs;
    private TaskCompletionSource<string>? _startPlayingTcs;
    private TaskCompletionSource<RoundOrSeriesEvent>? _roundOrSeriesTcs;
    private TaskCompletionSource<MatchResultData>? _matchResultTcs;
    private TaskCompletionSource<string>? _matchCancelledTcs;
    private TaskCompletionSource<string>? _errorTcs;

    private readonly List<OpponentScoreData> _opponentScores = new();
    private readonly object _lock = new();
    private bool _queueJoinRequested;
    private bool _isQueued;
    private bool _isInMatch;
    private string? _currentMatchId;

    public bool IsConnected => _rankedPlaySocket?.IsAlive() == true;
    public bool HasPendingQueueJoin {
        get { lock (_lock) return _queueJoinRequested && !_isQueued; }
    }
    public bool IsQueued {
        get { lock (_lock) return _isQueued; }
    }
    public bool IsInMatch {
        get { lock (_lock) return _isInMatch; }
    }
    public string? CurrentMatchId {
        get { lock (_lock) return _currentMatchId; }
    }
    public IReadOnlyList<OpponentScoreData> OpponentScores {
        get { lock (_lock) return _opponentScores.ToList(); }
    }

    public RankedPlayClient(string login, string apiBase, string rankedPlayWsUrl, string replayWsUrl) {
        Login = login;
        _apiBase = apiBase;
        _rankedPlayWsUrl = rankedPlayWsUrl;
        _replayWsUrl = replayWsUrl;

        var handler = new HttpClientHandler {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.Add("User-Agent", "BeatLeader-RankedPlay-Tester/1.0");
    }

    public async Task<bool> LoginAsync() {
        var handler = new HttpClientHandler {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        using var client = new HttpClient(handler);
        var content = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("login", Login),
            new KeyValuePair<string, string>("action", "login"),
            new KeyValuePair<string, string>("password", Login) // password = login
        });

        var response = await client.PostAsync($"{_apiBase}/signinoculus", content);

        var cookies = handler.CookieContainer.GetCookies(new Uri(_apiBase));
        var sb = new StringBuilder();
        foreach (Cookie c in cookies) {
            if (sb.Length > 0) sb.Append("; ");
            sb.Append($"{c.Name}={c.Value}");
        }

        if (sb.Length == 0) {
            var setCookies = response.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value);
            foreach (var raw in setCookies) {
                var part = raw.Split(';')[0];
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(part);
            }
        }

        _cookie = sb.ToString();
        if (string.IsNullOrEmpty(_cookie)) {
            Log($"Login failed: {response.StatusCode}");
            return false;
        }

        PlayerName = Login;
        Log("Logged in");
        return true;
    }

    public async Task<bool> ConnectRankedPlayAsync(CancellationToken token = default) {
        if (_cookie == null) return false;

        _rankedPlaySocket = new WebSocketClient(_rankedPlayWsUrl, _cookie);
        _rankedPlaySocket.OnTextMessage += HandleRankedPlayMessage;
        _rankedPlaySocket.OnDisconnected += () => {
            ResetQueueState();
            Log("RankedPlay socket disconnected");
        };

        try {
            await _rankedPlaySocket.ConnectAsync(token);
            Log("RankedPlay WebSocket connected");
            return true;
        } catch (Exception ex) {
            Log($"RankedPlay connect failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ConnectReplaySocketAsync(CancellationToken token = default) {
        if (_cookie == null) return false;

        _replaySocket = new WebSocketClient(_replayWsUrl, _cookie);
        _replaySocket.OnDisconnected += () => Log("Replay socket disconnected");

        try {
            await _replaySocket.ConnectAsync(token);
            Log("Replay WebSocket connected");
            return true;
        } catch (Exception ex) {
            Log($"Replay connect failed: {ex.Message}");
            return false;
        }
    }

    // ── Queue ──

    public void JoinQueue() {
        lock (_lock) {
            _queueJoinRequested = true;
        }
        SendRankedPlay(new { Type = "joinQueue" });
    }

    public void LeaveQueue() {
        lock (_lock) {
            _queueJoinRequested = false;
        }
        SendRankedPlay(new { Type = "leaveQueue" });
    }

    // ── BO3 negotiation actions ──

    // leaderboardId == null → pass (no discard this round).
    public void SendDiscard(string? leaderboardId) {
        SendRankedPlay(new { Type = "discardMap", LeaderboardId = leaderboardId });
    }

    public void SendPick(string leaderboardId) {
        SendRankedPlay(new { Type = "pickMap", LeaderboardId = leaderboardId });
    }

    public void SendMapReady() {
        SendRankedPlay(new { Type = "mapReady" });
    }

    public void SendMapDownloadFailed() {
        SendRankedPlay(new { Type = "mapDownloadFailed" });
    }

    public void SendForfeitGame() {
        SendRankedPlay(new { Type = "forfeitGame" });
    }

    public void SendForfeitMatch() {
        SendRankedPlay(new { Type = "forfeitMatch" });
    }

    // ── Awaiting messages ──

    public Task<MatchFoundData> WaitForMatchFound(CancellationToken token) {
        _matchFoundTcs = new TaskCompletionSource<MatchFoundData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _matchFoundTcs.TrySetCanceled());
        return _matchFoundTcs.Task;
    }

    public Task<HandDealtData> WaitForHandDealt(CancellationToken token) {
        _handDealtTcs = new TaskCompletionSource<HandDealtData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _handDealtTcs.TrySetCanceled());
        return _handDealtTcs.Task;
    }

    public Task<DiscardPhaseData> WaitForDiscardPhase(CancellationToken token) {
        _discardPhaseTcs = new TaskCompletionSource<DiscardPhaseData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _discardPhaseTcs.TrySetCanceled());
        return _discardPhaseTcs.Task;
    }

    public Task<DiscardRevealedData> WaitForDiscardRevealed(CancellationToken token) {
        _discardRevealedTcs = new TaskCompletionSource<DiscardRevealedData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _discardRevealedTcs.TrySetCanceled());
        return _discardRevealedTcs.Task;
    }

    public Task<PickPhaseData> WaitForPickPhase(CancellationToken token) {
        _pickPhaseTcs = new TaskCompletionSource<PickPhaseData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _pickPhaseTcs.TrySetCanceled());
        return _pickPhaseTcs.Task;
    }

    public Task<MapDecidedData> WaitForMapDecided(CancellationToken token) {
        _mapDecidedTcs = new TaskCompletionSource<MapDecidedData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _mapDecidedTcs.TrySetCanceled());
        return _mapDecidedTcs.Task;
    }

    public Task<CountdownData> WaitForCountdown(CancellationToken token) {
        _countdownTcs = new TaskCompletionSource<CountdownData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _countdownTcs.TrySetCanceled());
        return _countdownTcs.Task;
    }

    public Task<string> WaitForStartPlaying(CancellationToken token) {
        _startPlayingTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _startPlayingTcs.TrySetCanceled());
        return _startPlayingTcs.Task;
    }

    /// <summary>
    /// After a round finishes, the server may either send a roundResult
    /// (more rounds to play) or a matchResult (series is over). We bundle
    /// both into a single awaitable so the caller can branch on which arrived.
    /// </summary>
    public Task<RoundOrSeriesEvent> WaitForRoundOrSeriesEnd(CancellationToken token) {
        _roundOrSeriesTcs = new TaskCompletionSource<RoundOrSeriesEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _roundOrSeriesTcs.TrySetCanceled());
        return _roundOrSeriesTcs.Task;
    }

    public Task<MatchResultData> WaitForMatchResult(CancellationToken token) {
        _matchResultTcs = new TaskCompletionSource<MatchResultData>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _matchResultTcs.TrySetCanceled());
        return _matchResultTcs.Task;
    }

    public Task<string> WaitForMatchCancelled(CancellationToken token) {
        _matchCancelledTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _matchCancelledTcs.TrySetCanceled());
        return _matchCancelledTcs.Task;
    }

    public Task<string> WaitForError(CancellationToken token) {
        _errorTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => _errorTcs.TrySetCanceled());
        return _errorTcs.Task;
    }

    // ── Replay streaming ──

    public ReplayStreamer? CreateReplayStreamer() {
        if (_replaySocket == null) return null;
        return new ReplayStreamer(_replaySocket);
    }

    // ── Message handling ──

    private void HandleRankedPlayMessage(string json) {
        try {
            var obj = JObject.Parse(json);
            var type = obj["Type"]?.ToString();

            switch (type) {
                case "queueStatus":
                    var status = obj["Status"]?.ToString();
                    lock (_lock) {
                        if (string.Equals(status, "joined", StringComparison.OrdinalIgnoreCase)) {
                            _isQueued = true;
                            _queueJoinRequested = false;
                        } else if (string.Equals(status, "left", StringComparison.OrdinalIgnoreCase)) {
                            _isQueued = false;
                            _queueJoinRequested = false;
                        }
                    }
                    Log($"Queue: {status}");
                    break;

                case "matchFound":
                    var mf = obj.ToObject<MatchFoundData>();
                    if (mf != null) {
                        lock (_lock) {
                            _isQueued = false;
                            _queueJoinRequested = false;
                            _isInMatch = true;
                            _currentMatchId = mf.MatchId;
                        }
                        Log($"Match found: vs {mf.Opponent?.PlayerName} (MMR {mf.Opponent?.MMR:F0})");
                        _matchFoundTcs?.TrySetResult(mf);
                    }
                    break;

                case "handDealt":
                    var hd = obj.ToObject<HandDealtData>();
                    if (hd != null) {
                        Log($"Hand dealt: round {hd.RoundNumber}, {hd.Hand?.Count} maps, series {hd.YourGamesWon}-{hd.OpponentGamesWon}");
                        _handDealtTcs?.TrySetResult(hd);
                    }
                    break;

                case "discardPhase":
                    var dp = obj.ToObject<DiscardPhaseData>();
                    if (dp != null) {
                        Log($"Discard phase opened (deadline {dp.DeadlineSeconds}s)");
                        _discardPhaseTcs?.TrySetResult(dp);
                    }
                    break;

                case "discardRevealed":
                    var dr = obj.ToObject<DiscardRevealedData>();
                    if (dr != null) {
                        string yours = string.IsNullOrEmpty(dr.YourDiscardLeaderboardId) ? "pass" : dr.YourDiscardLeaderboardId;
                        string theirs = string.IsNullOrEmpty(dr.OpponentDiscardLeaderboardId) ? "pass" : dr.OpponentDiscardLeaderboardId;
                        Log($"Discard revealed: you={yours} | opp={theirs} | {dr.Replacements?.Count ?? 0} replacement(s)");
                        _discardRevealedTcs?.TrySetResult(dr);
                    }
                    break;

                case "pickPhase":
                    var pp = obj.ToObject<PickPhaseData>();
                    if (pp != null) {
                        Log($"Pick phase: picker={(pp.IsYou ? "you" : "opp")} (deadline {pp.DeadlineSeconds}s)");
                        _pickPhaseTcs?.TrySetResult(pp);
                    }
                    break;

                case "mapDecided":
                    var md = obj.ToObject<MapDecidedData>();
                    if (md != null) {
                        Log($"Map decided: round {md.RoundNumber} — {md.Map?.SongName} [{md.Map?.Difficulty}] ★{md.Map?.Stars:F1}");
                        _mapDecidedTcs?.TrySetResult(md);
                    }
                    break;

                case "countdown":
                    var cd = obj.ToObject<CountdownData>();
                    if (cd != null) {
                        _countdownTcs?.TrySetResult(cd);
                    }
                    break;

                case "startPlaying":
                    Log("Start playing!");
                    _startPlayingTcs?.TrySetResult("startPlaying");
                    break;

                case "opponentScore":
                    var os = obj.ToObject<OpponentScoreData>();
                    if (os != null) {
                        lock (_lock) _opponentScores.Add(os);
                    }
                    break;

                case "roundResult":
                    var rr = obj.ToObject<RoundResultData>();
                    if (rr != null) {
                        Log($"Round {rr.RoundNumber} result: winner={rr.WinnerId} | you {rr.YourAccuracy:P} vs opp {rr.OpponentAccuracy:P}");
                        _roundOrSeriesTcs?.TrySetResult(new RoundOrSeriesEvent { RoundResult = rr });
                    }
                    break;

                case "seriesUpdate":
                    var su = obj.ToObject<SeriesUpdateData>();
                    if (su != null) {
                        Log($"Series: {su.YourGamesWon}-{su.OpponentGamesWon} (over={su.IsSeriesOver})");
                    }
                    break;

                case "forfeitConfirmed":
                    var fc = obj.ToObject<ForfeitConfirmedData>();
                    if (fc != null) {
                        Log($"Opponent forfeited ({fc.OpponentForfeitType}) round {fc.RoundNumber}");
                    }
                    break;

                case "forfeitRequested":
                    // Server ack for our own forfeit — nothing to do, log only.
                    var fr = obj.ToObject<ForfeitRequestedData>();
                    if (fr != null) {
                        Log($"Forfeit accepted ({fr.ForfeitType}) round {fr.RoundNumber}");
                    }
                    break;

                case "matchResult":
                    var mr = obj.ToObject<MatchResultData>();
                    if (mr != null) {
                        ResetMatchState();
                        Log($"Result: {mr.Result}, winner={mr.WinnerId}, series {mr.YourGamesWon}-{mr.OpponentGamesWon}, MMR change={mr.MMRChange:+0.0;-0.0}");
                        _matchResultTcs?.TrySetResult(mr);
                        // Also unblock any pending round-or-series wait.
                        _roundOrSeriesTcs?.TrySetResult(new RoundOrSeriesEvent { MatchResult = mr });
                    }
                    break;

                case "matchCancelled":
                    var reason = obj["Reason"]?.ToString() ?? "unknown";
                    ResetMatchState();
                    Log($"Match cancelled: {reason}");
                    _matchCancelledTcs?.TrySetResult(reason);
                    break;

                case "error":
                    var err = obj["Error"]?.ToString() ?? "unknown";
                    lock (_lock) {
                        _queueJoinRequested = false;
                    }
                    Log($"Error: {err}");
                    _errorTcs?.TrySetResult(err);
                    break;

                case "opponentDisconnected":
                    Log("Opponent disconnected");
                    break;
            }
        } catch (Exception ex) {
            Log($"Parse error: {ex.Message}");
        }
    }

    private void SendRankedPlay(object message) {
        _rankedPlaySocket?.QueueText(JsonConvert.SerializeObject(message));
    }

    public void ClearOpponentScores() {
        lock (_lock) _opponentScores.Clear();
    }

    public async Task DisconnectAsync() {
        ResetQueueState();
        if (_rankedPlaySocket != null) await _rankedPlaySocket.CloseAsync();
        if (_replaySocket != null) await _replaySocket.CloseAsync();
    }

    private void ResetQueueState() {
        lock (_lock) {
            _queueJoinRequested = false;
            _isQueued = false;
            _isInMatch = false;
            _currentMatchId = null;
        }
    }

    private void ResetMatchState() {
        lock (_lock) {
            _isQueued = false;
            _queueJoinRequested = false;
            _isInMatch = false;
            _currentMatchId = null;
        }
    }

    private void Log(string msg) {
        Console.WriteLine($"[{Login}] {msg}");
    }

    public void Dispose() {
        _rankedPlaySocket?.Dispose();
        _replaySocket?.Dispose();
        _http.Dispose();
    }
}

// ── Protocol DTOs (mirror BeatLeader-RankedPlay-Sockets/Models/ProtocolMessages.cs) ──

public class MatchFoundData {
    public string MatchId { get; set; } = "";
    public MatchPlayerInfo? Opponent { get; set; }
    public bool IsLowerRated { get; set; }
}

public class MatchPlayerInfo {
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public float MMR { get; set; }
    public string Tier { get; set; } = "";
    public int TierDivision { get; set; }
}

public class MapCandidateData {
    public string LeaderboardId { get; set; } = "";
    public string SongName { get; set; } = "";
    public string SongAuthor { get; set; } = "";
    public string Mapper { get; set; } = "";
    public int MapperId { get; set; }
    public string CoverImage { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string Mode { get; set; } = "";
    public float Stars { get; set; }
    public double Duration { get; set; }
}

public class HandDealtData {
    public int RoundNumber { get; set; }
    public List<MapCandidateData>? Hand { get; set; }
    public int YourGamesWon { get; set; }
    public int OpponentGamesWon { get; set; }
}

public class DiscardPhaseData {
    public int RoundNumber { get; set; }
    public int DeadlineSeconds { get; set; }
}

public class DiscardRevealedData {
    public int RoundNumber { get; set; }
    public string? YourDiscardLeaderboardId { get; set; }
    public string? OpponentDiscardLeaderboardId { get; set; }
    public List<MapCandidateData>? Replacements { get; set; }
    public List<MapCandidateData>? NewHand { get; set; }
}

public class PickPhaseData {
    public int RoundNumber { get; set; }
    public string PickerId { get; set; } = "";
    public bool IsYou { get; set; }
    public int DeadlineSeconds { get; set; }
}

public class MapDecidedData {
    public int RoundNumber { get; set; }
    public MapCandidateData? Map { get; set; }
}

public class CountdownData {
    public int SecondsRemaining { get; set; }
}

public class OpponentScoreData {
    public float Accuracy { get; set; }
    public int Score { get; set; }
    public float Time { get; set; }
    public int Combo { get; set; }
    public int Mistakes { get; set; }
    public bool Failed { get; set; }
}

public class RoundResultData {
    public int RoundNumber { get; set; }
    public string? LeaderboardId { get; set; }
    public int YourScore { get; set; }
    public int OpponentScore { get; set; }
    public int YourFinalScore { get; set; }
    public int OpponentFinalScore { get; set; }
    public float YourAccuracy { get; set; }
    public float OpponentAccuracy { get; set; }
    public bool YourFailed { get; set; }
    public bool OpponentFailed { get; set; }
    public bool YourForfeit { get; set; }
    public bool OpponentForfeit { get; set; }
    public string? WinnerId { get; set; }
    public bool IsYouWinner { get; set; }
}

public class SeriesUpdateData {
    public int YourGamesWon { get; set; }
    public int OpponentGamesWon { get; set; }
    public int DrawnGames { get; set; }
    public bool IsSeriesOver { get; set; }
}

public class ForfeitRequestedData {
    public string ForfeitType { get; set; } = "game";
    public int RoundNumber { get; set; }
}

public class ForfeitConfirmedData {
    public string OpponentForfeitType { get; set; } = "game";
    public int RoundNumber { get; set; }
}

public class MatchResultData {
    public string WinnerId { get; set; } = "";
    public string Result { get; set; } = "";
    public int YourGamesWon { get; set; }
    public int OpponentGamesWon { get; set; }
    public int DrawnGames { get; set; }
    public bool IsYouWinner { get; set; }
    public float MMRChange { get; set; }
    public float NewMMR { get; set; }
    public float OpponentMMRChange { get; set; }
    public float OpponentNewMMR { get; set; }
}

// Bundled "after-this-round" event — either a roundResult (loop continues) OR
// a matchResult (series ended). Bot loop branches on which field is non-null.
public class RoundOrSeriesEvent {
    public RoundResultData? RoundResult { get; set; }
    public MatchResultData? MatchResult { get; set; }
    public bool IsSeriesEnd => MatchResult != null;
}
