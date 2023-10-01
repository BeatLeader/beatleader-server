using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class PlayerContextScoresController : Controller {
        private readonly AppContext _context;

        public PlayerContextScoresController(AppContext context) {
            _context = context;
        }

        [NonAction]
        public async Task<(IQueryable<ScoreContextExtension>?, bool, string, string)> ScoresQuery(
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
            bool showRatings = currentID != null ? (_context
                .Players
                .Include(p => p.ProfileSettings)
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings)
                .FirstOrDefault()
                ?.ShowAllRatings ?? false) : false;

            id = _context.PlayerIdToMain(id);

            var player = _context.Players.FirstOrDefault(p => p.Id == id);
            if (player == null) {
                return (null, false, "", "");
            }

            return (_context
               .ScoreContextExtensions
               .Where(t => t.PlayerId == id && t.Context == leaderboardContext)
               .Include(c => c.Score)
               .Filter(_context, !player.Banned, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId),
               showRatings, currentID, id);
        }

        [NonAction]
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
            (
                IQueryable<ScoreContextExtension>? sequence,
                bool showRatings,
                string currentID,
                string userId
            ) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            var result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                }
            };

            var resultList = sequence
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(s => new ScoreResponseWithMyScore {
                        Id = s.ScoreId ?? 0,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        TechPP = s.TechPP,
                        AccPP = s.AccPP,
                        PassPP = s.PassPP,
                        FcAccuracy = s.Score.FcAccuracy,
                        FcPp = s.Score.FcPp,
                        BonusPp = s.BonusPp,
                        Rank = s.Rank,
                        Replay = s.Score.Replay,
                        Modifiers = s.Modifiers,
                        BadCuts = s.Score.BadCuts,
                        MissedNotes = s.Score.MissedNotes,
                        BombCuts = s.Score.BombCuts,
                        WallsHit = s.Score.WallsHit,
                        Pauses = s.Score.Pauses,
                        FullCombo = s.Score.FullCombo,
                        Hmd = s.Score.Hmd,
                        Controller = s.Score.Controller,
                        MaxCombo = s.Score.MaxCombo,
                        Timeset = s.Score.Timeset,
                        ReplaysWatched = s.Score.AnonimusReplayWatched + s.Score.AuthorizedReplayWatched,
                        Timepost = s.Timeset,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Score.Platform,
                        ScoreImprovement = s.Score.ScoreImprovement,
                        Country = s.Score.Country,
                        Offsets = s.Score.ReplayOffsets,
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
                        AccLeft = s.Score.AccLeft,
                        AccRight = s.Score.AccRight,
                        MaxStreak = s.Score.MaxStreak,
                        ContextExtensions = s.Score.ContextExtensions
                    })
                    .ToList();

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

            return result;
        }



        [NonAction]
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
            (IQueryable<ScoreContextExtension>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            return new ResponseWithMetadata<CompactScoreResponse>() {
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
                            EpochTime = s.Timeset,
                            MaxCombo = s.Score.MaxCombo,
                            Hmd = s.Score.Hmd,
                            MissedNotes = s.Score.MissedNotes,
                            BadCuts = s.Score.BadCuts,
                        },
                        Leaderboard = new CompactLeaderboard {
                            Difficulty = s.Leaderboard.Difficulty.Value,
                            SongHash = s.Leaderboard.Song.Hash
                        }
                    })
                    .ToList()
            };
        }

        public class HistogrammValue {
            public int Value { get; set; }
            public int Page { get; set; }
        }

        [NonAction]
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
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] string? type = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null,
            [FromQuery] float? batch = null) {
            (IQueryable<ScoreContextExtension>? sequence, bool showRatings, string currentID, string userId) = await ScoresQuery(id, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            switch (sortBy) {
                case "date":
                    return HistogrammValuee(order, sequence.Select(s => s.Timeset).ToList(), (int)(batch > 60 * 60 ? batch : 60 * 60 * 24), count);
                case "pp":
                    return HistogrammValuee(order, sequence.Select(s => s.Pp).ToList(), batch ?? 5, count);
                case "acc":
                    return HistogrammValuee(order, sequence.Select(s => s.Accuracy).ToList(), batch ?? 0.0025f, count);
                case "pauses":
                    return HistogrammValuee(order, sequence.Select(s => s.Score.Pauses).ToList(), (int)(batch ?? 1), count);
                case "maxStreak":
                    return HistogrammValuee(order, sequence.Select(s => s.Score.MaxStreak ?? 0).ToList(), (int)(batch ?? 1), count);
                case "rank":
                    return HistogrammValuee(order, sequence.Select(s => s.Rank).ToList(), (int)(batch ?? 1), count);
                case "stars":
                    return HistogrammValuee(order, sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToList(), batch ?? 0.15f, count);
                case "replaysWatched":
                    return HistogrammValuee(order, sequence.Select(s => s.Score.AnonimusReplayWatched + s.Score.AuthorizedReplayWatched).ToList(), (int)(batch ?? 1), count);
                case "mistakes":
                    return HistogrammValuee(order, sequence.Select(s => s.Score.BadCuts + s.Score.MissedNotes + s.Score.BombCuts + s.Score.WallsHit).ToList(), (int)(batch ?? 1), count);
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

        [NonAction]
        public ActionResult<ICollection<GraphResponse>> AccGraph(string id, [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
            id = _context.PlayerIdToMain(id);
            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = currentID != null ? _context
                .Players
                .Include(p => p.ProfileSettings)
                .Where(p => p.Id == currentID)
                .Select(p => p.ProfileSettings)
                .FirstOrDefault()?.ShowAllRatings ?? false : false;

            var result = _context
                .ScoreContextExtensions
                .Include(ce => ce.Score)
                .Where(s => s.PlayerId == id && s.Context == leaderboardContext && !s.Score.IgnoreForStats && ((showRatings && s.Leaderboard.Difficulty.Stars != null) || s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked))
                .Select(s => new GraphResponse {
                    LeaderboardId = s.Leaderboard.Id,
                    Diff = s.Leaderboard.Difficulty.DifficultyName,
                    SongName = s.Leaderboard.Song.Name,
                    Hash = s.Leaderboard.Song.Hash,
                    Mapper = s.Leaderboard.Song.Author,
                    Mode = s.Leaderboard.Difficulty.ModeName,
                    Stars = s.Leaderboard.Difficulty.Stars,
                    Acc = s.Accuracy,
                    Timeset = s.Timeset.ToString(),
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
                        foreach (var modifier in score.Modifiers.ToUpper().Split(",")) {
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

        [NonAction]
        public async Task<ActionResult<ICollection<PlayerScoreStatsHistory>>> GetHistory(string id, [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General, [FromQuery] int count = 50) {
            id = _context.PlayerIdToMain(id);
            var result = _context
                    .PlayerScoreStatsHistory
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .OrderByDescending(s => s.Timestamp)
                    .Take(count)
                    .ToList();
            if (result.Count == 0) {
                var player = _context.PlayerContextExtensions.Where(p => p.PlayerId == id && p.Context == leaderboardContext).FirstOrDefault();
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24;
                result = new List<PlayerScoreStatsHistory> { new PlayerScoreStatsHistory { Timestamp = timeset, Rank = player?.Rank ?? 0, Pp = player?.Pp ?? 0, CountryRank = player?.CountryRank ?? 0 } };
            }

            return result;
        }
    }
}

