using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Models.SongSuggest;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class SongSuggestController : Controller
    {
        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;
        
        private readonly IMemoryCache _cache;
        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly LeaderboardController _leaderboardController;

        public SongSuggestController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            IMemoryCache cache,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            LeaderboardController leaderboardController)
        {
            _context = context;
            _dbFactory = dbFactory;
            _cache = cache;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
            
            _leaderboardController = leaderboardController;
        }

        [HttpGet("~/songsuggest")]
        public async Task<ActionResult> GetSongSuggest([FromQuery] int? before_time = null, [FromQuery] LeaderboardContexts? leaderboardContext = null)
        {
            var refresh = await _context
                .SongSuggestRefreshes
                .OrderByDescending(s => s.Timeset)
                .Where(s => before_time != null ? s.Timeset < before_time : true)
                .FirstOrDefaultAsync();
            if (refresh == null) return NotFound();

            var filename = refresh.File;
            if (leaderboardContext != null) {
                filename = filename.Replace($"songsuggestions-{refresh.Timeset}", $"songsuggestions-{refresh.Timeset}-{leaderboardContext}");
            }

            var stream = await _s3Client.DownloadAsset(filename);

            if (stream != null) {
                Response.Headers.Add("Content-Type", "application/json");
                return Ok(stream);
                
            } else {
                return NotFound();
            }
        }

        [HttpGet("~/songsuggest/songs")]
        public async Task<ActionResult<List<SongSuggestSong>>> GetSongSuggestSongs(
            [FromQuery] int? before_time = null) {

            var refresh = await _context
                .SongSuggestRefreshes
                .OrderByDescending(s => s.Timeset)
                .Where(s => before_time != null ? s.Timeset < before_time : true)
                .FirstOrDefaultAsync();
            if (refresh == null) return NotFound();

            var songsUrl = await _s3Client.GetPresignedUrl(refresh.SongsFile, S3Container.assets);
            if (songsUrl == null) {
                return NotFound();
            }
            return Redirect(songsUrl);
        }

        [HttpGet("~/songsuggest/refreshTime")]
        public async Task<ActionResult<int>> GetSongSuggestLastRefreshTime([FromQuery] int? before_time = null)
        {
            var refresh = await _context
                .SongSuggestRefreshes
                .OrderByDescending(s => s.Timeset)
                .Where(s => before_time != null ? s.Timeset < before_time : true)
                .FirstOrDefaultAsync();
            if (refresh == null) return NotFound();

            return refresh.Timeset;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/songsuggest/biglist")]
        public async Task<ActionResult> GetBigMapsList()
        {
            if (!(await HttpContext.ItsAdmin(_context))) return Unauthorized();

            return Ok( _context.Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                .OrderByDescending(lb => lb.Difficulty.Stars)
                .Select(lb => new {
                    ID = lb.Id,
                    name = lb.Song.Name,
                    hash = lb.Song.Hash,
                    difficulty = lb.Difficulty.DifficultyName,
                    mode = lb.Difficulty.ModeName,
                    stars = (float)lb.Difficulty.Stars,
                    accRating = (float)lb.Difficulty.AccRating,
                    passRating = (float)lb.Difficulty.PassRating,
                    techRating = (float)lb.Difficulty.TechRating,
                    cover = lb.Song.CoverImage,
                    time = lb.Difficulty.RankedTime
                })
                .ToListAsync());
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/gigalist")]
        public async Task<ActionResult> GetGigaList([FromQuery] int topCount)
        {
            if (!(await HttpContext.ItsAdmin(_context))) return Unauthorized();

            var objec = new { 
                Maps = _context.Leaderboards
                    .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                    .OrderByDescending(lb => lb.Difficulty.Stars)
                    .Select(lb => new {
                        id = lb.Id,
                        name = lb.Song.Name,
                        hash = lb.Song.Hash,
                        difficulty = lb.Difficulty.DifficultyName,
                        mode = lb.Difficulty.ModeName,
                        stars = (float)lb.Difficulty.Stars,
                        accRating = (float)lb.Difficulty.AccRating,
                        passRating = (float)lb.Difficulty.PassRating,
                        techRating = (float)lb.Difficulty.TechRating,
                        cover = lb.Song.CoverImage
                    })
                    .ToListAsync(),
                Players = _context
                    .Players
                    .Where(p => !p.Banned && p.Rank <= topCount && p.Rank >= 1)
                    .Select(p => new {
                        id = p.Id,
                        N = p.Name,
                        A = p.Avatar,
                        R = p.Rank
                    }),
                Scores = _context
                    .Scores
                    .Where(s => 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked &&
                        !s.Player.Banned && 
                        s.Player.Rank <= topCount &&
                        s.Player.Rank >= 1 &&
                        s.Weight > 0.4 &&
                        s.ValidForGeneral)
                    .Select(s => new {
                        P = s.Pp,
                        W = s.Weight,
                        I = s.PlayerId,
                        LI = s.LeaderboardId
                    })
                    .ToListAsync()
            };

            var filename = $"gigalistfile{topCount}.json";
            await _s3Client.UploadStream(filename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(objec)).ToStream());

            return Ok();
        }

        [NonAction]
        public async Task<ActionResult> RefreshSongSuggestContext(LeaderboardContexts leaderboardContext, int timeset)
        {
            var activeTreshold = timeset - 60 * 60 * 24 * 365;

            IQueryable<IScore> query = leaderboardContext.HasFlag(LeaderboardContexts.General) ?
                _context.Scores
                .Where(s => 
                    s.ValidForGeneral &&
                    !s.Qualification &&
                    s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked &&
                    s.Player.ScoreStats.RankedPlayCount >= 30 &&
                    !s.Player.Banned &&
                    s.Player.ScoreStats.LastRankedScoreTime >= activeTreshold)
                : _context.ScoreContextExtensions
                .Where(s => 
                    s.Context == leaderboardContext &&
                    !s.Qualification &&
                    s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked &&
                    s.Player.ScoreStats.RankedPlayCount >= 30 &&
                    !s.Player.Banned &&
                    s.Player.ScoreStats.LastRankedScoreTime >= activeTreshold);

            var list = (await query
                .Select(s => new { 
                    score = new Top10kScoreV2 {
                        songID = s.LeaderboardId,
                        pp = s.Pp,
                        accuracy = s.Accuracy,
                        timepost = s.Timepost,
                        modifiers = s.Modifiers.Length > 0 ? s.Modifiers : null
                    },
                    player = new Top10kPlayer {
                        id = s.Player.Id,
                        name = s.Player.Name,
                        rank = s.Player.Rank
                    }
                })
                .ToListAsync())
                .GroupBy(m => m.player.id)
                .OrderBy(group => group.First().player.rank)
                .Where(g => g.Count() >= 30)
                .Select((group, i) => new Top10kPlayerV2 {
                    id = group.First().player.id,
                    name = group.First().player.name,
                    rank = i + 1,
                    top10kScore = group
                        .Select(m => m.score)
                        .OrderByDescending(s => s.pp)
                        .Take(30)
                        .Select((score, i) => new Top10kScoreV2 {
                            songID = score.songID,
                            pp = score.pp,
                            rank = i + 1,
                            timepost = score.timepost,
                            modifiers = score.modifiers
                        })
                        .ToList()
                })
                .Where(p => p.top10kScore.Last().pp / p.top10kScore.First().pp > 0.7)
                .ToList();

            var filename = $"songsuggestions-{timeset}-{leaderboardContext}.json";
            await _s3Client.UploadStream(filename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(list)).ToStream());

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/songsuggest/refresh")]
        public async Task<ActionResult> RefreshSongSuggest()
        {
            if (HttpContext != null && !(await HttpContext.ItsAdmin(_context))) return Unauthorized();

            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var activeTreshold = timeset - 60 * 60 * 24 * 31 * 3;
            var scoreTreshold = timeset - 60 * 60 * 24 * 365;

            var list = (await _context.Scores
                .Where(s => 
                    s.ValidForGeneral &&
                    !s.Qualification &&
                    !s.Modifiers.Contains("FS") &&
                    !s.Modifiers.Contains("SF") &&
                    !s.Modifiers.Contains("GN") &&
                    !s.Modifiers.Contains("SS") &&
                    s.Leaderboard.Difficulty.ModeName == "Standard" &&
                    s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked &&
                    s.Player.ScoreStats.RankedPlayCount >= 30 &&
                    !s.Player.Banned &&
                    s.Player.ScoreStats.LastRankedScoreTime >= activeTreshold)
                .Select(s => new { 
                    score = new Top10kScore {
                        songID = s.LeaderboardId,
                        pp = s.Pp,
                        accuracy = s.Accuracy,
                        timepost = s.Timepost
                    },
                    player = new Top10kPlayer {
                        id = s.Player.Id,
                        name = s.Player.Name,
                        rank = s.Player.Rank
                    }
                })
                .ToListAsync())
                .GroupBy(m => m.player.id)
                .OrderBy(group => group.First().player.rank)
                .Where(g => g.Count() >= 20)
                .Select((group, i) => new Top10kPlayer {
                    id = group.First().player.id,
                    name = group.First().player.name,
                    rank = i + 1,
                    top10kScore = group
                        .Select(m => m.score)
                        .OrderByDescending(s => s.pp)
                        .Take(30)
                        .OrderByDescending(s => s.accuracy)
                        .Take(20)
                        .OrderByDescending(s => s.pp)
                        .Select((score, i) => new Top10kScore {
                            songID = score.songID,
                            pp = score.pp,
                            rank = i + 1,
                            timepost = score.timepost
                        })
                        .ToList()
                })
                .Where(p => p.top10kScore.Last().pp / p.top10kScore.First().pp > 0.7 && p.top10kScore.All(s => s.timepost > scoreTreshold))
                .ToList();

            var filename = $"songsuggestions-{timeset}.json";
            await _s3Client.UploadStream(filename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(list)).ToStream());

            var songs = await _context.Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                .OrderByDescending(lb => lb.Difficulty.Stars)
                .Select(lb => new SongSuggestSong {
                    ID = lb.Id,
                    name = lb.Song.Name,
                    hash = lb.Song.Hash,
                    difficulty = lb.Difficulty.DifficultyName,
                    mode = lb.Difficulty.ModeName,
                    stars = (float)lb.Difficulty.Stars,
                    accRating = (float)lb.Difficulty.AccRating,
                    passRating = (float)lb.Difficulty.PassRating,
                    techRating = (float)lb.Difficulty.TechRating,
                })
                .ToListAsync();

            var songsfilename = $"songsuggestions-{timeset}-songs.json";
            await _s3Client.UploadStream(songsfilename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(songs)).ToStream());

            await RefreshSongSuggestContext(LeaderboardContexts.General, timeset);
            await RefreshSongSuggestContext(LeaderboardContexts.NoMods, timeset);

            _context.SongSuggestRefreshes.Add(new SongSuggestRefresh {
                Timeset = timeset,
                File = filename,
                SongsFile = songsfilename
            });
            await _context.SaveChangesAsync();
            

            return Ok();
        }

        

        [HttpGet("~/maps/curated/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> CuratedMaps()
        {
            var stream = await _s3Client.DownloadAsset("curatedmaps-list.json");

            return File(stream, "application/json");
        }

        [HttpGet("~/maps/curated/refresh")]
        public async Task<ActionResult> RefreshCurated()
        {
            if (HttpContext != null && !(await HttpContext.ItsAdmin(_context))) return Unauthorized();

            var currentId = HttpContext.CurrentUserID(_context);
            var curated = _leaderboardController.GetAllGroupped(1, 3, MapSortBy.Timestamp, Order.Desc, type: Enums.Type.All, songStatus: SongStatus.Curated);

            var response = curated.Result.Value;
            DefaultContractResolver contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };
            await _s3Client.UploadStream("curatedmaps-list.json", S3Container.assets, new BinaryData(JsonConvert.SerializeObject(response, new JsonSerializerSettings
            {
                ContractResolver = contractResolver
            })).ToStream());

            return Ok();
        }

        [HttpGet("~/mod/maps/trending/")]
        public async Task<ActionResult<ResponseWithMetadata<TrendingLeaderboardInfoResponse>>> ModTrendingMaps() {
            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId == null) {
                var stream = await _s3Client.DownloadAsset("trendingmaps-list.json");

                return File(stream, "application/json");
            }

            var cacheKey = $"modTrendingMaps_{currentId}";
            var cachedResponse = _cache.Get<ResponseWithMetadata<TrendingLeaderboardInfoResponse>>(cacheKey);
            if (cachedResponse != null)
            {
                return cachedResponse;
            }

            var tasks = new Task<List<string>>[4];
            var treshold = Time.UnixNow() - 60 * 60 * 24 * 30;
            for (int i = 0; i < 4; i++) {
                var dbContext = _dbFactory.CreateDbContext();
                var query = dbContext.Leaderboards.Where(l => !l.Scores.Any(s => s.PlayerId == currentId));
                switch (i) {
                    case 0:
                        query = query
                            .Where(l => l.Song.UploadTime > treshold && l.Song.MapCreator == SongCreator.Human)
                            .OrderByDescending(l => l.TodayPlays);
                        break;
                    case 1:
                        query = query
                            .Where(l => l.Song.UploadTime > treshold && l.Song.MapCreator == SongCreator.Human)
                            .OrderByDescending(l => l.ThisWeekPlays);
                        break;
                    case 2:
                        query = query
                            .Where(l => l.Song.Status.HasFlag(SongStatus.Curated) && l.Song.MapCreator == SongCreator.Human)
                            .OrderByDescending(l => l.Song.UploadTime);
                        break;
                    case 3:
                        query = query
                            .Where(l => l.Song.UploadTime > treshold && l.Song.MapCreator == SongCreator.Human)
                            .OrderByDescending(l => l.PositiveVotes);
                        break;
                    default:
                        break;
                }

                tasks[i] = query
                    .TagWithCallerS()
                    .AsNoTracking()
                    .Select(lb => lb.Id)
                    .Take(3)
                    .ToListAsync();
            }

            await Task.WhenAll(tasks);

            var result = new Dictionary<string, int>();
            int counter = 0;
            foreach (var group in tasks) {
                counter++;
                foreach (var item in group.Result) {
                    if (!result.ContainsKey(item)) {
                        result[item] = counter - 1;
                    }
                    if (result.Count == counter) {
                        break;
                    }
                }
            }
            var ids = result.Keys.ToList();

            var response = new ResponseWithMetadata<TrendingLeaderboardInfoResponse> {
                Metadata = new Metadata {
                    Page = 1,
                    ItemsPerPage = result.Count,
                    Total = result.Count
                },
                Data = await _context
                    .Leaderboards
                    .Where(lb => ids.Contains(lb.Id))
                    .Select(lb => new TrendingLeaderboardInfoResponse {
                        Id = lb.Id,
                        Song = new SongResponse {
                            Id = lb.Song.Id,
                            Hash = lb.Song.Hash,
                            Name = lb.Song.Name,
                            SubName = lb.Song.SubName,
                            Author = lb.Song.Author,
                            Mapper = lb.Song.Mapper,
                            CoverImage  = lb.Song.CoverImage,
                            FullCoverImage = lb.Song.FullCoverImage,
                            DownloadUrl = lb.Song.DownloadUrl,
                            Explicity = lb.Song.Explicity,
                            UploadTime = lb.Song.UploadTime
                        },
                        Difficulty = new DifficultyResponse {
                            Id = lb.Difficulty.Id,
                            Value = lb.Difficulty.Value,
                            Mode = lb.Difficulty.Mode,
                            DifficultyName = lb.Difficulty.DifficultyName,
                            ModeName = lb.Difficulty.ModeName,
                            Status = lb.Difficulty.Status,
                            NominatedTime = lb.Difficulty.NominatedTime,
                            QualifiedTime = lb.Difficulty.QualifiedTime,
                            RankedTime = lb.Difficulty.RankedTime,

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
                        },
                        PositiveVotes = lb.PositiveVotes,
                        TodayPlays = lb.TodayPlays,
                        ThisWeekPlays = lb.ThisWeekPlays
                    }).ToListAsync()
            };

            response.Data = response.Data.OrderBy(s => result[s.Id]);
            foreach (var item in response.Data) {
                switch (result[item.Id]) {
                    case 0:
                        item.Description = "Top played today";
                        item.TrendingValue = $"{item.TodayPlays} plays";
                        break;
                    case 1:
                        item.Description = "Top played this week";
                        item.TrendingValue = $"{item.ThisWeekPlays} plays";
                        break;
                    case 2:
                        item.Description = "Newly curated map";
                        item.TrendingValue = $"{item.ThisWeekPlays} plays";
                        break;
                    case 3:
                        item.Description = "Top voted new map";
                        item.TrendingValue = $"{item.PositiveVotes} votes";
                        break;
                    default:
                        break;
                }
            }

            _cache.Set(cacheKey, response, TimeSpan.FromDays(1));

            return response;
        }

        //[HttpGet("~/mod/maps/trending/")]
        [HttpGet("~/maps/trending/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> TrendingMaps()
        {
            var stream = await _s3Client.DownloadAsset("trendingmaps-list.json");

            return File(stream, "application/json");
        }

        [HttpGet("~/maps/trending/refresh")]
        public async Task<ActionResult> RefreshTrending()
        {
            if (HttpContext != null && !(await HttpContext.ItsAdmin(_context))) return Unauthorized();

            int timeset = Time.UnixNow();
            var currentId = HttpContext.CurrentUserID(_context);
            var topToday = _leaderboardController.GetAll(1, 1, MapSortBy.PlayCount, date_from: timeset - 60 * 60 * 24, date_range: DateRangeType.Score, overrideCurrentId: currentId);
            var topWeek = _leaderboardController.GetAll(1, 1, MapSortBy.PlayCount, date_from: timeset - 60 * 60 * 24 * 7, date_range: DateRangeType.Score, overrideCurrentId: currentId);
            var topVoted = _leaderboardController.GetAll(1, 1, MapSortBy.Voting, date_from: timeset - 60 * 60 * 24 * 30, date_range: DateRangeType.Upload, overrideCurrentId: currentId);

            Task.WaitAll([topToday, topWeek, topVoted]);

            var topTodayResult = topToday.Result;
            if (topTodayResult.Value?.Data.FirstOrDefault()?.Id == topWeek.Result.Value?.Data.FirstOrDefault()?.Id) {
                topTodayResult = await _leaderboardController.GetAll(2, 1, MapSortBy.PlayCount, date_from: timeset - 60 * 60 * 24, overrideCurrentId: currentId);
            }

            var topVotedResult = topVoted.Result;
            if (topVotedResult.Value?.Data.FirstOrDefault()?.Id == topWeek.Result.Value?.Data.FirstOrDefault()?.Id || 
                topVotedResult.Value?.Data.FirstOrDefault()?.Id == topTodayResult.Value?.Data.FirstOrDefault()?.Id) {
                topVotedResult = await _leaderboardController.GetAll(2, 1, MapSortBy.Voting, date_from: timeset - 60 * 60 * 24 * 30, overrideCurrentId: currentId);
            }

            var response = new ResponseWithMetadata<LeaderboardInfoResponse> {
                Metadata = new Metadata {
                    Page = 1,
                    ItemsPerPage = 3,
                    Total = topTodayResult.Value.Metadata.Total + topWeek.Result.Value.Metadata.Total + topVotedResult.Value.Metadata.Total
                },
                Data = topTodayResult.Value.Data.Concat(topWeek.Result.Value.Data).Concat(topVotedResult.Value.Data)
            };
            DefaultContractResolver contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };
            var songsfilename = $"trendingmaps-{timeset}-list.json";
            await _s3Client.UploadStream(songsfilename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(response, new JsonSerializerSettings
            {
                ContractResolver = contractResolver
            })).ToStream());

            songsfilename = "trendingmaps-list.json";
            await _s3Client.UploadStream(songsfilename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(response, new JsonSerializerSettings
            {
                ContractResolver = contractResolver
            })).ToStream());

            return Ok();
        }
    }
}
