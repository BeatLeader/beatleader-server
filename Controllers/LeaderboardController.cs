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
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;

namespace BeatLeader_Server.Controllers {
    public class LeaderboardController : Controller {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;
        private readonly SongController _songController;
        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;

        public LeaderboardController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration,
            SongController songController,
            IServerTiming serverTiming) {
            _context = context;
            _readContext = readContext;
            _songController = songController;
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
            string sortBy,
            Order order,
            ScoreFilterStatus scoreStatus,
            string? countries,
            string? search,
            string? modifiers,
            List<string>? friendsList) {
            IQueryable<Score> scoreQuery = _context
                .Scores
                .Where(s => s.LeaderboardId == leaderboard.Id && s.ValidContexts.HasFlag(LeaderboardContexts.General));

            

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

            Order oppositeOrder = order.Reverse();

            switch (sortBy) {
                case "date":
                    scoreQuery = scoreQuery.Order(order, s => s.Timepost).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "pp":
                    scoreQuery = scoreQuery.Order(order, s => s.Pp).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "acc":
                    scoreQuery = scoreQuery.Order(order, s => s.Accuracy);
                    break;
                case "pauses":
                    scoreQuery = scoreQuery.Order(order, s => s.Pauses).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "rank":
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
                    break;
                case "maxStreak":
                    scoreQuery = scoreQuery.Order(order, s => s.MaxStreak).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "mistakes":
                    scoreQuery = scoreQuery.Order(order, s => s.BadCuts + s.MissedNotes + s.BombCuts + s.WallsHit);
                    break;
                case "weight":
                    scoreQuery = scoreQuery.Order(order, s => s.Weight);
                    break;
                case "weightedPp":
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
            using (_serverTiming.TimeAction("scorecount")) {
            leaderboard.Plays = scoreQuery.Count();
            }
            using (_serverTiming.TimeAction("scorelist")) {
            leaderboard.Scores = scoreQuery
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
                    Player = new PlayerResponse {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Bot = s.Player.Bot,
                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id)
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    RankVoting = showVoters ? s.RankVoting : null,
                })
                .ToList();
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
            string sortBy,
            Order order,
            ScoreFilterStatus scoreStatus,
            string? countries,
            string? search,
            string? modifiers,
            List<string>? friendsList) {
            IQueryable<ScoreContextExtension> scoreQuery = _context
                .ScoreContextExtensions
                .Include(s => s.Score)
                .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context);

