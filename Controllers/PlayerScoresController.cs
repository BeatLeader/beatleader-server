using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class PlayerScoresController : Controller {
        private readonly AppContext _context;
        private readonly PlayerContextScoresController _playerContextScoresController;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerScoresController(
            AppContext context,
            IConfiguration configuration,
            IServerTiming serverTiming,
            IWebHostEnvironment env,
            PlayerContextScoresController playerContextScoresController) {
            _context = context;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _playerContextScoresController = playerContextScoresController;
        }

        [NonAction]
        public async Task<(IQueryable<Score>?, bool, string, string)> ScoresQuery(
            string id,
            string sortBy = "date",
            Order order = Order.Desc,
            string? search = null,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            string? type = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null) {

            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = currentID != null ? _context
                .Players
                .Where(p => p.Id == currentID && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings.ShowAllRatings)
                .FirstOrDefault() : false;

            id = _context.PlayerIdToMain(id);

            var player = await _context.Players.Where(p => p.Id == id).Select(p => new { p.Banned }).FirstOrDefaultAsync();
            if (player == null) {
                return (null, false, "", "");
            }

            return (_context
               .Scores
               .Where(t => t.PlayerId == id && t.ValidContexts.HasFlag(leaderboardContext))
               .Filter(_context, !player.Banned, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId),
               showRatings, currentID, id);
        }

        [HttpGet("~/player/{id}/scores")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> GetScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null) {
            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }
            if (page < 1) {
                page = 1;
            }

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                string? currentID2 = HttpContext.CurrentUserID(_context);
                bool showRatings2 = currentID2 != null ? _context
                    .Players
                    .Where(p => p.Id == currentID2 && p.ProfileSettings != null)
                    .Select(p => p.ProfileSettings.ShowAllRatings)
                    .FirstOrDefault() : false;
                return await _playerContextScoresController.GetScores(id, showRatings2, currentID2, sortBy, order, page, count, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            }

            (
                IQueryable<Score>? sequence, 
                bool showRatings, 
                string currentID, 
                string userId
            ) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            ResponseWithMetadata<ScoreResponseWithMyScore> result;
            using (_serverTiming.TimeAction("count")) {
                result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                    Metadata = new Metadata() {
                        Page = page,
                        ItemsPerPage = count,
                        Total = sequence.Count()
                    }
                };
            }
            if (result.Metadata.Total == 0) {
                return result;
            }

            List<ScoreResponseWithMyScore> resultList;
            using (_serverTiming.TimeAction("list")) {
                resultList = sequence
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Skip((page - 1) * count)
                    .Take(count)
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
                        Hmd = s.Hmd,
                        Controller = s.Controller,
                        MaxCombo = s.MaxCombo,
                        Timeset = s.Timeset,
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Leaderboard = new LeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
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
                    .AsSplitQuery()
                    .ToList();
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

                    var myScores = _context.Scores.Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(LeaderboardContexts.General) && leaderboards.Contains(s.LeaderboardId)).Select(ToScoreResponseWithAcc).ToList();
                    foreach (var score in result.Data) {
                        score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                    }
                }
            }

            return result;
        }



        [HttpGet("~/player/{id}/scores/compact")]
        public async Task<ActionResult<ResponseWithMetadata<CompactScoreResponse>>> GetCompactScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null) {
            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }
            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                string? currentID2 = HttpContext.CurrentUserID(_context);
                bool showRatings2 = currentID2 != null ? _context
                    .Players
                    .Where(p => p.Id == currentID2 && p.ProfileSettings != null)
                    .Select(p => p.ProfileSettings.ShowAllRatings)
                    .FirstOrDefault() : false;
                return await _playerContextScoresController.GetCompactScores(id, showRatings2, sortBy, order, page, count, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            }
            (IQueryable<Score>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            ResponseWithMetadata<CompactScoreResponse> result;
            using (_serverTiming.TimeAction("db")) {
                result = new ResponseWithMetadata<CompactScoreResponse>() {
                    Metadata = new Metadata() {
                        Page = page,
                        ItemsPerPage = count,
                        Total = sequence.Count()
                    },
                    Data = sequence
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Include(s => s.Leaderboard)
                    .Select(s => new CompactScoreResponse {
                        Score = new CompactScore {
                            Id = s.Id,
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            EpochTime = s.Timepost,
                            MaxCombo = s.MaxCombo,
                            Hmd = s.Hmd,
                            MissedNotes = s.MissedNotes,
                            BadCuts = s.BadCuts,
                            Modifiers = s.Modifiers,
                            Controller = s.Controller
                        },
                        Leaderboard = new CompactLeaderboard {
                            Id = s.LeaderboardId,
                            Difficulty = s.Leaderboard.Difficulty.Value,
                            ModeName = s.Leaderboard.Difficulty.ModeName,
                            SongHash = s.Leaderboard.Song.Hash
                        }
                    })
                    .ToList()
                };
            }

            return result;
        }

        [HttpDelete("~/player/{id}/score/{leaderboardID}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(string id, string leaderboardID) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }
            Leaderboard? leaderboard = _context.Leaderboards.Where(l => l.Id == leaderboardID).Include(l => l.Scores).FirstOrDefault();
            if (leaderboard == null) {
                return NotFound();
            }
            Score? scoreToDelete = leaderboard.Scores.FirstOrDefault(t => t.PlayerId == id);

            if (scoreToDelete == null) {
                return NotFound();
            }

            _context.Scores.Remove(scoreToDelete);
            await _context.SaveChangesAsync();
            return Ok();

        }

        [HttpGet("~/player/{id}/scorevalue/{hash}/{difficulty}/{mode}")]
        public ActionResult<int> GetScoreValue(string id, string hash, string difficulty, string mode) {
            Score? score = _context
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .FirstOrDefault(s => s.PlayerId == id && s.Leaderboard.Song.Hash == hash && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode);

            if (score == null) {
                return NotFound();
            }

            return score.ModifiedScore;
        }

        [HttpGet("~/player/{id}/histogram")]
        public async Task<ActionResult<string>> GetPlayerHistogram(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null,
            [FromQuery] float? batch = null) {
            return BadRequest("Test");
            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                string? currentID2 = HttpContext.CurrentUserID(_context);
                bool showRatings2 = currentID2 != null ? _context
                    .Players
                    .Where(p => p.Id == currentID2 && p.ProfileSettings != null)
                    .Select(p => p.ProfileSettings.ShowAllRatings)
                    .FirstOrDefault() : false;
                return await _playerContextScoresController.GetPlayerHistogram(id, showRatings2, sortBy, order, count, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId, batch); 
            }
            (IQueryable<Score>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            switch (sortBy) {
                case "date":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.Timepost > 0 ? s.Timepost.ToString() : s.Timeset).Select(s => Int32.Parse(s)).ToList(), (int)(batch > 60 * 60 ? batch : 60 * 60 * 24), count);
                case "pp":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.Pp).ToList(), Math.Max(batch ?? 5, 1), count);
                case "acc":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.Accuracy).ToList(), Math.Max(batch ?? 0.0025f, 0.001f), count);
                case "pauses":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.Pauses).ToList(), Math.Max((int)(batch ?? 1), 1), count);
                case "maxStreak":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.MaxStreak ?? 0).ToList(), Math.Max((int)(batch ?? 1), 1), count);
                case "rank":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.Rank).ToList(), Math.Max((int)(batch ?? 1), 1), count);
                case "stars":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToList(), Math.Max(batch ?? 0.15f, 0.01f), count);
                case "replaysWatched":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.AnonimusReplayWatched + s.AuthorizedReplayWatched).ToList(), Math.Max((int)(batch ?? 1), 1), count);
                case "mistakes":
                    return HistogramUtils.GetHistogram(order, sequence.Select(s => s.BadCuts + s.MissedNotes + s.BombCuts + s.WallsHit).ToList(), Math.Max((int)(batch ?? 1), 1), count);
                default:
                    return BadRequest();
            }
        }

        [HttpGet("~/player/{id}/accgraph")]
        public ActionResult<ICollection<GraphResponse>> AccGraph(string id, [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                string? currentID2 = HttpContext.CurrentUserID(_context);
                bool showRatings2 = currentID2 != null ? _context
                    .Players
                    .Where(p => p.Id == currentID2 && p.ProfileSettings != null)
                    .Select(p => p.ProfileSettings.ShowAllRatings)
                    .FirstOrDefault() : false;
                return _playerContextScoresController.AccGraph(id, showRatings2, leaderboardContext); 
            }
            id = _context.PlayerIdToMain(id);
            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = currentID != null ? _context
                .Players
                .Include(p => p.ProfileSettings)
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings)
                .FirstOrDefault()?.ShowAllRatings ?? false : false;

            var result = _context
                .Scores
                .Where(s => s.PlayerId == id && s.ValidContexts.HasFlag(leaderboardContext) && !s.IgnoreForStats && ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
                .Select(s => new GraphResponse {
                    LeaderboardId = s.Leaderboard.Id,
                    Diff = s.Leaderboard.Difficulty.DifficultyName,
                    SongName = s.Leaderboard.Song.Name,
                    Hash = s.Leaderboard.Song.Hash,
                    Mapper = s.Leaderboard.Song.Author,
                    Mode = s.Leaderboard.Difficulty.ModeName,
                    Stars = s.Leaderboard.Difficulty.Stars,
                    Acc = s.Accuracy,
                    Timeset = s.Timeset,
                    Modifiers = s.Modifiers,

                    ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                    ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                    AccRating = s.Leaderboard.Difficulty.AccRating,
                    PassRating = s.Leaderboard.Difficulty.PassRating,
                    TechRating = s.Leaderboard.Difficulty.TechRating,
                })
                .ToList();
            var defaultModifiers = new ModifiersMap();

            foreach (var score in result) {
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

            return result;

        }

        [HttpGet("~/player/{id}/history")]
        public async Task<ActionResult<ICollection<PlayerScoreStatsHistory>>> GetHistory(string id, [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General, [FromQuery] int count = 50) {
            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                return await _playerContextScoresController.GetHistory(id, leaderboardContext, count); 
            }
            id = _context.PlayerIdToMain(id);
            var result = _context
                    .PlayerScoreStatsHistory
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .OrderByDescending(s => s.Timestamp)
                    .Take(count)
                    .ToList();
            if (result.Count == 0) {
                var player = _context.Players.Where(p => p.Id == id).FirstOrDefault();
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24;
                result = new List<PlayerScoreStatsHistory> { new PlayerScoreStatsHistory { Timestamp = timeset, Rank = player?.Rank ?? 0, Pp = player?.Pp ?? 0, CountryRank = player?.CountryRank ?? 0 } };
            }

            return result;
        }

        [HttpGet("~/player/{id}/pinnedScores")]
        public async Task<ActionResult<ICollection<ScoreResponseWithMyScore>>> GetPinnedScores(
            string id,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {

            id = _context.PlayerIdToMain(id);

            var query = _context
                    .Scores
                    .Where(s => 
                        s.PlayerId == id && 
                        s.ValidContexts.HasFlag(leaderboardContext) &&
                        s.Metadata != null && 
                        s.Metadata.PinnedContexts.HasFlag(leaderboardContext))
                    .OrderBy(s => s.Metadata.Priority);

            var resultList = query
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
                            Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        },
                        ScoreImprovement = s.ScoreImprovement,
                        RankVoting = s.RankVoting,
                        Metadata = s.Metadata,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Leaderboard = new LeaderboardResponse {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
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
                    .ToList();

            bool showRatings = HttpContext.ShouldShowAllRatings(_context);
            foreach (var resultScore in resultList) {
                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }
            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                var contexts = query
                    .Select(s => s.ContextExtensions.FirstOrDefault(ce => ce.Context == leaderboardContext))
                    .ToList();
                for (int i = 0; i < resultList.Count && i < contexts.Count; i++)
                {
                    if (contexts[i]?.ScoreId == resultList[i].Id) {
                        resultList[i].ToContext(contexts[i]);
                    }
                }

            }
            return resultList;
        }
    }
}
