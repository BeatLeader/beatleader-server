using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class PlayerScoresController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public PlayerScoresController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
        }

        [HttpGet("~/player/{id}/scores")]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> GetScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] string order = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null)
        {
            IQueryable<Score> sequence;

            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(id);
            }
            catch { 
                return BadRequest("Id should be a number");
            }
            AccountLink? link = null;
            if (oculusId < 1000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _readContext.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
                }
            }
            if (link == null && oculusId < 70000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _readContext.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
                }
            }
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : id);

            using (_serverTiming.TimeAction("sequence"))
            {
                sequence = _readContext.Scores.Where(t => t.PlayerId == userId);
                switch (sortBy)
                {
                    case "date":
                        sequence = sequence.Order(order, t => t.Timeset);
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
                    case "maxStreak":
                        sequence = sequence.Where(s => !s.IgnoreForStats).Order(order, t => t.MaxStreak);
                        break;
                    case "timing":
                        sequence = sequence.Order(order, t => (t.LeftTiming + t.RightTiming) / 2);
                        break;
                    case "predictedAcc":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.PredictedAcc)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "passRating":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.PassRating)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "techRating":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.TechRating)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "stars":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.Stars)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    default:
                        break;
                }
                if (search != null)
                {
                    string lowSearch = search.ToLower();
                    sequence = sequence
                        .Include(lb => lb.Leaderboard)
                        .ThenInclude(lb => lb.Song)
                        .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
                }
                if (eventId != null) {
                    var leaderboardIds = _context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefault();
                    if (leaderboardIds?.Count() != 0) {
                        sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                    }
                }
                if (diff != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
                }
                if (stars_from != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
                }
                if (time_from != null)
                {
                    sequence = sequence.Where(s => s.Timepost >= time_from);
                }
                if (time_to != null)
                {
                    sequence = sequence.Where(s => s.Timepost <= time_to);
                }
            }

            ResponseWithMetadata<ScoreResponseWithMyScore> result; 
            using (_serverTiming.TimeAction("db"))
            {
                result = new ResponseWithMetadata<ScoreResponseWithMyScore>()
                {
                    Metadata = new Metadata()
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = sequence.Count()
                    },
                    Data = sequence
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(s => new ScoreResponseWithMyScore
                    {
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
                        Player = new PlayerResponse
                        {
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
                        Leaderboard = new LeaderboardResponse
                        {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
                            Difficulty = s.Leaderboard.Difficulty
                        },
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        PlayCount = s.PlayCount,
                        MaxStreak = s.MaxStreak,
                    })
                    .ToList()
                };
            }

            string? currentID = HttpContext.CurrentUserID(_readContext);
            if (currentID != null && currentID != userId) {
                var leaderboards = result.Data.Select(s => s.LeaderboardId).ToList();

                var myScores = _readContext.Scores.Where(s => s.PlayerId == currentID && leaderboards.Contains(s.LeaderboardId)).Select(RemoveLeaderboard).ToList();
                foreach (var score in result.Data)
                {
                    score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                }
            }

            return result;
        }

        public class CompactScore {
            public int BaseScore { get; set; }
            public int ModifiedScore { get; set; }
            public int MaxCombo { get; set; }
            public int MissedNotes { get; set; }
            public int BadCuts { get; set; }
            public HMD Hmd { get; set; }

            public int EpochTime { get; set; }
        }

        public class CompactLeaderboard {
            public string SongHash { get; set; }
            public int Difficulty { get; set; }
        }

        public class CompactScoreResponse
        {
            public CompactScore Score { get; set; }
            public CompactLeaderboard Leaderboard { get; set; }
        }

        [HttpGet("~/player/{id}/scores/compact")]
        public async Task<ActionResult<ResponseWithMetadata<CompactScoreResponse>>> GetCompactScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] string order = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null)
        {
            IQueryable<Score> sequence;

            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(id);
            }
            catch { 
                return BadRequest("Id should be a number");
            }
            AccountLink? link = null;
            if (oculusId < 1000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _readContext.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
                }
            }
            if (link == null && oculusId < 70000000000000000)
            {
                using (_serverTiming.TimeAction("link"))
                {
                    link = _readContext.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
                }
            }
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : id);

            using (_serverTiming.TimeAction("sequence"))
            {
                sequence = _readContext.Scores.Where(t => t.PlayerId == userId);
                switch (sortBy)
                {
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
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.PredictedAcc)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "passRating":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.PassRating)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "stars":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.Stars)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    default:
                        break;
                }
                if (search != null)
                {
                    string lowSearch = search.ToLower();
                    sequence = sequence
                        .Include(lb => lb.Leaderboard)
                        .ThenInclude(lb => lb.Song)
                        .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
                }
                if (eventId != null) {
                    var leaderboardIds = _context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefault();
                    if (leaderboardIds?.Count() != 0) {
                        sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                    }
                }
                if (diff != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
                }
                if (stars_from != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
                }
                if (time_from != null)
                {
                    sequence = sequence.Where(s => s.Timepost >= time_from);
                }
                if (time_to != null)
                {
                    sequence = sequence.Where(s => s.Timepost <= time_to);
                }
            }

            ResponseWithMetadata<CompactScoreResponse> result; 
            using (_serverTiming.TimeAction("db"))
            {
                result = new ResponseWithMetadata<CompactScoreResponse>()
                {
                    Metadata = new Metadata()
                    {
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
        public async Task<ActionResult> DeleteScore(string id, string leaderboardID)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
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
            return Ok ();

        }

        [HttpGet("~/player/{id}/scorevalue/{hash}/{difficulty}/{mode}")]
        public ActionResult<int> GetScoreValue(string id, string hash, string difficulty, string mode)
        {
            Score? score = _readContext
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .FirstOrDefault(s => s.PlayerId == id && s.Leaderboard.Song.Hash == hash && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode);

            if (score == null)
            {
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
            [FromQuery] string order = "desc",
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? batch = null)
        {
            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence"))
            {
                if (id == "user-friends")
                {
                    string? currentID = HttpContext.CurrentUserID(_readContext);
                    var friends = _readContext.Friends.Where(f => f.Id == currentID).Include(f => f.Friends).FirstOrDefault();
                    if (friends != null)
                    {
                        var friendsList = friends.Friends.Select(f => f.Id).ToList();
                        sequence = _readContext.Scores.Where(s => s.PlayerId == currentID || friendsList.Contains(s.PlayerId));
                    }
                    else
                    {
                        sequence = _readContext.Scores.Where(s => s.PlayerId == currentID);
                    }
                }
                else
                {
                    sequence = _readContext.Scores.Where(t => t.PlayerId == id);
                }

                
                switch (sortBy)
                {
                    case "date":
                        sequence = sequence.Order(order, t => t.Timeset);
                        break;
                    case "pp":
                        sequence = sequence.Where(t => t.Pp > 0).Order(order, t => t.Pp);
                        break;
                    case "acc":
                        sequence = sequence.Order(order, t => t.Accuracy);
                        break;
                    case "pauses":
                        sequence = sequence.Order(order, t => t.Pauses);
                        break;
                    case "maxStreak":
                        sequence = sequence.Where(s => !s.IgnoreForStats).Order(order, t => t.MaxStreak);
                        break;
                    case "rank":
                        sequence = sequence.Order(order, t => t.Rank);
                        break;
                    case "stars":
                        sequence = sequence
                                    .Include(lb => lb.Leaderboard)
                                    .ThenInclude(lb => lb.Difficulty)
                                    .Order(order, s => s.Leaderboard.Difficulty.Stars)
                                    .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    default:
                        break;
                }
                if (search != null)
                {
                    string lowSearch = search.ToLower();
                    sequence = sequence
                        .Include(lb => lb.Leaderboard)
                        .ThenInclude(lb => lb.Song)
                        .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
                }
                if (diff != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
                }
                if (type != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
                }
                if (stars_from != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null)
                {
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
                }
            }

            switch (sortBy)
            {
                case "date":
                    return HistogrammValuee(order, sequence.Select(s => int.Parse(s.Timeset)).ToList(), (int)(batch ?? 60 * 60 * 24), count);
                case "pp":
                    return HistogrammValuee(order, sequence.Select(s => s.Pp).ToList(), batch ?? 5, count);
                case "acc":
                    return HistogrammValuee(order, sequence.Select(s => s.Accuracy).ToList(), batch ?? 0.0025f, count);
                case "pauses":
                    return HistogrammValuee(order, sequence.Select(s => s.Pauses).ToList(), (int)(batch ?? 1), count);
                case "maxStreak":
                    return HistogrammValuee(order, sequence.Select(s => s.MaxStreak).ToList(), (int)(batch ?? 1), count);
                case "rank":
                    return HistogrammValuee(order, sequence.Select(s => s.Rank).ToList(), (int)(batch ?? 1), count);
                case "stars":
                    return HistogrammValuee(order, sequence.Select(s => s.Leaderboard.Difficulty.Stars ?? 0).ToList(), batch ?? 0.15f, count);
                default:
                    return BadRequest();
            }
        }

        public string HistogrammValuee(string order, List<int> values, int batch, int count)
        {
            if (values.Count() == 0) {
                return "";
            }
            Dictionary<int, HistogrammValue> result = new Dictionary<int, HistogrammValue>();
            int normalizedMin = (values.Min() / batch) * batch;
            int normalizedMax = (values.Max() / batch + 1) * batch;
            int totalCount = 0;
            if (order == "desc")
            {
                for (int i = normalizedMax; i > normalizedMin; i -= batch)
                {
                    int value = values.Count(s => s <= i && s >= i - batch);
                    result[i - batch] = new HistogrammValue { Value = value,  Page = totalCount / count };
                    totalCount += value;
                }
            }
            else
            {
                for (int i = normalizedMin; i < normalizedMax; i += batch)
                {
                    int value = values.Count(s => s >= i && s <= i + batch);
                    result[i + batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        public string HistogrammValuee(string order, List<float> values, float batch, int count)
        {
            if (values.Count() == 0) return "";
            Dictionary<float, HistogrammValue> result = new Dictionary<float, HistogrammValue>();
            int totalCount = 0;
            float normalizedMin = (int)(values.Min() / batch) * batch;
            float normalizedMax = (int)(values.Max() / batch + 1) * batch;
            if (order == "desc")
            {
                for (float i = normalizedMax; i > normalizedMin; i -= batch)
                {
                    int value = values.Count(s => s <= i && s >= i - batch);
                    result[i - batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }
            else
            {
                for (float i = normalizedMin; i < normalizedMax; i += batch)
                {
                    int value = values.Count(s => s >= i && s <= i + batch);
                    result[i + batch] = new HistogrammValue { Value = value, Page = totalCount / count };
                    totalCount += value;
                }
            }

            return JsonConvert.SerializeObject(result);
        }

        public class GraphResponse
        {
            public string LeaderboardId { get; set; }
            public string Diff { get; set; }
            public string Mode { get; set; }
            public string Modifiers { get; set; }
            public string SongName { get; set; }
            public string Mapper { get; set; }
            public float Acc { get; set; }
            public string Timeset { get; set; }
            public float Stars { get; set; }
        }

        [HttpGet("~/player/{id}/accgraph")]
        public ActionResult<ICollection<GraphResponse>> GetScoreValue(string id)
        {
            return _readContext
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Where(s => s.PlayerId == id && s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                .Select(s => new GraphResponse
                {
                    LeaderboardId = s.Leaderboard.Id,
                    Diff = s.Leaderboard.Difficulty.DifficultyName,
                    SongName = s.Leaderboard.Song.Name,
                    Mapper = s.Leaderboard.Song.Author,
                    Mode = s.Leaderboard.Difficulty.ModeName,
                    Stars = (float)s.Leaderboard.Difficulty.Stars,
                    Acc = s.Accuracy,
                    Timeset = s.Timeset,
                    Modifiers = s.Modifiers
                })
                .ToList();

        }

        [HttpGet("~/player/{id}/history")]
        public async Task<ActionResult<ICollection<PlayerScoreStatsHistory>>> GetHistory(string id, [FromQuery] int count = 50)
        {
            return _readContext
                    .PlayerScoreStatsHistory
                    .Where(p => p.PlayerId == id)
                    .OrderByDescending(s => s.Timestamp)
                    .Take(count)
                    .ToList();
        }

        [HttpGet("~/player/{id}/pinnedScores")]
        public async Task<ActionResult<ICollection<ScoreResponseWithMyScore>>> GetPinnedScores(string id)
        {
            return _readContext
                    .Scores
                    .Where(s => s.PlayerId == id && s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned)
                    .OrderBy(s => s.Metadata.Priority)
                    .Select(s => new ScoreResponseWithMyScore
                    {
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
                        Player = new PlayerResponse
                        {
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
                        Leaderboard = new LeaderboardResponse
                        {
                            Id = s.LeaderboardId,
                            Song = s.Leaderboard.Song,
                            Difficulty = s.Leaderboard.Difficulty
                        },
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        PlayCount = s.PlayCount,
                        MaxStreak = s.MaxStreak
                    })
                    .ToList();
        }
    }
}
