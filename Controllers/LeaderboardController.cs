using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using Type = BeatLeader_Server.Enums.Type;
using Lib.ServerTiming;
using System.Net;
using BeatLeader_Server.ControllerHelpers;
using Swashbuckle.AspNetCore.Annotations;

namespace BeatLeader_Server.Controllers {
    public class LeaderboardController : Controller {

        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;

        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;

        public LeaderboardController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            IConfiguration configuration,
            IServerTiming serverTiming) {
            _context = context;
            _dbFactory = dbFactory;
            _s3Client = configuration.GetS3Client();
            _serverTiming = serverTiming;
        }

        [NonAction]
        public async Task GeneralScores(
            LeaderboardResponse leaderboard,
            bool showBots,
            bool voters,
            bool showVoters,
            int page,
            int count,
            LeaderboardSortBy sortBy,
            Order order,
            ScoreFilterStatus scoreStatus,
            string? countries,
            string? search,
            string? modifiers,
            List<string>? friendsList,
            string? clanTag,
            string? hmd,
            bool offsets = false) {
            IQueryable<Score> scoreQuery = _context
                .Scores
                .AsNoTracking()
                .Where(s => s.LeaderboardId == leaderboard.Id && s.ValidForGeneral);

            if (countries == null) {
                if (friendsList != null) {
                    scoreQuery = scoreQuery.Where(s => (!s.Banned || (showBots && s.Bot)) && friendsList.Contains(s.PlayerId));
                } else if (voters) {
                    scoreQuery = scoreQuery.Where(s => (!s.Banned || (showBots && s.Bot)) && s.RankVoting != null);
                } else {
                    scoreQuery = scoreQuery.Where(s => (!s.Banned || (showBots && s.Bot)));
                }
            } else {
                if (friendsList != null) {
                    scoreQuery = scoreQuery.Where(s => (!s.Banned || (showBots && s.Bot)) && friendsList.Contains(s.PlayerId) && countries.ToLower().Contains(s.Player.Country.ToLower()));
                } else {
                    scoreQuery = scoreQuery.Where(s => (!s.Banned || (showBots && s.Bot)) && countries.ToLower().Contains(s.Player.Country.ToLower()));
                }
            }

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(Score), "s");

