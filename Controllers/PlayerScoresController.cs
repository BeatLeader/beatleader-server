using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using BeatLeader_Server.ControllerHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class PlayerScoresController : Controller {
        private readonly AppContext _context;
        private readonly StorageContext _storageContext;

        private readonly IDbContextFactory<AppContext> _dbFactory;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;
        private readonly IAmazonS3 _s3Client;

        public PlayerScoresController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            StorageContext storageContext,
            IConfiguration configuration,
            IServerTiming serverTiming,
            IWebHostEnvironment env) {
            _context = context;
            _dbFactory = dbFactory;
            _storageContext = storageContext;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;

            _s3Client = configuration.GetS3Client();
        }

        [NonAction]
        public async Task<(IQueryable<IScore>?, int?, string)> ScoresQuery(
            string id,
            string currentID,
            bool showRatings,
            ScoresSortBy sortBy,
            Order order,
            ScoresSortBy thenSortBy,
            Order thenOrder,
            string? search,
            bool noSearchSort,
            string? diff,
            string? mode,
            Requirements requirements,
            ScoreFilterStatus scoreStatus,
            LeaderboardContexts leaderboardContext,
            DifficultyStatus? type,
            HMD? hmd,
            string? modifiers,
            float? stars_from,
            float? stars_to,
            float? acc_from,
            float? acc_to,
            int? time_from,
            int? time_to,
            int? eventId,
            List<PlaylistResponse>? playlists) {

            var player = await _context
                    .Players
                    .AsNoTracking()
                    .Where(p => p.Id == id)
                    .Select(p => new { p.Banned })
                    .FirstOrDefaultAsync();
            if (player == null) {
                return (null, null, "");
            }

            IQueryable<IScore> query = leaderboardContext == LeaderboardContexts.General 
                ? _context.Scores
                   .AsNoTracking()
                   .Where(t => t.PlayerId == id && t.ValidForGeneral)
                   .TagWithCaller()
                : _context.ScoreContextExtensions
                   .AsNoTracking()
                   .Include(ce => ce.ScoreInstance)
                   .Where(t => t.PlayerId == id && t.Context == leaderboardContext)
                   .TagWithCaller();
            (var resultQuery, int? searchId) = await query.Filter(_context, !player.Banned, showRatings, sortBy, order, thenSortBy, thenOrder, search, noSearchSort, diff, mode, requirements, scoreStatus, type, hmd, modifiers, stars_from, stars_to, acc_from, acc_to, time_from, time_to, eventId, playlists);
            return (resultQuery, searchId, id);
        }

        [HttpGet("~/player/{id}/scores")]
        [SwaggerOperation(Summary = "Retrieve player's scores", Description = "Fetches a paginated list of scores for a specified player ID. Allows filtering by various criteria like date, difficulty, mode, and more.")]
        [SwaggerResponse(200, "Scores retrieved successfully", typeof(ResponseWithMetadata<ScoreResponseWithMyScore>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Scores not found for the given player ID")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> GetScores(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id,
            [FromQuery, SwaggerParameter("Sorting criteria for scores, default is by 'pp'")] ScoresSortBy sortBy = ScoresSortBy.Pp,
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Additional sorting criteria for scores tied by the first sort, default is by 'date'")] ScoresSortBy thenSortBy = ScoresSortBy.Date,
            [FromQuery, SwaggerParameter("Order of additional sorting, default is descending")] Order thenOrder = Order.Desc,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 8")] int count = 8,
            [FromQuery, SwaggerParameter("Filter scores by search term in song name, author or mapper. Default is null")] string? search = null,
            [FromQuery, SwaggerParameter("Disabled scores sort by search relevance index")] bool noSearchSort = false,
            [FromQuery, SwaggerParameter("Filter scores by map difficulty(Easy, Expert, Expert+, etc), default is null")] string? diff = null,
            [FromQuery, SwaggerParameter("Filter scores by map characteristic(Standard, OneSaber, etc), default is null")] string? mode = null,
            [FromQuery, SwaggerParameter("Filter scores by map requirements, default is 'None'")] Requirements requirements = Requirements.None,
            [FromQuery, SwaggerParameter("Filter scores by score status, default is 'None'")] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Filter scores by map status, default is null")] DifficultyStatus? type = null,
            [FromQuery, SwaggerParameter("Filter scores by headset, default is null")] HMD? hmd = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Filter scores on score accuracy greater than, default is null")] float? acc_from = null,
            [FromQuery, SwaggerParameter("Filter scores on score accuracy lower than, default is null")] float? acc_to = null,
            [FromQuery, SwaggerParameter("Filter scores made after unix timestamp, default is null")] int? time_from = null,
            [FromQuery, SwaggerParameter("Filter scores made before unix timestamp, default is null")] int? time_to = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] int? eventId = null,
            [FromQuery, SwaggerParameter("Include score improvement and offsets, default is false")] bool includeIO = false,
            [FromQuery, SwaggerParameter("Playlits Ids to filter, default is null")] string? playlistIds = null,
            [FromBody, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] List<PlaylistResponse>? playlists = null) {
            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }
            if (page < 1) {
                page = 1;
            }

            id = await _context.PlayerIdToMain(id);

            string? currentID = HttpContext.CurrentUserID(_context);
            var currentProfileSettings = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync() : null;
            var profileSettings = await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == id && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();

            bool showRatings = currentProfileSettings?.ShowAllRatings ?? false;
            bool publicHistory = id == currentID || (profileSettings?.ShowStatsPublic ?? false);
            if (sortBy == ScoresSortBy.PlayCount && !publicHistory) {
                sortBy = ScoresSortBy.Pp;
            }

            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);

            (
                IQueryable<IScore>? sequence, 
                int? searchId,
                string userId
            ) = await ScoresQuery(id, currentID, showRatings, sortBy, order, thenSortBy, thenOrder, search, noSearchSort, diff, mode, requirements, scoreStatus, leaderboardContext, type, hmd, modifiers, stars_from, stars_to, acc_from, acc_to, time_from, time_to, eventId, playlistList);
            if (sequence == null) {
                return NotFound();
            }

            ResponseWithMetadata<ScoreResponseWithMyScore> result;
            using (_serverTiming.TimeAction("count")) {
                result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                    Metadata = new Metadata() {
                        Page = page,
                        ItemsPerPage = count,
                        Total = await sequence.CountAsync()
                    }
                };
            }
            if (result.Metadata.Total == 0) {
                return result;
            }

            var ids = await sequence.Select(s => s.Id).Skip((page - 1) * count).Take(count).ToListAsync();
            IQueryable<IScore> scoreQuery = leaderboardContext == LeaderboardContexts.General 
                ? _context.Scores
                   .AsNoTracking()
                   .Where(s => ids.Contains(s.Id))
                : _context.ScoreContextExtensions
                   .AsNoTracking()
                   .Include(es => es.ScoreInstance)
                   .Where(t => ids.Contains(t.Id));

            List<ScoreResponseWithMyScore> resultList;
            using (_serverTiming.TimeAction("list")) {
                resultList = await scoreQuery
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .TagWithCaller()
                    
                    .Select(s => new ScoreResponseWithMyScore {
                        Id = s.ScoreId,
                        OriginalId = s.Id,
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
                        PlayCount = publicHistory ? s.PlayCount : 0,
                        LastTryTime = publicHistory ? s.LastTryTime : 0,
                        FullCombo = s.FullCombo,
                        Hmd = s.Hmd,
                        Controller = s.Controller,
                        MaxCombo = s.MaxCombo,
                        Timeset = s.Time,
                        ReplaysWatched = s.ReplayWatchedTotal,
                        SotwNominations = s.SotwNominations,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = includeIO ? (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None ? s.ScoreImprovement : s.ScoreInstance.ScoreImprovement) : null,
                        Offsets = includeIO ? (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None ? s.ReplayOffsets : s.ScoreInstance.ReplayOffsets) : null,
                        Country = s.Country,
                        Status = s.Status,
                        Leaderboard = new CompactLeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = new CompactSongResponse {
                                Id = s.Leaderboard.Song.Id,
                                Hash = s.Leaderboard.Song.LowerHash,
                                Name = s.Leaderboard.Song.Name,
            
                                SubName = s.Leaderboard.Song.SubName,
                                Author = s.Leaderboard.Song.Author,
                                Mapper = s.Leaderboard.Song.Mapper,
                                MapperId = s.Leaderboard.Song.MapperId,
                                CollaboratorIds = s.Leaderboard.Song.CollaboratorIds,
                                CoverImage = s.Leaderboard.Song.CoverImage,
                                FullCoverImage = s.Leaderboard.Song.FullCoverImage,
                                Bpm = s.Leaderboard.Song.Bpm,
                                Duration = s.Leaderboard.Song.Duration,
                                Explicity = s.Leaderboard.Song.Explicity
                            },
                            Difficulty = new DifficultyResponse {
                                Id = s.Leaderboard.Difficulty.Id,
                                Value = s.Leaderboard.Difficulty.Value,
                                Mode = s.Leaderboard.Difficulty.Mode,
                                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                                ModeName = s.Leaderboard.Difficulty.ModeName,
                                Status = s.Leaderboard.Difficulty.Status,
                                ModifierValues = s.Leaderboard.Difficulty.ModifierValues != null 
                                    ? s.Leaderboard.Difficulty.ModifierValues
                                    : new ModifiersMap(),
                                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                                NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                                QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                                RankedTime = s.Leaderboard.Difficulty.RankedTime,

                                Stars  = s.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = s.Leaderboard.Difficulty.PassRating,
                                AccRating  = s.Leaderboard.Difficulty.AccRating,
                                TechRating  = s.Leaderboard.Difficulty.TechRating,
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
                        MaxStreak = s.MaxStreak,
                        Experience = (int)s.Experience
                    })
                    
                    .ToListAsync();
            }

            if (ids.Count > 0) {
                resultList = resultList.OrderBy(e => ids.IndexOf(e.OriginalId)).ToList();
            }

            using (_serverTiming.TimeAction("postprocess")) {
                foreach (var resultScore in resultList) {
                    if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                        resultScore.Leaderboard.HideRatings();
                    }
                }
                result.Data = resultList;

                if (currentID != null && currentID != userId) {
                    var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

                    IQueryable<IScore> myScoresQuery = leaderboardContext == LeaderboardContexts.General 
                        ? _context.Scores
                           .AsNoTracking()
                           .Where(s => s.PlayerId == currentID && s.ValidForGeneral && leaderboards.Contains(s.LeaderboardId))
                           .TagWithCaller()
                        : _context.ScoreContextExtensions
                           .AsNoTracking()
                           .Include(ce => ce.ScoreInstance)
                           .Where(s => s.PlayerId == currentID && s.Context == leaderboardContext && leaderboards.Contains(s.LeaderboardId))
                           .TagWithCaller();

                    var myScores = await myScoresQuery
                        .Select(s => new ScoreResponseWithMyScore {
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
                            PlayCount = s.PlayCount,
                            LastTryTime = s.LastTryTime,
                            Hmd = s.Hmd,
                            Controller = s.Controller,
                            MaxCombo = s.MaxCombo,
                            Timeset = s.Time,
                            ReplaysWatched = s.ReplayWatchedTotal,
                            SotwNominations = s.SotwNominations,
                            Timepost = s.Timepost,
                            LeaderboardId = s.LeaderboardId,
                            Platform = s.Platform,
                            Weight = s.Weight,
                            AccLeft = s.AccLeft,
                            AccRight = s.AccRight,
                            ScoreImprovement = s.ScoreImprovement,
                            Country = s.Country,
                            MaxStreak = s.MaxStreak
                        })
                        .ToListAsync();
                    foreach (var score in result.Data) {
                        score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                    }
                }
            }

            if (searchId != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                    foreach (var item in searchRecords) {
                        _context.SongSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            return result;
        }

        [HttpPost("~/player/{id}/scores/compact")]
        [HttpGet("~/player/{id}/scores/compact")]
        [SwaggerOperation(Summary = "Retrieve player's scores in a compact form", Description = "Fetches a paginated list of scores for a specified player ID. Returns less info to save bandwith or processing time")]
        [SwaggerResponse(200, "Scores retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Scores not found for the given player ID")]
        public async Task<ActionResult<ResponseWithMetadata<CompactScoreResponse>>> GetCompactScores(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id,
            [FromQuery, SwaggerParameter("Playlits Ids to filter, default is null")] string? playlistIds = null,
            [FromBody, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] List<PlaylistResponse>? playlists = null,
            [FromQuery, SwaggerParameter("Sorting criteria for scores, default is by 'date'")] ScoresSortBy sortBy = ScoresSortBy.Date,
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Additional sorting criteria for scores tied by the first sort")] ScoresSortBy thenSortBy = ScoresSortBy.Date,
            [FromQuery, SwaggerParameter("Order of additional sorting, default is descending")] Order thenOrder = Order.Desc,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 8")] int count = 8,
            [FromQuery, SwaggerParameter("Filter scores by search term in song name, author or mapper. Default is null")] string? search = null,
            [FromQuery, SwaggerParameter("Disabled scores sort by search relevance index")] bool noSearchSort = false,
            [FromQuery, SwaggerParameter("Filter scores by map difficulty(Easy, Expert, Expert+, etc), default is null")] string? diff = null,
            [FromQuery, SwaggerParameter("Filter scores by map characteristic(Standard, OneSaber, etc), default is null")] string? mode = null,
            [FromQuery, SwaggerParameter("Filter scores by map requirements, default is 'None'")] Requirements requirements = Requirements.None,
            [FromQuery, SwaggerParameter("Filter scores by score status, default is 'None'")] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Filter scores by map status, default is null")] DifficultyStatus? type = null,
            [FromQuery, SwaggerParameter("Filter scores by headset, default is null")] HMD? hmd = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Filter scores on score accuracy greater than, default is null")] float? acc_from = null,
            [FromQuery, SwaggerParameter("Filter scores on score accuracy lower than, default is null")] float? acc_to = null,
            [FromQuery, SwaggerParameter("Filter scores made after unix timestamp, default is null")] int? time_from = null,
            [FromQuery, SwaggerParameter("Filter scores made before unix timestamp, default is null")] int? time_to = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] int? eventId = null) {
            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }

            id = await _context.PlayerIdToMain(id);

            string? currentID = HttpContext.CurrentUserID(_context);
            var profileSettings = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync() : null;

            bool showRatings = profileSettings?.ShowAllRatings ?? false;
            bool publicHistory = profileSettings?.ShowStatsPublic ?? false;
            if (sortBy == ScoresSortBy.PlayCount && !publicHistory) {
                sortBy = ScoresSortBy.Pp;
            }

            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);
            (IQueryable<IScore>? sequence, int? searchId, string userId) = await ScoresQuery(id, currentID, showRatings, sortBy, order, thenSortBy, thenOrder, search, noSearchSort, diff, mode, requirements, scoreStatus, leaderboardContext, type, hmd, modifiers, stars_from, stars_to, acc_from, acc_to, time_from, time_to, eventId, playlistList);

            if (sequence == null) {
                return NotFound();
            }

            var ids = await sequence.Select(s => s.Id).Skip((page - 1) * count).Take(count).ToListAsync();
            var dbContext = _dbFactory.CreateDbContext();
            IQueryable<IScore> scoreQuery = leaderboardContext == LeaderboardContexts.General 
                ? dbContext.Scores
                   .AsNoTracking()
                   .Where(s => ids.Contains(s.Id))
                : dbContext.ScoreContextExtensions
                   .AsNoTracking()
                   .Include(es => es.ScoreInstance)
                   .Where(t => ids.Contains(t.Id));

            ResponseWithMetadata<CompactScoreResponse> result = new ResponseWithMetadata<CompactScoreResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count
                }
            };
            using (_serverTiming.TimeAction("db")) {
                (result.Metadata.Total, result.Data) = await sequence.CountAsync().CoundAndResults(scoreQuery
                    .AsNoTracking()
                    .TagWithCaller()
                    .Select(s => new CompactScoreResponse {
                        Score = new CompactScore {
                            Id = s.ScoreId,
                            OriginalId = s.Id,
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            EpochTime = s.Timepost,
                            FullCombo = s.FullCombo,
                            MaxCombo = s.MaxCombo,
                            Hmd = s.Hmd,
                            MissedNotes = s.MissedNotes,
                            BadCuts = s.BadCuts,
                            Modifiers = s.Modifiers,
                            Controller = s.Controller,
                            Accuracy = s.Accuracy,
                            Pp = s.Pp
                        },
                        Leaderboard = new CompactLeaderboard {
                            Id = s.LeaderboardId,
                            Difficulty = s.Leaderboard.Difficulty.Value,
                            ModeName = s.Leaderboard.Difficulty.ModeName,
                            SongHash = s.Leaderboard.Song.LowerHash
                        }
                    })
                    .ToListAsync());
            }

            if (ids.Count > 0) {
                result.Data = result.Data.OrderBy(e => ids.IndexOf(e.Score.OriginalId)).ToList();
            }

            if (searchId != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                    foreach (var item in searchRecords) {
                        _context.SongSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            return result;
        }

        [HttpGet("~/player/{id}/scorevalue/{hash}/{difficulty}/{mode}")]
        public async Task<ActionResult<int>> GetScoreValue(string id, string hash, string difficulty, string mode) {
            int? score = await _context
                .Scores
                .AsNoTracking()
                .TagWithCaller()
                .Where(s => s.PlayerId == id && s.Leaderboard.Song.LowerHash == hash.ToLower() && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode)
                .Select(s => s.ModifiedScore)
                .FirstOrDefaultAsync();

            if (score == null) {
                return NotFound();
            }

            return score;
        }

        [HttpGet("~/player/{id}/histogram")]
        public async Task<ActionResult<string>> GetPlayerHistogram(
            string id,
            [FromQuery] ScoresSortBy sortBy = ScoresSortBy.Date,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] ScoresSortBy thenSortBy = ScoresSortBy.Date,
            [FromQuery] Order thenOrder = Order.Desc,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] bool noSearchSort = false,
            [FromQuery] string? diff = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] DifficultyStatus? type = null,
            [FromQuery] HMD? hmd = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? acc_from = null,
            [FromQuery] float? acc_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null,
            [FromQuery] float? batch = null,
            [FromQuery] string? playlistIds = null,
            [FromBody] List<PlaylistResponse>? playlists = null) {
            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }

            id = await _context.PlayerIdToMain(id);

            string? currentID = HttpContext.CurrentUserID(_context);
            var currentProfileSettings = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync() : null;

            var profileSettings = await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == id && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();

            bool showRatings = currentProfileSettings?.ShowAllRatings ?? false;
            bool publicHistory = id == currentID || (profileSettings?.ShowStatsPublic ?? false);

            if (sortBy == ScoresSortBy.PlayCount && !publicHistory) {
                sortBy = ScoresSortBy.Pp;
            }

            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);

            (IQueryable<IScore>? sequence, int? searchId, string userId) = await ScoresQuery(id, currentID, showRatings, sortBy, order, thenSortBy, thenOrder, search, noSearchSort, diff, mode, requirements, scoreStatus, leaderboardContext, type, hmd, modifiers, stars_from, stars_to, acc_from, acc_to, time_from, time_to, eventId, playlistList);
            if (sequence == null) {
                return NotFound();
            }

            if (searchId != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                    foreach (var item in searchRecords) {
                        _context.SongSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            switch (sortBy) {
                case ScoresSortBy.Date:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Timepost > 0 ? s.Timepost.ToString() : s.Time).Select(s => Int32.Parse(s)).ToListAsync(), (int)(batch > 60 * 60 ? batch : 60 * 60 * 24), count);
                case ScoresSortBy.Pp:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Pp).ToListAsync(), Math.Max(batch ?? 5, 1), count);
                case ScoresSortBy.Acc:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Accuracy).ToListAsync(), Math.Max(batch ?? 0.0025f, 0.001f), count);
                case ScoresSortBy.Pauses:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Pauses).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.PlayCount:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.PlayCount).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.LastTryTime:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.LastTryTime).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.MaxStreak:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.MaxStreak ?? 0).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.Rank:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Rank).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.Stars:
                    return HistogramUtils.GetHistogram(order, (await sequence
                        .Select(s => new { Stars = (showRatings || 
                        (s.Leaderboard.Difficulty.Status != DifficultyStatus.unranked && 
                         s.Leaderboard.Difficulty.Status != DifficultyStatus.unrankable &&
                         s.Leaderboard.Difficulty.Status != DifficultyStatus.outdated)) ? (s.Leaderboard.Difficulty.Stars ?? 0) : 0,
                         s.Leaderboard.Difficulty.ModifiersRating,
                         s.Leaderboard.Difficulty.ModifierValues,
                         s.Modifiers} )
                        .Where(s => s.Stars > 0)
                        .ToListAsync())
                        .Select(s => ReplayUtils.EffectiveStarRating(s.Modifiers ?? "", s.Stars, s.ModifierValues ?? new ModifiersMap(), s.ModifiersRating))
                        .ToList(), Math.Max(batch ?? 0.15f, 0.01f), count);
                case ScoresSortBy.ReplaysWatched:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.ReplayWatchedTotal).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.Mistakes:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Mistakes).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);   
            }

            return BadRequest();
        }

        private IQueryable<AccGraphResponse> GetAccGraphResponse(IQueryable<IScore> query) {
            return query.Select(s => new AccGraphResponse {
                LeaderboardId = s.Leaderboard.Id,
                Diff = s.Leaderboard.Difficulty.DifficultyName,
                SongName = s.Leaderboard.Song.Name,
                Hash = s.Leaderboard.Song.LowerHash,
                Mapper = s.Leaderboard.Song.Author,
                Mode = s.Leaderboard.Difficulty.ModeName,
                Stars = s.Leaderboard.Difficulty.Stars,
                Acc = s.Accuracy,
                Timeset = s.Timepost,
                Pp = s.Pp,
                Modifiers = s.Modifiers,

                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                AccRating = s.Leaderboard.Difficulty.AccRating,
                PassRating = s.Leaderboard.Difficulty.PassRating,
                TechRating = s.Leaderboard.Difficulty.TechRating,
            });
        }

        private IQueryable<RankGraphResponse> GetRankGraphResponse(IQueryable<IScore> query) {
            return query.Select(s => new RankGraphResponse {
                LeaderboardId = s.Leaderboard.Id,
                Diff = s.Leaderboard.Difficulty.DifficultyName,
                SongName = s.Leaderboard.Song.Name,
                Hash = s.Leaderboard.Song.LowerHash,
                Mapper = s.Leaderboard.Song.Author,
                Mode = s.Leaderboard.Difficulty.ModeName,
                Stars = s.Leaderboard.Difficulty.Stars,
                Rank = s.Rank,
                Weight = s.Weight,
                ScoreCount = s.Leaderboard.Plays,
                Timeset = s.Timepost,
                Pp = s.Pp,
                Modifiers = s.Modifiers,

                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                AccRating = s.Leaderboard.Difficulty.AccRating,
                PassRating = s.Leaderboard.Difficulty.PassRating,
                TechRating = s.Leaderboard.Difficulty.TechRating,
            });
        }

        private IQueryable<WeightGraphResponse> GetWeightGraphResponse(IQueryable<IScore> query) {
            return query.Select(s => new WeightGraphResponse {
                LeaderboardId = s.Leaderboard.Id,
                Diff = s.Leaderboard.Difficulty.DifficultyName,
                SongName = s.Leaderboard.Song.Name,
                Hash = s.Leaderboard.Song.LowerHash,
                Mapper = s.Leaderboard.Song.Author,
                Mode = s.Leaderboard.Difficulty.ModeName,
                Stars = s.Leaderboard.Difficulty.Stars,
                Weight = s.Weight,
                Pp = s.Pp,
                Timeset = s.Timepost,
                Modifiers = s.Modifiers,

                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                AccRating = s.Leaderboard.Difficulty.AccRating,
                PassRating = s.Leaderboard.Difficulty.PassRating,
                TechRating = s.Leaderboard.Difficulty.TechRating,
            });
        }

        [HttpGet("~/player/{id}/accgraph")]
        [SwaggerOperation(Summary = "Retrieve player's accuracy graph", Description = "Usefull to visualise player's performance relative to map's complexity")]
        [SwaggerResponse(200, "Accuracy graph retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "No accuracy graph available for the given player ID")]
        public async Task<ActionResult> AccGraph(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id, 
            [FromQuery, SwaggerParameter("Type of the graph: acc, rank, weight or pp")] string type = "acc", 
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Exclude unranked scores")] bool no_unranked_stars = false) {

            id = await _context.PlayerIdToMain(id);
            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = !no_unranked_stars && currentID != null ? (await _context
                .Players
                .Include(p => p.ProfileSettings)
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync())?.ShowAllRatings ?? false : false;

            object? result = null;

            IQueryable<IScore> baseQuery;
            if (leaderboardContext != LeaderboardContexts.None && leaderboardContext != LeaderboardContexts.General) {
                baseQuery = _context
                .ScoreContextExtensions
                .AsNoTracking()
                .Where(s => 
                    s.PlayerId == id && 
                    s.Context == leaderboardContext && 
                    s.ScoreInstance != null &&
                    !s.ScoreInstance.IgnoreForStats && 
                    ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
                .TagWithCaller();
            } else {
              baseQuery = _context
                .Scores
                .AsNoTracking()
                .Where(s => 
                    s.PlayerId == id && 
                    s.ValidForGeneral && 
                    !s.IgnoreForStats && 
                    ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
                .TagWithCaller();
            }
            
            switch (type)
            {
                case "acc":
                    result = await GetAccGraphResponse(baseQuery).ToListAsync();
                    break;
                case "rank":
                    result = await GetRankGraphResponse(baseQuery).ToListAsync();
                    break;
                case "weight":
                    result = await GetWeightGraphResponse(baseQuery).ToListAsync();
                    break;
                default:
                    break;
            }

            if (result == null) {
                return BadRequest("Unknow graph `type`");
            }

            var defaultModifiers = new ModifiersMap();
            var listResponse = result as System.Collections.IEnumerable;
            foreach (var item in listResponse!) {
                var score = (item as GraphResponse)!;
                if (score.Modifiers.Length > 0) {
                    var modifierValues = score.ModifierValues ?? defaultModifiers; 
                    var modifiersRating = score.ModifiersRating;
                    float mp = modifierValues.GetTotalMultiplier(score.Modifiers, modifiersRating == null);

                    if (modifiersRating != null) {
                        var modifiersMap = modifiersRating.ToDictionary<float>();
                        foreach (var modifier in score.Modifiers.ToUpper().Split(","))
                        {
                            if (modifiersMap.ContainsKey(modifier + "AccRating")) { 
                                score.AccRating = modifiersMap[modifier + "AccRating"]; 
                                score.PassRating = modifiersMap[modifier + "PassRating"]; 
                                score.TechRating = modifiersMap[modifier + "TechRating"]; 

                                break;
                            }
                        }
                    }

                    score.AccRating *= mp;
                    score.PassRating *= mp;
                    score.TechRating *= mp;

                    score.Stars = ReplayUtils.ToStars(score.AccRating ?? 0, score.PassRating ?? 0, score.TechRating ?? 0);
                }
            }

            return Ok(result);
        }

        [HttpGet("~/player/{id}/history")]
        [SwaggerOperation(Summary = "Retrieve player's statistic history", Description = "Fetches a list of player's performance metrics and various stats saved daily")]
        [SwaggerResponse(200, "History retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "No history saved for the given player ID")]
        public async Task<ActionResult<ICollection<PlayerScoreStatsHistory>>> GetHistory(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id, 
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General, 
            [FromQuery, SwaggerParameter("Amount of days to include")] int count = 50) {
            id = await _context.PlayerIdToMain(id);
            var result = await _storageContext
                    .PlayerScoreStatsHistory
                    .TagWithCaller()
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .OrderByDescending(s => s.Timestamp)
                    .Take(count)
                    .ToListAsync();
            if (result.Count == 0) {
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24;

                PlayerScoreStatsHistory? tempStatsHistory;

                if (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None) {
                    tempStatsHistory = await _context
                        .Players
                        .AsNoTracking()
                        .Where(p => p.Id == id)
                        .Select(player => new PlayerScoreStatsHistory {
                            Rank = player.Rank, 
                            Pp = player.Pp, 
                            CountryRank = player.CountryRank
                        })
                        .FirstOrDefaultAsync();
                } else {
                    tempStatsHistory = await _context
                        .PlayerContextExtensions
                        .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                        .Select(player => new PlayerScoreStatsHistory {
                            Rank = player.Rank, 
                            Pp = player.Pp, 
                            CountryRank = player.CountryRank
                        })
                        .FirstOrDefaultAsync();
                }
                
                if (tempStatsHistory == null) {
                    tempStatsHistory = new PlayerScoreStatsHistory {
                        Rank = 0, 
                        Pp = 0, 
                        CountryRank = 0
                    };
                }

                tempStatsHistory.Timestamp = timeset;
                result = new List<PlayerScoreStatsHistory> { tempStatsHistory };
            }

            return result;
        }

        [HttpGet("~/player/{id}/history/compact")]
        [SwaggerOperation(Summary = "Retrieve player's statistic history in a compact form", Description = "Fetches a list of player's performance metrics subset. Use the main history endpoint for a full.")]
        [SwaggerResponse(200, "History retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "No history saved for the given player ID")]
        public async Task<ActionResult<ICollection<HistoryCompactResponse>>> GetCompactHistory(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id, 
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General, 
            [FromQuery, SwaggerParameter("Amount of days to include")] int count = 50) {
            id = await _context.PlayerIdToMain(id);
            var result = await _storageContext
                    .PlayerScoreStatsHistory
                    .AsNoTracking()
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .OrderByDescending(s => s.Timestamp)
                    .Take(count)
                    .Select(h => new HistoryCompactResponse {
                        Timestamp = h.Timestamp,

                        Pp = h.Pp,
                        Rank = h.Rank,
                        CountryRank = h.CountryRank,

                        AverageRankedAccuracy = h.AverageRankedAccuracy,
                        AverageUnrankedAccuracy = h.AverageUnrankedAccuracy,
                        AverageAccuracy = h.AverageAccuracy,

                        MedianRankedAccuracy = h.MedianRankedAccuracy,
                        MedianAccuracy = h.MedianAccuracy,

                        RankedPlayCount = h.RankedPlayCount,
                        UnrankedPlayCount = h.UnrankedPlayCount,
                        TotalPlayCount = h.TotalPlayCount,

                        RankedImprovementsCount = h.RankedImprovementsCount,
                        UnrankedImprovementsCount = h.UnrankedImprovementsCount,
                        TotalImprovementsCount = h.TotalImprovementsCount
                    })
                    .ToListAsync();
            if (result.Count == 0) {
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24;

                HistoryCompactResponse? tempStatsHistory;

                if (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None) {
                    tempStatsHistory = await _context
                        .Players
                        .AsNoTracking()
                        .Where(p => p.Id == id)
                        .Select(player => new HistoryCompactResponse {
                            Rank = player.Rank, 
                            Pp = player.Pp, 
                            CountryRank = player.CountryRank
                        })
                        .FirstOrDefaultAsync();
                } else {
                    tempStatsHistory = await _context
                        .PlayerContextExtensions
                        .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                        .Select(player => new HistoryCompactResponse {
                            Rank = player.Rank, 
                            Pp = player.Pp, 
                            CountryRank = player.CountryRank
                        })
                        .FirstOrDefaultAsync();
                }
                
                if (tempStatsHistory == null) {
                    tempStatsHistory = new HistoryCompactResponse {
                        Rank = 0, 
                        Pp = 0, 
                        CountryRank = 0
                    };
                }

                tempStatsHistory.Timestamp = timeset;
                result = new List<HistoryCompactResponse> { tempStatsHistory };
            }

            return result;
        }

        [HttpGet("~/player/{id}/history/triangle")]
        [SwaggerOperation(Summary = "Retrieve player's triangle history in a compact form", Description = "Fetches a list of player's performance metrics subset. Use the main history endpoint for a full.")]
        [SwaggerResponse(200, "History retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "No history saved for the given player ID")]
        public async Task<ActionResult<ICollection<HistoryTriangleResponse>>> GetTriangleHistory(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id, 
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
            id = await _context.PlayerIdToMain(id);
            var allHistory = await _storageContext
                    .PlayerScoreStatsHistory
                    .AsNoTracking()
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .OrderByDescending(s => s.Timestamp)
                    .Select(h => new HistoryTriangleResponse {
                        Timestamp = h.Timestamp,

                        Pp = h.Pp,
                        AccPp = h.AccPp,
                        PassPp = h.PassPp,
                        TechPp = h.TechPp,

                        Improvements = h.RankedImprovementsCount,
                        NewScores = h.RankedPlayCount
                    })
                    .ToListAsync();

            var first = leaderboardContext == LeaderboardContexts.General ? _context
                .Players
                .Where(p => p.Id == id)
                .Select(p => new HistoryTriangleResponse {
                Timestamp = Time.UnixNow(),

                Pp = p.Pp,
                AccPp = p.AccPp,
                PassPp = p.PassPp,
                TechPp = p.TechPp,

                Improvements = p.ScoreStats.RankedImprovementsCount,
                NewScores = p.ScoreStats.RankedPlayCount
            })
            .FirstOrDefault() : _context
                .PlayerContextExtensions
                .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                .Select(p => new HistoryTriangleResponse {
                Timestamp = Time.UnixNow(),

                Pp = p.Pp,
                AccPp = p.AccPp,
                PassPp = p.PassPp,
                TechPp = p.TechPp,

                Improvements = p.ScoreStats.RankedImprovementsCount,
                NewScores = p.ScoreStats.RankedPlayCount
            })
            .FirstOrDefault();
            if (first == null) return NotFound();

            var result = new List<HistoryTriangleResponse> { first };

            if (!allHistory.Any()) {
                return result;
            }

            foreach (var month in allHistory.GroupBy(h => { 
                var timeOfsset = DateTimeOffset.FromUnixTimeSeconds(h.Timestamp);
                    return $"{timeOfsset.Year} {timeOfsset.Month}";
                })
                .OrderByDescending(g => g.FirstOrDefault()?.Timestamp ?? 0)) {
                var h = month.OrderBy(h => h.Timestamp).FirstOrDefault();
                if (h != null && h != first) {
                    result.Add(h);
                }
            }

            // Find previous Sunday for each entry to calculate differences
            for (int i = 0; i < result.Count - 1; i++) {
                var currentEntry = result[i];
                var prevSunday = result[i + 1];

                if (prevSunday != null) {
                    currentEntry.Improvements = currentEntry.Improvements - prevSunday.Improvements;
                    currentEntry.NewScores = currentEntry.NewScores - prevSunday.NewScores;
                } else {
                    currentEntry.Improvements = 0;
                    currentEntry.NewScores = 0;
                }
            }
            
            return result.Take(12).ToList();
        }

        [HttpGet("~/player/legacygame")]
        public async Task<ActionResult<bool>> ShowLegacy() {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }
            return false;
        }
    }
}
