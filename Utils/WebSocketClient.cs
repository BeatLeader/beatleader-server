using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_Server.Utils
{
    public class WebSocketClient
    {
        private ClientWebSocket _webSocket;
        private readonly Uri _serverUri;
        private readonly TimeSpan _keepAliveInterval;
        private readonly TimeSpan _reconnectDelay;

        public WebSocketClient(string serverUri)
        {
            _serverUri = new Uri(serverUri);
            _webSocket = new ClientWebSocket();
            _keepAliveInterval = TimeSpan.FromSeconds(30);
            _reconnectDelay = TimeSpan.FromSeconds(5);
        }

        public bool IsAlive() {
            return _webSocket.State == WebSocketState.Open;
        }

        public async Task ConnectAsync()
        {
            await _webSocket.ConnectAsync(_serverUri, CancellationToken.None);
            StartKeepAlive();
        }

        private async Task StartKeepAlive()
        {
            while (_webSocket.State == WebSocketState.Open)
            {
                await Task.Delay(_keepAliveInterval);
                try
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(new byte[1]), WebSocketMessageType.Binary, true, CancellationToken.None);
                } catch
                {
                    await ReconnectAsync();
                }
            }
            await ReconnectAsync();
        }

        public async Task SendAsync(string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task ReconnectAsync()
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                _webSocket.Dispose();
                _webSocket = new ClientWebSocket();
                await ConnectAsync();
            }
        }

        public async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 4];
            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                } else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                }
            }
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
