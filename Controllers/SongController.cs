using Amazon.S3;
using beatleader_parser;
using Parser.Utils;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProtoBuf;
using System.IO.Compression;
using static BeatLeader_Server.Utils.ResponseUtils;
using BeatLeader_Server.ControllerHelpers;
using Swashbuckle.AspNetCore.Annotations;
using BeatLeader_Server.Enums;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using System.Dynamic;
using System.Net;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BeatLeader_Server.Controllers
{
    public class BeatSaverTrendingMap {
        public string Id { get; set; }
        public int Votes { get; set; }
    }

    [ApiController]
    [Route("[controller]")]
    public class SongController : Controller
    {
        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;

        private readonly IAmazonS3 _s3Client;
        private readonly IMemoryCache _cache;
        public SongController(AppContext context, IDbContextFactory<AppContext> dbFactory, IConfiguration configuration, IMemoryCache cache)
        {
            _s3Client = configuration.GetS3Client();
            _context = context;      
            _dbFactory = dbFactory;
            _cache = cache;
        }

        [HttpGet("~/maps/")]
        [SwaggerOperation(Summary = "Retrieve a list of maps", Description = "Fetches a paginated and optionally filtered list of Beat Saber maps.")]
        [SwaggerResponse(200, "Maps retrieved successfully", typeof(ResponseWithMetadata<MapInfoResponse>))]
        [SwaggerResponse(404, "Maps not found")]
        public async Task<ActionResult<ResponseWithMetadata<MapInfoResponse>>> GetAll(
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of leaderboards per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort leaderboards by, default is None")] MapSortBy sortBy = MapSortBy.None,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Search term to filter leaderboards by song, author or mapper name")] string? search = null,
            [FromQuery, SwaggerParameter("Type of leaderboards to filter, default is All")] Enums.Type type = Enums.Type.All,
            [FromQuery, SwaggerParameter("Mode to filter leaderboards by (Standard, OneSaber, etc...)")] string? mode = null,
            [FromQuery, SwaggerParameter("Difficulty to filter leaderboards by (Easy, Normal, Hard, Expert, ExpertPlus)")] string? difficulty = null,
            [FromQuery, SwaggerParameter("Map type to filter leaderboards by")] MapTypes mapType = MapTypes.None,
            [FromQuery, SwaggerParameter("Operation to filter all types, default is Any")] Operation allTypes = Operation.Any,
            [FromQuery, SwaggerParameter("Requirements to filter leaderboards by, default is Ignore")] Requirements mapRequirements = Requirements.Ignore,
            [FromQuery, SwaggerParameter("Operation to filter all requirements, default is Any")] Operation allRequirements = Operation.Any,
            [FromQuery, SwaggerParameter("Song status to filter leaderboards by, default is None")] SongStatus songStatus = SongStatus.None,
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is General")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Map creator, human by default")] SongCreator mapCreator = SongCreator.Human,
            [FromQuery, SwaggerParameter("My type to filter leaderboards by, default is None")] MyTypeMaps mytype = MyTypeMaps.None,
            [FromQuery, SwaggerParameter("Minimum stars to filter leaderboards by")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Maximum stars to filter leaderboards by")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Minimum accuracy rating to filter leaderboards by")] float? accrating_from = null,
            [FromQuery, SwaggerParameter("Maximum accuracy rating to filter leaderboards by")] float? accrating_to = null,
            [FromQuery, SwaggerParameter("Minimum pass rating to filter leaderboards by")] float? passrating_from = null,
            [FromQuery, SwaggerParameter("Maximum pass rating to filter leaderboards by")] float? passrating_to = null,
            [FromQuery, SwaggerParameter("Minimum tech rating to filter leaderboards by")] float? techrating_from = null,
            [FromQuery, SwaggerParameter("Maximum tech rating to filter leaderboards by")] float? techrating_to = null,
            [FromQuery, SwaggerParameter("Minimum duration to filter leaderboards by in seconds")] float? duration_from = null,
            [FromQuery, SwaggerParameter("Maximum duration to filter leaderboards by in seconds")] float? duration_to = null,
            [FromQuery, SwaggerParameter("Start date to filter leaderboards by (timestamp)")] int? date_from = null,
            [FromQuery, SwaggerParameter("End date to filter leaderboards by (timestamp)")] int? date_to = null,
            [FromQuery, SwaggerParameter("Type of the date filter, default by map upload date")] DateRangeType date_range = DateRangeType.Upload,
            [FromQuery, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] string? types = null,
            [FromQuery, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] string? playlistIds = null,
            [FromBody, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] List<PlaylistResponse>? playlists = null,
            [FromQuery, SwaggerParameter("Filter maps from a specific mappers. BeatSaver profile ID list, comma separated, default is null")] string? mappers = null,
            [FromQuery, SwaggerParameter("Override current user ID")] string? overrideCurrentId = null) {

            var dbContext = _dbFactory.CreateDbContext();

            string? currentID = HttpContext == null ? overrideCurrentId : HttpContext.CurrentUserID(dbContext);

            IQueryable<Player>? currentPlayerQuery = currentID != null ? dbContext
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings) : null;
            if (mytype == MyTypeMaps.FriendsPlayed && currentPlayerQuery != null) {
                currentPlayerQuery = currentPlayerQuery.Include(p => p.Friends);
            }

            Player? currentPlayer = currentPlayerQuery != null ? await currentPlayerQuery.FirstOrDefaultAsync(p => p.Id == currentID) : null;

            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);

            var sequence = dbContext
                .Songs
                .AsNoTracking()
                .Where(s => s.MapCreator == mapCreator)
                .Filter(dbContext, out int? searchId, sortBy, order, search, type, types, mode, difficulty, mapType, allTypes, mapRequirements, allRequirements, songStatus, leaderboardContext, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, duration_from, duration_to, date_from, date_to, date_range, mappers, playlistList, currentPlayer);

            var result = new ResponseWithMetadata<MapInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count
                }
            };

            bool showPlays = sortBy == MapSortBy.PlayCount;
            bool todayPlays = false;
            bool thisWeekPlays = false;

            if (date_to == null && date_from != null) {
                var currentTime = Time.UnixNow();
                if (Math.Abs(currentTime - (int)date_from - 60 * 60 * 24) < 60 * 30) {
                    todayPlays = true;
                } else if (Math.Abs(currentTime - (int)date_from - 60 * 60 * 24 * 7) < 60 * 30) {
                    thisWeekPlays = true;
                }
            }

            if (page <= 0) {
                page = 1;
            }

            var helpersList = await sequence
                .Where(s => s.Difficulties.Any())
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new { s.Song.Id, Difficulties = s.Difficulties.Select(d => d.Id) })
                .ToListAsync();

            var idsList = helpersList.Select(s => s.Id).ToList();

            using (var anotherContext = _dbFactory.CreateDbContext()) {
                var songSequence = anotherContext
                    .Songs
                    .AsNoTracking()
                    .Where(s => idsList.Contains(s.Id));

                (result.Metadata.Total, result.Data) = await sequence.Where(s => s.Difficulties.Any()).CountAsync().CoundAndResults(songSequence
                    .TagWithCallerS()
                    .Select(s => new MapInfoResponse {
                        Id = s.Id,
                        Hash = s.LowerHash,
                        Name = s.Name,
                        SubName = s.SubName,
                        Author = s.Author,
                        Mapper = s.Mapper,
                        Mappers = s.Mappers != null ? s.Mappers.Select(m => new MapperResponse {
                            Id = m.Id,
                            PlayerId = m.Player != null ? m.Player.Id : null,
                            Name = m.Player != null ? m.Player.Name : m.Name,
                            Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                            Curator = m.Curator,
                            VerifiedMapper = m.VerifiedMapper,
                            Status = m.Status,
                        }).ToList() : null,
                        MapperId = s.MapperId,
                        CollaboratorIds = s.CollaboratorIds,
                        CoverImage = s.CoverImage,
                        FullCoverImage = s.FullCoverImage,
                        DownloadUrl = s.DownloadUrl,
                        Bpm = s.Bpm,
                        Duration = s.Duration,
                        UploadTime = s.UploadTime,
                        Tags = s.Tags,
                        ExternalStatuses = s.ExternalStatuses.Where(es => es.Status == SongStatus.Curated).ToList(),

                        Difficulties = s.Leaderboards
                        .Select(lb => new MapDiffResponse {
                            Id = lb.Difficulty.Id,
                            Value = lb.Difficulty.Value,
                            Mode = lb.Difficulty.Mode,
                            DifficultyName = lb.Difficulty.DifficultyName,
                            ModeName = lb.Difficulty.ModeName,
                            Status = lb.Difficulty.Status,
                            ModifierValues = lb.Difficulty.ModifierValues,
                            ModifiersRating = lb.Difficulty.ModifiersRating,
                            NominatedTime = lb.Difficulty.NominatedTime,
                            QualifiedTime = lb.Difficulty.QualifiedTime,
                            RankedTime = lb.Difficulty.RankedTime,
                            LastScoreTime = lb.LastScoreTime,

                            LeaderboardId = lb.Id,
                            Plays = showPlays 
                                ? (todayPlays 
                                    ? lb.TodayPlays
                                    : (thisWeekPlays
                                    ? lb.ThisWeekPlays
                                    : (date_range != DateRangeType.Score || (date_from == null && date_to == null) ? lb.Plays : lb.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(s => date_range != DateRangeType.Score || ((date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to))))))
                                : 0,
                            Attempts = lb.PlayCount,
                            PositiveVotes = lb.PositiveVotes,
                            StarVotes = lb.StarVotes,
                            NegativeVotes = lb.NegativeVotes,

                            Stars = lb.Difficulty.Stars,
                            PredictedAcc = lb.Difficulty.PredictedAcc,
                            PassRating = lb.Difficulty.PassRating,
                            AccRating = lb.Difficulty.AccRating,
                            TechRating = lb.Difficulty.TechRating,
                            Type = lb.Difficulty.Type,

                            SpeedTags = lb.Difficulty.SpeedTags,
                            StyleTags = lb.Difficulty.StyleTags,
                            FeatureTags = lb.Difficulty.FeatureTags,

                            Njs = lb.Difficulty.Njs,
                            Nps = lb.Difficulty.Nps,
                            Notes = lb.Difficulty.Notes,
                            Bombs = lb.Difficulty.Bombs,
                            Walls = lb.Difficulty.Walls,
                            MaxScore = lb.Difficulty.MaxScore,
                            Duration = lb.Difficulty.Duration,

                            Requirements = lb.Difficulty.Requirements,
                            MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(leaderboardContext) && !s.Banned).Select(s => new ScoreResponseWithAcc {
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
                                    Offsets = s.ReplayOffsets,
                                    Modifiers = s.Modifiers,
                                    BadCuts = s.BadCuts,
                                    MissedNotes = s.MissedNotes,
                                    BombCuts = s.BombCuts,
                                    WallsHit = s.WallsHit,
                                    Pauses = s.Pauses,
                                    FullCombo = s.FullCombo,
                                    Hmd = s.Hmd,
                                    Timeset = s.Timeset,
                                    Timepost = s.Timepost,
                                    ReplaysWatched = s.ReplayWatchedTotal,
                                    LeaderboardId = s.LeaderboardId,
                                    Platform = s.Platform,
                                    Weight = s.Weight,
                                    AccLeft = s.AccLeft,
                                    AccRight = s.AccRight,
                                    MaxStreak = s.MaxStreak,
                                }).FirstOrDefault(),
                        }).OrderBy(d => d.Mode > 0 ? d.Mode : 2000).ThenBy(d => d.Value).ToList(),
                    })
                    .ToListAsync());
            }

            if (result.Data.Count() > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var song in result.Data) {
                    var helper = helpersList.FirstOrDefault(s => s.Id == song.Id);
                    
                    foreach (var diff in song.Difficulties) {
                        if (!showRatings && !diff.Status.WithRating()) {
                            diff.HideRatings();
                        }
                        if (helper != null) {
                            diff.Applicable = helper.Difficulties.Any(d => d == diff.Id);
                        }
                    }
                    
                }

                result.Data = result.Data.OrderBy(e => helpersList.FindIndex(s => s.Id == e.Id));

                foreach (var song in result.Data) {
                    song.SortDifficulties(sortBy, order);
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

        [HttpGet("~/maps/trending/beatsaver")]
        [SwaggerOperation(Summary = "Retrieve a list of maps", Description = "Fetches a paginated and optionally filtered list of Beat Saber maps.")]
        [SwaggerResponse(200, "Maps retrieved successfully", typeof(ResponseWithMetadata<MapInfoResponseWithUpvotes>))]
        [SwaggerResponse(404, "Maps not found")]
        public async Task<ActionResult<ResponseWithMetadata<MapInfoResponseWithUpvotes>>> GetBeatSaverTrending([FromQuery, SwaggerParameter("Context of the leaderboard, default is General")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {

            var dbContext = _dbFactory.CreateDbContext();

            string? currentID = HttpContext.CurrentUserID(dbContext);

            IQueryable<Player>? currentPlayerQuery = currentID != null ? dbContext
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings) : null;

            Player? currentPlayer = currentPlayerQuery != null ? await currentPlayerQuery.FirstOrDefaultAsync(p => p.Id == currentID) : null;

            var result = new ResponseWithMetadata<MapInfoResponseWithUpvotes>() {
                Metadata = new Metadata() {
                    Page = 1,
                    ItemsPerPage = 5
                }
            };

            var cacheKey = $"beatsaverTrendingMaps";
            var trendingMaps = _cache.Get<List<BeatSaverTrendingMap>>(cacheKey);

            if (trendingMaps == null) {
                var songs = await SongUtils.GetTrendingSongsFromBeatSaver(DateTime.Now.Date.AddDays(-7));
                trendingMaps = songs?.Select(s => new BeatSaverTrendingMap {
                    Id = s.Id,
                    Votes = s.Stats?.Upvotes ?? 0
                }).OrderByDescending(s => s.Votes).ToList();

                if (trendingMaps == null) {
                    return NotFound("Can't reach BeatSaver for trending maps");
                }

                _cache.Set(cacheKey, trendingMaps, TimeSpan.FromDays(1));
            }

            var idsList = trendingMaps
                .Select(s => s.Id)
                .ToList();

            var lbSongs = await _context
                .Leaderboards
                .AsNoTracking()
                .Where(lb => idsList.Contains(lb.SongId))
                .Select(lb => new { lb.SongId, Group = lb.LeaderboardGroup != null ? lb.LeaderboardGroup.Leaderboards.Select(l => new { SongId = l.SongId, UploadDate = l.Timestamp }).ToList() : null })
                .ToListAsync();

            foreach (var group in lbSongs.GroupBy(lb => lb.SongId)) {
                var recentSong = group.OrderByDescending(g => g.Group.OrderByDescending(s => s.UploadDate).FirstOrDefault()?.UploadDate ?? 0).First();
                trendingMaps.First(m => m.Id == group.Key).Id = recentSong.SongId;
            }

            idsList = trendingMaps
                .Select(s => s.Id)
                .ToList();

            using (var anotherContext = _dbFactory.CreateDbContext()) {
                var songSequence = anotherContext
                    .Songs
                    .AsNoTracking()
                    .Where(s => idsList.Contains(s.Id));

                (result.Metadata.Total, result.Data) = await dbContext
                .Songs
                .AsNoTracking().CountAsync().CoundAndResults(songSequence
                    .TagWithCallerS()
                    .Select(s => new MapInfoResponseWithUpvotes {
                        Id = s.Id,
                        Hash = s.LowerHash,
                        Name = s.Name,
                        SubName = s.SubName,
                        Author = s.Author,
                        Mapper = s.Mapper,
                        Mappers = s.Mappers != null ? s.Mappers.Select(m => new MapperResponse {
                            Id = m.Id,
                            PlayerId = m.Player != null ? m.Player.Id : null,
                            Name = m.Player != null ? m.Player.Name : m.Name,
                            Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                            Curator = m.Curator,
                            VerifiedMapper = m.VerifiedMapper,
                            Status = m.Status,
                        }).ToList() : null,
                        MapperId = s.MapperId,
                        CollaboratorIds = s.CollaboratorIds,
                        CoverImage = s.CoverImage,
                        FullCoverImage = s.FullCoverImage,
                        DownloadUrl = s.DownloadUrl,
                        Bpm = s.Bpm,
                        Duration = s.Duration,
                        UploadTime = s.UploadTime,
                        Tags = s.Tags,
                        ExternalStatuses = s.ExternalStatuses.Where(es => es.Status == SongStatus.Curated).ToList(),

                        Difficulties = s.Leaderboards
                        .Select(lb => new MapDiffResponse {
                            Id = lb.Difficulty.Id,
                            Value = lb.Difficulty.Value,
                            Mode = lb.Difficulty.Mode,
                            DifficultyName = lb.Difficulty.DifficultyName,
                            ModeName = lb.Difficulty.ModeName,
                            Status = lb.Difficulty.Status,
                            ModifierValues = lb.Difficulty.ModifierValues,
                            ModifiersRating = lb.Difficulty.ModifiersRating,
                            NominatedTime = lb.Difficulty.NominatedTime,
                            QualifiedTime = lb.Difficulty.QualifiedTime,
                            RankedTime = lb.Difficulty.RankedTime,

                            Applicable = true,

                            LeaderboardId = lb.Id,
                            Attempts = lb.PlayCount,
                            PositiveVotes = lb.PositiveVotes,
                            StarVotes = lb.StarVotes,
                            NegativeVotes = lb.NegativeVotes,

                            Stars = lb.Difficulty.Stars,
                            PredictedAcc = lb.Difficulty.PredictedAcc,
                            PassRating = lb.Difficulty.PassRating,
                            AccRating = lb.Difficulty.AccRating,
                            TechRating = lb.Difficulty.TechRating,
                            Type = lb.Difficulty.Type,

                            SpeedTags = lb.Difficulty.SpeedTags,
                            StyleTags = lb.Difficulty.StyleTags,
                            FeatureTags = lb.Difficulty.FeatureTags,

                            Njs = lb.Difficulty.Njs,
                            Nps = lb.Difficulty.Nps,
                            Notes = lb.Difficulty.Notes,
                            Bombs = lb.Difficulty.Bombs,
                            Walls = lb.Difficulty.Walls,
                            MaxScore = lb.Difficulty.MaxScore,
                            Duration = lb.Difficulty.Duration,

                            Requirements = lb.Difficulty.Requirements,
                            MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(leaderboardContext) && !s.Banned).Select(s => new ScoreResponseWithAcc {
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
                                    Offsets = s.ReplayOffsets,
                                    Modifiers = s.Modifiers,
                                    BadCuts = s.BadCuts,
                                    MissedNotes = s.MissedNotes,
                                    BombCuts = s.BombCuts,
                                    WallsHit = s.WallsHit,
                                    Pauses = s.Pauses,
                                    FullCombo = s.FullCombo,
                                    Hmd = s.Hmd,
                                    Timeset = s.Timeset,
                                    Timepost = s.Timepost,
                                    ReplaysWatched = s.ReplayWatchedTotal,
                                    LeaderboardId = s.LeaderboardId,
                                    Platform = s.Platform,
                                    Weight = s.Weight,
                                    AccLeft = s.AccLeft,
                                    AccRight = s.AccRight,
                                    MaxStreak = s.MaxStreak,
                                }).FirstOrDefault(),
                        }).OrderBy(d => d.Mode > 0 ? d.Mode : 2000).ThenBy(d => d.Value).ToList(),
                    })
                    .ToListAsync());
            }

            if (result.Data.Count() > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var song in result.Data) {
                    song.Upvotes = trendingMaps.FirstOrDefault(m => m.Id == song.Id)?.Votes ?? 0;
                    foreach (var diff in song.Difficulties) {
                        if (!showRatings && !diff.Status.WithRating()) {
                            diff.HideRatings();
                        }
                    }
                }

                result.Data = result.Data.OrderBy(e => idsList.IndexOf(e.Id));
            }

            return result;
        }

        [HttpGet("~/map/hash/{hash}")]
        public async Task<ActionResult<Song>> GetHash(string hash)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }
            Song? song = await SongControllerHelper.GetOrAddSong(_context, hash);
            if (song is null)
            {
                return NotFound();
            }
            return song;
        }

        [NonAction]
        private async Task<Image<Rgba32>?> LoadRemoteImage(string url)
        {
            Image<Rgba32>? result = null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                try {
                    Stream contentStream = await (await new HttpClient().SendAsync(request)).Content.ReadAsStreamAsync();
                    result = Image.Load<Rgba32>(contentStream);
                } catch { }
            }
            return result;
        }
        
        [HttpGet("~/cover/processed/{songId}/{covername}")]
        public async Task<ActionResult> GetProcessedCover(string songId, string covername, [FromQuery] bool original = false)
        {
            var song = _context.Songs.Where(s => s.Id == songId).Select(s => new { s.CoverImage, s.FullCoverImage }).FirstOrDefault();
            string? coverUrl = null;
            if (song != null) {
                if (song.CoverImage.EndsWith(covername) || song.FullCoverImage?.EndsWith(covername) == true) {
                    coverUrl = song.CoverImage.Replace($"https://api.beatleader.com/cover/processed/{songId}/", "https://cdn.beatsaver.com/");
                }
            }
            if (coverUrl == null) {
                return NotFound();
            }

            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            if (currentPlayer?.ProfileSettings?.ShowExplicitCovers ?? false) {
                original = true;
            }

            if (original) {
                return Redirect(coverUrl);
            }

            var bluredUrl = $"cover-blured-{songId}.png";
            var previewUrl = await _s3Client.GetPresignedUrl(bluredUrl, S3Container.previews);
            if (previewUrl != null)
            {
                return Redirect(previewUrl);
            }

            var originalImage = await LoadRemoteImage(coverUrl);
            if (originalImage == null) {
                return NotFound();
            }

            // Apply Gaussian blur
            originalImage.Mutate(x => x.GaussianBlur(10f));

            MemoryStream ms = new MemoryStream();
            originalImage.SaveAsPng(ms);
            ms.Position = 0;
            try
            {
                await _s3Client.UploadPreview(bluredUrl, ms);
            }
            catch { }
            ms.Position = 0;

            return File(ms, "image/png");
        }

        [HttpGet("~/map/modinterface/{hash}")]
        public async Task<ActionResult<IEnumerable<DiffModResponse>>> GetModSongInfos(string hash)
        {

            var resFromLB = await _context.Leaderboards
                .Where(lb => lb.Song.LowerHash == hash.ToLower())
                .Include(lb => lb.Difficulty)
                    .ThenInclude(diff => diff.ModifierValues)
                .Include(lb => lb.Difficulty)
                    .ThenInclude(diff => diff.ModifiersRating)
                .Include(lb => lb.Clan)
                .Select(lb => new { 
                    DiffModResponse = new DiffModResponse {
                        DifficultyName = lb.Difficulty.DifficultyName,
                        ModeName = lb.Difficulty.ModeName,
                        Stars = lb.Difficulty.Stars,
                        Status = lb.Difficulty.Status,
                        Type = lb.Difficulty.Type,
                        Votes = lb.Scores
                            .Where(score => score.RankVoting != null)
                            .Select(score => score.RankVoting!.Rankability)
                            .ToArray(),
                        ModifierValues = lb.Difficulty.ModifierValues,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        PassRating = lb.Difficulty.PassRating,
                        AccRating = lb.Difficulty.AccRating,
                        TechRating = lb.Difficulty.TechRating,
                        ClanStatus = new ClanRankingStatus {
                            Clan = lb.Clan != null ? new ClanResponse {
                                Id = lb.Clan.Id,
                                Color = lb.Clan.Color,
                                Tag = lb.Clan.Tag,
                                Name = lb.Clan.Name,
                            } : null,
                            ClanRankingContested = lb.ClanRankingContested,
                            Applicable = lb.Difficulty.Status == DifficultyStatus.ranked
                        }
                    }, 
                    SongDiffs = lb.Song.Difficulties 
                })
                .ToArrayAsync();

            ICollection<DifficultyDescription> difficulties;
            if(resFromLB.Length == 0)
            {
                // We couldnt find any Leaderboard with that hash. Therefor we need to check if we can atleast get the song
                Song? song = await SongControllerHelper.GetOrAddSong(_context, hash);
                // Otherwise the song does not exist
                if (song is null)
                {
                    return NotFound();
                }
                difficulties = song.Difficulties;
            }
            else
            {
                // Else we can use the found difficulties of the song
                difficulties = resFromLB[0].SongDiffs;
            }

            // Now we need to return the LB DiffModResponses. If there are diffs in the song, that have no leaderboard we return the diffs without votes, as no leaderboard = no scores = no votes
            var result = difficulties.Select(diff =>
                resFromLB.FirstOrDefault(element => element.DiffModResponse.DifficultyName == diff.DifficultyName && element.DiffModResponse.ModeName == diff.ModeName)?.DiffModResponse
                ?? ResponseUtils.DiffModResponseFromDiffAndVotes(diff, Array.Empty<float>())).ToArray();
            
            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = currentID != null ? (await _context.Players.Include(p => p.ProfileSettings).Where(p => p.Id == currentID).Select(p => p.ProfileSettings).FirstOrDefaultAsync())?.ShowAllRatings ?? false : false;
            foreach (var item in result) {
                if (!showRatings && !item.Status.WithRating()) {
                    item.HideRatings();
                }
            }

            return result;
        }

        const string allStarsZipFile = "allStarsZipFile";

        [HttpGet("~/map/allstars")]
        public async Task<ActionResult> GetAllStars()
        {
            if(!_cache.TryGetValue(allStarsZipFile, out byte[]? zipFile) || zipFile is null)
            {
                var songs = (await _context.Songs
                        .Select(s => 
                            s.Difficulties.Where(d => d.Stars > 0).Select(d => 
                                new HashDiffStarTuple(
                                    s.LowerHash, 
                                    d.DifficultyName + d.ModeName, 
                                    (float)(d.Stars != null ? d.Stars : 0))).ToArray())
                        .ToArrayAsync())
                        .SelectMany(x => x)
                        .Distinct()
                        .Where(d => d.Stars > 0)
                        .ToArray();

                // Serialize Hashes, Diffs and Stars
                using MemoryStream originalms = new();
                Serializer.Serialize(originalms, songs);

                // Zip them in a gzip file
                originalms.Position = 0;
                using MemoryStream compressedms = new();
                using (var compressor = new GZipStream(compressedms, CompressionLevel.SmallestSize, true))
                {
                    await originalms.CopyToAsync(compressor);
                }

                // And cache the Result until midnight
                zipFile = compressedms.ToArray();
                _cache.Set(allStarsZipFile, zipFile, DateTimeOffset.UtcNow.AddDays(1).Date);
            }
            return File(zipFile, "application/gzip", "Testfile");
        }
        
        [HttpGet("~/map/migratenominations")]
        public async Task<ActionResult> MigrateNominations([FromQuery] string baseSongId, [FromQuery] string oldSongId, [FromQuery] string newSongId)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Song? baseSong = await _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync(i => i.Id == baseSongId);
            Song? oldSong = await _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync(i => i.Id == oldSongId);
            Song? newSong = await _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync(i => i.Id == newSongId);

            if (baseSong == null || oldSong == null || newSong == null) return NotFound();

            foreach (var item in oldSong.Difficulties)
            {
                await SongControllerHelper.MigrateLeaderboards(_context, newSong, oldSong, baseSong, item);
                item.Status = DifficultyStatus.outdated;
                item.Stars = 0;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/map/uploadOst")]
        public async Task<ActionResult> UploadOstMap()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var parse = new Parse(); 
            var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            var map = parse.TryLoadZip(ms)?.FirstOrDefault();
            if (map == null) return BadRequest();
            var info = map.Info;

            var song = new Song
            {
                Id = info._songFilename.Replace(".ogg", ""),
                Hash = info._songFilename.Replace(".ogg", ""),
                Name = info._songName + " [OST I]",
                SubName = info._songSubName,
                Author = info._songAuthorName,
                Mapper = "Beat Games",
                Bpm = info._beatsPerMinute,
                Duration = map.SongLength,
                UploadTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            };

            var diffs = new List<DifficultyDescription>();

            foreach (var set in map.Difficulties)
            {

                var diff = info
                    ._difficultyBeatmapSets
                    .First(s => s._beatmapCharacteristicName == set.Characteristic)
                    ._difficultyBeatmaps
                    .First(b => b._difficulty == set.Difficulty);

                var newDD = new DifficultyDescription
                {
                    Value = Song.DiffForDiffName(set.Difficulty),
                    Mode = Song.ModeForModeName(set.Characteristic),
                    DifficultyName = set.Difficulty,
                    ModeName = set.Characteristic,
                    Status = DifficultyStatus.OST,
                    Hash = song.LowerHash,

                    Njs = diff._noteJumpMovementSpeed,
                    Nps = set.Data.Notes.Count() / (float)map.SongLength,
                    Notes = set.Data.Notes.Count(),
                    Bombs = set.Data.Bombs.Count(),
                    Walls = set.Data.Walls.Count(),
                    MaxScore = set.MaxScore(),
                    Duration = map.SongLength,
                };

                if (set.Data.Chains.Count > 0 || set.Data.Arcs.Count > 0)
                {
                    newDD.Requirements |= Requirements.V3;
                    newDD.RequiresV3 = true;

                    newDD.Chains = set.Data.Chains.Sum(c => c.SliceCount > 1 ? c.SliceCount - 1 : 0);
                    newDD.Sliders = set.Data.Arcs.Count;

                    
                }
                if (set.Data.njsEvents.Count > 0) {
                    newDD.Requirements |= Models.Requirements.VNJS;
                    newDD.RequiresVNJS = true;
                }
                if (set.Data.lightColorEventBoxGroups.Count > 0 ||
                    set.Data.lightRotationEventBoxGroups.Count > 0 ||
                    set.Data.lightTranslationEventBoxGroups.Count > 0 ||
                    set.Data.vfxEventBoxGroups?.Count() > 0) {

                    newDD.Requirements |= Models.Requirements.GroupLighting;
                    newDD.RequiresGroupLighting = true;
                }

                newDD.MaxScoreGraph = new MaxScoreGraph();
                newDD.MaxScoreGraph.SaveList(set.MaxScoreGraph());
                if (newDD.MaxScore == 0) {
                    newDD.MaxScore = set.MaxScore();
                }

                diffs.Add(newDD);
            }
            song.Difficulties = diffs;

            ms.Position = 0;
            var archive = new ZipArchive(ms);

            if (info._coverImageFilename != null)
            {
                var coverFile = archive.Entries.FirstOrDefault(e => e.Name.ToLower() == info._coverImageFilename.ToLower());
                if (coverFile != null)
                {
                    using (var coverStream = coverFile.Open())
                    {
                        using (var coverMs = new MemoryStream(5))
                        {
                            await coverStream.CopyToAsync(coverMs);
                            var fileName = ($"songcover-{song.Id}-" + info._coverImageFilename).Replace(" ", "").Replace("(", "").Replace(")", "");

                            song.FullCoverImage = await _s3Client.UploadAsset(fileName, coverMs);
                            song.CoverImage = song.FullCoverImage;
                        }
                    }
                }
            }

            ms.Position = 0;
            song.DownloadUrl = await _s3Client.UploadSong(song.LowerHash + ".zip", ms);
            _context.Songs.Add(song);

            foreach (var diff in song.Difficulties) {
                await RatingUtils.UpdateFromExMachina(diff, song.DownloadUrl, null);
                var newLeaderboard = await SongControllerHelper.NewLeaderboard(_context, song, null, diff.DifficultyName, diff.ModeName);
            }

            song.Checked = true;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/map/uploadEarthDay")]
        public async Task<ActionResult> uploadEarthDay()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var song = new Song
            {
                Id = "EarthDay2025",
                LowerHash = "EarthDay2025",
                Name = "Earth Day 2025",
                SubName = "RECYCLED",
                Author = "Various",
                Mapper = "BeatLeader",
                Bpm = 120,
                Duration = 3600,
                UploadTime = Time.UnixNow() - 60 * 60 * 24 * 180,
                DownloadUrl = ""
            };

            var diffs = new List<DifficultyDescription>();

            var newDD = new DifficultyDescription
            {
                Value = 9,
                Mode = 1,
                DifficultyName = "ExpertPlus",
                ModeName = "Standard",
                Status = DifficultyStatus.unranked,
                Hash = song.LowerHash,

                Njs = 18,
                Nps = 0,
                Notes = 0,
                Bombs = 0,
                Walls = 0,
                MaxScore = 1,
                Duration = 3600,
            };

            diffs.Add(newDD);
            song.Difficulties = diffs;

            song.FullCoverImage = "https://cdn.assets.beatleader.com/EarthDayMapCover.jpg";
            song.CoverImage = "https://cdn.assets.beatleader.com/EarthDayMapCover.jpg";

            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            var leaderboard = new Leaderboard();
            leaderboard.SongId = song.Id;

            leaderboard.Difficulty = newDD;
            leaderboard.Scores = new List<Score>();
            leaderboard.Id = "EarthDay2025";
            leaderboard.Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

            _context.Leaderboards.Add(leaderboard);

            song.Checked = true;

            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
