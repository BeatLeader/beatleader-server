using Amazon.S3;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;
using System.Linq.Expressions;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class ScoresController : Controller {

        private readonly AppContext _context;
        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public ScoresController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration) {
            _context = context;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [HttpGet("~/scores/all")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> AllScores(
            [FromQuery, SwaggerParameter("Sorting criteria for scores, default is by 'date'")] ScoresSortBy sortBy = ScoresSortBy.Date,
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Secondary sorting criteria for scores with the same value for the first sorting")] ScoresSortBy thenSortBy = ScoresSortBy.Date,
            [FromQuery, SwaggerParameter("Secondary order of sorting for scores with the same value for the first sorting")] Order thenOrder = Order.Desc,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 8")] int count = 8,
            [FromQuery, SwaggerParameter("Filter scores by search term in song name, author or mapper. Default is null")] string? search = null,
            [FromQuery, SwaggerParameter("Filter scores by map difficulty(Easy, Expert, Expert+, etc), default is null")] string? diff = null,
            [FromQuery, SwaggerParameter("Filter scores by map characteristic(Standard, OneSaber, etc), default is null")] string? mode = null,
            [FromQuery, SwaggerParameter("Filter scores by map requirements, default is 'None'")] Requirements mapRequirements = Requirements.None,
            [FromQuery, SwaggerParameter("Operation to filter all requirements, default is Any")] Operation allRequirements = Operation.Any,
            [FromQuery, SwaggerParameter("Filter scores by score status, default is 'None'")] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Filter scores by map status, default is null")] DifficultyStatus? type = null,
            [FromQuery, SwaggerParameter("Map type to filter leaderboards by")] MapTypes mapType = MapTypes.None,
            [FromQuery, SwaggerParameter("Operation to filter all types, default is Any")] Operation allTypes = Operation.Any,
            [FromQuery, SwaggerParameter("Filter scores by headset, default is null")] HMD? hmd = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with accuracy rating greater than, default is null")] float? accrating_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with accuracy rating lower than, default is null")] float? accrating_to = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with pass rating greater than, default is null")] float? passrating_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with pass rating lower than, default is null")] float? passrating_to = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with tech rating greater than, default is null")] float? techrating_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with tech rating lower than, default is null")] float? techrating_to = null,
            [FromQuery, SwaggerParameter("Filter scores made after unix timestamp, default is null")] int? date_from = null,
            [FromQuery, SwaggerParameter("Filter scores made before unix timestamp, default is null")] int? date_to = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] int? eventId = null,
            [FromQuery, SwaggerParameter("Filter maps from a specific mappers. BeatSaver profile ID list, comma separated, default is null")] string? mappers = null,
            [FromQuery, SwaggerParameter("Filter scores from a specific players. Profile ID list, comma separated, default is null")] string? players = null,
            [FromQuery, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] string? playlistIds = null,
            [FromBody, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] List<PlaylistResponse>? playlists = null) {

            if (count < 0 || count > 100) {
                return BadRequest("Please use count between 0 and 100");
            }

            string? userId = HttpContext.CurrentUserID(_context);
            var player = userId != null 
                ? await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == userId)
                : null;

            bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
            int treshold = Time.UnixNow() - 60 * 60 * 24 * 2 * 7;
            IQueryable<IScore> sequence = leaderboardContext == LeaderboardContexts.General 
                ? _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => s.ValidForGeneral && (sortBy != ScoresSortBy.SotwNominations || s.Timepost > treshold))
                    .TagWithCaller()
                : _context
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Include(ce => ce.ScoreInstance)
                    .Where(s => s.Context == leaderboardContext && (sortBy != ScoresSortBy.SotwNominations || s.Timepost > treshold))
                    .TagWithCaller();

            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, userId, _s3Client, playlistIds, playlists);

            (sequence, int? searchId, int? scoreCount) = await sequence.FilterAll(_context, true, showRatings, sortBy, order, thenSortBy, thenOrder, search, diff, mode, mapRequirements, allRequirements, scoreStatus, type, mapType, allTypes, hmd, modifiers, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, eventId, mappers, players, playlistList);

            var scoreIds = leaderboardContext == LeaderboardContexts.General 
                ? (await sequence.Skip((page - 1) * count).Take(count).Select(s => s.Id).ToListAsync()).Where(id => id != null).ToList()
                : (await sequence.Skip((page - 1) * count).Take(count).Select(s => s.ScoreId).ToListAsync()).Where(id => id != null).Select(id => (int)id).ToList();
            IQueryable<IScore> filteredSequence = leaderboardContext == LeaderboardContexts.General 
                        ? _context.Scores.AsNoTracking().Where(s => scoreIds.Contains(s.Id))
                        : _context.ScoreContextExtensions.AsNoTracking().Include(ce => ce.ScoreInstance).Where(s => s.Context == leaderboardContext && s.ScoreId != null && scoreIds.Contains((int)s.ScoreId));
            var resultList = await filteredSequence
                    .TagWithCaller()
                    .Select(s => new ScoreResponseWithMyScore {
                        Id = s.ScoreId,
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
                        Timeset = s.Time,
                        ReplaysWatched = s.ReplayWatchedTotal,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        SotwNominations = s.SotwNominations,
                        Status = s.Status,
                        Player = new PlayerResponse {
                            Id = s.Player.Id,
                            Name = s.Player.Name,
                            Alias = s.Player.Alias,
                            Avatar = s.Player.Avatar,
                            Platform = s.Player.Platform,
                            Country = s.Player.Country,

                            Pp = s.Player.Pp,
                            Rank = s.Player.Rank,
                            CountryRank = s.Player.CountryRank,
                            ProfileSettings = s.Player.ProfileSettings 
                        },
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
                                Explicity = s.Leaderboard.Song.Explicity
                            },
                            Difficulty = new DifficultyResponse {
                                Id = s.Leaderboard.Difficulty.Id,
                                Value = s.Leaderboard.Difficulty.Value,
                                Mode = s.Leaderboard.Difficulty.Mode,
                                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                                ModeName = s.Leaderboard.Difficulty.ModeName,
                                Status = s.Leaderboard.Difficulty.Status,
                                NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                                QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                                RankedTime = s.Leaderboard.Difficulty.RankedTime,

                                Stars  = s.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = s.Leaderboard.Difficulty.PassRating,
                                AccRating  = s.Leaderboard.Difficulty.AccRating,
                                TechRating  = s.Leaderboard.Difficulty.TechRating,
                                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
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

            foreach (var resultScore in resultList) {
                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }

            var result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = scoreCount ?? await sequence.TagWithCaller().CountAsync()
                },
                Data = resultList.OrderBy(s => scoreIds.IndexOf((int)s.Id))
            };

            var leaderboards = result.Data.Where(s => s.PlayerId != userId).Select(s => s.LeaderboardId).ToList();

            var myScores = await ((IQueryable<IScore>)(leaderboardContext == LeaderboardContexts.General ? _context.Scores.TagWithCaller().Where(s => s.ValidForGeneral) : _context.ScoreContextExtensions.TagWithCaller().Include(ce => ce.ScoreInstance).Where(ce => ce.Context == leaderboardContext)))
                .Where(s => s.PlayerId == userId && leaderboards.Contains(s.LeaderboardId))
                .Select(s => new ScoreResponseWithAcc
                    {
                        Id = s.ScoreId,
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
                        Hmd = s.Hmd,
                        Controller = s.Controller,
                        MaxCombo = s.MaxCombo,
                        Timeset = s.Time,
                        ReplaysWatched = s.ReplayWatchedTotal,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak
                    })
                .ToListAsync();
            foreach (var score in result.Data)
            {
                PostProcessSettings(score.Player, false);
                if (score.PlayerId != userId) {
                    score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
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

        [HttpGet("~/scores/sotws")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithNominations>>> ScoresOfTheWeekNominations() {

            string? userId = HttpContext.CurrentUserID(_context);
            var player = userId != null 
                ? await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == userId)
                : null;

            if (player == null || !player.AnyTeam()) {
                return Unauthorized();
            }

            bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
            int treshold = Time.UnixNow() - 60 * 60 * 24 * 2 * 7;
            var sequence = _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => s.ValidForGeneral && s.Timepost > treshold && s.SotwNominations > 0 && s.Status == ScoreStatus.None)
                    .OrderByDescending(s => s.SotwNominations)
                    .TagWithCaller();

            var scoreIds = (await sequence.Select(s => s.Id).ToListAsync()).Where(id => id != null).ToList();
            IQueryable<IScore> filteredSequence = _context.Scores.AsNoTracking().Where(s => scoreIds.Contains(s.Id));
            var resultList = await filteredSequence
                    .TagWithCaller()
                    .Select(s => new ScoreResponseWithNominations {
                        Id = s.ScoreId,
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
                        Timeset = s.Time,
                        ReplaysWatched = s.ReplayWatchedTotal,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        SotwNominations = s.SotwNominations,
                        Status = s.Status,
                        Player = new PlayerResponse {
                            Id = s.Player.Id,
                            Name = s.Player.Name,
                            Alias = s.Player.Alias,
                            Avatar = s.Player.Avatar,
                            Platform = s.Player.Platform,
                            Country = s.Player.Country,

                            Pp = s.Player.Pp,
                            Rank = s.Player.Rank,
                            CountryRank = s.Player.CountryRank,
                            ProfileSettings = s.Player.ProfileSettings 
                        },
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
                                Explicity = s.Leaderboard.Song.Explicity
                            },
                            Difficulty = new DifficultyResponse {
                                Id = s.Leaderboard.Difficulty.Id,
                                Value = s.Leaderboard.Difficulty.Value,
                                Mode = s.Leaderboard.Difficulty.Mode,
                                DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                                ModeName = s.Leaderboard.Difficulty.ModeName,
                                Status = s.Leaderboard.Difficulty.Status,
                                NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                                QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                                RankedTime = s.Leaderboard.Difficulty.RankedTime,

                                Stars  = s.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = s.Leaderboard.Difficulty.PassRating,
                                AccRating  = s.Leaderboard.Difficulty.AccRating,
                                TechRating  = s.Leaderboard.Difficulty.TechRating,
                                ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                                ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
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

            foreach (var resultScore in resultList) {
                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }

            var result = new ResponseWithMetadata<ScoreResponseWithNominations>() {
                Metadata = new Metadata() {
                    Page = 1,
                    ItemsPerPage = 0,
                    Total = await sequence.TagWithCaller().CountAsync()
                },
                Data = resultList.OrderBy(s => scoreIds.IndexOf((int)s.Id))
            };

            var leaderboards = result.Data.Where(s => s.PlayerId != userId).Select(s => s.LeaderboardId).ToList();

            var myScores = await _context.Scores.TagWithCaller().Where(s => s.ValidForGeneral)
                .Where(s => s.PlayerId == userId && leaderboards.Contains(s.LeaderboardId))
                .Select(s => new ScoreResponseWithAcc
                    {
                        Id = s.ScoreId,
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
                        Hmd = s.Hmd,
                        Controller = s.Controller,
                        MaxCombo = s.MaxCombo,
                        Timeset = s.Time,
                        ReplaysWatched = s.ReplayWatchedTotal,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak
                    })
                .ToListAsync();

            var nominationsList = await _context.ScoreNominations.Where(sn => scoreIds.Contains(sn.ScoreId)).Select(sn => new ScoreNominationResponse {
                Description = sn.Description,
                Timestamp = sn.Timestamp,
                PlayerId = sn.PlayerId,
                ScoreId = sn.ScoreId
            }).ToListAsync();
            var nominations = nominationsList.GroupBy(sn => sn.ScoreId).ToDictionary(g => g.Key, g => g.ToList());

            var playerIds = nominationsList.Select(sn => sn.PlayerId).Distinct().ToList();
            var players = await _context.Players.Where(p => playerIds.Contains(p.Id)).Select(p => new PlayerResponse {
                    Id = p.Id,
                    Name = p.Name,
                    Alias = p.Alias,
                    Avatar = p.Avatar,
                    Platform = p.Platform,
                    Country = p.Country,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    ProfileSettings = p.ProfileSettings 
                }).ToListAsync();

            foreach (var score in result.Data)
            {
                PostProcessSettings(score.Player, false);
                if (score.PlayerId != userId) {
                    score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                }

                if (nominations.TryGetValue(score.Id ?? 0, out var nmns)) {
                    score.Nominations = nmns.OrderByDescending(s => s.Timestamp).ToList();
                    foreach (var item in score.Nominations) {
                        item.Player = players.FirstOrDefault(p => p.Id == item.PlayerId);
                    }
                }
            }

            return result;
        }
    }
}
