using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class GeneralSocketController : Controller
    {
        public static List<(WebSocket, TaskCompletionSource)> sockets = new();

        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;
        static IHostApplicationLifetime _lifetime;

        public GeneralSocketController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration,
            IWebHostEnvironment env,
            IHostApplicationLifetime lifetime)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _environment = env;

            if (_lifetime == null) {
                _lifetime = lifetime;

                lifetime.ApplicationStopping.Register(async () => {
                    var newList = sockets.ToList();
                    foreach (var socket in newList)
                    {
                        if (socket.Item1.State == WebSocketState.Open) {
                            await socket.Item1.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server shutdown!", CancellationToken.None);
                        }
                    }
                });
            }
        }

        [Route("/general")]
        public async Task<ActionResult> GeneralSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                string serverName = _configuration.GetValue<string?>("ServerName");
                string socketHost = _configuration.GetValue<string?>("SocketHost");
                if (serverName == socketHost) {
                    using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                    var socketFinished = new TaskCompletionSource();
                    var socketWithTask = (webSocket, socketFinished);

                    sockets.Add(socketWithTask);

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
                    } catch (Exception ex) { }

                    sockets.Remove(socketWithTask);

                    return new EmptyResult();
                } else {
                    return Redirect($"wss://{socketHost}/general");
                }
            }
            else
            {
                return BadRequest();
            }
        }

        class SocketMessage {
            public string Message { get; set; } 
            public dynamic Data { get; set; } 
        }

        [NonAction]
        private static async Task PublishScore(ScoreResponseWithMyScore score, string messageName) {
            var socketMessage = new SocketMessage { Message = messageName, Data = score };

            var message = JsonConvert.SerializeObject(socketMessage, new JsonSerializerSettings 
            { 
                ContractResolver = new CamelCasePropertyNamesContractResolver() 
            });
            var bytes = Encoding.UTF8.GetBytes(message);
            var arraySegment = new ArraySegment<byte>(bytes);

            foreach (var t in GeneralSocketController.sockets)
            {
                if (t.Item1.State != WebSocketState.Open) {
                    t.Item2.TrySetResult();
                }
            }

            await Task.WhenAll(GeneralSocketController.sockets.Select(t => 
                t.Item1.SendAsync(
                    arraySegment,
                    WebSocketMessageType.Text,
                    true,
                    _lifetime.ApplicationStopping))
                );
        }

        [NonAction]
        public static async Task ScoreWas(string message, Score score, IConfiguration configuration, AppContext context) {
            try {
                string serverName = configuration.GetValue<string?>("ServerName");
                string socketHost = configuration.GetValue<string?>("SocketHost");
                if (serverName == socketHost) {
                    var scoreToPublish = ScoreWithMyScore(score, 0);
                    if (score.Leaderboard?.SongId != null && scoreToPublish.Leaderboard.Song == null) {
                        scoreToPublish.Leaderboard.Song = await context.Songs.Where(s => s.Id == score.Leaderboard.SongId).FirstOrDefaultAsync();
                    }
                    await PublishScore(scoreToPublish, message);
                }
            } catch { }
        }

        [NonAction]
        public static async Task ScoreWasUploaded(Score score, IConfiguration configuration, AppContext context) {
            await ScoreWas("upload", score, configuration, context);
        }

        [NonAction]
        public static async Task ScoreWasAccepted(Score score, IConfiguration configuration, AppContext context) {
            await ScoreWas("accepted", score, configuration, context);
        }

        [NonAction]
        public static async Task ScoreWasRejected(Score score, IConfiguration configuration, AppContext context) {
            await ScoreWas("rejected", score, configuration, context);
        }
    }
}

