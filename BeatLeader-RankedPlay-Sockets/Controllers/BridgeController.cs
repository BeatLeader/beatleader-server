using BeatLeader_RankedPlay_Sockets.Core;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_RankedPlay_Sockets.Controllers {
    [ApiExplorerSettings(IgnoreApi = true)]
    public class BridgeController : Controller {
        private readonly MatchmakingService _matchmaking;
        private readonly IConfiguration _configuration;
        private readonly IHostApplicationLifetime _lifetime;

        public BridgeController(
            MatchmakingService matchmaking,
            IConfiguration configuration,
            IHostApplicationLifetime lifetime) {
            _matchmaking = matchmaking;
            _configuration = configuration;
            _lifetime = lifetime;
        }

        [Route("/rankedplay/bridge")]
        public async Task<ActionResult> ConnectBridge() {
            if (!HttpContext.WebSockets.IsWebSocketRequest) {
                return BadRequest();
            }

            string? secret = HttpContext.Request.Query["secret"];
            string? configSecret = _configuration.GetValue<string>("BridgeSecret");
            if (string.IsNullOrEmpty(configSecret) || secret != configSecret) {
                return Unauthorized();
            }

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            _matchmaking.SetBridgeSocket(webSocket);

            try {
                await ProcessBridgeConnection(webSocket);
            } finally {
                _matchmaking.ClearBridgeSocket(webSocket);
            }

            return new EmptyResult();
        }

        private async Task ProcessBridgeConnection(WebSocket socket) {
            var buffer = new byte[8192];
            using var messageStream = new MemoryStream();

            while (socket.State == WebSocketState.Open && !_lifetime.ApplicationStopped.IsCancellationRequested) {
                WebSocketReceiveResult result;
                try {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _lifetime.ApplicationStopped);
                } catch (WebSocketException) { break; }

                if (result.MessageType == WebSocketMessageType.Close || result.Count == 0) break;

                if (result.MessageType == WebSocketMessageType.Text) {
                    messageStream.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage) continue;

                    var json = Encoding.UTF8.GetString(messageStream.ToArray());
                    messageStream.SetLength(0);

                    _matchmaking.HandleBridgeMessage(json);
                }
            }
        }
    }
}
