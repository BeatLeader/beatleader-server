using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class FriendsController : Controller {

        private readonly AppContext _context;
        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public FriendsController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration) {
            _context = context;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [NonAction]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> ContextsFriendsScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null) {

            string userId = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.Include(p => p.ProfileSettings).FirstOrDefaultAsync(p => p.Id == userId);
            if (player == null) {
                return NotFound();
            }

            bool showAllRatings = player.ProfileSettings?.ShowAllRatings ?? false; 
            IQueryable<ScoreContextExtension> sequence;

            using (_serverTiming.TimeAction("sequence")) {
                var friends = _context
                    .Friends
                    .Where(f => f.Id == player.Id)
                    .Include(f => f.Friends)
                    .FirstOrDefault();

                if (friends != null) {
                    var friendsList = friends.Friends.Select(f => f.Id).ToList();
                    sequence = _context.ScoreContextExtensions.Where(s => (s.PlayerId == player.Id || friendsList.Contains(s.PlayerId)) && s.Context == leaderboardContext);
                } else {
                    sequence = _context.ScoreContextExtensions.Where(s => s.PlayerId == player.Id && s.Context == leaderboardContext);
                }

                switch (sortBy) {
                    case "date":
                        sequence = sequence.Order(order, t => t.Timeset);
                        break;
                    case "pp":
                        sequence = sequence.Order(order, t => t.Pp);
                        break;
                    case "acc":
                        sequence = sequence.Order(order, t => t.Accuracy);
                        break;
                    case "pauses":
                        sequence = sequence.Order(order, t => t.Score.Pauses);
                        break;
                    case "rank":
                        sequence = sequence.Order(order, t => t.Rank);
                        break;
                    case "passRating":
                        sequence = sequence.Order(order, s => showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? s.Leaderboard.Difficulty.PassRating : 0);
                        break;
                    case "techRating":
                        sequence = sequence.Order(order, s => showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? s.Leaderboard.Difficulty.TechRating : 0);
                        break;
                    case "stars":
                        sequence = sequence.Order(order, s => showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? s.Leaderboard.Difficulty.Stars : 0);
                        break;
                    case "mistakes":
                        sequence = sequence.Order(order, t => t.Score.BadCuts + t.Score.BombCuts + t.Score.MissedNotes + t.Score.WallsHit);
                        break;
                    default:
                        break;
                }
                if (search != null) {
                    string lowSearch = search.ToLower();
                    sequence = sequence
                        .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
                }
                if (diff != null) {
                    sequence = sequence.Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null) {
                    sequence = sequence.Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
                }
                if (stars_from != null) {
                    sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null) {
                    sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars <= stars_to);
                }
            }

            var resultList = sequence
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .AsSplitQuery()
                    .Select(s => new ScoreResponseWithMyScore {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        FcAccuracy = s.Score.FcAccuracy,
                        FcPp = s.Score.FcPp,
                        BonusPp = s.BonusPp,
                        Rank = s.Rank,
                        Replay = s.Score.Replay,
                        Modifiers = s.Modifiers,
                        BadCuts = s.Score.BadCuts,
                        MissedNotes = s.Score.MissedNotes,
                        BombCuts = s.Score.BombCuts,
                        WallsHit = s.Score.WallsHit,
                        Pauses = s.Score.Pauses,
                        FullCombo = s.Score.FullCombo,
                        Hmd = s.Score.Hmd,
                        Controller = s.Score.Controller,
                        MaxCombo = s.Score.MaxCombo,
                        Timeset = s.Score.Timeset,
                        ReplaysWatched = s.Score.AnonimusReplayWatched + s.Score.AuthorizedReplayWatched,
                        Timepost = s.Score.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Score.Platform,
                        Player = new PlayerResponse {
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
                            Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        },
                        ScoreImprovement = s.ScoreImprovement,
                        RankVoting = s.Score.RankVoting,
                        Metadata = s.Score.Metadata,
                        Country = s.Score.Country,
                        Offsets = s.Score.ReplayOffsets,
                        Leaderboard = new LeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
                            Difficulty = new DifficultyResponse {
                                Id = s.Leaderboard.Difficulty.Id,
                                Value = s.Leaderboard.Difficulty.Value,
                                Mode = s.Leaderboard.Difficulty.Mode,
                                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                                ModeName = s.Leaderboard.Difficulty.ModeName,
                                Status = s.Leaderboard.Difficulty.Status,
                                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                                NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                                QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                                RankedTime = s.Leaderboard.Difficulty.RankedTime,

                                Stars  = s.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = s.Leaderboard.Difficulty.PassRating,
                                AccRating  = s.Leaderboard.Difficulty.AccRating,
                                TechRating  = s.Leaderboard.Difficulty.TechRating,
                                PatternRating = s.Leaderboard.Difficulty.PatternRating,
                                LinearRating = s.Leaderboard.Difficulty.LinearRating,
                                Type  = s.Leaderboard.Difficulty.Type,

                                Njs  = s.Leaderboard.Difficulty.Njs,
                                Nps  = s.Leaderboard.Difficulty.Nps,
                                Notes  = s.Leaderboard.Difficulty.Notes,
                                Bombs  = s.Leaderboard.Difficulty.Bombs,
                                Walls  = s.Leaderboard.Difficulty.Walls,
                                MaxScore = s.Leaderboard.Difficulty.MaxScore,
                                Duration  = s.Leaderboard.Difficulty.Duration,

                                Requirements = s.Leaderboard.Difficulty.Requirements,
                            }
                        },
                        Weight = s.Weight,
                        AccLeft = s.Score.AccLeft,
                        AccRight = s.Score.AccRight,
                        MaxStreak = s.Score.MaxStreak
                    }).ToList();

            foreach (var resultScore in resultList) {
                if (!showAllRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }

            var result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = resultList
            };

            var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

            var myScores = _context
                .ScoreContextExtensions
                .Where(s => s.PlayerId == userId && leaderboards.Contains(s.LeaderboardId))
                .Select(s => new ScoreResponseWithMyScore {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        FcAccuracy = s.Score.FcAccuracy,
                        FcPp = s.Score.FcPp,
                        BonusPp = s.BonusPp,
                        Rank = s.Rank,
                        Replay = s.Score.Replay,
                        Modifiers = s.Modifiers,
                        BadCuts = s.Score.BadCuts,
                        MissedNotes = s.Score.MissedNotes,
                        BombCuts = s.Score.BombCuts,
                        WallsHit = s.Score.WallsHit,
                        Pauses = s.Score.Pauses,
                        FullCombo = s.Score.FullCombo,
                        Hmd = s.Score.Hmd,
                        Controller = s.Score.Controller,
                        MaxCombo = s.Score.MaxCombo,
                        Timeset = s.Score.Timeset,
                        ReplaysWatched = s.Score.AnonimusReplayWatched + s.Score.AuthorizedReplayWatched,
                        Timepost = s.Score.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Score.Platform,
                        Player = new PlayerResponse {
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
                            Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        },
                        ScoreImprovement = s.ScoreImprovement,
                        RankVoting = s.Score.RankVoting,
                        Metadata = s.Score.Metadata,
                        Country = s.Score.Country,
                        Offsets = s.Score.ReplayOffsets,
                        Leaderboard = new LeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
                            Difficulty = new DifficultyResponse {
                                Id = s.Leaderboard.Difficulty.Id,
                                Value = s.Leaderboard.Difficulty.Value,
                                Mode = s.Leaderboard.Difficulty.Mode,
                                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                                ModeName = s.Leaderboard.Difficulty.ModeName,
                                Status = s.Leaderboard.Difficulty.Status,
                                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                                NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                                QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                                RankedTime = s.Leaderboard.Difficulty.RankedTime,

                                Stars  = s.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = s.Leaderboard.Difficulty.PassRating,
                                AccRating  = s.Leaderboard.Difficulty.AccRating,
                                TechRating  = s.Leaderboard.Difficulty.TechRating,
                                PatternRating = s.Leaderboard.Difficulty.PatternRating,
                                LinearRating = s.Leaderboard.Difficulty.LinearRating,
                                Type  = s.Leaderboard.Difficulty.Type,

                                Njs  = s.Leaderboard.Difficulty.Njs,
                                Nps  = s.Leaderboard.Difficulty.Nps,
                                Notes  = s.Leaderboard.Difficulty.Notes,
                                Bombs  = s.Leaderboard.Difficulty.Bombs,
                                Walls  = s.Leaderboard.Difficulty.Walls,
                                MaxScore = s.Leaderboard.Difficulty.MaxScore,
                                Duration  = s.Leaderboard.Difficulty.Duration,

                                Requirements = s.Leaderboard.Difficulty.Requirements,
                            }
                        },
                        Weight = s.Weight,
                        AccLeft = s.Score.AccLeft,
                        AccRight = s.Score.AccRight,
                        MaxStreak = s.Score.MaxStreak
                    })
                .ToList();
            foreach (var score in result.Data)
            {
                score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
            }

            return result;
        }

        [HttpGet("~/user/friendScores")]
        [Authorize]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> FriendsScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null) {

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                return await ContextsFriendsScores(id, sortBy, order, page, count, leaderboardContext, search, diff, type, stars_from, stars_to);
            }

            string userId = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.Include(p => p.ProfileSettings).FirstOrDefaultAsync(p => p.Id == userId);
            if (player == null) {
                return NotFound();
            }

            bool showAllRatings = player.ProfileSettings?.ShowAllRatings ?? false; 
            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence")) {
                var friends = _context
                    .Friends
                    .Where(f => f.Id == player.Id)
                    .Include(f => f.Friends)
                    .FirstOrDefault();

                if (friends != null) {
                    var friendsList = friends.Friends.Select(f => f.Id).ToList();
                    sequence = _context.Scores.Where(s => (s.PlayerId == player.Id || friendsList.Contains(s.PlayerId)) && s.ValidContexts.HasFlag(leaderboardContext));
                } else {
                    sequence = _context.Scores.Where(s => s.PlayerId == player.Id && s.ValidContexts.HasFlag(leaderboardContext));
                }

                switch (sortBy) {
                    case "date":
                        sequence = sequence.Order(order, t => t.Timepost);
                        break;
                    case "pp":
                        sequence = sequence.Order(order, t => t.Pp);
                        break;
                    case "acc":
                        sequence = sequence.Order(order, t => t.Accuracy);
                        break;
                    case "pauses":
                        sequence = sequence.Order(order, t => t.Pauses);
                        break;
                    case "rank":
                        sequence = sequence.Order(order, t => t.Rank);
                        break;
                    case "passRating":
                        sequence = sequence.Order(order, s => showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? s.Leaderboard.Difficulty.PassRating : 0);
                        break;
                    case "techRating":
                        sequence = sequence.Order(order, s => showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? s.Leaderboard.Difficulty.TechRating : 0);
                        break;
                    case "stars":
                        sequence = sequence.Order(order, s => showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? s.Leaderboard.Difficulty.Stars : 0);
                        break;
                    case "mistakes":
                        sequence = sequence.Order(order, t => t.BadCuts + t.BombCuts + t.MissedNotes + t.WallsHit);
                        break;
                    default:
                        break;
                }
                if (search != null) {
                    string lowSearch = search.ToLower();
                    sequence = sequence
                        .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
                }
                if (diff != null) {
                    sequence = sequence.Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null) {
                    sequence = sequence.Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
                }
                if (stars_from != null) {
                    sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null) {
                    sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars <= stars_to);
                }
            }

            var resultList = sequence
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .AsSplitQuery()
                    .Select(s => new ScoreResponseWithMyScore {
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
                        Player = new PlayerResponse {
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
                            Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        },
                        ScoreImprovement = s.ScoreImprovement,
                        RankVoting = s.RankVoting,
                        Metadata = s.Metadata,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Leaderboard = new LeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
                            Difficulty = new DifficultyResponse {
                                Id = s.Leaderboard.Difficulty.Id,
                                Value = s.Leaderboard.Difficulty.Value,
                                Mode = s.Leaderboard.Difficulty.Mode,
                                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                                ModeName = s.Leaderboard.Difficulty.ModeName,
                                Status = s.Leaderboard.Difficulty.Status,
                                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                                NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                                QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                                RankedTime = s.Leaderboard.Difficulty.RankedTime,

                                Stars  = s.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = s.Leaderboard.Difficulty.PassRating,
                                AccRating  = s.Leaderboard.Difficulty.AccRating,
                                TechRating  = s.Leaderboard.Difficulty.TechRating,
                                PatternRating = s.Leaderboard.Difficulty.PatternRating,
                                LinearRating = s.Leaderboard.Difficulty.LinearRating,
                                Type  = s.Leaderboard.Difficulty.Type,

                                Njs  = s.Leaderboard.Difficulty.Njs,
                                Nps  = s.Leaderboard.Difficulty.Nps,
                                Notes  = s.Leaderboard.Difficulty.Notes,
                                Bombs  = s.Leaderboard.Difficulty.Bombs,
                                Walls  = s.Leaderboard.Difficulty.Walls,
                                MaxScore = s.Leaderboard.Difficulty.MaxScore,
                                Duration  = s.Leaderboard.Difficulty.Duration,

                                Requirements = s.Leaderboard.Difficulty.Requirements,
                            }
                        },
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak
                    }).ToList();

            foreach (var resultScore in resultList) {
                if (!showAllRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }

            var result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = resultList
            };

            var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

            var myScores = _context.Scores.Where(s => s.PlayerId == userId && leaderboards.Contains(s.LeaderboardId)).Select(ToScoreResponseWithAcc).ToList();
            foreach (var score in result.Data)
            {
                score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
            }

            return result;
        }

        [HttpGet("~/user/friendActivity")]
        [Authorize]
        public async Task<ActionResult<ResponseWithMetadata<FriendActivity>>> FriendsActivity(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] FriendActivityType? type = null) {

            string userId = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.FindAsync(userId);
            if (player == null) {
                return NotFound();
            }

            var friends = _context
                    .Friends
                    .Where(f => f.Id == player.Id)
                    .Include(f => f.Friends)
                    .FirstOrDefault();

            IQueryable<Achievement> achievementsSequence;
            if (friends != null) {
                var friendsList = friends.Friends.Select(f => f.Id).ToList();
                achievementsSequence = _context.Achievements.Where(s => s.PlayerId == player.Id || friendsList.Contains(s.PlayerId));
            } else {
                achievementsSequence = _context.Achievements.Where(s => s.PlayerId == player.Id);
            }

            switch (sortBy) {
                case "date":
                    achievementsSequence = achievementsSequence.Order(order, t => t.Timeset);
                    break;
                default:
                    break;
            }

            var result = new ResponseWithMetadata<FriendActivity>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = achievementsSequence.Count()
                }
            };

            var data = new List<FriendActivity>();

            var achievements = achievementsSequence
                .Skip((page - 1) * count)
                .Take(count)
                .Select(a => new {
                    Player = new PlayerResponse {
                        Id = a.Player.Id,
                        Name = a.Player.Name,
                        Platform = a.Player.Platform,
                        Avatar = a.Player.Avatar,
                        Country = a.Player.Country,

                        Pp = a.Player.Pp,
                        Rank = a.Player.Rank,
                        CountryRank = a.Player.CountryRank,
                        Role = a.Player.Role,
                        Socials = a.Player.Socials,
                        PatreonFeatures = a.Player.PatreonFeatures,
                        ProfileSettings = a.Player.ProfileSettings,
                        Clans = a.Player.Clans.OrderBy(c => player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    Achievement = a
                })
                .ToList();
            foreach (var achievement in achievements) {
                data.Add(new FriendActivity {
                    Player = achievement.Player,
                    Type = FriendActivityType.Achievement,
                    ActivityObject = achievement.Achievement,
                });
            }

            return result;
        }
    }
}
