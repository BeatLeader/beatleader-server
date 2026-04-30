using BeatLeader_Replay_Sockets;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReplayDecoder;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;

namespace BeatLeader_Server_Sockets.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SocketsController : Controller
    {
        public static readonly object ViewersLock = new();
        public static readonly object PlayersLock = new();
        public static readonly object PresenceListenersLock = new();
        public static List<Viewer> viewers = new();
        public static List<Player> players = new();
        public static List<WebSocket> presenceListeners = new();

        private static readonly SemaphoreSlim _presenceSendLock = new(1, 1);
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<SocketAuthResponse>> _pendingAuth = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<SocketMapResponse>> _pendingMapRequests = new();

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;
        static IHostApplicationLifetime _lifetime;

        public SocketsController(
            IConfiguration configuration,
            IWebHostEnvironment env,
            IHostApplicationLifetime lifetime)
        {
            _configuration = configuration;
            _environment = env;

            if (_lifetime == null) {
                _lifetime = lifetime;

                lifetime.ApplicationStopping.Register(async () => {
                    List<Viewer> snapshot;
                    lock (ViewersLock) snapshot = viewers.ToList();
                    foreach (var socket in snapshot)
                    {
                        try {
                            if (socket.socket.State == WebSocketState.Open) {
                                await socket.socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server shutdown!", CancellationToken.None);
                            }
                        } catch { }
                    }

                    List<WebSocket> presenceSnapshot;
                    lock (PresenceListenersLock) presenceSnapshot = presenceListeners.ToList();
                    foreach (var ws in presenceSnapshot)
                    {
                        try {
                            if (ws.State == WebSocketState.Open) {
                                await ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server shutdown!", CancellationToken.None);
                            }
                        } catch { }
                    }
                });
            }
        }

        public static Player GetOrCreatePlayer(string playerId) {
            lock (PlayersLock) {
                var existing = players.FirstOrDefault(p => p.playerId == playerId);
                if (existing != null) return existing;

                var player = new Player(playerId);
                players.Add(player);
                return player;
            }
        }

        [Route("/stream/player/listen")]
        public async Task<ActionResult> ConnectOutputSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                string? viewerUserId = HttpContext?.User?.Claims?
                    .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                    .Split("/").LastOrDefault();

                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var socketFinished = new TaskCompletionSource();
                var viewer = new Viewer(webSocket, socketFinished, _lifetime.ApplicationStopped, viewerUserId);

                lock (ViewersLock) viewers.Add(viewer);

                await viewer.StartProcessing();

                lock (ViewersLock) viewers.Remove(viewer);
                viewer.DetachFromCurrentPlayer();

                return new EmptyResult();
            }
            else
            {
                return BadRequest();
            }
        }

        [Route("/stream/player/post")]
        public async Task<ActionResult> ConnectInputSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                string? currentID = HttpContext?.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value.Split("/").LastOrDefault();
                if (currentID == null) {
                    return Unauthorized();
                }

                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var socketFinished = new TaskCompletionSource();

                Player? player;
                lock (PlayersLock) {
                    player = players.FirstOrDefault(p => p.playerId == currentID && !p.IsConnected)
                          ?? players.FirstOrDefault(p => p.playerId == currentID);

                    if (player == null) {
                        player = new Player(currentID);
                        players.Add(player);
                    }
                }

                await player.AttachSocket(webSocket, socketFinished, _lifetime.ApplicationStopped);

                await BroadcastPresenceUpdate(currentID, "Online");

                await player.StartProcessing();

                await player.DetachSocket(webSocket);

                await BroadcastPresenceUpdate(currentID, "Offline");

                lock (PlayersLock) {
                    if (!player.IsConnected && !player.HasViewers) {
                        players.Remove(player);
                    }
                }

                return new EmptyResult();
            }
            else
            {
                return BadRequest();
            }
        }

        [Route("/stream/richpresence/listen")]
        public async Task<ActionResult> ConnectPresenceSocket()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest) {
                return BadRequest();
            }

            string? secret = HttpContext.Request.Query["secret"];
            string? configSecret = _configuration.GetValue<string>("PresenceSecret");
            if (string.IsNullOrEmpty(configSecret) || secret != configSecret) {
                return Unauthorized();
            }

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            List<string> connectedPlayerIds;
            lock (PlayersLock) {
                connectedPlayerIds = players.Select(p => p.playerId).ToList();
            }
            var initMsg = JsonConvert.SerializeObject(new {
                Type = "presence",
                Updates = connectedPlayerIds.Select(id => new PresenceUpdate { PlayerId = id, Status = "Online" }).ToList()
            });
            var initBytes = Encoding.UTF8.GetBytes(initMsg);

            await _presenceSendLock.WaitAsync();
            try {
                if (webSocket.State == WebSocketState.Open) {
                    await webSocket.SendAsync(new ArraySegment<byte>(initBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            } finally {
                _presenceSendLock.Release();
            }

            lock (PresenceListenersLock) presenceListeners.Add(webSocket);

            try {
                var buffer = new byte[4096];
                using var messageStream = new MemoryStream();

                while (webSocket.State == WebSocketState.Open && !_lifetime.ApplicationStopped.IsCancellationRequested) {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _lifetime.ApplicationStopped);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    if (result.MessageType == WebSocketMessageType.Text) {
                        messageStream.Write(buffer, 0, result.Count);

                        if (!result.EndOfMessage) continue;

                        var json = Encoding.UTF8.GetString(messageStream.ToArray());
                        messageStream.SetLength(0);

                        try {
                            var obj = JObject.Parse(json);
                            var msgType = obj["Type"]?.ToString();
                            if (msgType == "authResponse") {
                                var response = obj.ToObject<SocketAuthResponse>();
                                if (response?.RequestId != null && _pendingAuth.TryRemove(response.RequestId, out var authTcs)) {
                                    authTcs.TrySetResult(response);
                                }
                            } else if (msgType == "mapResponse") {
                                var response = obj.ToObject<SocketMapResponse>();
                                if (response?.RequestId != null && _pendingMapRequests.TryRemove(response.RequestId, out var mapTcs)) {
                                    mapTcs.TrySetResult(response);
                                }
                            }
                        } catch { }
                    }
                }
            } catch { }

            lock (PresenceListenersLock) presenceListeners.Remove(webSocket);

            foreach (var pending in _pendingAuth.Values) {
                pending.TrySetCanceled();
            }
            foreach (var pending in _pendingMapRequests.Values) {
                pending.TrySetCanceled();
            }

            return new EmptyResult();
        }

        public static async Task<SocketAuthResponse?> RequestViewerAuth(string playerId, string? viewerId, string action)
        {
            WebSocket? ws;
            lock (PresenceListenersLock) {
                ws = presenceListeners.FirstOrDefault(s => s.State == WebSocketState.Open);
            }
            if (ws == null) return null;

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<SocketAuthResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingAuth[requestId] = tcs;

            try {
                var request = JsonConvert.SerializeObject(new {
                    Type = "authRequest",
                    RequestId = requestId,
                    PlayerId = playerId,
                    ViewerId = viewerId,
                    Action = action
                });
                var bytes = Encoding.UTF8.GetBytes(request);

                await _presenceSendLock.WaitAsync();
                try {
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                } finally {
                    _presenceSendLock.Release();
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                cts.Token.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            } catch (TaskCanceledException) {
                return null;
            } finally {
                _pendingAuth.TryRemove(requestId, out _);
            }
        }

        public static async Task<BeatLeader_Server.Utils.ResponseUtils.CompactLeaderboardResponse?> RequestMapInfo(string hash, string difficulty, string mode)
        {
            WebSocket? ws;
            lock (PresenceListenersLock) {
                ws = presenceListeners.FirstOrDefault(s => s.State == WebSocketState.Open);
            }
            if (ws == null) return null;

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<SocketMapResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingMapRequests[requestId] = tcs;

            try {
                var request = JsonConvert.SerializeObject(new {
                    Type = "mapRequest",
                    RequestId = requestId,
                    Hash = hash,
                    Difficulty = difficulty,
                    Mode = mode
                });
                var bytes = Encoding.UTF8.GetBytes(request);

                await _presenceSendLock.WaitAsync();
                try {
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                } finally {
                    _presenceSendLock.Release();
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                cts.Token.Register(() => tcs.TrySetCanceled());
                var response = await tcs.Task;
                return response?.Map;
            } catch (TaskCanceledException) {
                return null;
            } finally {
                _pendingMapRequests.TryRemove(requestId, out _);
            }
        }

        public static async Task BroadcastPresenceUpdate(string playerId, string status)
        {
            var msg = JsonConvert.SerializeObject(new {
                Type = "presence",
                Updates = new List<PresenceUpdate> { new PresenceUpdate { PlayerId = playerId, Status = status } }
            });
            var bytes = Encoding.UTF8.GetBytes(msg);
            var segment = new ArraySegment<byte>(bytes);

            List<WebSocket> snapshot;
            lock (PresenceListenersLock) {
                for (int i = presenceListeners.Count - 1; i >= 0; i--) {
                    if (presenceListeners[i].State != WebSocketState.Open) {
                        presenceListeners.RemoveAt(i);
                    }
                }
                snapshot = presenceListeners.ToList();
            }

            await _presenceSendLock.WaitAsync();
            try {
                foreach (var ws in snapshot) {
                    try {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    } catch {
                        lock (PresenceListenersLock) presenceListeners.Remove(ws);
                    }
                }
            } finally {
                _presenceSendLock.Release();
            }
        }
    }

    public class PresenceUpdate
    {
        public string PlayerId { get; set; }
        public string Status { get; set; }
    }

    public class SocketAuthResponse
    {
        public string Type { get; set; }
        public string RequestId { get; set; }
        public bool Allowed { get; set; }
        public string? Error { get; set; }
    }

    public class SocketMapResponse
    {
        public string Type { get; set; }
        public string RequestId { get; set; }
        public BeatLeader_Server.Utils.ResponseUtils.CompactLeaderboardResponse? Map { get; set; }
    }
}
