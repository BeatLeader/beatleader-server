using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_Server_Sockets.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SocketsController : Controller
    {
        public static Dictionary<string, List<(WebSocket, TaskCompletionSource)>> outputSockets = new();
        public static List<(WebSocket, TaskCompletionSource)> inputSockets = new();

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
                    var newList = outputSockets.ToList();
                    foreach (var item in newList)
                    {
                        foreach (var socket in item.Value)
                        {
                            if (socket.Item1.State == WebSocketState.Open) {
                                await socket.Item1.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server shutdown!", CancellationToken.None);
                            }
                        }
                    }
                });
            }
        }

        [Route("/general")]
        [Route("/scores")]
        public async Task<ActionResult> ConnectOutputSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var socketFinished = new TaskCompletionSource();
                var socketWithTask = (webSocket, socketFinished);
                var name = HttpContext.Request.Path.ToString().Replace("/", "");

                if (!outputSockets.ContainsKey(name)) {
                    outputSockets[name] = new List<(WebSocket, TaskCompletionSource)>();
                }

                outputSockets[name].Add(socketWithTask);

                var buffer = new Byte[1024];
                try {
                    while (true)
                    {
                        var byteCount = await webSocket.ReceiveAsync(buffer, _lifetime.ApplicationStopped);

                        if (byteCount == null || byteCount.Count == 0)
                        {
                            break;
                        }
                        else
                        {
                            // pong
                            await webSocket.SendAsync(new Byte[] { 0b1000_1010, 0 }, WebSocketMessageType.Binary, true, _lifetime.ApplicationStopped);
                        }

                        if (socketFinished.Task.IsCompleted) {
                            break;
                        }
                    }
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }

                outputSockets[name].Remove(socketWithTask);

                return new EmptyResult();
            }
            else
            {
                return BadRequest();
            }
        }

        [NonAction]
        private static async Task PublishNewMessage(string message, string socketName) {
            if (!outputSockets.ContainsKey(socketName)) return;

            var bytes = Encoding.UTF8.GetBytes(message);
            var arraySegment = new ArraySegment<byte>(bytes);

            foreach (var t in outputSockets[socketName])
            {
                if (t.Item1.State != WebSocketState.Open) {
                    t.Item2.TrySetResult();
                }
            }

            await Task.WhenAll(outputSockets[socketName].Select(t => 
                t.Item1.SendAsync(
                    arraySegment,
                    WebSocketMessageType.Text,
                    true,
                    _lifetime.ApplicationStopping))
                );
        }

        private string? GetIpAddress()
        {
            if (!string.IsNullOrEmpty(HttpContext.Request.Headers["cf-connecting-ip"]))
                return HttpContext.Request.Headers["cf-connecting-ip"];

            var ipAddress = HttpContext.GetServerVariable("HTTP_X_FORWARDED_FOR");

            if (!string.IsNullOrEmpty(ipAddress))
            {
                var addresses = ipAddress.Split(',');
                if (addresses.Length != 0)
                    return addresses.Last();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        [Route("/inputsocket")]
        public async Task<ActionResult> ConnectInputSocket()
        {
            string? allowedIps = _configuration.GetValue<string?>("AllowedIps");

            if (allowedIps?.Contains(GetIpAddress()) == false)
            {
                return Unauthorized();
            }

            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var socketFinished = new TaskCompletionSource();
                var socketWithTask = (webSocket, socketFinished);

                inputSockets.Add(socketWithTask);

                var buffer = new Byte[20240];
                try {
                    while (true)
                    {
                        var byteCount = await webSocket.ReceiveAsync(buffer, _lifetime.ApplicationStopped);

                        if (byteCount.MessageType == WebSocketMessageType.Text)
                        {
                            // Decode the received bytes into a string
                            var receivedMessage = Encoding.UTF8.GetString(buffer, 0, byteCount.Count);

                            var socketName = receivedMessage.Split("###").First();

                            // Call PublishNewScore with the input message
                            await PublishNewMessage(receivedMessage.Replace(socketName + "###", ""), socketName);
                        }

                        if (socketFinished.Task.IsCompleted) {
                            break;
                        }
                    }
                } catch (Exception ex) { }

                inputSockets.Remove(socketWithTask);

                return new EmptyResult();
            }
            else
            {
                return BadRequest();
            }
        }
    }
}