            if (countries == null) {
                if (friendsList != null) {
                    scoreQuery = scoreQuery.Where(s => (!s.Score.Banned || (showBots && s.Score.Bot)) && friendsList.Contains(s.PlayerId));
                } else if (voters) {
                    scoreQuery = scoreQuery.Where(s => (!s.Score.Banned || (showBots && s.Score.Bot)) && s.Score.RankVoting != null);
                } else {
                    scoreQuery = scoreQuery.Where(s => (!s.Score.Banned || (showBots && s.Score.Bot)));
                }
            } else {
                if (friendsList != null) {
                    scoreQuery = scoreQuery.Where(s => (!s.Score.Banned || (showBots && s.Score.Bot)) && friendsList.Contains(s.PlayerId) && countries.ToLower().Contains(s.Player.Country.ToLower()));
                } else {
                    scoreQuery = scoreQuery.Where(s => (!s.Score.Banned || (showBots && s.Score.Bot)) && countries.ToLower().Contains(s.Player.Country.ToLower()));
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

            Order oppositeOrder = order.Reverse();

            switch (sortBy) {
                case "date":
                    scoreQuery = scoreQuery.Order(order, s => s.Timeset).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "pp":
                    scoreQuery = scoreQuery.Order(order, s => s.Pp).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "acc":
                    scoreQuery = scoreQuery.Order(order, s => s.Accuracy);
                    break;
                case "pauses":
                    scoreQuery = scoreQuery.Order(order, s => s.Score.Pauses).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "rank":
                    scoreQuery = scoreQuery.Order(oppositeOrder, s => s.Rank);
                    break;
                case "maxStreak":
                    scoreQuery = scoreQuery.Order(order, s => s.Score.MaxStreak).ThenOrder(oppositeOrder, s => s.Rank);
                    break;
                case "mistakes":
                    scoreQuery = scoreQuery.Order(order, s => s.Score.BadCuts + s.Score.MissedNotes + s.Score.BombCuts + s.Score.WallsHit);
                    break;
                case "weight":
                    scoreQuery = scoreQuery.Order(order, s => s.Weight);
                    break;
                case "weightedPp":
                    scoreQuery = scoreQuery.Order(order, s => s.Weight * s.Pp);
                    break;
                default:
                    break;
            }
            switch (scoreStatus) {
                case ScoreFilterStatus.None:
                    break;
                case ScoreFilterStatus.Suspicious:
                    scoreQuery = scoreQuery.Where(s => s.Score.Suspicious);
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

            leaderboard.Plays = scoreQuery.Count();
            leaderboard.Scores = scoreQuery
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
                    BadCuts = s.Score.BadCuts,
                    MissedNotes = s.Score.MissedNotes,
                    BombCuts = s.Score.BombCuts,
                    WallsHit = s.Score.WallsHit,
                    Pauses = s.Score.Pauses,
                    FullCombo = s.Score.FullCombo,
                    Timeset = s.Score.Timeset,
                    Timepost = s.Score.Timepost,
                    MaxStreak = s.Score.MaxStreak,
                    AccPP = s.AccPP,
                    TechPP = s.TechPP,
                    PassPP = s.PassPP,
                    Weight = s.Weight,
                    Hmd = s.Score.Hmd,
                    Platform = s.Score.Platform,
                    Controller = s.Score.Controller,
                    FcAccuracy = s.Score.FcAccuracy,
                    FcPp = s.Score.FcPp,
                    Player = new PlayerResponse {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Bot = s.Player.Bot,
                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id)
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    RankVoting = showVoters ? s.Score.RankVoting : null,
                })
                .ToList();
        }

