using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeatLeader {
    public class WebSocketClient : IDisposable
    {
        private ClientWebSocket _webSocket;
        private readonly Uri _serverUri;
        private readonly string _cookie;
        private readonly ConcurrentQueue<(byte[] data, WebSocketMessageType type)> _sendQueue = new();
        private readonly SemaphoreSlim _sendSignal = new(0, int.MaxValue);
        private CancellationTokenSource _cts;
        private Task _senderTask;

        public WebSocketClient(string serverUri, string cookie)
        {
            _serverUri = new Uri(serverUri);
            _cookie = cookie;
        }

        public bool IsAlive() =>
            _webSocket != null && _webSocket.State == WebSocketState.Open;

        public async Task ConnectAsync(CancellationToken token = default)
        {
            _webSocket = CreateSocket();
            await _webSocket.ConnectAsync(_serverUri, token);
            _cts = new CancellationTokenSource();
            _senderTask = Task.Run(() => SenderLoop(_cts.Token));
        }

        private ClientWebSocket CreateSocket()
        {
            var ws = new ClientWebSocket();
            var container = new CookieContainer();
            container.SetCookies(_serverUri, _cookie);
            ws.Options.Cookies = container;
            return ws;
        }

        public void QueueBinary(byte[] data)
        {
            _sendQueue.Enqueue((data, WebSocketMessageType.Binary));
            _sendSignal.Release();
        }

        public void QueueText(string message)
        {
            _sendQueue.Enqueue((Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text));
            _sendSignal.Release();
        }

        private async Task SenderLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await _sendSignal.WaitAsync(token); }
                catch (OperationCanceledException) { break; }

                while (_sendQueue.TryDequeue(out var msg))
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        if (!IsAlive()) await ReconnectAsync(token);
                        if (IsAlive())
                        {
                            await _webSocket.SendAsync(
                                new ArraySegment<byte>(msg.data),
                                msg.type,
                                true,
                                token);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch
                    {
                        try { await ReconnectAsync(token); } catch { }
                    }
                }
            }
        }

        private async Task ReconnectAsync(CancellationToken token = default)
        {
            try { _webSocket?.Dispose(); } catch { }
            _webSocket = CreateSocket();
            await _webSocket.ConnectAsync(_serverUri, token);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _senderTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts?.Dispose();

            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                            .Wait(TimeSpan.FromSeconds(2));
                    }
                    catch { }
                }
                _webSocket.Dispose();
            }
        }
    }
}
