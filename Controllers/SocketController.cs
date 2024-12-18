using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ClanRankingChangesResponse {
        public string LeaderboardId { get; set; }
        public int? PreviousCaptorId { get; set; }
        public int? CurrentCaptorId { get; set; }

        public ClanRankingChangesResponse(ClanRankingChanges changes) {
            PreviousCaptorId = changes.PreviousCaptorId;
            CurrentCaptorId = changes.CurrentCaptorId;
            LeaderboardId = changes.Leaderboard.Id;
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
        public static string SocketHost = "wss://sockets.api.beatleader.com/";

        private readonly AppContext _context;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        public SocketController(
            AppContext context,
            IConfiguration configuration,
            IWebHostEnvironment env)
        {
            _context = context;

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
                    scoreToPublish.Leaderboard.Song = await context
                        .Songs
                        .Where(s => s.Id == score.Leaderboard.SongId)
                        .Select(s => new CompactSongResponse {
                            Id = s.Id,
                            Hash = s.Hash,
                            Name = s.Name,
            
                            SubName = s.SubName,
                            Author = s.Author,
                            Mapper = s.Mapper,
                            MapperId = s.MapperId,
                            CollaboratorIds = s.CollaboratorIds,
                            CoverImage = s.CoverImage
                        })
                        .FirstOrDefaultAsync();
                }
                if (scoreToPublish.Leaderboard.Difficulty != null && !scoreToPublish.Leaderboard.Difficulty.Status.WithRating()) {
                    scoreToPublish.Leaderboard.HideRatings();
                }
                var message = JsonSerializer.Serialize(scoreToPublish, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    ReferenceHandler = ReferenceHandler.IgnoreCycles
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
                if (scoreToPublish.Leaderboard == null) {
                    scoreToPublish.Leaderboard = await context
                        .Leaderboards
                        .Where(l => l.Id == score.LeaderboardId)
                        .Include(l => l.Song)
                        .Select(lb => new CompactLeaderboardResponse {
                            Id = lb.Id,
                            Song = new CompactSongResponse {
                                Id = lb.Song.Id,
                                Hash = lb.Song.Hash,
                                Name = lb.Song.Name,
            
                                SubName = lb.Song.SubName,
                                Author = lb.Song.Author,
                                Mapper = lb.Song.Mapper,
                                MapperId = lb.Song.MapperId,
                                CollaboratorIds = lb.Song.CollaboratorIds,
                                CoverImage = lb.Song.CoverImage,
                                FullCoverImage = lb.Song.FullCoverImage,
                                Bpm = lb.Song.Bpm,
                                Duration = lb.Song.Duration,
                            },
                            Difficulty = new DifficultyResponse {
                                Id = lb.Difficulty.Id,
                                Value = lb.Difficulty.Value,
                                Mode = lb.Difficulty.Mode,
                                DifficultyName = lb.Difficulty.DifficultyName,
                                ModeName = lb.Difficulty.ModeName,
                                Status = lb.Difficulty.Status,
                                ModifierValues = lb.Difficulty.ModifierValues,
                                ModifiersRating = lb.Difficulty.ModifiersRating,
                                NominatedTime  = lb.Difficulty.NominatedTime,
                                QualifiedTime  = lb.Difficulty.QualifiedTime,
                                RankedTime = lb.Difficulty.RankedTime,

                                Stars  = lb.Difficulty.Stars,
                                PredictedAcc  = lb.Difficulty.PredictedAcc,
                                PassRating  = lb.Difficulty.PassRating,
                                AccRating  = lb.Difficulty.AccRating,
                                TechRating  = lb.Difficulty.TechRating,
                                Type  = lb.Difficulty.Type,

                                Njs  = lb.Difficulty.Njs,
                                Nps  = lb.Difficulty.Nps,
                                Notes  = lb.Difficulty.Notes,
                                Bombs  = lb.Difficulty.Bombs,
                                Walls  = lb.Difficulty.Walls,
                                MaxScore = lb.Difficulty.MaxScore,
                                Duration  = lb.Difficulty.Duration,

                                Requirements = lb.Difficulty.Requirements,
                            }
                        })
                        .FirstOrDefaultAsync();
                }
                if (score.Leaderboard?.SongId != null && scoreToPublish.Leaderboard.Song == null) {
                    scoreToPublish.Leaderboard.Song = await context
                        .Songs
                        .Where(s => s.Id == score.Leaderboard.SongId)
                        .Select(s => new CompactSongResponse {
                            Id = s.Id,
                            Hash = s.Hash,
                            Name = s.Name,
            
                            SubName = s.SubName,
                            Author = s.Author,
                            Mapper = s.Mapper,
                            MapperId = s.MapperId,
                            CollaboratorIds = s.CollaboratorIds,
                            CoverImage = s.CoverImage
                        })
                        .FirstOrDefaultAsync();
                }
                var socketMessage = new SocketMessage { Message = action, Data = scoreToPublish };

                var message = JsonSerializer.Serialize(socketMessage, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    ReferenceHandler = ReferenceHandler.IgnoreCycles
                });
                await PublishNewMessage(message, "general");
            } catch { }
        }

        [NonAction]
        public static async Task ClanRankingChanges(ClanRankingChangesDescription changes) {
            try {
                if (changes.Changes.Count == 0) return;
                var socketMessage = new SocketMessage { Message = "globalmap", Data = new ClanRankingChangesDescriptionResponse(changes) };

                var message = JsonSerializer.Serialize(socketMessage, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    ReferenceHandler = ReferenceHandler.IgnoreCycles
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
