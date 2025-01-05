using Amazon.S3;
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

        [HttpGet("~/user/friendScores")]
        [Authorize]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> FriendsScores(
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
            [FromQuery, SwaggerParameter("Filter scores by headset, default is null")] HMD? hmd = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Filter scores made after unix timestamp, default is null")] int? time_from = null,
            [FromQuery, SwaggerParameter("Filter scores made before unix timestamp, default is null")] int? time_to = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] int? eventId = null) {

            string? userId = HttpContext.CurrentUserID(_context);
            var player = userId != null 
                ? await _context
                    .Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == userId)
                : null;
            if (player == null) {
                return NotFound();
            }

            bool showRatings = player.ProfileSettings?.ShowAllRatings ?? false;
            IQueryable<IScore> sequence = leaderboardContext == LeaderboardContexts.General 
                ? _context.Scores.AsNoTracking().Where(s => s.ValidForGeneral)
                : _context.ScoreContextExtensions.AsNoTracking().Include(ce => ce.ScoreInstance).Where(s => s.Context == leaderboardContext);
            int? searchId = null;

            using (_serverTiming.TimeAction("sequence")) {
                var friends = await _context
                    .Friends
                    .AsNoTracking()
                    .Where(f => f.Id == player.Id)
                    .Include(f => f.Friends)
                    .FirstOrDefaultAsync();

                (sequence, searchId) = await sequence.Filter(_context, !player.Banned, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, type, hmd, modifiers, stars_from, stars_to, time_from, time_to, eventId);

                var friendsList = new List<string> { player.Id };
                if (friends != null) {
                    friendsList.AddRange(friends.Friends.Select(f => f.Id));
                }

                var score = Expression.Parameter(typeof(IScore), "s");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in friendsList)
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(score, "PlayerId"), Expression.Constant(term)));
                }
                sequence = sequence.Where((Expression<Func<IScore, bool>>)Expression.Lambda(exp, score));
            }

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
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        Player = new PlayerResponse {
                            Id = s.Player.Id,
                            Name = s.Player.Name,
                            Alias = s.Player.Alias,
                            Platform = s.Player.Platform,
                            Country = s.Player.Country,

                            Pp = s.Player.Pp,
                            Rank = s.Player.Rank,
                            CountryRank = s.Player.CountryRank,
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
                    ItemsPerPage = count
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
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
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

            var friends = await _context
                    .Friends
                    .Where(f => f.Id == player.Id)
                    .Include(f => f.Friends)
                    .FirstOrDefaultAsync();

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
                    Total = await achievementsSequence.CountAsync()
                }
            };

            var data = new List<FriendActivity>();

            var achievements = await achievementsSequence
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
                .ToListAsync();
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
