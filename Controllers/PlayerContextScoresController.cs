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
        public async Task<(IQueryable<ScoreContextExtension>?, string)> ScoresQuery(
            string id,
            bool showRatings,
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

            id = await _context.PlayerIdToMain(id);

            var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == id);
            if (player == null) {
                return (null, "");
            }

            return (await _context
               .ScoreContextExtensions
               .Where(t => t.PlayerId == id && t.Context == leaderboardContext)
               .Include(c => c.Score)
               .Filter(_context, !player.Banned, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, type, modifiers, stars_from, stars_to, time_from, time_to, eventId), id);
        }

        [HttpGet("~/player/{id}/contextscores")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> GetScores(
            string id,
            bool showRatings,
            string currentID,
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
                string userId
            ) = await ScoresQuery(id, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            var result = new ResponseWithMetadata<ScoreResponseWithMyScore>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await sequence.CountAsync()
                }
            };

            var resultList = await sequence
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(s => s.Leaderboard)
                    .ThenInclude(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(s => s.ScoreImprovement)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(s => new ScoreResponseWithMyScoreAndContexts {
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
                        ScoreImprovement = s.ScoreImprovement,
                        Country = s.Score.Country,
                        Offsets = s.Score.ReplayOffsets,
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
                            },
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
                        ContextExtensions = s.Score.ContextExtensions.Select(ce => new ScoreContextExtensionResponse {
                            Id = ce.Id,
                            PlayerId = ce.PlayerId,
        
                            Weight = ce.Weight,
                            Rank = ce.Rank,
                            BaseScore = ce.BaseScore,
                            ModifiedScore = ce.ModifiedScore,
                            Accuracy = ce.Accuracy,
                            Pp = ce.Pp,
                            PassPP = ce.PassPP,
                            AccPP = ce.AccPP,
                            TechPP = ce.TechPP,
                            BonusPp = ce.BonusPp,
                            Modifiers = ce.Modifiers,

                            Context = ce.Context,
                            ScoreImprovement = ce.ScoreImprovement,
                        }).ToList()
                    })
                    .ToListAsync();

            foreach (var resultScore in resultList) {
                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }
            result.Data = resultList;

            if (currentID != null && currentID != userId) {
                var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

                var myScores = await _context
                            .ScoreContextExtensions
                            .Include(ce => ce.Score)
                            .Where(s => s.PlayerId == currentID && s.Context == leaderboardContext && leaderboards.Contains(s.LeaderboardId))
                            .Select(s => new ScoreResponseWithMyScore {
                                    Id = s.Id,
                                    BaseScore = s.BaseScore,
                                    ModifiedScore = s.ModifiedScore,
                                    PlayerId = s.PlayerId,
                                    Accuracy = s.Accuracy,
                                    Pp = s.Pp,
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
                                    Weight = s.Weight,
                                    AccLeft = s.Score.AccLeft,
                                    AccRight = s.Score.AccRight,
                                    Player = s.Player != null ? new PlayerResponse {
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
                                        ContextExtensions = s.Player.ContextExtensions != null ? s.Player.ContextExtensions.Select(ce => new PlayerContextExtension {
                                            Context = ce.Context,
                                            Pp = ce.Pp,
                                            AccPp = ce.AccPp,
                                            TechPp = ce.TechPp,
                                            PassPp = ce.PassPp,

                                            Rank = ce.Rank,
                                            Country  = ce.Country,
                                            CountryRank  = ce.CountryRank,
                                        }).ToList() : null,
                                        Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                                                .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                                    } : null,
                                    ScoreImprovement = s.ScoreImprovement,
                                    RankVoting = s.Score.RankVoting,
                                    Metadata = s.Score.Metadata,
                                    Country = s.Score.Country,
                                    Offsets = s.Score.ReplayOffsets,
                                    MaxStreak = s.Score.MaxStreak
                                })
                            .ToListAsync();
                foreach (var score in result.Data) {
                    score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                }
            }

            return result;
        }



        [NonAction]
        public async Task<ActionResult<ResponseWithMetadata<CompactScoreResponse>>> GetCompactScores(
            string id,
            bool showRatings,
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
            (IQueryable<ScoreContextExtension>? sequence, string userId) = await ScoresQuery(id, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            return new ResponseWithMetadata<CompactScoreResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await sequence.CountAsync()
                },
                Data = await sequence
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Include(s => s.Leaderboard)
                    .Select(s => new CompactScoreResponse {
                        Score = new CompactScore {
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            EpochTime = s.Timeset,
                            FullCombo = s.Score.FullCombo,
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
                    .ToListAsync()
            };
        }

        public class HistogrammValue {
            public int Value { get; set; }
            public int Page { get; set; }
        }

        [NonAction]
        public async Task<ActionResult<string>> GetPlayerHistogram(
            string id,
            bool showRatings,
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
            (IQueryable<ScoreContextExtension>? sequence, string userId) = await ScoresQuery(id, showRatings, sortBy, order, search, diff, mode, requirements, scoreStatus, leaderboardContext, type, modifiers, stars_from, stars_to, time_from, time_to, eventId);
            if (sequence == null) {
                return NotFound();
            }

            switch (sortBy) {
                case "date":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Timeset).ToListAsync(), (int)(batch > 60 * 60 ? batch : 60 * 60 * 24), count);
                case "pp":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Pp).ToListAsync(), Math.Max(batch ?? 5, 1), count);
                case "acc":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Accuracy).ToListAsync(), Math.Max(batch ?? 0.0025f, 0.001f), count);
                case "pauses":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Score.Pauses).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case "maxStreak":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Score.MaxStreak ?? 0).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case "rank":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Rank).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case "stars":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToListAsync(), Math.Max(batch ?? 0.15f, 0.01f), count);
                case "replaysWatched":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Score.AnonimusReplayWatched + s.Score.AuthorizedReplayWatched).ToListAsync(), Math.Max((int)(batch ?? 1), 1), count);
                case "mistakes":
                    return HistogramUtils.GetHistogram(order, await sequence.Select(s => s.Score.BadCuts + s.Score.MissedNotes + s.Score.BombCuts + s.Score.WallsHit).ToListAsync(), (int)(batch ?? 1), count);
                default:
                    return BadRequest();
            }
        }

        [NonAction]
        public async Task<ActionResult<ICollection<GraphResponse>>> AccGraph(
            string id, 
            bool showRatings,
            LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
            id = await _context.PlayerIdToMain(id);

            var result = await _context
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
                .ToListAsync();
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
            id = await _context.PlayerIdToMain(id);
            var result = await _context
                    .PlayerScoreStatsHistory
                    .Where(p => p.PlayerId == id && p.Context == leaderboardContext)
                    .OrderByDescending(s => s.Timestamp)
                    .Take(count)
                    .ToListAsync();
            if (result.Count == 0) {
                var player = await _context.PlayerContextExtensions.Where(p => p.PlayerId == id && p.Context == leaderboardContext).FirstOrDefaultAsync();
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24;
                result = new List<PlayerScoreStatsHistory> { new PlayerScoreStatsHistory { Timestamp = timeset, Rank = player?.Rank ?? 0, Pp = player?.Pp ?? 0, CountryRank = player?.CountryRank ?? 0 } };
            }

            return result;
        }
    }
}