                    var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                    var any = modifiers.Contains("any");
                    var not = modifiers.Contains("not");
                    // 1 != 2 is here to trigger `OrElse` further the line.
                    var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(any ? 2 : 1));
                    var modifiersList = modifiers.Split(",").Where(m => m != "any" && m != "none" && m != "not");

                    foreach (var term in modifiersList) {
                        var subexpression = Expression.Call(Expression.Property(score, "Modifiers"), contains, Expression.Constant(term));
                        if (not) {
                            exp = Expression.And(exp, Expression.Not(subexpression));
                        } else {
                            if (any) {
                                exp = Expression.OrElse(exp, subexpression);
                            } else {
                                exp = Expression.And(exp, subexpression);
                            }
                        }
                    }
                    scoreQuery = scoreQuery.Where((Expression<Func<Score, bool>>)Expression.Lambda(exp, score));
                } else {
                    scoreQuery = scoreQuery.Where(s => s.Modifiers.Length == 0);
                }
            }

            if (hmd != null) {
                try {
                    var hmds = hmd.ToLower().Split(",").Select(s => (HMD)Int32.Parse(s));
                    scoreQuery = scoreQuery.Where(s => hmds.Contains(s.Hmd));
                } catch { }
            }

            Order oppositeOrder = order.Reverse();

            switch (sortBy) {
                case LeaderboardSortBy.Date:
                    scoreQuery = scoreQuery.Order(order, s => s.Timepost).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Pp:
                    scoreQuery = scoreQuery.Order(order, s => s.Pp).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Acc:
                    scoreQuery = scoreQuery.Order(order, s => s.Accuracy);
                    break;
                case LeaderboardSortBy.Pauses:
                    scoreQuery = scoreQuery.Order(order, s => s.Pauses).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Rank:
                    scoreQuery = leaderboard.Difficulty.Status.WithPP() 
                                ? scoreQuery
                                    .Order(order, el => Math.Round(el.Pp, 2))
                                    .ThenOrder(order, el => Math.Round(el.Accuracy, 4))
                                    .ThenOrder(oppositeOrder, el => el.Timeset)
                                : scoreQuery
                                    .Order(oppositeOrder, el => el.Priority)
                                    .ThenOrder(order, el => el.ModifiedScore)
                                    .ThenOrder(order, el => Math.Round(el.Accuracy, 4))
                                    .ThenOrder(oppositeOrder, el => el.Timeset);
                    break;
                case LeaderboardSortBy.MaxStreak:
                    scoreQuery = scoreQuery.Order(order, s => s.MaxStreak).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Mistakes:
                    scoreQuery = scoreQuery.Order(order, s => s.BadCuts + s.MissedNotes + s.BombCuts + s.WallsHit);
                    break;
                case LeaderboardSortBy.Weight:
                    scoreQuery = scoreQuery.Order(order, s => s.Weight);
                    break;
                case LeaderboardSortBy.WeightedPp:
                    scoreQuery = scoreQuery.Order(order, s => s.Weight * s.Pp);
                    break;
                default:
                    break;
            }
            switch (scoreStatus) {
                case ScoreFilterStatus.None:
                    break;
                case ScoreFilterStatus.Suspicious:
                    scoreQuery = scoreQuery.Where(s => s.Suspicious);
                    break;
                default:
                    break;
            }
            if (search != null) {
                string lowSearch = search.ToLower();
                scoreQuery = scoreQuery
                    .Where(s => s.Player.Name.ToLower().Contains(lowSearch) ||
                                s.Player.Clans.FirstOrDefault(c => c.Name.ToLower().Contains(lowSearch)) != null ||
                                s.Player.Clans.FirstOrDefault(c => c.Tag.ToLower().Contains(lowSearch)) != null);
            }
            if (clanTag != null) {
                scoreQuery = scoreQuery
                    .Where(s => s.Player.Clans.FirstOrDefault(c => c.Tag == clanTag.ToUpper()) != null);
            }
            using (_serverTiming.TimeAction("scorecount")) {
                leaderboard.Plays = await scoreQuery.CountAsync();
            }
            using (_serverTiming.TimeAction("scorelist")) {
            leaderboard.Scores = await scoreQuery
                .AsSplitQuery()
                .TagWithCaller()
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new ScoreResponse {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    Rank = s.Rank,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    Pauses = s.Pauses,
                    FullCombo = s.FullCombo,
                    Timeset = s.Timeset,
                    Timepost = s.Timepost,
                    MaxStreak = s.MaxStreak,
                    AccPP = s.AccPP,
                    TechPP = s.TechPP,
                    PassPP = s.PassPP,
                    Weight = s.Weight,
                    FcAccuracy = s.FcAccuracy,
                    Hmd = s.Hmd,
                    Platform = s.Platform,
                    Controller = s.Controller,
                    FcPp = s.FcPp,
                    Offsets = offsets ? s.ReplayOffsets : null,
                    Replay = offsets ? s.Replay : null,
                    Player = new PlayerResponse {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Bot = s.Player.Bot,
                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player
                            .Clans
                            .OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id)
                            .Take(1)
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    RankVoting = showVoters ? s.RankVoting : null,
                })
                .ToListAsync();
            }
        }

        [NonAction]
        public async Task PredictedScores(
            LeaderboardResponse leaderboard,
            bool showBots,
            bool voters,
            bool showVoters,
            int page,
            int count,
            LeaderboardSortBy sortBy,
            Order order,
            ScoreFilterStatus scoreStatus,
            string? countries,
            string? search,
            string? modifiers,
            List<string>? friendsList,
            bool offsets = false) {
            var scoreQuery = _context
                .PredictedScores
                .AsNoTracking()
                .Where(s => s.LeaderboardId == leaderboard.Id);

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(PredictedScore), "s");

                    var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                    var any = modifiers.Contains("any");
                    var not = modifiers.Contains("not");
                    // 1 != 2 is here to trigger `OrElse` further the line.
                    var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(any ? 2 : 1));
                    var modifiersList = modifiers.Split(",").Where(m => m != "any" && m != "none" && m != "not");

                    foreach (var term in modifiersList) {
                        var subexpression = Expression.Call(Expression.Property(score, "Modifiers"), contains, Expression.Constant(term));
                        if (not) {
                            exp = Expression.And(exp, Expression.Not(subexpression));
                        } else {
                            if (any) {
                                exp = Expression.OrElse(exp, subexpression);
                            } else {
                                exp = Expression.And(exp, subexpression);
                            }
                        }
                    }
                    scoreQuery = scoreQuery.Where((Expression<Func<PredictedScore, bool>>)Expression.Lambda(exp, score));
                } else {
                    scoreQuery = scoreQuery.Where(s => s.Modifiers.Length == 0);
                }
            }

            Order oppositeOrder = order.Reverse();

            switch (sortBy) {
                case LeaderboardSortBy.Date:
                    scoreQuery = scoreQuery.Order(order, s => s.Timepost).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Pp:
                    scoreQuery = scoreQuery.Order(order, s => s.Pp).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Acc:
                    scoreQuery = scoreQuery.Order(order, s => s.Accuracy);
                    break;
                case LeaderboardSortBy.Rank:
                    scoreQuery = leaderboard.Difficulty.Status.WithPP() 
                                ? scoreQuery
                                    .Order(order, el => Math.Round(el.Pp, 2))
                                    .ThenOrder(order, el => Math.Round(el.Accuracy, 4))
                                    .ThenOrder(oppositeOrder, el => el.Timepost)
                                : scoreQuery
                                    .Order(oppositeOrder, el => el.Priority)
                                    .ThenOrder(order, el => el.ModifiedScore)
                                    .ThenOrder(order, el => Math.Round(el.Accuracy, 4))
                                    .ThenOrder(oppositeOrder, el => el.Timepost);
                    break;
                case LeaderboardSortBy.Mistakes:
                    scoreQuery = scoreQuery.Order(order, s => s.BadCuts + s.MissedNotes + s.BombCuts + s.WallsHit);
                    break;
                case LeaderboardSortBy.Weight:
                    scoreQuery = scoreQuery.Order(order, s => s.Weight);
                    break;
                case LeaderboardSortBy.WeightedPp:
                    scoreQuery = scoreQuery.Order(order, s => s.Weight * s.Pp);
                    break;
                default:
                    break;
            }
            if (search != null) {
                string lowSearch = search.ToLower();
                scoreQuery = scoreQuery
                    .Where(s => s.Player.Name.ToLower().Contains(lowSearch) ||
                                s.Player.Clans.FirstOrDefault(c => c.Name.ToLower().Contains(lowSearch)) != null ||
                                s.Player.Clans.FirstOrDefault(c => c.Tag.ToLower().Contains(lowSearch)) != null);
            }
            using (_serverTiming.TimeAction("scorecount")) {
            leaderboard.Plays = await scoreQuery.CountAsync();
            }
            using (_serverTiming.TimeAction("scorelist")) {
            leaderboard.Scores = await scoreQuery
                .AsSplitQuery()
                .TagWithCaller()
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new ScoreResponse {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    Rank = s.Rank,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    FullCombo = s.FullCombo,
                    Timepost = s.Timepost,
                    AccPP = s.AccPP,
                    TechPP = s.TechPP,
                    PassPP = s.PassPP,
                    Weight = s.Weight,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
                    Player = new PlayerResponse {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Bot = s.Player.Bot,
                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player
                            .Clans
                            .OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id)
                            .Take(1)
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                })
                .ToListAsync();
            }
        }

        [NonAction]
        public async Task ContextScores(
            LeaderboardResponse leaderboard,
            LeaderboardContexts context,
            bool showBots,
            bool voters,
            bool showVoters,
            int page,
            int count,
            LeaderboardSortBy sortBy,
            Order order,
            ScoreFilterStatus scoreStatus,
            string? countries,
            string? search,
            string? modifiers,
            List<string>? friendsList,
            string? clanTag,
            string? hmd,
            bool offsets = false) {
            IQueryable<ScoreContextExtension> scoreQuery = _context
                .ScoreContextExtensions
                .AsNoTracking()
                .Include(s => s.ScoreInstance)
                .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context);

            if (countries == null) {
                if (friendsList != null) {
                    scoreQuery = scoreQuery.Where(s => (!s.ScoreInstance.Banned || (showBots && s.ScoreInstance.Bot)) && friendsList.Contains(s.PlayerId));
                } else if (voters) {
                    scoreQuery = scoreQuery.Where(s => (!s.ScoreInstance.Banned || (showBots && s.ScoreInstance.Bot)) && s.ScoreInstance.RankVoting != null);
                } else {
                    scoreQuery = scoreQuery.Where(s => (!s.ScoreInstance.Banned || (showBots && s.ScoreInstance.Bot)));
                }
            } else {
                if (friendsList != null) {
                    scoreQuery = scoreQuery.Where(s => (!s.ScoreInstance.Banned || (showBots && s.ScoreInstance.Bot)) && friendsList.Contains(s.PlayerId) && countries.ToLower().Contains(s.Player.Country.ToLower()));
                } else {
                    scoreQuery = scoreQuery.Where(s => (!s.ScoreInstance.Banned || (showBots && s.ScoreInstance.Bot)) && countries.ToLower().Contains(s.Player.Country.ToLower()));
                }
            }

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(ScoreContextExtension), "s");

                    var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                    var any = modifiers.Contains("any");
                    var not = modifiers.Contains("not");
                    // 1 != 2 is here to trigger `OrElse` further the line.
                    var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(any ? 2 : 1));
                    var modifiersList = modifiers.Split(",").Where(m => m != "any" && m != "none" && m != "not");

                    foreach (var term in modifiersList) {
                        var subexpression = Expression.Call(Expression.Property(score, "Modifiers"), contains, Expression.Constant(term));
                        if (not) {
                            exp = Expression.And(exp, Expression.Not(subexpression));
                        } else {
                            if (any) {
                                exp = Expression.OrElse(exp, subexpression);
                            } else {
                                exp = Expression.And(exp, subexpression);
                            }
                        }
                    }
                    scoreQuery = scoreQuery.Where((Expression<Func<ScoreContextExtension, bool>>)Expression.Lambda(exp, score));
                } else {
                    scoreQuery = scoreQuery.Where(s => s.Modifiers.Length == 0);
                }
            }

            if (hmd != null) {
                try {
                    var hmds = hmd.ToLower().Split(",").Select(s => (HMD)Int32.Parse(s));
                    scoreQuery = scoreQuery.Where(s => hmds.Contains(s.ScoreInstance.Hmd));
                } catch { }
            }

            Order oppositeOrder = order.Reverse();

            switch (sortBy) {
                case LeaderboardSortBy.Date:
                    scoreQuery = scoreQuery.Order(order, s => s.Timepost).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Pp:
                    scoreQuery = scoreQuery.Order(order, s => s.Pp).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Acc:
                    scoreQuery = scoreQuery.Order(order, s => s.Accuracy);
                    break;
                case LeaderboardSortBy.Pauses:
                    scoreQuery = scoreQuery.Order(order, s => s.ScoreInstance.Pauses).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Rank:
                    scoreQuery = scoreQuery.Order(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.MaxStreak:
                    scoreQuery = scoreQuery.Order(order, s => s.ScoreInstance.MaxStreak).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case LeaderboardSortBy.Mistakes:
                    scoreQuery = scoreQuery.Order(order, s => s.ScoreInstance.BadCuts + s.ScoreInstance.MissedNotes + s.ScoreInstance.BombCuts + s.ScoreInstance.WallsHit);
                    break;
                case LeaderboardSortBy.Weight:
                    scoreQuery = scoreQuery.Order(order, s => s.Weight);
                    break;
                case LeaderboardSortBy.WeightedPp:
                    scoreQuery = scoreQuery.Order(order, s => s.Weight * s.Pp);
                    break;
                default:
                    break;
            }
            switch (scoreStatus) {
                case ScoreFilterStatus.None:
                    break;
                case ScoreFilterStatus.Suspicious:
                    scoreQuery = scoreQuery.Where(s => s.ScoreInstance.Suspicious);
                    break;
                default:
                    break;
            }
            if (search != null) {
                string lowSearch = search.ToLower();
                scoreQuery = scoreQuery
                    .Where(s => s.Player.Name.ToLower().Contains(lowSearch) ||
                                s.Player.Clans.FirstOrDefault(c => c.Name.ToLower().Contains(lowSearch)) != null ||
                                s.Player.Clans.FirstOrDefault(c => c.Tag.ToLower().Contains(lowSearch)) != null);
            }
            if (clanTag != null) {
                scoreQuery = scoreQuery
                    .Where(s => s.Player.Clans.FirstOrDefault(c => c.Tag == clanTag.ToUpper()) != null);
            }

            leaderboard.Plays = await scoreQuery.CountAsync();
            leaderboard.Scores = await scoreQuery
                .AsSplitQuery()
                .TagWithCaller()
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new ScoreResponse {
                    Id = s.ScoreId ?? 0,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    Rank = s.Rank,
                    Modifiers = s.Modifiers,
                    BadCuts = s.ScoreInstance.BadCuts,
                    MissedNotes = s.ScoreInstance.MissedNotes,
                    BombCuts = s.ScoreInstance.BombCuts,
                    WallsHit = s.ScoreInstance.WallsHit,
                    Pauses = s.ScoreInstance.Pauses,
                    FullCombo = s.ScoreInstance.FullCombo,
                    Timeset = s.ScoreInstance.Timeset,
                    Timepost = s.ScoreInstance.Timepost,
                    MaxStreak = s.ScoreInstance.MaxStreak,
                    AccPP = s.AccPP,
                    TechPP = s.TechPP,
                    PassPP = s.PassPP,
                    Weight = s.Weight,
                    Hmd = s.ScoreInstance.Hmd,
                    Platform = s.ScoreInstance.Platform,
                    Controller = s.ScoreInstance.Controller,
                    FcAccuracy = s.ScoreInstance.FcAccuracy,
                    FcPp = s.ScoreInstance.FcPp,
                    Offsets = offsets ? s.ScoreInstance.ReplayOffsets : null,
                    Replay = offsets ? s.ScoreInstance.Replay : null,
                    Player = new PlayerResponse {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Bot = s.Player.Bot,
                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player.Clans.OrderBy(c => s.Player
                            .ClanOrder
                            .IndexOf(c.Tag))
                            .ThenBy(c => c.Id)
                            .Take(1)
                            .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    RankVoting = showVoters ? s.ScoreInstance.RankVoting : null,
                })
                .ToListAsync();
        }

        [HttpGet("~/leaderboard/{id}")]
        [SwaggerOperation(Summary = "Retrieve leaderboard details", Description = "Fetches details of a leaderboard identified by its ID, with optional sorting and filtering for scores.")]
        [SwaggerResponse(200, "Leaderboard details retrieved successfully", typeof(LeaderboardResponse))]
        [SwaggerResponse(404, "Leaderboard not found")]
        public async Task<ActionResult<LeaderboardResponse>> Get(
            [SwaggerParameter("ID of the leaderboard to retrieve details for")] string id,
            [FromQuery, SwaggerParameter("Scores page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort scores by, default is Rank")] LeaderboardSortBy sortBy = LeaderboardSortBy.Rank,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Filter for score status, default is None")] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is General")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Filter for specific countries (country code, comma separated)")] string? countries = null,
            [FromQuery, SwaggerParameter("Search term to filter scores by player name or clan")] string? search = null,
            [FromQuery, SwaggerParameter("Modifiers to filter scores by (comma separated)")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Whether to include only scores from friends, default is false")] bool friends = false,
            [FromQuery, SwaggerParameter("Whether to include only scores from voters, default is false")] bool voters = false,
            [FromQuery, SwaggerParameter("Whether to include only scores from clan, default is false")] string? clanTag = null,
            [FromQuery, SwaggerParameter("Comma-separated range to filter by hmd (headset), default is null")] string? hmds = null,
            [FromQuery, SwaggerParameter("Whether to include predicted scores, default is false")] bool prediction = false) {

            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID)
                .Select(p => new { 
                    p.Role, 
                    ShowBots = p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false, 
                    ShowAllRatings = p.ProfileSettings != null ? p.ProfileSettings.ShowAllRatings : false, 
                    p.MapperId })
                .FirstOrDefaultAsync() : null;

            bool showBots = currentPlayer?.ShowBots ?? false;

            bool isRt = (currentPlayer != null &&
                            (currentPlayer.Role.Contains("admin") ||
                             currentPlayer.Role.Contains("rankedteam") ||
                             currentPlayer.Role.Contains("qualityteam")));

            IQueryable<Leaderboard> query = _context
                    .Leaderboards
                    .AsNoTracking()
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(lb => lb.Votes)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Modifiers)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.CriteriaComments)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.NewModifiers)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.OldModifiers)
                    .Include(lb => lb.Reweight)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.NewModifiers)
                    .Include(lb => lb.Reweight)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.OldModifiers)
                    .Include(lb => lb.Reweight)
                    .ThenInclude(q => q.Modifiers)
                    .Include(lb => lb.Changes)
                    .ThenInclude(ch => ch.NewModifiers)
                    .Include(lb => lb.Changes)
                    .ThenInclude(ch => ch.OldModifiers)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.ExternalStatuses)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Mappers)
                    .Include(lb => lb.LeaderboardGroup)
                    .ThenInclude(g => g.Leaderboards)
                    .ThenInclude(glb => glb.Difficulty);
                    //.Include(lb => lb.FeaturedPlaylists);


            LeaderboardResponse? leaderboard;
            using (_serverTiming.TimeAction("leaderboard")) {
                leaderboard = await query
                .AsNoTracking()
                .AsSplitQuery()
                .Select(l => new LeaderboardResponse {
                Id = l.Id,
                Song = new SongResponse {
                    Id = l.Song.Id,
                    Hash = l.Song.Hash,
                    Name = l.Song.Name,
                    SubName = l.Song.SubName,
                    Author = l.Song.Author,
                    Mapper = l.Song.Mapper,
                    MapperId  = l.Song.MapperId,
                    CoverImage   = l.Song.CoverImage,
                    FullCoverImage = l.Song.FullCoverImage,
                    DownloadUrl = l.Song.DownloadUrl,
                    Bpm = l.Song.Bpm,
                    Duration = l.Song.Duration,
                    UploadTime = l.Song.UploadTime,
                    Explicity = l.Song.Explicity,
                    Mappers = l.Song != null ? l.Song.Mappers.Select(m => new MapperResponse {
                        Id = m.Id,
                        PlayerId = m.Player != null ? m.Player.Id : null,
                        Name = m.Player != null ? m.Player.Name : m.Name,
                        Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                        Curator = m.Curator,
                        VerifiedMapper = m.VerifiedMapper,
                    }).ToList() : null,
                    Difficulties = l.Song.Difficulties,
                    ExternalStatuses = l.Song.ExternalStatuses,
                },
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
                },
                Plays = l.Plays,
                Qualification = l.Qualification,
                Reweight = l.Reweight,
                Changes = l.Changes,
                ClanRankingContested = l.ClanRankingContested,
                Clan = l.Clan == null ? null : new ClanResponseFull {
                    Id = l.Clan.Id,
                    Name = l.Clan.Name,
                    Color = l.Clan.Color,
                    Icon = l.Clan.Icon,
                    Tag = l.Clan.Tag,
                    LeaderID = l.Clan.LeaderID,
                    Description = l.Clan.Description,
                    Pp = l.Clan.Pp,
                    Rank = l.Clan.Rank
                },
                FeaturedPlaylists = l.FeaturedPlaylists,
                LeaderboardGroup = l.LeaderboardGroup.Leaderboards.Select(it =>
                    new LeaderboardGroupEntry {
                        Id = it.Id,
                        Status = it.Difficulty.Status,
                        Timestamp = it.Timestamp
                    }
                   ),
            })
               .FirstOrDefaultAsync();
            }

            if (leaderboard != null) {

                if (leaderboard.Qualification != null && (isRt || leaderboard.Song.MapperId == currentPlayer?.MapperId)) {
                    leaderboard.Qualification.Comments = await _context.QualificationCommentary.Where(c => c.RankQualificationId == leaderboard.Qualification.Id).ToListAsync();
                }

                bool showRatings = currentPlayer?.ShowAllRatings ?? false;

                if (!showRatings) {
                    if (!leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    }

                    if (leaderboard.Song?.Difficulties != null) {
                        foreach (var diff in leaderboard.Song.Difficulties) {
                            if (!diff.Status.WithRating()) {
                                diff.HideRatings();
                            }
                        }
                    }
                }

                bool showVoters = false;
                if (voters) {
                    if (isRt) {
                        showVoters = true;
                    } else if (currentPlayer?.MapperId != 0 && leaderboard.Song.MapperId == currentPlayer.MapperId) {
                        showVoters = true;
                    }
                }

                List<string>? friendsList = null;

                if (friends) {
                    if (currentID == null) {
                        return NotFound();
                    }
                    using (_serverTiming.TimeAction("friends")) {
                    var friendsContainer = await _context
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => f.Friends.Select(fs => fs.Id))
                        .FirstOrDefaultAsync();
                    if (friendsContainer != null) {
                        friendsList = friendsContainer.ToList();
                        friendsList.Add(currentID);
                    } else {
                        friendsList = new List<string> { currentID };
                    }
                    }
                }

                if (prediction) {
                    await PredictedScores(leaderboard, showBots, voters, showVoters, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList);
                } else {
                    if (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None) {
                        await GeneralScores(leaderboard, showBots, voters, showVoters, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList, clanTag, hmds);
                    } else {
                        await ContextScores(leaderboard, leaderboardContext, showBots, voters, showVoters, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList, clanTag, hmds);
                    }
                }

                foreach (var score in leaderboard.Scores) {
                    score.Player = PostProcessSettings(score.Player, false);
                }
            }

            if (leaderboard == null) {
                Song? song = await _context.Songs.Include(s => s.Difficulties).FirstOrDefaultAsync(s => s.Id == id);
                if (song == null) {
                    return NotFound();
                } else {
                    DifficultyDescription? difficulty = song.Difficulties.OrderByDescending(d => d.Value).FirstOrDefault();

                    return difficulty == null ? NotFound() : await Get(song.Id + difficulty.Value + difficulty.Mode, page, count, sortBy, order, scoreStatus, leaderboardContext, countries, search, modifiers, friends, voters);
                }
            } else if (leaderboard.Difficulty.Status == DifficultyStatus.nominated && isRt) {
                var qualification = leaderboard.Qualification;
                var recalculated = leaderboard.Scores.Select(s => {

                    s.ModifiedScore = (int)(s.BaseScore * qualification.Modifiers.GetNegativeMultiplier(s.Modifiers, true));

                    if (leaderboard.Difficulty.MaxScore > 0) {
                        s.Accuracy = (float)s.BaseScore / (float)leaderboard.Difficulty.MaxScore;
                    } else {
                        s.Accuracy = (float)s.BaseScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                    }

                    (s.Pp, s.BonusPp, s.PassPP, s.AccPP, s.TechPP) = ReplayUtils.PpFromScoreResponse(
                        s,
                        leaderboard.Difficulty.AccRating ?? 0,
                        leaderboard.Difficulty.PassRating ?? 0,
                        leaderboard.Difficulty.TechRating ?? 0,
                        leaderboard.Difficulty.ModifierValues,
                        leaderboard.Difficulty.ModifiersRating
                        );

                    return s;
                }).ToList();

                var rankedScores = recalculated.OrderByDescending(el => el.Pp).ToList();
                foreach ((int i, ScoreResponse s) in rankedScores.Select((value, i) => (i, value))) {
                    s.ResponseRank = i + 1 + ((page - 1) * count);
                }

                leaderboard.Scores = recalculated;
            }

            for (int i = 0; i < leaderboard.Scores?.Count; i++) {
                leaderboard.Scores[i].ResponseRank = i + (page - 1) * count + 1;
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboard/scores/{id}")]
        public async Task<ActionResult<LeaderboardResponse>> GetScores(
            string id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] LeaderboardSortBy sortBy = LeaderboardSortBy.Rank,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? countries = null,
            [FromQuery] string? search = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] string? hmds = null,
            [FromQuery] bool friends = false,
            [FromQuery] bool voters = false) {

            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID)
                .Select(p => new { 
                    p.Role, 
                    ShowBots = p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false, 
                    ShowAllRatings = p.ProfileSettings != null ? p.ProfileSettings.ShowAllRatings : false, 
                    p.MapperId })
                .FirstOrDefaultAsync() : null;

            bool showBots = currentPlayer?.ShowBots ?? false;

            IQueryable<Leaderboard> query = _context
                    .Leaderboards
                    .AsNoTracking()
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating);


            LeaderboardResponse? leaderboard;
            using (_serverTiming.TimeAction("leaderboard")) {
                leaderboard = await query
                .AsSplitQuery()
                .Select(l => new LeaderboardResponse {
                Id = l.Id,
                Song = new SongResponse {
                    Id = l.Song.Id,
                    Hash = l.Song.Hash,
                    Name = l.Song.Name,
                    SubName = l.Song.SubName,
                    Author = l.Song.Author,
                    Mapper = l.Song.Mapper,
                    MapperId  = l.Song.MapperId,
                    CoverImage   = l.Song.CoverImage,
                    FullCoverImage = l.Song.FullCoverImage,
                    DownloadUrl = l.Song.DownloadUrl,
                    Bpm = l.Song.Bpm,
                    Duration = l.Song.Duration,
                    UploadTime = l.Song.UploadTime,
                    Explicity = l.Song.Explicity,
                    Mappers = l.Song.Mappers != null ? l.Song.Mappers.Select(m => new MapperResponse {
                        Id = m.Id,
                        PlayerId = m.Player != null ? m.Player.Id : null,
                        Name = m.Player != null ? m.Player.Name : m.Name,
                        Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                        Curator = m.Curator,
                        VerifiedMapper = m.VerifiedMapper,
                    }).ToList() : null,
                    Difficulties = l.Song.Difficulties,
                    ExternalStatuses = l.Song.ExternalStatuses,
                },
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
                },
                Plays = l.Plays,
                })
               .FirstOrDefaultAsync();
            }

            if (leaderboard != null) {

                bool showRatings = currentPlayer?.ShowAllRatings ?? false;
                if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                    leaderboard.HideRatings();
                }

                List<string>? friendsList = null;

                if (friends) {
                    if (currentID == null) {
                        return NotFound();
                    }
                    using (_serverTiming.TimeAction("friends")) {
                    var friendsContainer = await _context
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => f.Friends.Select(fs => fs.Id))
                        .FirstOrDefaultAsync();
                    if (friendsContainer != null) {
                        friendsList = friendsContainer.ToList();
                        friendsList.Add(currentID);
                    } else {
                        friendsList = new List<string> { currentID };
                    }
                    }
                }

                if (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None) {
                    await GeneralScores(leaderboard, showBots, voters, false, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList, null, hmds, true);
                } else {
                    await ContextScores(leaderboard, leaderboardContext, showBots, voters, false, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList, null, hmds, true);
                }

                foreach (var score in leaderboard.Scores) {
                    score.Player = PostProcessSettings(score.Player, false);
                }
            }

            for (int i = 0; i < leaderboard.Scores?.Count; i++) {
                leaderboard.Scores[i].ResponseRank = i + (page - 1) * count + 1;
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboard/clanRankings/{leaderboardId}/clan/{clanId}")]
        public async Task<ActionResult<ClanRankingResponse>> GetClanRankingAssociatedScoresClanId(
            string leaderboardId,
            int clanId,
            [FromQuery] int page = 1,
            [FromQuery] int count = 5)
        {
            var clanRankingScores = await _context
                .ClanRanking
                .AsNoTracking()
                .Where(cr => cr.LeaderboardId == leaderboardId && cr.ClanId == clanId)
                .Select(cr => new ClanRankingResponse
                {
                    Id = cr.Id,
                    Clan = cr.Clan == null ? null : new ClanResponseFull {
                        Id = cr.Clan.Id,
                        Name = cr.Clan.Name,
                        Color = cr.Clan.Color,
                        Icon = cr.Clan.Icon,
                        Tag = cr.Clan.Tag,
                        LeaderID = cr.Clan.LeaderID,
                        Description = cr.Clan.Description,
                        Pp = cr.Clan.Pp,
                        Rank = cr.Clan.Rank
                    },
                    LastUpdateTime = cr.LastUpdateTime,
                    AverageRank = cr.AverageRank,
                    Pp = cr.Pp,
                    AverageAccuracy = cr.AverageAccuracy,
                    TotalScore = cr.TotalScore,
                    LeaderboardId = cr.LeaderboardId,
                    Leaderboard = new LeaderboardResponse {
                        Id = cr.Leaderboard.Id,
                        Song = new SongResponse {
                            Id = cr.Leaderboard.Song.Id,
                            Hash = cr.Leaderboard.Song.Hash,
                            Name = cr.Leaderboard.Song.Name,
                            SubName = cr.Leaderboard.Song.SubName,
                            Author = cr.Leaderboard.Song.Author,
                            Mapper = cr.Leaderboard.Song.Mapper,
                            CoverImage  = cr.Leaderboard.Song.CoverImage,
                            FullCoverImage = cr.Leaderboard.Song.FullCoverImage,
                            DownloadUrl = cr.Leaderboard.Song.DownloadUrl,
                            Explicity = cr.Leaderboard.Song.Explicity
                        },
                        Difficulty = new DifficultyResponse {
                            Id = cr.Leaderboard.Difficulty.Id,
                            Value = cr.Leaderboard.Difficulty.Value,
                            Mode = cr.Leaderboard.Difficulty.Mode,
                            DifficultyName = cr.Leaderboard.Difficulty.DifficultyName,
                            ModeName = cr.Leaderboard.Difficulty.ModeName,
                            Status = cr.Leaderboard.Difficulty.Status,
                            ModifierValues = cr.Leaderboard.Difficulty.ModifierValues,
                            ModifiersRating = cr.Leaderboard.Difficulty.ModifiersRating,

                            Stars  = cr.Leaderboard.Difficulty.Stars,
                            PredictedAcc  = cr.Leaderboard.Difficulty.PredictedAcc,
                            PassRating  = cr.Leaderboard.Difficulty.PassRating,
                            AccRating  = cr.Leaderboard.Difficulty.AccRating,
                            TechRating  = cr.Leaderboard.Difficulty.TechRating,
                            Type  = cr.Leaderboard.Difficulty.Type,
                            MaxScore = cr.Leaderboard.Difficulty.MaxScore,
                        },
                        Plays = cr.Leaderboard.Plays,
                    },
                    AssociatedScores = _context
                        .Scores
                        .Where(s => 
                            s.LeaderboardId == leaderboardId && 
                            s.ValidForGeneral && 
                            !s.Banned &&
                            s.Player.Clans.OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id).Take(1).Contains(cr.Clan))
                        .Include(sc => sc.Player)
                        .ThenInclude(p => p.ProfileSettings)
                        .Include(s => s.Player)
                        .ThenInclude(s => s.Clans)
                        .OrderByDescending(s => s.Pp)
                        .Skip((page - 1) * count)
                        .Take(count)
                        .Select(s => new ScoreResponse
                        {
                            Id = s.Id,
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            PlayerId = s.PlayerId,
                            Accuracy = s.Accuracy,
                            Pp = s.Pp,
                            Rank = s.Rank,
                            Modifiers = s.Modifiers,
                            BadCuts = s.BadCuts,
                            MissedNotes = s.MissedNotes,
                            BombCuts = s.BombCuts,
                            WallsHit = s.WallsHit,
                            Pauses = s.Pauses,
                            FullCombo = s.FullCombo,
                            Timeset = s.Timeset,
                            Timepost = s.Timepost,
                            Hmd = s.Hmd,
                            Player = new PlayerResponse
                            {
                                Id = s.Player.Id,
                                Name = s.Player.Name,
                                Alias = s.Player.Alias,
                                Avatar = s.Player.Avatar,
                                Country = s.Player.Country,
                                Pp = s.Player.Pp,
                                Rank = s.Player.Rank,
                                CountryRank = s.Player.CountryRank,
                                Role = s.Player.Role,
                                ProfileSettings = s.Player.ProfileSettings,
                                Clans = s.Player.Clans.OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id).Take(1)
                                    .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                            },
                            RankVoting = null,
                        })
                        .ToList(),
                    AssociatedScoresCount = _context
                        .Scores
                        .Where(sc => 
                            sc.LeaderboardId == leaderboardId && 
                            sc.ValidForGeneral &&
                            !sc.Banned &&
                            sc.Player.Clans.OrderBy(c => ("," + sc.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + sc.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id).Take(1).Contains(cr.Clan))
                        .Count()
                })
                .FirstOrDefaultAsync();

            return clanRankingScores;
        }

        [HttpGet("~/leaderboard/clanRankings/{leaderboardId}/{clanRankingId}")]
        public async Task<ActionResult<ClanRankingResponse>> GetClanRankingAssociatedScores(
            string leaderboardId,
            int clanRankingId,
            [FromQuery] int page = 1,
            [FromQuery] int count = 5)
        {
            var clanRankingScores = await _context
                .ClanRanking
                .AsNoTracking()
                .Where(cr => cr.LeaderboardId == leaderboardId && cr.Id == clanRankingId)
                .Select(cr => new ClanRankingResponse
                {
                    Id = cr.Id,
                    Clan = cr.Clan == null ? null : new ClanResponseFull {
                        Id = cr.Clan.Id,
                        Name = cr.Clan.Name,
                        Color = cr.Clan.Color,
                        Icon = cr.Clan.Icon,
                        Tag = cr.Clan.Tag,
                        LeaderID = cr.Clan.LeaderID,
                        Description = cr.Clan.Description,
                        Pp = cr.Clan.Pp,
                        Rank = cr.Clan.Rank
                    },
                    LastUpdateTime = cr.LastUpdateTime,
                    AverageRank = cr.AverageRank,
                    Pp = cr.Pp,
                    AverageAccuracy = cr.AverageAccuracy,
                    TotalScore = cr.TotalScore,
                    LeaderboardId = cr.LeaderboardId,
                    Leaderboard = new LeaderboardResponse {
                        Id = cr.Leaderboard.Id,
                        Song = new SongResponse {
                            Id = cr.Leaderboard.Song.Id,
                            Hash = cr.Leaderboard.Song.Hash,
                            Name = cr.Leaderboard.Song.Name,
                            SubName = cr.Leaderboard.Song.SubName,
                            Author = cr.Leaderboard.Song.Author,
                            Mapper = cr.Leaderboard.Song.Mapper,
                            CoverImage  = cr.Leaderboard.Song.CoverImage,
                            FullCoverImage = cr.Leaderboard.Song.FullCoverImage,
                            Explicity = cr.Leaderboard.Song.Explicity
                        },
                        Difficulty = new DifficultyResponse {
                            Id = cr.Leaderboard.Difficulty.Id,
                            Value = cr.Leaderboard.Difficulty.Value,
                            Mode = cr.Leaderboard.Difficulty.Mode,
                            DifficultyName = cr.Leaderboard.Difficulty.DifficultyName,
                            ModeName = cr.Leaderboard.Difficulty.ModeName,
                            Status = cr.Leaderboard.Difficulty.Status,
                            ModifierValues = cr.Leaderboard.Difficulty.ModifierValues,
                            ModifiersRating = cr.Leaderboard.Difficulty.ModifiersRating,

                            Stars  = cr.Leaderboard.Difficulty.Stars,
                            PredictedAcc  = cr.Leaderboard.Difficulty.PredictedAcc,
                            PassRating  = cr.Leaderboard.Difficulty.PassRating,
                            AccRating  = cr.Leaderboard.Difficulty.AccRating,
                            TechRating  = cr.Leaderboard.Difficulty.TechRating,
                            Type  = cr.Leaderboard.Difficulty.Type,
                            MaxScore = cr.Leaderboard.Difficulty.MaxScore,
                        },
                        Plays = cr.Leaderboard.Plays,
                    },
                    AssociatedScores = _context
                        .Scores
                        .Where(s => 
                            s.LeaderboardId == leaderboardId && 
                            s.ValidForGeneral && 
                            !s.Banned &&
                            s
                             .Player
                             .Clans
                             .OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                             .ThenBy(c => c.Id)
                             .Take(1)
                             .Contains(cr.Clan))
                        .Include(sc => sc.Player)
                        .ThenInclude(p => p.ProfileSettings)
                        .Include(s => s.Player)
                        .ThenInclude(s => s.Clans)
                        .OrderByDescending(s => s.Pp)
                        .Skip((page - 1) * count)
                        .Take(count)
                        .Select(s => new ScoreResponse
                        {
                            Id = s.Id,
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            PlayerId = s.PlayerId,
                            Accuracy = s.Accuracy,
                            Pp = s.Pp,
                            Rank = s.Rank,
                            Modifiers = s.Modifiers,
                            BadCuts = s.BadCuts,
                            MissedNotes = s.MissedNotes,
                            BombCuts = s.BombCuts,
                            WallsHit = s.WallsHit,
                            Pauses = s.Pauses,
                            FullCombo = s.FullCombo,
                            Timeset = s.Timeset,
                            Timepost = s.Timepost,
                            Hmd = s.Hmd,
                            Player = new PlayerResponse
                            {
                                Id = s.Player.Id,
                                Name = s.Player.Name,
                                Alias = s.Player.Alias,
                                Avatar = s.Player.Avatar,
                                Country = s.Player.Country,
                                Pp = s.Player.Pp,
                                Rank = s.Player.Rank,
                                CountryRank = s.Player.CountryRank,
                                Role = s.Player.Role,
                                ProfileSettings = s.Player.ProfileSettings,
                                Clans = s.Player.Clans.OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id).Take(1)
                                    .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                            },
                            RankVoting = null,
                        })
                        .ToList(),
                    AssociatedScoresCount = _context
                        .Scores
                        .Where(sc => 
                            sc.LeaderboardId == leaderboardId && 
                            sc.ValidForGeneral && 
                            !sc.Banned &&
                            sc.Player.Clans.OrderBy(c => ("," + sc.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + sc.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                            .ThenBy(c => c.Id).Take(1).Contains(cr.Clan))
                        .Count()
                })
                .FirstOrDefaultAsync();

            return clanRankingScores;
        }


        [HttpGet("~/leaderboard/clanRankings/{id}")]
        [SwaggerOperation(Summary = "Retrieve clan rankings for a leaderboard", Description = "Fetches clan rankings for a leaderboard identified by its ID.")]
        [SwaggerResponse(200, "Clan rankings retrieved successfully", typeof(LeaderboardClanRankingResponse))]
        [SwaggerResponse(404, "Leaderboard not found")]
        public async Task<ActionResult<LeaderboardClanRankingResponse>> GetClanRankings(
            [SwaggerParameter("ID of the leaderboard to retrieve clan rankings for")] string id,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of rankings per page, default is 10")] int count = 10) {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID)
                .Select(p => new {
                    p.Role,
                    ShowBots = p.ProfileSettings != null ? p.ProfileSettings.ShowBots : false,
                    ShowAllRatings = p.ProfileSettings != null ? p.ProfileSettings.ShowAllRatings : false,
                    p.MapperId
                })
                .FirstOrDefaultAsync() : null;

            bool showBots = currentPlayer?.ShowBots ?? false;

            bool isRt = (currentPlayer != null &&
                            (currentPlayer.Role.Contains("admin") ||
                             currentPlayer.Role.Contains("rankedteam") ||
                             currentPlayer.Role.Contains("qualityteam")));

            IQueryable<Leaderboard> query = _context
                    .Leaderboards
                    .AsNoTracking()
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(lb => lb.Votes)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Modifiers)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.CriteriaComments)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.NewModifiers)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.OldModifiers)
                    .Include(lb => lb.Reweight)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.NewModifiers)
                    .Include(lb => lb.Reweight)
                    .ThenInclude(q => q.Changes)
                    .ThenInclude(ch => ch.OldModifiers)
                    .Include(lb => lb.Reweight)
                    .ThenInclude(q => q.Modifiers)
                    .Include(lb => lb.Changes)
                    .ThenInclude(ch => ch.NewModifiers)
                    .Include(lb => lb.Changes)
                    .ThenInclude(ch => ch.OldModifiers)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties)
                    .Include(lb => lb.LeaderboardGroup)
                    .ThenInclude(g => g.Leaderboards)
                    .ThenInclude(glb => glb.Difficulty);


            LeaderboardClanRankingResponse? leaderboard;
            using (_serverTiming.TimeAction("leaderboard"))
            {
                leaderboard = await query
                .AsSplitQuery()
                .Select(l => new LeaderboardClanRankingResponse
                {
                    Id = l.Id,
                    Song = new SongResponse {
                        Id = l.Song.Id,
                        Hash = l.Song.Hash,
                        Name = l.Song.Name,
                        SubName = l.Song.SubName,
                        Author = l.Song.Author,
                        Mapper = l.Song.Mapper,
                        MapperId  = l.Song.MapperId,
                        CoverImage   = l.Song.CoverImage,
                        FullCoverImage = l.Song.FullCoverImage,
                        DownloadUrl = l.Song.DownloadUrl,
                        Bpm = l.Song.Bpm,
                        Duration = l.Song.Duration,
                        UploadTime = l.Song.UploadTime,
                        Explicity = l.Song.Explicity,
                        Mappers = l.Song.Mappers != null ? l.Song.Mappers.Select(m => new MapperResponse {
                            Id = m.Id,
                            PlayerId = m.Player != null ? m.Player.Id : null,
                            Name = m.Player != null ? m.Player.Name : m.Name,
                            Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                            Curator = m.Curator,
                            VerifiedMapper = m.VerifiedMapper,
                        }).ToList() : null,
                        Difficulties = l.Song.Difficulties,
                        ExternalStatuses = l.Song.ExternalStatuses,
                    },
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
                    },
                    Plays = l.Plays,
                    Qualification = l.Qualification,
                    Reweight = l.Reweight,
                    Changes = l.Changes,
                    ClanRankingContested = l.ClanRankingContested,
                    Clan = l.Clan == null ? null : new ClanResponseFull {
                        Id = l.Clan.Id,
                        Name = l.Clan.Name,
                        Color = l.Clan.Color,
                        Icon = l.Clan.Icon,
                        Tag = l.Clan.Tag,
                        LeaderID = l.Clan.LeaderID,
                        Description = l.Clan.Description,
                        Pp = l.Clan.Pp,
                        Rank = l.Clan.Rank
                    },
                    LeaderboardGroup = l.LeaderboardGroup.Leaderboards.Select(it =>
                        new LeaderboardGroupEntry
                        {
                            Id = it.Id,
                            Status = it.Difficulty.Status,
                            Timestamp = it.Timestamp
                        }
                   ),
                })
               .FirstOrDefaultAsync();
            }

            if (leaderboard != null)
            {
                if (leaderboard.Qualification != null && (isRt || leaderboard.Song.MapperId == currentPlayer?.MapperId))
                {
                    leaderboard.Qualification.Comments = await _context.QualificationCommentary.Where(c => c.RankQualificationId == leaderboard.Qualification.Id).ToListAsync();
                }

                bool showRatings = currentPlayer?.ShowAllRatings ?? false;
                if (!showRatings && !leaderboard.Difficulty.Status.WithRating())
                {
                    leaderboard.HideRatings();
                }

                var clanRankingQuery = await _context.ClanRanking.Where(s => s.LeaderboardId == leaderboard.Id).ToListAsync();

                leaderboard.Plays = clanRankingQuery.Count();
                leaderboard.Scores = null;

                // This clanRanking data is required for displaying clanRankings on each leaderboard
                leaderboard.ClanRanking = await _context
                    .ClanRanking
                    .AsNoTracking()
                    .Include(cr => cr.Clan)
                    .Where(cr => cr.LeaderboardId == leaderboard.Id)
                    .OrderByDescending(cr => cr.Pp)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(cr => new ClanRankingResponse
                    {
                        Id = cr.Id,
                        Clan = cr.Clan == null ? null : new ClanResponseFull {
                            Id = cr.Clan.Id,
                            Name = cr.Clan.Name,
                            Color = cr.Clan.Color,
                            Icon = cr.Clan.Icon,
                            Tag = cr.Clan.Tag,
                            LeaderID = cr.Clan.LeaderID,
                            Description = cr.Clan.Description,
                            Pp = cr.Clan.Pp,
                            Rank = cr.Clan.Rank
                        },
                        LastUpdateTime = cr.LastUpdateTime,
                        AverageRank = cr.AverageRank,
                        Rank = cr.Rank,
                        Pp = cr.Pp,
                        AverageAccuracy = cr.AverageAccuracy,
                        TotalScore = cr.TotalScore,
                        LeaderboardId = cr.LeaderboardId,
                        Leaderboard = new LeaderboardResponse {
                            Id = cr.Leaderboard.Id,
                            Song = new SongResponse {
                                Id = cr.Leaderboard.Song.Id,
                                Hash = cr.Leaderboard.Song.Hash,
                                Name = cr.Leaderboard.Song.Name,
                                SubName = cr.Leaderboard.Song.SubName,
                                Author = cr.Leaderboard.Song.Author,
                                Mapper = cr.Leaderboard.Song.Mapper,
                                CoverImage  = cr.Leaderboard.Song.CoverImage,
                                FullCoverImage = cr.Leaderboard.Song.FullCoverImage,
                                Explicity = cr.Leaderboard.Song.Explicity
                            },
                            Difficulty = new DifficultyResponse {
                                Id = cr.Leaderboard.Difficulty.Id,
                                Value = cr.Leaderboard.Difficulty.Value,
                                Mode = cr.Leaderboard.Difficulty.Mode,
                                DifficultyName = cr.Leaderboard.Difficulty.DifficultyName,
                                ModeName = cr.Leaderboard.Difficulty.ModeName,
                                Status = cr.Leaderboard.Difficulty.Status,
                                ModifierValues = cr.Leaderboard.Difficulty.ModifierValues,
                                ModifiersRating = cr.Leaderboard.Difficulty.ModifiersRating,

                                Stars  = cr.Leaderboard.Difficulty.Stars,
                                PredictedAcc  = cr.Leaderboard.Difficulty.PredictedAcc,
                                PassRating  = cr.Leaderboard.Difficulty.PassRating,
                                AccRating  = cr.Leaderboard.Difficulty.AccRating,
                                TechRating  = cr.Leaderboard.Difficulty.TechRating,
                                Type  = cr.Leaderboard.Difficulty.Type,
                                MaxScore = cr.Leaderboard.Difficulty.MaxScore,
                            },
                            Plays = cr.Leaderboard.Plays,
                        },
                        AssociatedScores = null,
                        AssociatedScoresCount = 0
                    })
                    .ToListAsync();
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/hash/{hash}")]
        public async Task<ActionResult<LeaderboardsResponseWithScores>> GetLeaderboardsByHash(
            string hash,
            [FromQuery] bool my_scores = false) {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            string? currentID = HttpContext.CurrentUserID(_context);

            var song = await _context
                .Songs
                .AsNoTracking()
                .Where(s => s.Hash == hash)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .AsSplitQuery()
                .FirstOrDefaultAsync();

            if (song == null) {
                return NotFound();
            }

            var query = _context
            .Leaderboards
            .AsNoTracking()
            .Where(lb => lb.SongId == song.Id);

            if (my_scores && currentID != null) {
                query = query.Include(lb => lb.Scores.Where(s => s.PlayerId == currentID));
            }

            var resultList = await query
            .Include(lb => lb.Qualification)
            .AsSplitQuery()
            .Select(lb => new LeaderboardsInfoResponseWithScore {
                Id = lb.Id,
                Qualification = lb.Qualification,
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
                },
                Clan = lb.Clan == null ? null : new ClanResponseFull {
                    Id = lb.Clan.Id,
                    Name = lb.Clan.Name,
                    Color = lb.Clan.Color,
                    Icon = lb.Clan.Icon,
                    Tag = lb.Clan.Tag,
                    LeaderID = lb.Clan.LeaderID,
                    Description = lb.Clan.Description,
                    Pp = lb.Clan.Pp,
                    Rank = lb.Clan.Rank
                },
                ClanRankingContested = lb.ClanRankingContested,
                MyScore = my_scores && currentID != null 
                    ? lb.Scores.AsQueryable().Where(s => s.PlayerId == currentID && s.ValidForGeneral).Select(ScoreResponseQuery.SelectWithAcc()).FirstOrDefault()
                    : null
            }).ToListAsync();

            if (resultList.Count > 0) {
                Player? currentPlayer = currentID != null 
                    ? await _context.Players.Include(p => p.ProfileSettings).FirstOrDefaultAsync(p => p.Id == currentID) 
                    : null;

                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var leaderboard in resultList) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    } else {
                        leaderboard.RemoveSpeedMultipliers();
                    }
                }
            }

            return new LeaderboardsResponseWithScores {
                Song = song,
                Leaderboards = resultList
            };
        }

        [HttpGet("~/leaderboards/")]
        [SwaggerOperation(Summary = "Retrieve a list of leaderboards (maps)", Description = "Fetches a paginated and optionally filtered list of leaderboards (Beat Saber maps).")]
        [SwaggerResponse(200, "Leaderboards retrieved successfully", typeof(ResponseWithMetadata<LeaderboardInfoResponse>))]
        [SwaggerResponse(404, "Leaderboards not found")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> GetAll(
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of leaderboards per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort leaderboards by, default is None")] MapSortBy sortBy = MapSortBy.None,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Search term to filter leaderboards by song, author or mapper name")] string? search = null,
            [FromQuery, SwaggerParameter("Type of leaderboards to filter, default is All")] Type type = Type.All,
            [FromQuery, SwaggerParameter("Mode to filter leaderboards by (Standard, OneSaber, etc...)")] string? mode = null,
            [FromQuery, SwaggerParameter("Difficulty to filter leaderboards by (Easy, Normal, Hard, Expert, ExpertPlus)")] string? difficulty = null,
            [FromQuery, SwaggerParameter("Map type to filter leaderboards by")] int? mapType = null,
            [FromQuery, SwaggerParameter("Operation to filter all types, default is Any")] Operation allTypes = Operation.Any,
            [FromQuery, SwaggerParameter("Requirements to filter leaderboards by, default is Ignore")] Requirements mapRequirements = Requirements.Ignore,
            [FromQuery, SwaggerParameter("Operation to filter all requirements, default is Any")] Operation allRequirements = Operation.Any,
            [FromQuery, SwaggerParameter("Song status to filter leaderboards by, default is None")] SongStatus songStatus = SongStatus.None,
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is General")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Map creator, human by default")] SongCreator mapCreator = SongCreator.Human,
            [FromQuery, SwaggerParameter("My type to filter leaderboards by, default is None")] MyType mytype = MyType.None,
            [FromQuery, SwaggerParameter("Minimum stars to filter leaderboards by")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Maximum stars to filter leaderboards by")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Minimum accuracy rating to filter leaderboards by")] float? accrating_from = null,
            [FromQuery, SwaggerParameter("Maximum accuracy rating to filter leaderboards by")] float? accrating_to = null,
            [FromQuery, SwaggerParameter("Minimum pass rating to filter leaderboards by")] float? passrating_from = null,
            [FromQuery, SwaggerParameter("Maximum pass rating to filter leaderboards by")] float? passrating_to = null,
            [FromQuery, SwaggerParameter("Minimum tech rating to filter leaderboards by")] float? techrating_from = null,
            [FromQuery, SwaggerParameter("Maximum tech rating to filter leaderboards by")] float? techrating_to = null,
            [FromQuery, SwaggerParameter("Start date to filter leaderboards by (timestamp)")] int? date_from = null,
            [FromQuery, SwaggerParameter("End date to filter leaderboards by (timestamp)")] int? date_to = null,
            [FromQuery, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] string? types = null,
            [FromQuery, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] string? playlistIds = null,
            [FromBody, SwaggerParameter("Types of leaderboards to filter, default is null(All). Same as type but multiple")] List<PlaylistResponse>? playlists = null,
            [FromQuery, SwaggerParameter("Filter maps from a specific mappers. BeatSaver profile ID list, comma separated, default is null")] string? mappers = null,
            string? overrideCurrentId = null,
            int? uploadTreshold = null) {

            var dbContext = _dbFactory.CreateDbContext();

            string? currentID = HttpContext == null ? overrideCurrentId : HttpContext.CurrentUserID(dbContext);
            Player? currentPlayer = currentID != null ? await dbContext
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            if (type == Type.Ranking && count == 500) {

                return Ok(await LeaderboardControllerHelper.GetModList(_dbFactory, currentPlayer?.ProfileSettings?.ShowAllRatings ?? false, page, count, date_from, date_to));
            }
            
            var sequence = dbContext
                .Leaderboards
                .AsNoTracking()
                .Where(lb => lb.Song.MapCreator == mapCreator);
            if (uploadTreshold != null) {
                sequence = sequence.Where(lb => lb.Song.UploadTime > uploadTreshold);
            }

            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);

            sequence = sequence.Filter(dbContext, out int? searchId, sortBy, order, search, type, types, mode, difficulty, mapType, allTypes, mapRequirements, allRequirements, songStatus, leaderboardContext, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, mappers, playlistList, currentPlayer);

            var result = new ResponseWithMetadata<LeaderboardInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count
                }
            };

            bool showPlays = sortBy == MapSortBy.PlayCount;

            if (page <= 0) {
                page = 1;
            }

            var idsList = await sequence
                .TagWithCallerS()
                .AsNoTracking()
                .Skip((page - 1) * count)
                .Take(count)
                .Select(lb => lb.Id)
                .ToListAsync();

            using (var anotherContext = _dbFactory.CreateDbContext()) {
                var lbsequence = anotherContext
                    .Leaderboards
                    .AsNoTracking()
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(lb => lb.Song)
                    .Include(lb => lb.Reweight)
                    .Where(lb => idsList.Contains(lb.Id));

                if (type == Type.Staff) {
                    lbsequence = lbsequence
                        .Include(lb => lb.Qualification)
                        .ThenInclude(q => q.Votes)
                        .Include(lb => lb.Song)
                        .ThenInclude(s => s.Difficulties.Where(d => d.Status == DifficultyStatus.qualified || d.Status == DifficultyStatus.nominated));
                }

                (result.Metadata.Total, result.Data) = await sequence.CountAsync().CoundAndResults(lbsequence
                    .TagWithCallerS()
                    .AsSplitQuery()
                    .Select(lb => new LeaderboardInfoResponse {
                        Id = lb.Id,
                        Song = type == Type.Staff ? new SongResponse {
                            Id = lb.Song.Id,
                            Hash = lb.Song.Hash,
                            Name = lb.Song.Name,
                            SubName = lb.Song.SubName,
                            Author = lb.Song.Author,
                            Mapper = lb.Song.Mapper,
                            CoverImage  = lb.Song.CoverImage,
                            DownloadUrl = lb.Song.DownloadUrl,
                            FullCoverImage = lb.Song.FullCoverImage,
                            Explicity = lb.Song.Explicity,
                            Mappers = lb.Song != null ? lb.Song.Mappers.Select(m => new MapperResponse {
                                Id = m.Id,
                                PlayerId = m.Player != null ? m.Player.Id : null,
                                Name = m.Player != null ? m.Player.Name : m.Name,
                                Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                                Curator = m.Curator,
                                VerifiedMapper = m.VerifiedMapper,
                            }).ToList() : null,
                            Difficulties = lb.Song.Difficulties,
                        } : new SongResponse {
                            Id = lb.Song.Id,
                            Hash = lb.Song.Hash,
                            Name = lb.Song.Name,
                            SubName = lb.Song.SubName,
                            Author = lb.Song.Author,
                            Mapper = lb.Song.Mapper,
                            CoverImage  = lb.Song.CoverImage,
                            DownloadUrl = lb.Song.DownloadUrl,
                            FullCoverImage = lb.Song.FullCoverImage,
                            Explicity = lb.Song.Explicity
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

                            SpeedTags = lb.Difficulty.SpeedTags,
                            StyleTags = lb.Difficulty.StyleTags,
                            FeatureTags = lb.Difficulty.FeatureTags,

                            Njs  = lb.Difficulty.Njs,
                            Nps  = lb.Difficulty.Nps,
                            Notes  = lb.Difficulty.Notes,
                            Bombs  = lb.Difficulty.Bombs,
                            Walls  = lb.Difficulty.Walls,
                            MaxScore = lb.Difficulty.MaxScore,
                            Duration  = lb.Difficulty.Duration,

                            Requirements = lb.Difficulty.Requirements,
                        },
                        Qualification = lb.Qualification,
                        Reweight = lb.Reweight,
                        ClanRankingContested = lb.ClanRankingContested,
                        Clan = lb.Clan == null ? null : new ClanResponseFull {
                            Id = lb.Clan.Id,
                            Name = lb.Clan.Name,
                            Color = lb.Clan.Color,
                            Icon = lb.Clan.Icon,
                            Tag = lb.Clan.Tag,
                            LeaderID = lb.Clan.LeaderID,
                            Description = lb.Clan.Description,
                            Pp = lb.Clan.Pp,
                            Rank = lb.Clan.Rank
                        },
                        PositiveVotes = lb.PositiveVotes,
                        NegativeVotes = lb.NegativeVotes,
                        VoteStars = lb.VoteStars,
                        StarVotes = lb.StarVotes,
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
                            ReplaysWatched = s.AuthorizedReplayWatched + s.AnonimusReplayWatched,
                            LeaderboardId = s.LeaderboardId,
                            Platform = s.Platform,
                            Weight = s.Weight,
                            AccLeft = s.AccLeft,
                            AccRight = s.AccRight,
                            MaxStreak = s.MaxStreak,
                        }).FirstOrDefault(),
                        Plays = showPlays ? lb.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to)) : 0
                    })
                    .ToListAsync());
            }

            if (result.Data.Count() > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var leaderboard in result.Data) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    }
                }

                bool isLoloppe = currentPlayer?.Id == "76561198073989976" || currentPlayer?.Role?.Contains("admin") == true;
                if (!isLoloppe) {
                    foreach (var leaderboard in result.Data) {
                        leaderboard.HideTags();
                    }
                }

                result.Data = result.Data.OrderBy(e => idsList.IndexOf(e.Id));
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

        [HttpGet("~/leaderboards/groupped/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> GetAllGroupped(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] MapSortBy sortBy = MapSortBy.None,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] Type type = Type.All,
            [FromQuery] string? mode = null,
            [FromQuery] string? difficulty = null,
            [FromQuery] int? mapType = null,
            [FromQuery] Operation allTypes = Operation.Any,
            [FromQuery] Requirements mapRequirements = Requirements.Ignore,
            [FromQuery] Operation allRequirements = Operation.Any,
            [FromQuery] SongStatus songStatus = SongStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] MyType mytype = MyType.None,
            [FromQuery] string? playlistIds = null,
            [FromBody] List<PlaylistResponse>? playlists = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? accrating_from = null,
            [FromQuery] float? accrating_to = null,
            [FromQuery] float? passrating_from = null,
            [FromQuery] float? passrating_to = null,
            [FromQuery] float? techrating_from = null,
            [FromQuery] float? techrating_to = null,
            [FromQuery] string? types = null,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null,
            [FromQuery] string? mappers = null) {

            var sequence = _context.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;
            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);
            sequence = sequence
                .Filter(_context, out int? searchId, sortBy, order, search, type, types, mode, difficulty, mapType, allTypes, mapRequirements, allRequirements, songStatus, leaderboardContext, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, mappers, playlistList, currentPlayer);

            var nonuniqueids = await sequence.Select(lb => lb.SongId).ToListAsync();
            var ids = new List<string>();
            foreach (var item in nonuniqueids)
            {
                if (!ids.Contains(item)) {
                    ids.Add(item);
                }
            }

            if (searchId != null) {
                var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                foreach (var item in searchRecords) {
                    _context.SongSearches.Remove(item);
                }
            }

            var result = new ResponseWithMetadata<LeaderboardInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = ids.Count
                }
            };

            if (ids.Count > 0) {
                ids = ids.Skip((page - 1) * count).Take(count).ToList();
            }

            sequence = _context.Leaderboards
                .Where(lb => ids.Contains(lb.SongId)).Filter(_context, out searchId, sortBy, order, search, type, types, mode, difficulty, mapType, allTypes, mapRequirements, allRequirements, songStatus, leaderboardContext, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, mappers, playlistList, currentPlayer)
                .Include(lb => lb.Difficulty)
                .Include(lb => lb.Song);

            if (type == Type.Staff) {
                sequence = sequence
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Votes);
            } else if (type == Type.Ranking) {
                sequence = sequence
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(q => q.ModifierValues);
            }

            result.Data = await sequence
                .Select(lb => new LeaderboardInfoResponse {
                    Id = lb.Id,
                    Song = new SongResponse {
                        Id = lb.Song.Id,
                        Hash = lb.Song.Hash,
                        Name = lb.Song.Name,
                        SubName = lb.Song.SubName,
                        Author = lb.Song.Author,
                        Mapper = lb.Song.Mapper,
                        CoverImage  = lb.Song.CoverImage,
                        DownloadUrl = lb.Song.DownloadUrl,
                        FullCoverImage = lb.Song.FullCoverImage,
                        Explicity = lb.Song.Explicity
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
                    },
                    Qualification = lb.Qualification,
                    PositiveVotes = lb.PositiveVotes,
                    NegativeVotes = lb.NegativeVotes,
                    VoteStars = lb.VoteStars,
                    StarVotes = lb.StarVotes
                }).ToListAsync();

            if (searchId != null) {
                var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                foreach (var item in searchRecords) {
                    _context.SongSearches.Remove(item);
                }
            }

            return result;
        }
        public class LeaderboardVoting {
            public float Rankability { get; set; }
            public float Stars { get; set; }
            public float[] Type { get; set; } = new float[4];
        }

        public class LeaderboardVotingCounts {
            public int Rankability { get; set; }
            public int Stars { get; set; }
            public int Type { get; set; }
        }

        [HttpGet("~/custommodes")]
        public async Task<ActionResult<ICollection<CustomMode>>> CustomModes() {
            return await _context.CustomModes.ToListAsync();
        }

        [HttpGet("~/leaderboard/ranking/{id}")]
        public async Task<ActionResult<LeaderboardVoting>> GetVoting(string id) {
            var rankVotings = (await _context
                    .Leaderboards
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Scores)
                    .ThenInclude(s => s.RankVoting)
                    .FirstOrDefaultAsync())?
                    .Scores
                    .Where(s => s.RankVoting != null)
                    .Select(s => s.RankVoting)
                    .ToList();


            if (rankVotings == null || rankVotings.Count == 0) {
                return NotFound();
            }

            var result = new LeaderboardVoting();
            var counters = new LeaderboardVotingCounts();

            foreach (var voting in rankVotings) {
                counters.Rankability++;
                result.Rankability += voting.Rankability;

                if (voting.Stars != 0) {
                    counters.Stars++;
                    result.Stars += voting.Stars;
                }

                if (voting.Type != 0) {
                    counters.Type++;

                    for (int i = 0; i < 4; i++) {
                        if ((voting.Type & (1 << i)) != 0) {
                            result.Type[i]++;
                        }
                    }
                }
            }
            result.Rankability /= (counters.Rankability != 0 ? (float)counters.Rankability : 1.0f);
            result.Stars /= (counters.Stars != 0 ? (float)counters.Stars : 1.0f);

            for (int i = 0; i < result.Type.Length; i++) {
                result.Type[i] /= (counters.Type != 0 ? (float)counters.Type : 1.0f);
            }

            return result;
        }

        public class ScoreGraphEntry {
            public string PlayerId { get; set; }
            public float Weight { get; set; }
            public int Rank { get; set; }
            public int Timepost { get; set; }
            public int Pauses { get; set; }
            public int? MaxStreak { get; set; }
            public int Mistakes { get; set; }
            public string Modifiers { get; set; }

            public int PlayerRank { get; set; }
            public string PlayerName { get; set; }
            public string PlayerAvatar { get; set; }
            public string PlayerAlias { get; set; }
            public float Accuracy { get; set; }
            public float Pp { get; set; }
        }

        [HttpGet("~/leaderboard/{id}/scoregraph")]
        [SwaggerOperation(Summary = "Retrieve scores graph for a leaderboard", Description = "Fetches the scores graph for a leaderboard identified by its ID. Compact endpoint to use in the leaderboard data visualizations.")]
        [SwaggerResponse(200, "Score graph retrieved successfully", typeof(ICollection<ScoreGraphEntry>))]
        [SwaggerResponse(404, "Leaderboard not found")]
        public async Task<ActionResult<ICollection<ScoreGraphEntry>>> GetScoregraph(
            [SwaggerParameter("ID of the leaderboard to retrieve score graph for")] string id,
            [SwaggerParameter("Leaderboard context, general by default."), FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
            return leaderboardContext == LeaderboardContexts.General ? await _context.Scores.Where(s => 
                s.LeaderboardId == id &&
                s.ValidForGeneral &&
                !s.Banned)
                .AsNoTracking()
                .TagWithCaller()
                .Select(s => new ScoreGraphEntry {
                    PlayerId = s.PlayerId,
                    Weight = s.Weight,
                    Rank = s.Rank,
                    Timepost = s.Timepost,
                    Pauses = s.Pauses,
                    MaxStreak = s.MaxStreak,
                    Mistakes = s.BadCuts + s.MissedNotes + s.WallsHit + s.BombCuts,
                    Modifiers = s.Modifiers,
                    PlayerRank = s.Player.Rank,
                    PlayerName = s.Player.Name,
                    PlayerAlias = s.Player.Alias,
                    Accuracy = s.Accuracy * 100f,
                    Pp = s.Pp,
                    PlayerAvatar = s.Player.Avatar
                }).ToListAsync() : await _context.ScoreContextExtensions.Where(s => 
                s.LeaderboardId == id &&
                s.Context == leaderboardContext &&
                !s.Banned)
                .AsNoTracking()
                .TagWithCaller()
                .Select(s => new ScoreGraphEntry {
                    PlayerId = s.PlayerId,
                    Weight = s.Weight,
                    Rank = s.Rank,
                    Timepost = s.Timepost,
                    Pauses = s.ScoreInstance.Pauses,
                    MaxStreak = s.ScoreInstance.MaxStreak,
                    Mistakes = s.ScoreInstance.BadCuts + s.ScoreInstance.MissedNotes + s.ScoreInstance.WallsHit + s.ScoreInstance.BombCuts,
                    Modifiers = s.Modifiers,
                    PlayerRank = s.Player.Rank,
                    PlayerName = s.Player.Name,
                    PlayerAlias = s.Player.Alias,
                    Accuracy = s.Accuracy * 100f,
                    Pp = s.Pp,
                    PlayerAvatar = s.Player.Avatar
                }).ToListAsync();
        }
    }
}
