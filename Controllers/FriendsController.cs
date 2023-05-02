using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
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

        [HttpGet("~/user/friendScores")]
        [Authorize]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> FriendsScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null) {

            string userId = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.FindAsync(userId);
            if (player == null) {
                return NotFound();
            }

            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence")) {
                var friends = _context
                    .Friends
                    .Where(f => f.Id == player.Id)
                    .Include(f => f.Friends)
                    .FirstOrDefault();

                if (friends != null) {
                    var friendsList = friends.Friends.Select(f => f.Id).ToList();
                    sequence = _context.Scores.Where(s => s.PlayerId == player.Id || friendsList.Contains(s.PlayerId));
                } else {
                    sequence = _context.Scores.Where(s => s.PlayerId == player.Id);
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
                    case "predictedAcc":
                        sequence = sequence.Order(order, t => t.Leaderboard.Difficulty.PredictedAcc);
                        break;
                    case "passRating":
                        sequence = sequence.Order(order, t => t.Leaderboard.Difficulty.PassRating);
                        break;
                    case "techRating":
                        sequence = sequence.Order(order, t => t.Leaderboard.Difficulty.TechRating);
                        break;
                    case "stars":
                        sequence = sequence.Order(order, t => t.Leaderboard.Difficulty.Stars);
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
                    sequence = sequence.Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null) {
                    sequence = sequence.Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
                }
            }

            var result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = sequence
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
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
                            Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        },
                        ScoreImprovement = s.ScoreImprovement,
                        RankVoting = s.RankVoting,
                        Metadata = s.Metadata,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Leaderboard = new LeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
                            Difficulty = s.Leaderboard.Difficulty
                        },
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak
                    }).ToList()
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
                        Clans = a.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
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
