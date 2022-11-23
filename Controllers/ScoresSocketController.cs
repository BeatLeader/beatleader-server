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
    public class ScoresSocketController : Controller
    {
        public static List<(WebSocket, TaskCompletionSource)> sockets = new();

        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        public ScoresSocketController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration,
            IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _environment = env;
        }

        [Route("/scores")]
        public async Task<ActionResult> ScoresSocket()
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

                    await socketFinished.Task;

                    sockets.Remove(socketWithTask);

                    return Ok();
                } else {
                    return Redirect($"wss://{socketHost}.azurewebsites.net/scores");
                }
            }
            else
            {
                return BadRequest();
            }
        }

        [NonAction]
        private static async Task PublishNewScore(ScoreResponseWithMyScore score) {
            var message = JsonConvert.SerializeObject(score, new JsonSerializerSettings 
            { 
                ContractResolver = new CamelCasePropertyNamesContractResolver() 
            });
            var bytes = Encoding.UTF8.GetBytes(message);
            var arraySegment = new ArraySegment<byte>(bytes);

            foreach (var t in ScoresSocketController.sockets)
            {
                if (t.Item1.State != WebSocketState.Open) {
                    t.Item2.TrySetResult();
                }
            }

            await Task.WhenAll(ScoresSocketController.sockets.Select(t => 
                t.Item1.SendAsync(
                    arraySegment,
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None))
                );
        }

        [NonAction]
        public static async Task TryPublishNewScore(Score score, IConfiguration configuration, AppContext context) {

            string serverName = configuration.GetValue<string?>("ServerName");
            string socketHost = configuration.GetValue<string?>("SocketHost");
            if (serverName != socketHost) {
                await WebRequest.Create($"https://{socketHost}.azurewebsites.net/newscore?id={score.Id}").GetResponseAsync();
            } else {
                var scoreToPublish = ScoreWithMyScore(score, 0);
                if (scoreToPublish.Leaderboard.Song == null) {
                    scoreToPublish.Leaderboard.Song = await context.Songs.Where(s => s.Id == score.Leaderboard.SongId).FirstOrDefaultAsync();
                }
                await PublishNewScore(scoreToPublish);
            }
        }

        [HttpGet("~/newscore")]
        public async Task<ActionResult> NewScoreIsPosted([FromQuery] int id)
        {
            if (!_environment.IsDevelopment()) {
                string allowedIps = _configuration.GetValue<string?>("AllowedIps");

                if (!allowedIps.Contains(HttpContext.Request.HttpContext.Connection.RemoteIpAddress.ToString())) {
                    return Unauthorized();
                }
            }

            var score = _context
                .Scores
                .Where(s => s.Id == id)
                .Include(s => s.Player)
                .Include(s => s.Leaderboard)
                    .ThenInclude(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Leaderboard)
                    .ThenInclude(lb => lb.Song)
                .Select(ScoreWithMyScore)
                .FirstOrDefault();
            if (score != null) {
                await PublishNewScore(score);
            }

            return Ok();
        }
    }
}
