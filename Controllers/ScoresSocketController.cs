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
            try {

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
            } catch { }
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
                .Select(s => new ScoreResponseWithMyScore
                {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
                    BonusPp = s.BonusPp,
                    Rank = s.Rank,
                    Replay = s.Replay,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    Pauses = s.Pauses,
                    FullCombo = s.FullCombo,
                    Hmd = s.Hmd,
                    Controller = s.Controller,
                    MaxCombo = s.MaxCombo,
                    Timeset = s.Timeset,
                    ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                    Timepost = s.Timepost,
                    LeaderboardId = s.LeaderboardId,
                    Platform = s.Platform,
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Platform = s.Player.Platform,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        Socials = s.Player.Socials,
                        PatreonFeatures = s.Player.PatreonFeatures,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement,
                    RankVoting = s.RankVoting,
                    Metadata = s.Metadata,
                    Country = s.Country,
                    Offsets = s.ReplayOffsets,
                    Leaderboard = new LeaderboardResponse
                    {
                        Id = s.LeaderboardId,
                        Song = s.Leaderboard.Song,
                        Difficulty = s.Leaderboard.Difficulty
                    },
                    Weight = s.Weight,
                    AccLeft = s.AccLeft,
                    AccRight = s.AccRight,
                })
                .FirstOrDefault();
            if (score != null) {
                await PublishNewScore(score);
            }

            return Ok();
        }
    }
}
