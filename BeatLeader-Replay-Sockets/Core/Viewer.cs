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
        public string action { get; set; }
        public string playerId { get; set; }
    }

    public class Viewer {
        public ViewerType? viewerType;
        public string? playerId;
        public string? viewerUserId;

        public WebSocket socket;
        public TaskCompletionSource finishTask;
        public CancellationToken cancellationToken;

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
            if (playerId == null) return;

            lock (SocketsController.PlayersLock) {
                var current = SocketsController.players.FirstOrDefault(p => p.playerId == playerId);
                if (current != null) {
                    current.RemoveViewer(this);
                    if (!current.IsConnected && !current.HasViewers) {
                        SocketsController.players.Remove(current);
                    }
                }
            }

            playerId = null;
            viewerType = null;
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
                                DetachFromCurrentPlayer();
                                continue;
                            }

                            DetachFromCurrentPlayer();

                            if (actionMessage.action == "replay") {
                                viewerType = ViewerType.Replay;
                            } else {
                                viewerType = ViewerType.Status;
                            }
                            playerId = actionMessage.playerId;

                            var authResult = await SocketsController.RequestViewerAuth(
                                playerId, viewerUserId, actionMessage.action);

                            if (authResult != null && !authResult.Allowed) {
                                var errorMsg = Encoding.UTF8.GetBytes(
                                    JsonConvert.SerializeObject(new { error = authResult.Error ?? "Not authorized" }));
                                await socket.SendAsync(
                                    new ArraySegment<byte>(errorMsg),
                                    WebSocketMessageType.Text, true, cancellationToken);
                                playerId = null;
                                viewerType = null;
                                continue;
                            }

                            var player = SocketsController.GetOrCreatePlayer(playerId);
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
