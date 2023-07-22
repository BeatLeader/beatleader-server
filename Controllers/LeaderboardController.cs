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

namespace BeatLeader_Server.Controllers {
    public class LeaderboardController : Controller {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;
        private readonly SongController _songController;
        private readonly IAmazonS3 _s3Client;

        public LeaderboardController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration,
            SongController songController) {
            _context = context;
            _readContext = readContext;
            _songController = songController;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/leaderboard/{id}")]
        public async Task<ActionResult<LeaderboardResponse>> Get(
            string id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "rank",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] string? countries = null,
            [FromQuery] string? search = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] bool friends = false,
            [FromQuery] bool voters = false,
            [FromQuery] bool clanRanking = false) {
            var currentContext = _readContext;

            string? currentID = HttpContext.CurrentUserID(currentContext);
            Player? currentPlayer = currentID != null ? await currentContext
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            bool showBots = currentPlayer?.ProfileSettings?.ShowBots ?? false;

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


                 LeaderboardResponse? leaderboard = query.Select(l => new LeaderboardResponse {
                        Id = l.Id,
                        Song = l.Song,
                        Difficulty = l.Difficulty,
                        Plays = l.Plays,
                        Qualification = l.Qualification,
                        Reweight = l.Reweight,
                        Changes = l.Changes,
                        ClanRankingContested = l.ClanRankingContested,
                        LeaderboardGroup = l.LeaderboardGroup.Leaderboards.Select(it =>
                            new LeaderboardGroupEntry
                            {
                                Id = it.Id,
                                Status = it.Difficulty.Status,
                                Timestamp = it.Timestamp
                            }
                        )
                    })
                    .FirstOrDefault();

            if (leaderboard != null) {

                if (leaderboard.Qualification != null && (isRt || leaderboard.Song.MapperId == currentPlayer?.MapperId)) {
                    leaderboard.Qualification.Comments = _context.QualificationCommentary.Where(c => c.RankQualificationId == leaderboard.Qualification.Id).ToList();
                }

                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                    leaderboard.HideRatings();
                }

                var scoreQuery = currentContext.Scores.Where(s => s.LeaderboardId == leaderboard.Id);
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
                        scoreQuery = scoreQuery.Order(oppositeOrder, s => s.Rank);
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

                leaderboard.Plays = scoreQuery.Count();
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
                            Clans = s.Player.Clans
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        },
                        RankVoting = showVoters ? s.RankVoting : null,
                    })
                    .ToList();
                foreach (var score in leaderboard.Scores) {
                    score.Player = PostProcessSettings(score.Player);
                }

                // TODO: IS THIS THE RIGHT WAY TO DO THIS?
                leaderboard.ClanRanking = currentContext
                .ClanRanking
                .Include(cr => cr.Clan)
                .Where(cr => cr.LeaderboardId == leaderboard.Id)
                .OrderByDescending(cr => cr.ClanPP)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(cr => new ClanRankingResponse
                {
                    Id = cr.Id,
                    Clan = cr.Clan,
                    LastUpdateTime = cr.LastUpdateTime,
                    ClanRank = cr.ClanRank,
                    ClanAverageRank = cr.ClanAverageRank,
                    ClanPP = cr.ClanPP,
                    ClanAverageAccuracy = cr.ClanAverageAccuracy,
                    ClanTotalScore = cr.ClanTotalScore,
                    LeaderboardId = cr.LeaderboardId,
                    Leaderboard = cr.Leaderboard,
                    AssociatedScores = currentContext
                        .Scores
                        .Where(s => s.LeaderboardId == leaderboard.Id && s.Player.Clans.Contains(cr.Clan))
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
                            RankVoting = showVoters ? s.RankVoting : null,
                        })
                        .ToList(),
                })
                .ToList();
            }

            if (leaderboard == null) {
                Song? song = currentContext.Songs.Include(s => s.Difficulties).FirstOrDefault(s => s.Difficulties.FirstOrDefault(d => s.Id + d.Value + d.Mode == id) != null);
                if (song == null) {
                    song = currentContext.Songs.Include(s => s.Difficulties).FirstOrDefault(s => s.Difficulties.FirstOrDefault(d => s.Id == id) != null);
                    if (song == null) {
                        return NotFound();
                    } else {
                        DifficultyDescription? difficulty = song.Difficulties.OrderByDescending(d => d.Value).FirstOrDefault();

                        return difficulty == null ? NotFound() : await Get(song.Id + difficulty.Value + difficulty.Mode, page, count, sortBy, order, scoreStatus, countries, search, modifiers, friends, voters);
                    }
                } else {
                    DifficultyDescription difficulty = song.Difficulties.First(d => song.Id + d.Value + d.Mode == id);
                    return ResponseFromLeaderboard((await GetByHash(song.Hash, difficulty.DifficultyName, difficulty.ModeName)).Value);
                }
            } else if (leaderboard.Reweight != null && !leaderboard.Reweight.Finished) {
                if (isRt) {
                    var reweight = leaderboard.Reweight;
                    var recalculated = leaderboard.Scores.Select(s => {

                        s.ModifiedScore = (int)(s.BaseScore * reweight.Modifiers.GetNegativeMultiplier(s.Modifiers));

                        if (leaderboard.Difficulty.MaxScore > 0) {
                            s.Accuracy = (float)s.BaseScore / (float)leaderboard.Difficulty.MaxScore;
                        } else {
                            s.Accuracy = (float)s.BaseScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        }
                        (s.Pp, s.BonusPp, s.PassPP, s.AccPP, s.TechPP) = ReplayUtils.PpFromScoreResponse(s, ReplayUtils.AccRating(reweight.PredictedAcc, reweight.PassRating, reweight.TechRating), reweight.PassRating, reweight.TechRating, reweight.Modifiers, reweight.ModifiersRating);

                        return s;
                    });

                    var rankedScores = recalculated.OrderByDescending(el => el.Pp).ToList();
                    foreach ((int i, ScoreResponse s) in rankedScores.Select((value, i) => (i, value))) {
                        s.Rank = i + 1 + ((page - 1) * count);
                    }

                    leaderboard.Scores = recalculated.ToList();
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

            for (int i = 0; i < leaderboard.Scores.Count; i++) {
                leaderboard.Scores[i].Rank = i + (page - 1) * count + 1;
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/hash/{hash}")]
        public async Task<ActionResult<LeaderboardsResponse>> GetLeaderboardsByHash(string hash) {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }

            // TODO: THIS MIGHT BE SOMETHING WONKY
            var leaderboards = _readContext.Leaderboards
                 .Where(lb => lb.Song.Hash == hash)
                 .Include(lb => lb.Song)
                 .ThenInclude(s => s.Difficulties)
                 .Include(lb => lb.Difficulty)
                 .ThenInclude(d => d.ModifierValues)
                 .Include(lb => lb.Difficulty)
                 .ThenInclude(d => d.ModifiersRating)
                 .Include(lb => lb.Qualification)
                 .Include(lb => lb.Reweight)
                 .Include(lb => lb.ClanRanking)
                 .ThenInclude(cr => cr.Clan)
                 .Select(lb => new {
                     Song = lb.Song,
                     Id = lb.Id,
                     Qualification = lb.Qualification,
                     Difficulty = lb.Difficulty,
                     Reweight = lb.Reweight,
                     ClanRankingContested = lb.ClanRankingContested,
                     ClanRanking = lb.ClanRanking.FirstOrDefault()
                 })
                .ToList();


            if (leaderboards.Count() == 0) {
                return NotFound();
            }

            var resultList = leaderboards.Select(lb => new LeaderboardsInfoResponse {
                Id = lb.Id,
                Qualification = lb.Qualification,
                Difficulty = lb.Difficulty,
                Reweight = lb.Reweight,
                ClanRankingContested = lb.ClanRankingContested,
                ClanRanking = lb.ClanRanking
            }).ToList();

            if (resultList.Count > 0) {
                string? currentID = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = currentID != null ? await _context
               .Players
               .Include(p => p.ProfileSettings)
               .FirstOrDefaultAsync(p => p.Id == currentID) : null;
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var leaderboard in resultList) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    }
                }
            }

            return new LeaderboardsResponse {
                Song = leaderboards[0].Song,
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
                .Include(lb => lb.ClanRanking)
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
                .Include(lb => lb.Song)
                .Include(lb => lb.ClanRanking)
                .ThenInclude(cr => cr.Clan)
                .Include(lb => lb.Reweight);

            if (type == Type.Staff) {
                sequence = sequence
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Votes);
            } else if (type == Type.Ranking) {
                sequence = sequence
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(q => q.ModifierValues);
            }

            bool showPlays = sortBy == SortBy.PlayCount;

            var resultList = sequence
                .Select(lb => new LeaderboardInfoResponse {
                    Id = lb.Id,
                    Song = lb.Song,
                    Difficulty = lb.Difficulty,
                    Qualification = lb.Qualification,
                    Reweight = lb.Reweight,
                    ClanRankingContested = lb.ClanRankingContested,
                    ClanRanking = lb.ClanRanking.FirstOrDefault(),
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
                }).ToList();

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
                    Difficulty = lb.Difficulty,
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

        [HttpGet("~/leaderboards/refresh")]
        public async Task<ActionResult> RefreshLeaderboards([FromQuery] string? id = null) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }
            var query = _context
                .Leaderboards.Where(lb => true);

            if (id != null) {
                query = query.Where(lb => lb.Id == id);
            }

            int count = query.Count();

            for (int i = 0; i < count; i += 1000) {
                var leaderboards =
                query
                .OrderBy(lb => lb.Id)
                .Skip(i)
                .Take(1000)
                .Select(lb => new {
                    lb.Id,
                    lb.Difficulty.Status,
                    Scores = lb.Scores.Where(s => !s.Banned).Select(s => new { s.Id, s.Pp, s.Accuracy, s.ModifiedScore, s.Timeset })
                })
                .ToArray();

                foreach (var leaderboard in leaderboards) {
                    var status = leaderboard.Status;

                    var rankedScores = status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent
                        ? leaderboard
                            .Scores
                            .OrderByDescending(el => el.Pp)
                            .ThenByDescending(el => el.Accuracy)
                            .ThenBy(el => el.Timeset)
                            .ToList()
                        : leaderboard
                            .Scores
                            .OrderByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => el.Accuracy)
                            .ThenBy(el => el.Timeset)
                            .ToList();
                    if (rankedScores.Count > 0) {
                        foreach ((int ii, var s) in rankedScores.Select((value, ii) => (ii, value))) {
                            var score = _context.Scores.Local.FirstOrDefault(ls => ls.Id == s.Id);
                            if (score == null) {
                                score = new Score() { Id = s.Id };
                                _context.Scores.Attach(score);
                            }
                            score.Rank = ii + 1;

                            _context.Entry(score).Property(x => x.Rank).IsModified = true;
                        }
                    }

                    Leaderboard lb = new Leaderboard() { Id = leaderboard.Id };
                    _context.Leaderboards.Attach(lb);
                    lb.Plays = rankedScores.Count;

                    _context.Entry(lb).Property(x => x.Plays).IsModified = true;
                }

                try {
                    await _context.BulkSaveChangesAsync();
                } catch (Exception e) {
                    _context.RejectChanges();
                }
            }

            return Ok();
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
