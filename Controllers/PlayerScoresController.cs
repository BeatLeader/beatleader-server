using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class PlayerScoresController : Controller {
        private readonly AppContext _context;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerScoresController(
            AppContext context,
            IConfiguration configuration,
            IServerTiming serverTiming,
            IWebHostEnvironment env) {
            _context = context;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
        }

        [NonAction]
        public async Task<(IQueryable<IScore>?, string)> ScoresQuery(
            string id,
            string currentID,
            bool showRatings,
            ScoresSortBy sortBy = ScoresSortBy.Date,
            Order order = Order.Desc,
            string? search = null,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            DifficultyStatus? type = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null) {

            var player = await _context
                    .Players
                    .AsNoTracking()
                    .Where(p => p.Id == id)
                    .Select(p => new { p.Banned })
                    .FirstOrDefaultAsync();
            if (player == null) {
                return (null, "");
            }

            IQueryable<IScore> query = leaderboardContext == LeaderboardContexts.General 
                ? _context.Scores
                   .AsNoTracking()
                   .Where(t => t.PlayerId == id && t.ValidContexts.HasFlag(leaderboardContext))
                   .TagWithCallSite()
                : _context.ScoreContextExtensions
                   .AsNoTracking()
                   .Include(ce => ce.Score)
                   .Where(t => t.PlayerId == id && t.Context == leaderboardContext)
                   .TagWithCallSite();

            return (await query.Filter(_context, !player.Banned, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId), id);
        }

        [HttpGet("~/player/{id}/scores")]
        [SwaggerOperation(Summary = "Retrieve player's scores", Description = "Fetches a paginated list of scores for a specified player ID. Allows filtering by various criteria like date, difficulty, mode, and more.")]
        [SwaggerResponse(200, "Scores retrieved successfully", typeof(ResponseWithMetadata<ScoreResponseWithMyScore>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Scores not found for the given player ID")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> GetScores(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id,
            [FromQuery, SwaggerParameter("Sorting criteria for scores, default is by 'date'")] ScoresSortBy sortBy = ScoresSortBy.Pp,
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 8")] int count = 8,
            [FromQuery, SwaggerParameter("Filter scores by search term in song name, author or mapper. Default is null")] string? search = null,
            [FromQuery, SwaggerParameter("Filter scores by map difficulty(Easy, Expert, Expert+, etc), default is null")] string? diff = null,
            [FromQuery, SwaggerParameter("Filter scores by map characteristic(Standard, OneSaber, etc), default is null")] string? mode = null,
            [FromQuery, SwaggerParameter("Filter scores by map requirements, default is 'None'")] Requirements requirements = Requirements.None,
            [FromQuery, SwaggerParameter("Filter scores by score status, default is 'None'")] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Filter scores by map status, default is null")] DifficultyStatus? type = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Filter scores made after unix timestamp, default is null")] int? time_from = null,
            [FromQuery, SwaggerParameter("Filter scores made before unix timestamp, default is null")] int? time_to = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] int? eventId = null) {
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

            (
                IQueryable<IScore>? sequence,
                string userId
            ) = await ScoresQuery(id, currentID, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
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

            List<ScoreResponseWithMyScore> resultList;
            using (_serverTiming.TimeAction("list")) {
                resultList = await sequence
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .AsSplitQuery()
                    .TagWithCallSite()
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(s => new ScoreResponseWithMyScore {
                        Id = s.ScoreId,
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
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        Offsets = s.ReplayOffsets,
                        Leaderboard = new CompactLeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = new CompactSongResponse {
                                Id = s.Leaderboard.Song.Id,
                                Hash = s.Leaderboard.Song.Hash,
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
                            },
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
                    })
                    
                    .ToListAsync();
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

                    var myScores = await _context
                        .Scores
                        .AsNoTracking()
                        .Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(LeaderboardContexts.General) && leaderboards.Contains(s.LeaderboardId))
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
                            PlayCount = s.PlayCount,
                            LastTryTime = s.LastTryTime,
                            Hmd = s.Hmd,
                            Controller = s.Controller,
                            MaxCombo = s.MaxCombo,
                            Timeset = s.Timeset,
                            ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                            Timepost = s.Timepost,
                            LeaderboardId = s.LeaderboardId,
                            Platform = s.Platform,
                            Weight = s.Weight,
                            AccLeft = s.AccLeft,
                            AccRight = s.AccRight,
                            Player = s.Player != null ? new PlayerResponse {
                                Id = s.Player.Id,
                                Name = s.Player.Name,
                                Alias = s.Player.Alias,
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
                                ContextExtensions = s.Player.ContextExtensions != null ? s.Player.ContextExtensions.Select(ce => new PlayerContextExtension {
                                    Context = ce.Context,
                                    Pp = ce.Pp,
                                    AccPp = ce.AccPp,
                                    TechPp = ce.TechPp,
                                    PassPp = ce.PassPp,
                                    PlayerId = ce.PlayerId,

                                    Rank = ce.Rank,
                                    Country  = ce.Country,
                                    CountryRank  = ce.CountryRank,
                                }).ToList() : null,
                                Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                                        .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                            } : null,
                            ScoreImprovement = s.ScoreImprovement,
                            RankVoting = s.RankVoting,
                            Metadata = s.Metadata,
                            Country = s.Country,
                            Offsets = s.ReplayOffsets,
                            MaxStreak = s.MaxStreak
                        })
                        .ToListAsync();
                    foreach (var score in result.Data) {
                        score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                    }
                }
            }

            return result;
        }

        [HttpGet("~/player/{id}/scores/compact")]
        [SwaggerOperation(Summary = "Retrieve player's scores in a compact form", Description = "Fetches a paginated list of scores for a specified player ID. Returns less info to save bandwith or processing time")]
        [SwaggerResponse(200, "Scores retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Scores not found for the given player ID")]
        public async Task<ActionResult<ResponseWithMetadata<CompactScoreResponse>>> GetCompactScores(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id,
            [FromQuery, SwaggerParameter("Sorting criteria for scores, default is by 'date'")] ScoresSortBy sortBy = ScoresSortBy.Date,
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 8")] int count = 8,
            [FromQuery, SwaggerParameter("Filter scores by search term in song name, author or mapper. Default is null")] string? search = null,
            [FromQuery, SwaggerParameter("Filter scores by map difficulty(Easy, Expert, Expert+, etc), default is null")] string? diff = null,
            [FromQuery, SwaggerParameter("Filter scores by map characteristic(Standard, OneSaber, etc), default is null")] string? mode = null,
            [FromQuery, SwaggerParameter("Filter scores by map requirements, default is 'None'")] Requirements requirements = Requirements.None,
            [FromQuery, SwaggerParameter("Filter scores by score status, default is 'None'")] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Filter scores by map status, default is null")] DifficultyStatus? type = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
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

            (IQueryable<IScore>? sequence, string userId) = await ScoresQuery(id, currentID, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            ResponseWithMetadata<CompactScoreResponse> result;
            using (_serverTiming.TimeAction("db")) {
                result = new ResponseWithMetadata<CompactScoreResponse>() {
                    Metadata = new Metadata() {
                        Page = page,
                        ItemsPerPage = count,
                        Total = await sequence.CountAsync()
                    },
                    Data = await sequence
                    .Include(s => s.Leaderboard)
                    .TagWithCallSite()
                    .AsSplitQuery()
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(s => new CompactScoreResponse {
                        Score = new CompactScore {
                            Id = s.ScoreId,
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
                            SongHash = s.Leaderboard.Song.Hash
                        }
                    })
                    
                    .ToListAsync()
                };
            }

            return result;
        }

        [HttpGet("~/player/{id}/scorevalue/{hash}/{difficulty}/{mode}")]
        [SwaggerOperation(Summary = "Retrieve player's score for a specific map", Description = "Fetches a score made by a Player with ID for a map specified by Hash and difficulty")]
        [SwaggerResponse(200, "Score retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Score not found for the given player ID")]
        public async Task<ActionResult<int>> GetScoreValue(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id, 
            [FromRoute, SwaggerParameter("Maps's hash")] string hash, 
            [FromRoute, SwaggerParameter("Maps's difficulty(Easy, Expert, Expert+, etc)")] string difficulty, 
            [FromRoute, SwaggerParameter("Maps's characteristic(Standard, OneSaber, etc)")] string mode) {
            int? score = await _context
                .Scores
                .AsNoTracking()
                .TagWithCallSite()
                .Where(s => s.PlayerId == id && s.Leaderboard.Song.Hash == hash && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode)
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
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] DifficultyStatus? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null,
            [FromQuery] float? batch = null) {
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

            (IQueryable<IScore>? sequence, string userId) = await ScoresQuery(id, currentID, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
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
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToListAsync(), Math.Max(batch ?? 0.15f, 0.01f), count);
                case ScoresSortBy.ReplaysWatched:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.AnonimusReplayWatched + s.AuthorizedReplayWatched).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case ScoresSortBy.Mistakes:
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.BadCuts + s.MissedNotes + s.BombCuts + s.WallsHit).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);   
            }

            return BadRequest();
        }

        private IQueryable<AccGraphResponse> GetAccGraphResponse(IQueryable<IScore> query) {
            return query.Select(s => new AccGraphResponse {
                LeaderboardId = s.Leaderboard.Id,
                Diff = s.Leaderboard.Difficulty.DifficultyName,
                SongName = s.Leaderboard.Song.Name,
                Hash = s.Leaderboard.Song.Hash,
                Mapper = s.Leaderboard.Song.Author,
                Mode = s.Leaderboard.Difficulty.ModeName,
                Stars = s.Leaderboard.Difficulty.Stars,
                Acc = s.Accuracy,
                Timeset = s.Timepost,
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
                Hash = s.Leaderboard.Song.Hash,
                Mapper = s.Leaderboard.Song.Author,
                Mode = s.Leaderboard.Difficulty.ModeName,
                Stars = s.Leaderboard.Difficulty.Stars,
                Rank = s.Rank,
                Weight = s.Weight,
                ScoreCount = s.Leaderboard.PlayCount,
                Timeset = s.Timepost,
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
                Hash = s.Leaderboard.Song.Hash,
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
                .TagWithCallSite()
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
                    s.Score != null &&
                    !s.Score.IgnoreForStats && 
                    ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
                .TagWithCallSite();
            } else {
              baseQuery = _context
                .Scores
                .AsNoTracking()
                .Where(s => 
                    s.PlayerId == id && 
                    s.ValidContexts.HasFlag(leaderboardContext) && 
                    !s.IgnoreForStats && 
                    ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
                .TagWithCallSite();
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
            var result = await _context
                    .PlayerScoreStatsHistory
                    .AsNoTracking()
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .TagWithCallSite()
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

        [HttpGet("~/player/{id}/pinnedScores")]
        [SwaggerOperation(Summary = "Retrieve player's pinned scores", Description = "Fetches a paginated list of scores pinned by player for their ID.")]
        [SwaggerResponse(200, "Scores retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Scores not found for the given player ID")]
        public async Task<ActionResult<ICollection<ScoreResponseWithMyScore>>> GetPinnedScores(
            [FromRoute, SwaggerParameter("Player's unique identifier")]string id,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {

            id = await _context.PlayerIdToMain(id);

            var query = _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => 
                        s.PlayerId == id && 
                        s.ValidContexts.HasFlag(leaderboardContext) &&
                        s.Metadata != null && 
                        s.Metadata.PinnedContexts.HasFlag(leaderboardContext))
                    .OrderBy(s => s.Metadata.Priority)
                    .TagWithCallSite();

            var resultList = await query
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .TagWithCallSite()
                    .AsSplitQuery()
                    .Select(s => new ScoreResponseWithMyScore {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        PassPP = s.PassPP,
                        AccPP = s.AccPP,
                        TechPP = s.TechPP,
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
                        Hmd = s.Hmd,
                        Controller = s.Controller,
                        MaxCombo = s.MaxCombo,
                        Timeset = s.Timeset,
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        RankVoting = s.RankVoting,
                        Metadata = s.Metadata,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Leaderboard = new CompactLeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = new CompactSongResponse {
                                Id = s.Leaderboard.Song.Id,
                                Hash = s.Leaderboard.Song.Hash,
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
                            },
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
                    })
                    .ToListAsync();

            bool showRatings = HttpContext.ShouldShowAllRatings(_context);
            foreach (var resultScore in resultList) {
                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None)
            {
                var scoreIds = resultList.Select(s => s.Id).ToList();

                var contexts = await _context
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Where(s => s.Context == leaderboardContext && s.ScoreId != null && scoreIds.Contains((int)s.ScoreId))
                    .ToListAsync();

                for (int i = 0; i < resultList.Count && i < contexts.Count; i++)
                {
                    var ce = contexts.FirstOrDefault(c => c.ScoreId == resultList[i].Id);
                    if (ce != null)
                    {
                        resultList[i].ToContext(ce);
                    }
                }

            }
            return resultList;
        }
    }
}
