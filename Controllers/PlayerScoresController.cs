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
        public async Task<(IQueryable<Score>?, bool, string, string)> ScoresQuery(
            string id,
            string sortBy = "date",
            Order order = Order.Desc,
            string? search = null,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
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
               .Where(t => t.PlayerId == id)
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
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null) {
            (IQueryable<Score>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
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
                            Difficulty = s.Leaderboard.Difficulty
                        },
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak,
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

                    var myScores = _context.Scores.Where(s => s.PlayerId == currentID && leaderboards.Contains(s.LeaderboardId)).Select(ToScoreResponseWithAcc).ToList();
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
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null) {
            (IQueryable<Score>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
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
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            EpochTime = s.Timepost,
                            MaxCombo = s.MaxCombo,
                            Hmd = s.Hmd,
                            MissedNotes = s.MissedNotes,
                            BadCuts = s.BadCuts,
                        },
                        Leaderboard = new CompactLeaderboard {
                            Difficulty = s.Leaderboard.Difficulty.Value,
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

        public class HistogrammValue {
            public int Value { get; set; }
            public int Page { get; set; }
        }

        [HttpGet("~/player/{id}/histogram")]
        public async Task<ActionResult<string>> GetHistogram(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null,
            [FromQuery] float? batch = null) {
            (IQueryable<Score>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            switch (sortBy) {
                case "date":
                    return HistogrammValuee(order, sequence.Select(s => s.Timepost > 0 ? s.Timepost.ToString() : s.Timeset).Select(s => Int32.Parse(s)).ToList(), (int)(batch > 60 * 60 ? batch : 60 * 60 * 24), count);
                case "pp":
                    return HistogrammValuee(order, sequence.Select(s => s.Pp).ToList(), batch ?? 5, count);
                case "acc":
                    return HistogrammValuee(order, sequence.Select(s => s.Accuracy).ToList(), batch ?? 0.0025f, count);
                case "pauses":
                    return HistogrammValuee(order, sequence.Select(s => s.Pauses).ToList(), (int)(batch ?? 1), count);
                case "maxStreak":
                    return HistogrammValuee(order, sequence.Select(s => s.MaxStreak ?? 0).ToList(), (int)(batch ?? 1), count);
                case "rank":
                    return HistogrammValuee(order, sequence.Select(s => s.Rank).ToList(), (int)(batch ?? 1), count);
                case "stars":
                    return HistogrammValuee(order, sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToList(), batch ?? 0.15f, count);
                case "replaysWatched":
                    return HistogrammValuee(order, sequence.Select(s => s.AnonimusReplayWatched + s.AuthorizedReplayWatched).ToList(), (int)(batch ?? 1), count);
                case "mistakes":
                    return HistogrammValuee(order, sequence.Select(s => s.BadCuts + s.MissedNotes + s.BombCuts + s.WallsHit).ToList(), (int)(batch ?? 1), count);
                default:
                    return BadRequest();
            }
        }

        public string HistogrammValuee(Order order, List<int> values, int batch, int count) {
            if (values.Count() == 0) {
                return "";
            }
            Dictionary<int, HistogrammValue> result = new Dictionary<int, HistogrammValue>();
            int normalizedMin = (values.Min() / batch) * batch;
            int normalizedMax = (values.Max() / batch) * batch;
            int totalCount = 0;
            if (order == Order.Desc) {
                for (int i = normalizedMax; i >= normalizedMin; i -= batch) {
                    int value = values.Count(s => s <= i && s > i - batch);
                    result[i] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            } else {
                for (int i = normalizedMin; i <= normalizedMax; i += batch) {
                    int value = values.Count(s => s >= i && s < i + batch);
                    result[i] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        public string HistogrammValuee(Order order, List<float> values, float batch, int count) {
            if (values.Count() == 0) return "";
            Dictionary<float, HistogrammValue> result = new Dictionary<float, HistogrammValue>();
            int totalCount = 0;
            float normalizedMin = (int)(values.Min() / batch) * batch;
            float normalizedMax = (int)(values.Max() / batch + 1) * batch;
            if (order == Order.Desc) {
                for (float i = normalizedMax; i > normalizedMin; i -= batch) {
                    int value = values.Count(s => s <= i && s >= i - batch);
                    result[i - batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            } else {
                for (float i = normalizedMin; i < normalizedMax; i += batch) {
                    int value = values.Count(s => s >= i && s <= i + batch);
                    result[i + batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        public class GraphResponse {
            public string LeaderboardId { get; set; }
            public string Diff { get; set; }
            public string Mode { get; set; }
            public string Modifiers { get; set; }
            public string SongName { get; set; }
            public string Hash { get; set; }
            public string Mapper { get; set; }
            public float Acc { get; set; }
            public string Timeset { get; set; }
            public float? Stars { get; set; }

            [JsonIgnore]
            public ModifiersRating? ModifiersRating { get; set; }
            [JsonIgnore]
            public ModifiersMap? ModifierValues { get; set; }
            [JsonIgnore]
            public float? PassRating { get; set; }
            [JsonIgnore]
            public float? AccRating { get; set; }
            [JsonIgnore]
            public float? TechRating { get; set; }
        }

        [HttpGet("~/player/{id}/accgraph")]
        public ActionResult<ICollection<GraphResponse>> GetScoreValue(string id) {
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
                .Where(s => s.PlayerId == id && !s.IgnoreForStats && ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
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
        public async Task<ActionResult<ICollection<PlayerScoreStatsHistory>>> GetHistory(string id, [FromQuery] int count = 50) {
            id = _context.PlayerIdToMain(id);
            var result = _context
                    .PlayerScoreStatsHistory
                    .Where(p => p.PlayerId == id)
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
        public async Task<ActionResult<ICollection<ScoreResponseWithMyScore>>> GetPinnedScores(string id) {
            id = _context.PlayerIdToMain(id);
            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = currentID != null ? _context.Players.Include(p => p.ProfileSettings).Where(p => p.Id == currentID).Select(p => p.ProfileSettings).FirstOrDefault()?.ShowAllRatings ?? false : false;

            var result = _context
                    .Scores
                    .Where(s => s.PlayerId == id && s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .OrderBy(s => s.Metadata.Priority)
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
                            Difficulty = s.Leaderboard.Difficulty
                        },
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak
                    })
                    .ToList();
            foreach (var resultScore in result) {
                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }
            return result;
        }
    }
}
