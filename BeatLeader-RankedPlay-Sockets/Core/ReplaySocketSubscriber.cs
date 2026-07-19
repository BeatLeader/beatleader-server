using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_RankedPlay_Sockets.Core {

    public class PlayerStatusUpdate {
        public string Id { get; set; } = "";
        public string Status { get; set; } = "";
        public StatusContext? Context { get; set; }
    }

    public class StatusContext {
        public float Time { get; set; }
        public float Accuracy { get; set; }
        public int Score { get; set; }
        public int Combo { get; set; }
        public int Mistakes { get; set; }
    }

    /// <summary>
    /// Connects to BeatLeader-Replay-Sockets as a status listener for specific players.
    /// Relays live score updates back to the MatchmakingService.
    /// </summary>
    public class ReplaySocketSubscriber : IDisposable {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReplaySocketSubscriber> _logger;
        private readonly string _replaySocketUrl;

        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private readonly object _lock = new();
        private readonly HashSet<string> _subscribedPlayers = new();
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public ReplaySocketSubscriber(
            IServiceProvider serviceProvider,
            ILogger<ReplaySocketSubscriber> logger,
            IConfiguration configuration) {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _replaySocketUrl = configuration.GetValue<string>("ReplaySocketUrl")
                ?? "wss://sockets.api.beatleader.com/stream/player/listen";
        }

        public async Task SubscribeToPlayer(string playerId) {
            lock (_lock) {
                if (!_subscribedPlayers.Add(playerId)) return;
            }

            await EnsureConnected();
            await SendCommand("status", playerId);
        }

        public async Task UnsubscribeFromPlayer(string playerId) {
            bool wasSubscribed;

            lock (_lock) {
                wasSubscribed = _subscribedPlayers.Remove(playerId);
            }
            if (!wasSubscribed) return;

            if (_socket?.State == WebSocketState.Open) {
                await SendCommand("disconnect", playerId);
            }
        }

        private async Task<bool> EnsureConnected() {
            if (_socket?.State == WebSocketState.Open) return false;

            await _connectionLock.WaitAsync();
            try {
                if (_socket?.State == WebSocketState.Open) return false;

                _cts?.Cancel();
                _socket?.Dispose();

                _cts = new CancellationTokenSource();
                var socket = new ClientWebSocket();
                _socket = socket;

                try {
                    await socket.ConnectAsync(new Uri(_replaySocketUrl), _cts.Token);
                    _logger.LogInformation("Connected to replay sockets at {Url}", _replaySocketUrl);

                    _ = Task.Run(() => ReceiveLoop(socket, _cts.Token));
                    await ResubscribeAll();
                    return true;
                } catch (Exception ex) {
                    if (ReferenceEquals(_socket, socket)) {
                        _socket = null;
                    }
                    socket.Dispose();
                    _logger.LogError(ex, "Failed to connect to replay sockets");
                    return false;
                }
            } finally {
                _connectionLock.Release();
            }
        }

        private async Task SendCommand(string action, string playerId) {
            var json = JsonConvert.SerializeObject(new { action, playerId });
            var bytes = Encoding.UTF8.GetBytes(json);

            await _sendLock.WaitAsync();
            try {
                if (_socket?.State != WebSocketState.Open) return;

                await _socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true,
                    CancellationToken.None);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to send command to replay socket");
            } finally {
                _sendLock.Release();
            }
        }

        private async Task ResubscribeAll() {
            List<string> players;
            lock (_lock) {
                players = _subscribedPlayers.ToList();
            }

            foreach (var playerId in players) {
                await SendCommand("status", playerId);
            }
        }

        private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token) {
            var buffer = new byte[4096];
            using var messageStream = new MemoryStream();

            try {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested) {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    if (result.MessageType == WebSocketMessageType.Text) {
                        messageStream.Write(buffer, 0, result.Count);

                        if (!result.EndOfMessage) continue;

                        var json = Encoding.UTF8.GetString(messageStream.ToArray());
                        messageStream.SetLength(0);

                        ProcessStatusMessage(json);
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                _logger.LogError(ex, "Replay socket receive error");
            }
        }

        private void ProcessStatusMessage(string json) {
            try {
                var obj = JObject.Parse(json);
                var status = obj["Status"]?.ToString();
                var playerId = obj["Id"]?.ToString();

                if (playerId == null) return;

                bool isSubscribed;
                lock (_lock) {
                    isSubscribed = _subscribedPlayers.Contains(playerId);
                }
                if (!isSubscribed) return;

                if (status == "playing" || status == "startedMap") {
                    var context = obj["Context"]?.ToObject<StatusContext>();
                    if (context != null) {
                        // Failed flag is sticky on the session — once it flips true for
                        // this round it stays true until StartRound resets it.
                        _serviceProvider.GetRequiredService<MatchmakingService>()
                            .HandlePlayerScore(
                                playerId,
                                context.Accuracy,
                                context.Score,
                                context.Time,
                                context.Combo,
                                context.Mistakes,
                                failed: false);
                    }
                } else if (status == "Fail") {
                    // NF is on for ranked play — the level keeps running. We mark the
                    // session's failed flag so the score is halved at resolution time
                    // (§5.3) and the opponent's HUD can show the fail badge.
                    _ = _serviceProvider.GetRequiredService<MatchmakingService>()
                        .HandlePlayerFailed(playerId);
                } else if (status == "Clear" || status == "Quit") {
                    var context = obj["Context"]?.ToObject<StatusContext>();
                    float accuracy = context?.Accuracy ?? 0;
                    int score = context?.Score ?? 0;

                    if (status == "Quit") {
                        accuracy = 0;
                        score = 0;
                    }

                    _ = _serviceProvider.GetRequiredService<MatchmakingService>()
                        .HandlePlayerFinished(playerId, accuracy, score);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing replay status message");
            }
        }

        public void Dispose() {
            _cts?.Cancel();
            _socket?.Dispose();
        }
    }
}
