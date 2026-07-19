using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_RankedPlay_Tester;

public class WebSocketClient : IDisposable {
    private ClientWebSocket _webSocket;
    private readonly Uri _serverUri;
    private readonly string _cookie;
    private readonly ConcurrentQueue<(byte[] data, WebSocketMessageType type)> _sendQueue = new();
    private readonly SemaphoreSlim _sendSignal = new(0, int.MaxValue);
    private CancellationTokenSource? _cts;
    private Task? _senderTask;
    private Task? _receiverTask;

    public event Action<string>? OnTextMessage;
    public event Action<byte[]>? OnBinaryMessage;
    public event Action? OnDisconnected;

    public WebSocketClient(string serverUri, string cookie) {
        _serverUri = new Uri(serverUri);
        _cookie = cookie;
        _webSocket = CreateSocket();
    }

    public bool IsAlive() =>
        _webSocket.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken token = default) {
        _webSocket = CreateSocket();
        await _webSocket.ConnectAsync(_serverUri, token);
        _cts = new CancellationTokenSource();
        _senderTask = Task.Run(() => SenderLoop(_cts.Token));
        _receiverTask = Task.Run(() => ReceiverLoop(_cts.Token));
    }

    private ClientWebSocket CreateSocket() {
        var ws = new ClientWebSocket();
        var container = new CookieContainer();
        container.SetCookies(_serverUri, _cookie);
        ws.Options.Cookies = container;
        return ws;
    }

    public void QueueBinary(byte[] data) {
        _sendQueue.Enqueue((data, WebSocketMessageType.Binary));
        _sendSignal.Release();
    }

    public void QueueText(string message) {
        _sendQueue.Enqueue((Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text));
        _sendSignal.Release();
    }

    private async Task SenderLoop(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try { await _sendSignal.WaitAsync(token); } catch (OperationCanceledException) { break; }

            while (_sendQueue.TryDequeue(out var msg)) {
                if (token.IsCancellationRequested) break;
                try {
                    if (!IsAlive()) break;
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(msg.data),
                        msg.type, true, token);
                } catch (OperationCanceledException) { break; } catch { break; }
            }
        }
    }

    private async Task ReceiverLoop(CancellationToken token) {
        var buffer = new byte[8192];
        using var messageStream = new MemoryStream();

        try {
            while (_webSocket.State == WebSocketState.Open && !token.IsCancellationRequested) {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.Count == 0) continue;

                messageStream.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage) continue;

                var data = messageStream.ToArray();
                messageStream.SetLength(0);

                if (result.MessageType == WebSocketMessageType.Text) {
                    OnTextMessage?.Invoke(Encoding.UTF8.GetString(data));
                } else {
                    OnBinaryMessage?.Invoke(data);
                }
            }
        } catch (OperationCanceledException) {
        } catch (WebSocketException) {
        }

        OnDisconnected?.Invoke();
    }

    public async Task CloseAsync() {
        if (_webSocket.State == WebSocketState.Open) {
            try {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "Done",
                    CancellationToken.None);
            } catch { }
        }
        _cts?.Cancel();
    }

    public void Dispose() {
        _cts?.Cancel();
        try { _senderTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _receiverTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _webSocket.Dispose();
    }
}
