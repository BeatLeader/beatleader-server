using BeatLeader_Server_Sockets.Controllers;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReplayDecoder;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_Replay_Sockets {
    public enum ViewerType {
        Replay,
        Status
    }

    public class ViewerCommand {
        public string action { get; set; } = "";
        public string? playerId { get; set; }
    }

    public class Viewer {
        public ViewerType? viewerType;
        public string? playerId;
        public string? viewerUserId;

        public WebSocket socket;
        public TaskCompletionSource finishTask;
        public CancellationToken cancellationToken;

        private readonly object _subscriptionsLock = new();
        private string? _replayPlayerId;
        private readonly HashSet<string> _statusPlayerIds = new(StringComparer.Ordinal);

        public Viewer(
                WebSocket socket, 
                TaskCompletionSource task,
                CancellationToken token,
                string? viewerUserId = null) {
            this.socket = socket;
            this.finishTask = task;
            this.cancellationToken = token;
            this.viewerUserId = viewerUserId;
        }

        public void DetachFromCurrentPlayer() {
            string? replayPlayerId;
            List<string> statusPlayerIds;

            lock (_subscriptionsLock) {
                replayPlayerId = _replayPlayerId;
                statusPlayerIds = _statusPlayerIds.ToList();

                _replayPlayerId = null;
                _statusPlayerIds.Clear();
                RefreshSubscriptionStateUnsafe();
            }

            if (!string.IsNullOrEmpty(replayPlayerId)) {
                DetachFromPlayer(replayPlayerId, ViewerType.Replay);
            }

            foreach (var statusPlayerId in statusPlayerIds) {
                DetachFromPlayer(statusPlayerId, ViewerType.Status);
            }
        }

        private void DetachFromReplayPlayer() {
            string? replayPlayerId;

            lock (_subscriptionsLock) {
                replayPlayerId = _replayPlayerId;
                _replayPlayerId = null;
                RefreshSubscriptionStateUnsafe();
            }

            if (!string.IsNullOrEmpty(replayPlayerId)) {
                DetachFromPlayer(replayPlayerId, ViewerType.Replay);
            }
        }

        private void DetachFromStatusPlayer(string targetPlayerId) {
            bool removed;

            lock (_subscriptionsLock) {
                removed = _statusPlayerIds.Remove(targetPlayerId);
                if (removed) {
                    RefreshSubscriptionStateUnsafe();
                }
            }

            if (removed) {
                DetachFromPlayer(targetPlayerId, ViewerType.Status);
            }
        }

        private void DetachTargetPlayer(string targetPlayerId) {
            bool isReplayTarget;

            lock (_subscriptionsLock) {
                isReplayTarget = string.Equals(_replayPlayerId, targetPlayerId, StringComparison.Ordinal);
            }

            if (isReplayTarget) {
                DetachFromReplayPlayer();
            } else {
                DetachFromStatusPlayer(targetPlayerId);
            }
        }

        private void DetachFromPlayer(string targetPlayerId, ViewerType targetViewerType) {
            lock (SocketsController.PlayersLock) {
                var current = SocketsController.players.FirstOrDefault(p => p.playerId == targetPlayerId);
                if (current == null) return;

                if (targetViewerType == ViewerType.Replay) {
                    current.RemoveReplayViewer(this);
                } else {
                    current.RemoveStatusViewer(this);
                }

                if (!current.IsConnected && !current.HasViewers) {
                    SocketsController.players.Remove(current);
                }
            }
        }

        private void RefreshSubscriptionStateUnsafe() {
            if (_replayPlayerId != null) {
                viewerType = ViewerType.Replay;
                playerId = _replayPlayerId;
                return;
            }

            if (_statusPlayerIds.Count > 0) {
                viewerType = ViewerType.Status;
                playerId = _statusPlayerIds.FirstOrDefault();
                return;
            }

            viewerType = null;
            playerId = null;
        }

        public async Task StartProcessing() {
            var buffer = new Byte[1024];
            try {
                while (true)
                {
                    var byteCount = await socket.ReceiveAsync(buffer, cancellationToken);

                    if (byteCount == null || byteCount.Count == 0)
                    {
                        break;
                    }
                    else if (byteCount.MessageType == WebSocketMessageType.Text)
                    {
                        var receivedMessage = Encoding.UTF8.GetString(buffer, 0, byteCount.Count);

                        ViewerCommand? actionMessage = null;
                        try {
                            actionMessage = JsonConvert.DeserializeObject<ViewerCommand>(receivedMessage);
                        } catch {}

                        if (actionMessage != null) {
                            if (actionMessage.action == "disconnect") {
                                if (string.IsNullOrEmpty(actionMessage.playerId)) {
                                    DetachFromCurrentPlayer();
                                } else {
                                    DetachTargetPlayer(actionMessage.playerId);
                                }
                                continue;
                            }

                            if (string.IsNullOrEmpty(actionMessage.playerId)) {
                                continue;
                            }

                            if (actionMessage.action == "replay") {
                                lock (_subscriptionsLock) {
                                    if (string.Equals(_replayPlayerId, actionMessage.playerId, StringComparison.Ordinal)) {
                                        continue;
                                    }
                                }
                            } else {
                                lock (_subscriptionsLock) {
                                    if (_statusPlayerIds.Contains(actionMessage.playerId)) {
                                        continue;
                                    }
                                }
                            }

                            var authResult = await SocketsController.RequestViewerAuth(
                                actionMessage.playerId, viewerUserId, actionMessage.action);

                            if (authResult != null && !authResult.Allowed) {
                                var errorMsg = Encoding.UTF8.GetBytes(
                                    JsonConvert.SerializeObject(new { error = authResult.Error ?? "Not authorized" }));
                                await socket.SendAsync(
                                    new ArraySegment<byte>(errorMsg),
                                    WebSocketMessageType.Text, true, cancellationToken);
                                continue;
                            }

                            if (actionMessage.action == "replay") {
                                DetachFromCurrentPlayer();

                                lock (_subscriptionsLock) {
                                    _replayPlayerId = actionMessage.playerId;
                                    RefreshSubscriptionStateUnsafe();
                                }
                            } else {
                                DetachFromReplayPlayer();

                                lock (_subscriptionsLock) {
                                    _statusPlayerIds.Add(actionMessage.playerId);
                                    RefreshSubscriptionStateUnsafe();
                                }
                            }

                            var player = SocketsController.GetOrCreatePlayer(actionMessage.playerId);
                            await player.AddViewer(this);
                        }
                    }
                    else
                    {
                        // pong
                        await socket.SendAsync(new Byte[] { 0b1000_1010, 0 }, WebSocketMessageType.Binary, true, cancellationToken);
                    }

                    if (finishTask.Task.IsCompleted) {
                        break;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
