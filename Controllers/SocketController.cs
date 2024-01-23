using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SocketController : Controller
    {
        public static WebSocketClient SocketClient;
        public static string SocketHost = "wss://sockets.api.beatleader.xyz/";

        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        public SocketController(
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
                return Redirect(SocketHost + "scores");
            } else {
                return BadRequest();
            }
        }

        [Route("/general")]
        public async Task<ActionResult> GeneralSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                return Redirect(SocketHost + "general");
            } else {
                return BadRequest();
            }
        }

        [NonAction]
        private static async Task PublishNewMessage(string message, string socketName) {
            if (SocketClient == null) {
                SocketClient = new WebSocketClient(SocketHost + "inputsocket");
                await SocketClient.ConnectAsync();
                SocketService.AddSocketToManage(SocketClient);
            }
            await SocketClient.SendAsync(socketName + "###" + message);
        }

        [NonAction]
        public static async Task TryPublishNewScore(Score score, AppContext context) {
            try {
                var scoreToPublish = ScoreWithMyScore(score, 0);
                if (scoreToPublish.Leaderboard.Song == null) {
                    scoreToPublish.Leaderboard.Song = await context.Songs.Where(s => s.Id == score.Leaderboard.SongId).FirstOrDefaultAsync();
                }
                if (scoreToPublish.Leaderboard.Difficulty != null && !scoreToPublish.Leaderboard.Difficulty.Status.WithRating()) {
                    scoreToPublish.Leaderboard.HideRatings();
                }
                var message = JsonConvert.SerializeObject(scoreToPublish, new JsonSerializerSettings 
                { 
                    ContractResolver = new CamelCasePropertyNamesContractResolver() 
                });
                await PublishNewMessage(message, "scores");
            } catch {
                SocketClient = null;
            }
        }

        class SocketMessage {
            public string Message { get; set; } 
            public dynamic Data { get; set; } 
        }

        [NonAction]
        public static async Task ScoreWas(string action, Score score, AppContext context) {
            try {
                var scoreToPublish = ScoreWithMyScore(score, 0);
                if (score.Leaderboard?.SongId != null && scoreToPublish.Leaderboard.Song == null) {
                    scoreToPublish.Leaderboard.Song = await context.Songs.Where(s => s.Id == score.Leaderboard.SongId).FirstOrDefaultAsync();
                }
                var socketMessage = new SocketMessage { Message = action, Data = score };

                var message = JsonConvert.SerializeObject(socketMessage, new JsonSerializerSettings 
                { 
                    ContractResolver = new CamelCasePropertyNamesContractResolver() 
                });
                await PublishNewMessage(message, "general");
            } catch { }
        }

        [NonAction]
        public static async Task ScoreWasUploaded(Score score, AppContext context) {
            await ScoreWas("upload", score, context);
        }

        [NonAction]
        public static async Task ScoreWasAccepted(Score score, AppContext context) {
            await ScoreWas("accepted", score, context);
        }

        [NonAction]
        public static async Task ScoreWasRejected(Score score, AppContext context) {
            await ScoreWas("rejected", score, context);
        }
    }
}
