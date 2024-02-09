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
    public class ClanRankingChangesResponse {
        public LeaderboardResponse Leaderboard { get; set; }
        public int? PreviousCaptorId { get; set; }
        public int? CurrentCaptorId { get; set; }

        public ClanRankingChangesResponse(ClanRankingChanges changes) {
            PreviousCaptorId = changes.PreviousCaptorId;
            CurrentCaptorId = changes.CurrentCaptorId;
            var l = changes.Leaderboard;
            Leaderboard = new LeaderboardResponse {
                Id = l.Id,
                Song = l.Song,
                Difficulty = new DifficultyResponse {
                    Id = l.Difficulty.Id,
                    Value = l.Difficulty.Value,
                    Mode = l.Difficulty.Mode,
                    DifficultyName = l.Difficulty.DifficultyName,
                    ModeName = l.Difficulty.ModeName,
                    Status = l.Difficulty.Status,
                    ModifierValues = l.Difficulty.ModifierValues,
                    ModifiersRating = l.Difficulty.ModifiersRating,
                    NominatedTime  = l.Difficulty.NominatedTime,
                    QualifiedTime  = l.Difficulty.QualifiedTime,
                    RankedTime = l.Difficulty.RankedTime,

                    Stars  = l.Difficulty.Stars,
                    PredictedAcc  = l.Difficulty.PredictedAcc,
                    PassRating  = l.Difficulty.PassRating,
                    AccRating  = l.Difficulty.AccRating,
                    TechRating  = l.Difficulty.TechRating,
                    Type  = l.Difficulty.Type,

                    Njs  = l.Difficulty.Njs,
                    Nps  = l.Difficulty.Nps,
                    Notes  = l.Difficulty.Notes,
                    Bombs  = l.Difficulty.Bombs,
                    Walls  = l.Difficulty.Walls,
                    MaxScore = l.Difficulty.MaxScore,
                    Duration  = l.Difficulty.Duration,

                    Requirements = l.Difficulty.Requirements,
                }
            };
        }
    }
    public class ClanRankingChangesDescriptionResponse {
        public string? PlayerId { get; set; }
        public int? ClanId { get; set; }
        public ClanResponseFull? Clan { get; set; }
        public GlobalMapEvent? PlayerAction { get; set; }
        public ScoreResponse? Score { get; set; }
        public List<ClanRankingChangesResponse>? Changes { get; set; }

        public ClanRankingChangesDescriptionResponse(ClanRankingChangesDescription description) {
            PlayerId = description.PlayerId;
            ClanId = description.ClanId;
            PlayerAction = description.GlobalMapEvent;

            var clan = description.Clan;
            if (clan != null) {
                Clan = new ClanResponseFull {
                    Id = clan.Id,
                    Name = clan.Name,
                    Color = clan.Color,
                    Icon = clan.Icon,
                    Tag = clan.Tag,
                    LeaderID = clan.LeaderID,
                    Description = clan.Description,
                    Bio = clan.Bio,
                    PlayersCount = clan.PlayersCount,
                    Pp = clan.Pp,
                    Rank = clan.Rank,
                    AverageRank = clan.AverageRank,
                    AverageAccuracy = clan.AverageAccuracy,
                    RankedPoolPercentCaptured = clan.RankedPoolPercentCaptured,
                    CaptureLeaderboardsCount = clan.CaptureLeaderboardsCount
                };
            }

            var s = description.Score;
            if (s != null) {
                Score = new ScoreResponse {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    TechPP = s.TechPP,
                    AccPP = s.AccPP,
                    PassPP = s.PassPP,
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
                    ScoreImprovement = s.ScoreImprovement,
                    Country = s.Country,
                    Offsets = s.ReplayOffsets,
                };
            }

            Changes = description.Changes.Select(c => new ClanRankingChangesResponse(c)).ToList();
        }
    }

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

        [Route("/clansocket")]
        public async Task<ActionResult> ClansSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                return Redirect(SocketHost + "clansocket");
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
        public static async Task ClanRankingChanges(ClanRankingChangesDescription changes) {
            try {
                if (changes.Changes.Count == 0) return;
                var socketMessage = new SocketMessage { Message = "globalmap", Data = new ClanRankingChangesDescriptionResponse(changes) };

                var message = JsonConvert.SerializeObject(socketMessage, new JsonSerializerSettings 
                { 
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });
                await PublishNewMessage(message, "clansocket");
            } catch (Exception e)
            {
                Console.WriteLine($"clansocket exception {e}");
            }
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