        [HttpGet("~/leaderboard/{id}")]
        public async Task<ActionResult<LeaderboardResponse>> Get(
            string id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "rank",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? countries = null,
            [FromQuery] string? search = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] bool friends = false,
            [FromQuery] bool voters = false) {
            var currentContext = _readContext;

            string? currentID = HttpContext.CurrentUserID(currentContext);
            var currentPlayer = currentID != null ? await currentContext
                .Players
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

            IQueryable<Leaderboard> query = currentContext
                    .Leaderboards
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


            LeaderboardResponse? leaderboard;
            using (_serverTiming.TimeAction("leaderboard")) {
                leaderboard = query
                .AsSplitQuery()
                .Select(l => new LeaderboardResponse {
                Id = l.Id,
                Song = l.Song,
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
                Clan = l.Clan,
                LeaderboardGroup = l.LeaderboardGroup.Leaderboards.Select(it =>
                    new LeaderboardGroupEntry {
                        Id = it.Id,
                        Status = it.Difficulty.Status,
                        Timestamp = it.Timestamp
                    }
                   ),
            })
               .FirstOrDefault();
            }

            if (leaderboard != null) {

                if (leaderboard.Qualification != null && (isRt || leaderboard.Song.MapperId == currentPlayer?.MapperId)) {
                    leaderboard.Qualification.Comments = _context.QualificationCommentary.Where(c => c.RankQualificationId == leaderboard.Qualification.Id).ToList();
                }

                bool showRatings = currentPlayer?.ShowAllRatings ?? false;
                if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                    leaderboard.HideRatings();
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
                    var friendsContainer = currentContext
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => f.Friends.Select(fs => fs.Id))
                        .FirstOrDefault();
                    if (friendsContainer != null) {
                        friendsList = friendsContainer.ToList();
                        friendsList.Add(currentID);
                    } else {
                        friendsList = new List<string> { currentID };
                    }
                    }
                }

                if (leaderboardContext == LeaderboardContexts.General || leaderboardContext == LeaderboardContexts.None) {
                    await GeneralScores(leaderboard, showBots, voters, showVoters, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList);
                } else {
                    await ContextScores(leaderboard, leaderboardContext, showBots, voters, showVoters, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friendsList);
                }

                foreach (var score in leaderboard.Scores) {
                    score.Player = PostProcessSettings(score.Player);
                }
            }

            if (leaderboard == null) {
                Song? song = currentContext.Songs.Include(s => s.Difficulties).FirstOrDefault(s => s.Difficulties.FirstOrDefault(d => s.Id + d.Value + d.Mode == id) != null);
                if (song == null) {
                    song = currentContext.Songs.Include(s => s.Difficulties).FirstOrDefault(s => s.Difficulties.FirstOrDefault(d => s.Id == id) != null);
                    if (song == null) {
                        return NotFound();
                    } else {
                        DifficultyDescription? difficulty = song.Difficulties.OrderByDescending(d => d.Value).FirstOrDefault();

                        return difficulty == null ? NotFound() : await Get(song.Id + difficulty.Value + difficulty.Mode, page, count, sortBy, order, scoreStatus, leaderboardContext, countries, search, modifiers, friends, voters);
                    }
                } else {
                    DifficultyDescription difficulty = song.Difficulties.First(d => song.Id + d.Value + d.Mode == id);
                    return ResponseFromLeaderboard((await GetByHash(song.Hash, difficulty.DifficultyName, difficulty.ModeName)).Value);
                }
            } else if (leaderboard.Difficulty.Status == DifficultyStatus.nominated) {

                if (isRt) {
                    var qualification = leaderboard.Qualification;
                    var recalculated = leaderboard.Scores.Select(s => {

                        s.ModifiedScore = (int)(s.BaseScore * qualification.Modifiers.GetNegativeMultiplier(s.Modifiers));

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
                            qualification.Modifiers,
                            qualification.ModifiersRating
                            );

                        return s;
                    }).ToList();

                    var rankedScores = recalculated.OrderByDescending(el => el.Pp).ToList();
                    foreach ((int i, ScoreResponse s) in rankedScores.Select((value, i) => (i, value))) {
                        s.Rank = i + 1 + ((page - 1) * count);
                    }

                    leaderboard.Scores = recalculated;
                }
            }

            for (int i = 0; i < leaderboard.Scores?.Count; i++) {
                leaderboard.Scores[i].Rank = i + (page - 1) * count + 1;
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboard/clanRankings/{leaderboardId}/{clanRankingId}")]
        public async Task<ActionResult<ClanRankingResponse>> GetClanRankingAssociatedScores(
            string leaderboardId,
            int clanRankingId,
            [FromQuery] int page = 1,
            [FromQuery] int count = 5)
        {
            var currentContext = _readContext;

            var clanRankingScores = currentContext
                .ClanRanking
                .Where(cr => cr.LeaderboardId == leaderboardId && cr.Id == clanRankingId)
                .Select(cr => new ClanRankingResponse
                {
                    Id = cr.Id,
                    Clan = cr.Clan,
                    LastUpdateTime = cr.LastUpdateTime,
                    AverageRank = cr.AverageRank,
                    Pp = cr.Pp,
                    AverageAccuracy = cr.AverageAccuracy,
                    TotalScore = cr.TotalScore,
                    LeaderboardId = cr.LeaderboardId,
                    Leaderboard = cr.Leaderboard,
                    AssociatedScores = currentContext
                        .Scores
                        .Where(s => s.LeaderboardId == leaderboardId && s.Player.Clans.Contains(cr.Clan))
                        .Include(sc => sc.Player)
                        .ThenInclude(p => p.ProfileSettings)
                        .Include(s => s.Player)
                        .ThenInclude(s => s.Clans)
                        .OrderBy(s => s.Rank)
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
                            Player = new PlayerResponse
                            {
                                Id = s.Player.Id,
                                Name = s.Player.Name,
                                Avatar = s.Player.Avatar,
                                Country = s.Player.Country,
                                Pp = s.Player.Pp,
                                Rank = s.Player.Rank,
                                CountryRank = s.Player.CountryRank,
                                Role = s.Player.Role,
                                ProfileSettings = s.Player.ProfileSettings,
                                Clans = s.Player.Clans
                                    .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                            },
                            RankVoting = null,
                        })
                        .ToList(),
                    AssociatedScoresCount = currentContext
                        .Scores
                        .Where(sc => sc.LeaderboardId == leaderboardId && sc.Player.Clans.Contains(cr.Clan))
                        .Count()
                })
                .FirstOrDefault();

            return clanRankingScores;
        }


        [HttpGet("~/leaderboard/clanRankings/{id}")]
        public async Task<ActionResult<LeaderboardClanRankingResponse>> GetClanRankings(
            string id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            var currentContext = _readContext;

            string? currentID = HttpContext.CurrentUserID(currentContext);
            var currentPlayer = currentID != null ? await currentContext
                .Players
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

            IQueryable<Leaderboard> query = currentContext
                    .Leaderboards
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
                leaderboard = query
                .AsSplitQuery()
                .Select(l => new LeaderboardClanRankingResponse
                {
                    Id = l.Id,
                    Song = l.Song,
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
                    Clan = l.Clan,
                    LeaderboardGroup = l.LeaderboardGroup.Leaderboards.Select(it =>
                        new LeaderboardGroupEntry
                        {
                            Id = it.Id,
                            Status = it.Difficulty.Status,
                            Timestamp = it.Timestamp
                        }
                   ),
                })
               .FirstOrDefault();
            }

            if (leaderboard != null)
            {
                if (leaderboard.Qualification != null && (isRt || leaderboard.Song.MapperId == currentPlayer?.MapperId))
                {
                    leaderboard.Qualification.Comments = _context.QualificationCommentary.Where(c => c.RankQualificationId == leaderboard.Qualification.Id).ToList();
                }

                bool showRatings = currentPlayer?.ShowAllRatings ?? false;
                if (!showRatings && !leaderboard.Difficulty.Status.WithRating())
                {
                    leaderboard.HideRatings();
                }

                var clanRankingQuery = currentContext.ClanRanking.Where(s => s.LeaderboardId == leaderboard.Id).ToList();

                leaderboard.Plays = clanRankingQuery.Count();
                leaderboard.Scores = null;

                // This clanRanking data is required for displaying clanRankings on each leaderboard
                leaderboard.ClanRanking = currentContext
                    .ClanRanking
                    .Include(cr => cr.Clan)
                    .Where(cr => cr.LeaderboardId == leaderboard.Id)
                    .OrderByDescending(cr => cr.Pp)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(cr => new ClanRankingResponse
                    {
                        Id = cr.Id,
                        Clan = cr.Clan,
                        LastUpdateTime = cr.LastUpdateTime,
                        AverageRank = cr.AverageRank,
                        Pp = cr.Pp,
                        AverageAccuracy = cr.AverageAccuracy,
                        TotalScore = cr.TotalScore,
                        LeaderboardId = cr.LeaderboardId,
                        Leaderboard = cr.Leaderboard,
                        AssociatedScores = null,
                        AssociatedScoresCount = 0
                    })
                    .ToList();
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/hash/{hash}")]
        public async Task<ActionResult<LeaderboardsResponseWithScores>> GetLeaderboardsByHash(
            string hash,
            [FromQuery] bool my_scores = false) {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }

            string? currentID = HttpContext.CurrentUserID(_context);

            var song = _context
                .Songs
                .Where(s => s.Hash == hash)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .AsSplitQuery()
                .FirstOrDefault();

            if (song == null) {
                return NotFound();
            }

            var query = _context
            .Leaderboards
            .Where(lb => lb.SongId == song.Id && lb.Difficulty.Mode <= 7);

            if (my_scores && currentID != null) {
                query = query.Include(lb => lb.Scores.Where(s => s.PlayerId == currentID));
            }

            var resultList = query
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
                Clan = lb.Clan,
                ClanRankingContested = lb.ClanRankingContested,
                MyScore = my_scores && currentID != null 
                    ? lb.Scores.AsQueryable().Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(LeaderboardContexts.General)).Select(ScoreResponseQuery.SelectWithAcc()).FirstOrDefault()
                    : null
            }).ToList();

            if (resultList.Count > 0) {
                Player? currentPlayer = currentID != null 
                    ? await _context.Players.Include(p => p.ProfileSettings).FirstOrDefaultAsync(p => p.Id == currentID) 
                    : null;
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var leaderboard in resultList) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    }
                }
            }

            return new LeaderboardsResponseWithScores {
                Song = song,
                Leaderboards = resultList
            };
        }


        //[HttpDelete("~/leaderboard/{id}")]
        //public async Task<ActionResult> Delete(
        //    string id)
        //{
        //    string currentID = HttpContext.CurrentUserID(_context);
        //    var currentPlayer = await _context.Players.FindAsync(currentID);

        //    if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
        //    {
        //        return Unauthorized();
        //    }

        //    var stats = _context.PlayerLeaderboardStats.FirstOrDefault(lb => lb.LeaderboardId == id);
        //    if (stats != null) {
        //        _context.PlayerLeaderboardStats.Remove(stats);
        //        _context.SaveChanges();
        //    }

        //    var lb = _context.Leaderboards.FirstOrDefault(lb => lb.Id == id);

        //    if (lb != null) {
        //        _context.Leaderboards.Remove(lb);
        //        _context.SaveChanges();
        //    } else {
        //        return NotFound();
        //    }

        //    return Ok();
        //}

        [NonAction]
        public async Task<ActionResult<Leaderboard>> GetByHash(string hash, string diff, string mode, bool recursive = true) {
            Leaderboard? leaderboard;

            leaderboard = _context
                .Leaderboards
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefault(lb => lb.Song.Hash == hash && lb.Difficulty.ModeName == mode && lb.Difficulty.DifficultyName == diff);

            if (leaderboard == null) {
                Song? song = await _songController.GetOrAddSong(hash);
                if (song == null) {
                    return NotFound();
                }
                // Song migrated leaderboards
                if (recursive) {
                    return await GetByHash(hash, diff, mode, false);
                } else {
                    leaderboard = await _songController.NewLeaderboard(song, null, diff, mode);
                }

                if (leaderboard == null) {
                    return NotFound();
                }
            }

            return leaderboard;
        }

        public class QualificationInfo {
            public int Id { get; set; }
            public int Timeset { get; set; }
            public string RTMember { get; set; }
            public int CriteriaMet { get; set; }
            public int CriteriaTimeset { get; set; }
            public string CriteriaChecker { get; set; }
            public string CriteriaCommentary { get; set; }
            public bool MapperAllowed { get; set; }
            public string MapperId { get; set; }
            public bool MapperQualification { get; set; }
            public int ApprovalTimeset { get; set; }
            public bool Approved { get; set; }
            public string Approvers { get; set; }
        }

        public class MassLeaderboardsInfoResponse {
            public string Id { get; set; }
            public SongInfo Song { get; set; }
            public MassLeaderboardsDiffInfo Difficulty { get; set; }
            public QualificationInfo? Qualification { get; set; }

            public void HideRatings() {
                Difficulty.HideRatings();
            }
        }

        public class SongInfo {
            public string Id { get; set; }
            public string Hash { get; set; }
        }

        public class MassLeaderboardsDiffInfo {
            public int Id { get; set; }
            public int Value { get; set; }
            public int Mode { get; set; }
            public DifficultyStatus Status { get; set; }
            public string ModeName { get; set; }
            public string DifficultyName { get; set; }
            public int NominatedTime { get; set; }
            public int QualifiedTime { get; set; }
            public int RankedTime { get; set; }
            public float? Stars { get; set; }
            public float? AccRating { get; set; }
            public float? PassRating { get; set; }
            public float? TechRating { get; set; }
            public int MaxScore { get; set; }
            public int Type { get; set; }
            public ModifiersMap ModifierValues { get; set; }
            public ModifiersRating? ModifiersRating { get; set; }

            public void HideRatings() {
                this.AccRating = null;
                this.TechRating = null;
                this.PassRating = null;
                this.Stars = null;

                this.ModifiersRating = null;
            }
        }

        [NonAction]
        public async Task<ActionResult<ResponseWithMetadata<MassLeaderboardsInfoResponse>>> GetModList(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] SortBy sortBy = SortBy.None,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null
            ) {
            var sequence = _context.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            sequence = sequence.FilterRanking(_context, page, count, out int totalMatches, sortBy, order, date_from, date_to);

            var result = new ResponseWithMetadata<MassLeaderboardsInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = totalMatches,
                }
            };

            sequence = sequence
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating);

            var resultList = sequence
                .Select(lb => new MassLeaderboardsInfoResponse {
                    Id = lb.Id,
                    Song = new SongInfo {
                        Id = lb.Song.Id,
                        Hash = lb.Song.Hash
                    },
                    Difficulty = new MassLeaderboardsDiffInfo {
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
                        PassRating  = lb.Difficulty.PassRating,
                        AccRating  = lb.Difficulty.AccRating,
                        TechRating  = lb.Difficulty.TechRating,
                        Type  = lb.Difficulty.Type,
                        MaxScore = lb.Difficulty.MaxScore,
                    },
                    Qualification = new QualificationInfo {
                        Id = lb.Qualification.Id,
                        Timeset = lb.Qualification.Timeset,
                        RTMember = lb.Qualification.RTMember,
                        CriteriaMet = lb.Qualification.CriteriaMet,
                        CriteriaTimeset = lb.Qualification.CriteriaTimeset,
                        CriteriaChecker = lb.Qualification.CriteriaChecker,
                        CriteriaCommentary = lb.Qualification.CriteriaCommentary,
                        MapperAllowed = lb.Qualification.MapperAllowed,
                        MapperId = lb.Qualification.MapperId,
                        MapperQualification = lb.Qualification.MapperQualification,
                        ApprovalTimeset = lb.Qualification.ApprovalTimeset,
                        Approved = lb.Qualification.Approved,
                        Approvers = lb.Qualification.Approvers,
                    }
                })
                .ToList();

            if (resultList.Count > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var leaderboard in resultList) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    }
                }
            }

            result.Data = resultList;

            return result;
        }

        [HttpGet("~/leaderboards/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] SortBy sortBy = SortBy.None,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] Type type = Type.All,
            [FromQuery] string? mode = null,
            [FromQuery] int? mapType = null,
            [FromQuery] Operation allTypes = Operation.Any,
            [FromQuery] Requirements mapRequirements = Requirements.Ignore,
            [FromQuery] Operation allRequirements = Operation.Any,
            [FromQuery] MyType mytype = MyType.None,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? accrating_from = null,
            [FromQuery] float? accrating_to = null,
            [FromQuery] float? passrating_from = null,
            [FromQuery] float? passrating_to = null,
            [FromQuery] float? techrating_from = null,
            [FromQuery] float? techrating_to = null,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null) {

            if (type == Type.Ranking && count == 500) {
                return Ok((await GetModList(page, count, sortBy, order, date_from, date_to)).Value);
            }

            var sequence = _context.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            sequence = sequence.Filter(_context, page, count, out int totalMatches, out int? searchId, sortBy, order, search, type, mode, mapType, allTypes, mapRequirements, allRequirements, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, currentPlayer);

            var result = new ResponseWithMetadata<LeaderboardInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = totalMatches,
                }
            };

            sequence = sequence
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
                .Include(lb => lb.Song)
                .Include(lb => lb.Reweight);

            if (type == Type.Staff) {
                sequence = sequence
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Votes)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties.Where(d => d.Status == DifficultyStatus.qualified || d.Status == DifficultyStatus.nominated));
            }

            bool showPlays = sortBy == SortBy.PlayCount;

            var resultList = sequence
                .Select(lb => new LeaderboardInfoResponse {
                    Id = lb.Id,
                    Song = lb.Song,
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
                    Reweight = lb.Reweight,
                    ClanRankingContested = lb.ClanRankingContested,
                    Clan = lb.Clan,
                    PositiveVotes = lb.PositiveVotes,
                    NegativeVotes = lb.NegativeVotes,
                    VoteStars = lb.VoteStars,
                    StarVotes = lb.StarVotes,
                    MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID && !s.Banned).Select(s => new ScoreResponseWithAcc {
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
                    Plays = showPlays ? lb.Scores.Count(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to)) : 0
                })
                .ToList();

            if (resultList.Count > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var leaderboard in resultList) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    }
                }
            }
            if (searchId != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = _context.SongSearches.Where(s => s.SearchId == searchId).ToList();
                    foreach (var item in searchRecords) {
                        _context.SongSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            result.Data = resultList;

            return result;
        }

        [HttpGet("~/leaderboards/groupped/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> GetAllGroupped(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] SortBy sortBy = SortBy.None,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] Type type = Type.All,
            [FromQuery] string? mode = null,
            [FromQuery] int? mapType = null,
            [FromQuery] Operation allTypes = Operation.Any,
            [FromQuery] Requirements mapRequirements = Requirements.Ignore,
            [FromQuery] Operation allRequirements = Operation.Any,
            [FromQuery] MyType mytype = MyType.None,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? accrating_from = null,
            [FromQuery] float? accrating_to = null,
            [FromQuery] float? passrating_from = null,
            [FromQuery] float? passrating_to = null,
            [FromQuery] float? techrating_from = null,
            [FromQuery] float? techrating_to = null,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null) {

            var sequence = _context.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;
            sequence = sequence.Filter(_context, page, count, out int totalMatches, out int? searchId, sortBy, order, search, type, mode, mapType, allTypes, mapRequirements, allRequirements, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, currentPlayer);

            var ids = sequence.Select(lb => lb.SongId).ToList();

            if (searchId != null) {
                var searchRecords = _context.SongSearches.Where(s => s.SearchId == searchId).ToList();
                foreach (var item in searchRecords) {
                    _context.SongSearches.Remove(item);
                }
            }

            var result = new ResponseWithMetadata<LeaderboardInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = totalMatches
                }
            };

            sequence = sequence
                .Where(lb => ids.Contains(lb.SongId)).Filter(_context, page, count, out totalMatches, out searchId, sortBy, order, search, type, mode, mapType, allTypes, mapRequirements, allRequirements, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, currentPlayer)
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

            result.Data = sequence
                .Select(lb => new LeaderboardInfoResponse {
                    Id = lb.Id,
                    Song = lb.Song,
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
                }).ToList();

            if (searchId != null) {
                var searchRecords = _context.SongSearches.Where(s => s.SearchId == searchId).ToList();
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

        [HttpGet("~/leaderboard/ranking/{id}")]
        public ActionResult<LeaderboardVoting> GetVoting(string id) {
            var rankVotings = _readContext
                    .Leaderboards
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Scores)
                    .ThenInclude(s => s.RankVoting)
                    .FirstOrDefault()?
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

        [HttpGet("~/leaderboard/statistic/{id}")]
        public async Task<ActionResult<Models.ScoreStatistic>> RefreshStatistic(string id) {
            var stream = await _s3Client.DownloadStats(id + "-leaderboard.json");
            if (stream != null) {
                return File(stream, "application/json");
            }

            var leaderboard = _context.Leaderboards.Where(lb => lb.Id == id).Include(lb => lb.Scores.Where(s =>
                !s.Banned
                && !s.Modifiers.Contains("SS")
                && !s.Modifiers.Contains("NA")
                && !s.Modifiers.Contains("NB")
                && !s.Modifiers.Contains("NF")
                && !s.Modifiers.Contains("NO"))).FirstOrDefault();
            if (leaderboard == null || leaderboard.Scores.Count == 0) {
                return NotFound();
            }

            var scoreIds = leaderboard.Scores.Select(s => s.Id);

            var statistics = scoreIds.Select(async id => {
                using (var stream = await _s3Client.DownloadStats(id + ".json")) {
                    if (stream == null) {
                        return null;
                    }

                    return stream.ObjectFromStream<Models.ScoreStatistic>();
                }
            });

            var result = new Models.ScoreStatistic();

            await ReplayStatisticUtils.AverageStatistic(statistics, result);

            await _s3Client.UploadScoreStats(id + "-leaderboard.json", result);

            return result;
        }
    }
}
