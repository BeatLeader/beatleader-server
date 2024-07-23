using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_Server.Utils
{
    public class WebSocketClient
    {
        private ClientWebSocket _webSocket;
        private readonly Uri _serverUri;

        public WebSocketClient(string serverUri)
        {
            _serverUri = new Uri(serverUri);
            _webSocket = new ClientWebSocket();
        }

        public bool IsAlive() {
            return _webSocket.State == WebSocketState.Open;
        }

        public async Task ConnectAsync(CancellationToken? token = null)
        {
            await _webSocket.ConnectAsync(_serverUri, token ?? CancellationToken.None);
        }

        public async Task Ping()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(new byte[1]), WebSocketMessageType.Binary, true, CancellationToken.None);
                } catch
                {
                    await ReconnectAsync();
                }
            } else {
                await ReconnectAsync();
            }
        }

        public async Task SendAsync(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReconnectAsync(CancellationToken? token = null)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                _webSocket.Dispose();
                _webSocket = new ClientWebSocket();
                await ConnectAsync(token);
            }
        }

        public async Task<string?> ReceiveMessagesAsync(CancellationToken? token = null)
        {
            try {
                var buffer = new byte[1024 * 64];

                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token ?? CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token ?? CancellationToken.None);
                } else if (result.MessageType == WebSocketMessageType.Text)
                {
                    return Encoding.UTF8.GetString(buffer, 0, result.Count);
                }
            } catch {
                await ReconnectAsync(token);
            }
            return null;
        }

        public async Task CloseAsync()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            _webSocket.Dispose();
        }
    }

}
