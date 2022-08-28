using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class LeaderboardController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;
        private readonly SongController _songController;

        public LeaderboardController(AppContext context, ReadAppContext readContext, SongController songController)
        {
            _context = context;
            _readContext = readContext;
            _songController = songController;
        }

        [HttpGet("~/leaderboard/{id}")]
        public async Task<ActionResult<LeaderboardResponse>> Get(
            string id, 
            [FromQuery] int page = 1, 
            [FromQuery] int count = 10, 
            [FromQuery] string? countries = null,
            [FromQuery] bool friends = false,
            [FromQuery] bool voters = false)
        {
            LeaderboardResponse? leaderboard;
            var currentContext = _readContext;

            var query = currentContext
                    .Leaderboards
                    .Where(lb => lb.Id == id);

            List<string>? friendsList = null;

            if (friends)
            {
                string currentID = HttpContext.CurrentUserID(currentContext);
                if (currentID == null)
                {
                    return NotFound();
                }
                var friendsContainer = currentContext.Friends.Where(f => f.Id == currentID).Include(f => f.Friends).FirstOrDefault();
                if (friendsContainer != null)
                {
                    friendsList = friendsContainer.Friends.Select(f => f.Id).ToList();
                    friendsList.Add(currentID);
                }
                else
                {
                    friendsList = new List<string> { currentID };
                }
            }

            if (voters)
            {
                string currentID = HttpContext.CurrentUserID(currentContext);
                var currentPlayer = currentContext.Players.Find(currentID);

                if (currentPlayer != null && (currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("rankedteam")))
                {
                    query = query.Include(lb => lb.Scores).ThenInclude(s => s.RankVoting).ThenInclude(v => v.Feedbacks);
                } else if (currentPlayer?.MapperId != 0) {
                    int mapperId = currentPlayer.MapperId;
                    query = query.Where(lb => lb.Song.MapperId == mapperId).Include(lb => lb.Scores).ThenInclude(s => s.RankVoting).ThenInclude(v => v.Feedbacks);
                }
            }

            if (countries == null)
            {
                if (friendsList != null) {
                    query = query.Include(lb => lb.Scores
                            .Where(s => !s.Banned && friendsList.Contains(s.PlayerId))
                            .OrderBy(s => s.Rank)
                            .Skip((page - 1) * count)
                            .Take(count))
                        .ThenInclude(s => s.Player)
                        .ThenInclude(s => s.Clans);
                } else if (voters) {
                    query = query.Include(lb => lb.Scores
                            .Where(s => !s.Banned && s.RankVoting != null)
                            .OrderBy(s => s.Rank)
                            .Skip((page - 1) * count)
                            .Take(count))
                        .ThenInclude(s => s.Player)
                        .ThenInclude(s => s.Clans);
                }
                else {
                    query = query.Include(lb => lb.Scores
                            .Where(s => !s.Banned)
                            .OrderBy(s => s.Rank)
                            .Skip((page - 1) * count)
                            .Take(count))
                        .ThenInclude(s => s.Player)
                        .ThenInclude(s => s.Clans);
                }
            } else {
                if (friendsList != null)
                {
                    query = query.Include(lb => lb.Scores
                        .Where(s => !s.Banned && friendsList.Contains(s.PlayerId) && countries.ToLower().Contains(s.Player.Country.ToLower()))
                        .OrderBy(s => s.Rank)
                        .Skip((page - 1) * count)
                        .Take(count))
                    .ThenInclude(s => s.Player)
                    .ThenInclude(s => s.Clans);
                } else {
                    query = query.Include(lb => lb.Scores
                        .Where(s => !s.Banned && countries.ToLower().Contains(s.Player.Country.ToLower()))
                        .OrderBy(s => s.Rank)
                        .Skip((page - 1) * count)
                        .Take(count))
                    .ThenInclude(s => s.Player)
                    .ThenInclude(s => s.Clans);
                }
            }

            leaderboard = query.Include(lb => lb.Scores)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(lb => lb.Qualification)
                    .ThenInclude(q => q.Changes)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties)
                    .Select(ResponseFromLeaderboard)
                    .FirstOrDefault();

            if (leaderboard == null) {
                Song? song = currentContext.Songs.Include(s => s.Difficulties).Where(s => s.Difficulties.FirstOrDefault(d => s.Id + d.Value + d.Mode == id) != null).FirstOrDefault();
                if (song == null) {
                    return NotFound();
                } else {
                    DifficultyDescription difficulty = song.Difficulties.First(d => song.Id + d.Value + d.Mode == id);
                    return ResponseFromLeaderboard((await GetByHash(song.Hash, difficulty.DifficultyName, difficulty.ModeName)).Value);
                }
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/hash/{hash}")]
        public ActionResult<LeaderboardsResponse> GetLeaderboardsByHash(string hash) {
           var leaderboards = _readContext.Leaderboards
                .Where(lb => lb.Song.Hash == hash)
                .Include(lb => lb.Song)
                .Include(lb => lb.Difficulty)
                .Include(lb => lb.Qualification)
                .Select(lb => new {
                    Song = lb.Song,
                    Id = lb.Id,
                    Qualification = lb.Qualification,
                    Difficulty = lb.Difficulty
                })
                .ToList();

            if (leaderboards.Count() == 0) {
                return NotFound();
            }

            return new LeaderboardsResponse
            {
                Song = leaderboards[0].Song,
                Leaderboards = leaderboards.Select(lb => new LeaderboardsInfoResponse {
                    Id = lb.Id,
                    Qualification = lb.Qualification,
                    Difficulty = lb.Difficulty,
                }).ToList()
            };
        }

        [HttpGet("~/leaderboard/hash/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<Leaderboard>> GetByHash(string hash, string diff, string mode) {
            Leaderboard? leaderboard;

            leaderboard = _context
                    .Leaderboards
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Where(lb => lb.Song.Hash == hash && lb.Difficulty.ModeName == mode && lb.Difficulty.DifficultyName == diff)
                    .Include(lb => lb.Statistic)
                    .Include(lb => lb.Scores.Where(s => !s.Banned))
                    .ThenInclude(s => s.RankVoting)
                    .ThenInclude(v => v.Feedbacks)
                    .FirstOrDefault();

            if (leaderboard == null) {
                Song? song = (await _songController.GetHash(hash)).Value;
                if (song == null)
                {
                    return NotFound();
                }

                leaderboard = new Leaderboard();
                leaderboard.SongId = song.Id;
                IEnumerable<DifficultyDescription> difficulties = song.Difficulties.Where(el => el.DifficultyName == diff);
                DifficultyDescription? difficulty = difficulties.FirstOrDefault(x => x.ModeName == mode);
                if (difficulty == null) {
                    difficulty = difficulties.FirstOrDefault(x => x.ModeName == "Standard");
                    if (difficulty == null) {
                        return NotFound();
                    } else {
                        CustomMode? customMode = _context.CustomModes.FirstOrDefault(m => m.Name == mode);
                        if (customMode == null) {
                            customMode = new CustomMode {
                                Name = mode
                            };
                            _context.CustomModes.Add(customMode);
                            await _context.SaveChangesAsync();
                        }

                        difficulty = new DifficultyDescription {
                            Value = difficulty.Value,
                            Mode = customMode.Id + 10,
                            DifficultyName = difficulty.DifficultyName,
                            ModeName = mode,

                            Njs = difficulty.Njs,
                            Nps = difficulty.Nps,
                            Notes = difficulty.Notes,
                            Bombs = difficulty.Bombs,
                            Walls = difficulty.Walls,
                        };
                        song.Difficulties.Add(difficulty);
                        await _context.SaveChangesAsync();
                    }
                }

                leaderboard.Difficulty = difficulty;
                leaderboard.Scores = new List<Score>();
                leaderboard.Id = song.Id + difficulty.Value.ToString() + difficulty.Mode.ToString();

                _context.Leaderboards.Add(leaderboard);
                await _context.SaveChangesAsync();
            }

            if (leaderboard == null)
            {
                return NotFound();
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "stars",
            [FromQuery] string order = "desc",
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] int? mapType = null,
            [FromQuery] bool allTypes = false,
            [FromQuery] string? mytype = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null) {

            var sequence = _readContext.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_readContext);
            switch (sortBy)
            {
                case "timestamp":
                    switch (type)
                    {
                        default:
                            sequence = sequence.Where(s => (date_from == null || s.Difficulty.RankedTime >= date_from) && (date_to == null || s.Difficulty.RankedTime <= date_to))
                            .Order(order, t => t.Difficulty.RankedTime);
                            break;
                        case "nominated":
                            sequence = sequence.Where(s => (date_from == null || s.Difficulty.NominatedTime >= date_from) && (date_to == null || s.Difficulty.NominatedTime <= date_to))
                            .Order(order, t => t.Difficulty.NominatedTime);
                            break;
                        case "qualified":
                            sequence = sequence.Where(s => (date_from == null || s.Difficulty.QualifiedTime >= date_from) && (date_to == null || s.Difficulty.QualifiedTime <= date_to))
                            .Order(order, t => t.Difficulty.QualifiedTime);
                            break;
                    }
                    
                    break;
                case "name":
                    sequence = sequence
                        .Where(s => (date_from == null || s.Song.UploadTime >= date_from) && (date_to == null || s.Song.UploadTime <= date_to))
                        .Order(order == "desc" ? "asc" : "desc", t => t.Song.Name);
                    break;
                case "stars":
                    sequence = sequence
                        .Where(s => (date_from == null || (
                                        (s.Difficulty.Status == DifficultyStatus.nominated && s.Difficulty.NominatedTime >= date_from) ||
                                        (s.Difficulty.Status == DifficultyStatus.qualified && s.Difficulty.QualifiedTime >= date_from) ||
                                        (s.Difficulty.Status == DifficultyStatus.ranked && s.Difficulty.RankedTime >= date_from)
                                        )) 
                                 && (date_to == null || (
                                        (s.Difficulty.Status == DifficultyStatus.nominated && s.Difficulty.NominatedTime <= date_to) ||
                                        (s.Difficulty.Status == DifficultyStatus.qualified && s.Difficulty.QualifiedTime <= date_to) ||
                                        (s.Difficulty.Status == DifficultyStatus.ranked && s.Difficulty.RankedTime <= date_to)
                                        )))
                        .Include(lb => lb.Difficulty).Order(order, t => t.Difficulty.Stars);
                    break;
                case "scoreTime":
                    if (mytype == "played") {
                        sequence = sequence
                            .Order(order, lb => 
                                lb.Scores
                                    .Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to) && s.PlayerId == currentID)
                                    .Max(s => s.Timepost));
                    } else {
                        sequence = sequence
                            .Order(order, lb => 
                                lb.Scores
                                    .Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to))
                                    .Max(s => s.Timepost));
                    }
                    
                    break;
                case "playcount":
                    sequence = sequence
                        .Order(order, lb => 
                            lb.Scores
                                .Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to))
                                .Count());
                    break;
                case "voting":
                    sequence = sequence
                        .Order(order, lb => 
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting.Rankability > 0).Count() 
                            - 
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting.Rankability <= 0).Count());
                    break;
                case "votecount":
                    sequence = sequence
                        .Order(order, lb => 
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null)
                                .Count());
                    break;
                case "voteratio":
                    sequence = sequence
                        .Where(lb => 
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null)
                                .FirstOrDefault() != null)
                        .Order(order, lb => (int)(
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting.Rankability > 0).Count() 
                            / 
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null).Count() * 100.0))
                        .ThenOrder(order, lb => 
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null).Count());
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Include(lb => lb.Song)
                    .Where(p => p.Song.Author.ToLower().Contains(lowSearch) ||
                                p.Song.Mapper.ToLower().Contains(lowSearch) ||
                                p.Song.Name.ToLower().Contains(lowSearch));
            }
            if (diff != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
            }
            
            if (type != null && type.Length != 0)
            {
                switch (type) {
                    case "ranked":
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "nominated":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .Where(p => p.Difficulty.Status == DifficultyStatus.nominated);
                        break;
                    case "qualified":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .Where(p => p.Difficulty.Status == DifficultyStatus.qualified);
                        break;
                    case "unranked":
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Status == DifficultyStatus.unranked);
                        break;
                }
            }
            if (mapType != null) {
                int maptype = (int)mapType;
                if (allTypes) {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Type == maptype);
                } else {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => (p.Difficulty.Type & maptype) != 0);
                }
            }

            if (mytype != null && mytype.Length != 0)
            {
                switch (mytype)
                {
                    case "played":
                        sequence = sequence.Where(p => p.Scores.FirstOrDefault(s => s.PlayerId == currentID) != null);
                        break;
                    case "unplayed":
                        sequence = sequence.Where(p => p.Scores.FirstOrDefault(s => s.PlayerId == currentID) == null);
                        break;
                    case "mynominated":
                        sequence = sequence.Where(p => p.Qualification != null && p.Qualification.RTMember == currentID);
                        break;
                    case "othersnominated":
                        sequence = sequence.Where(p => p.Qualification != null && p.Qualification.RTMember != currentID);
                        break;
                    case "mymaps":
                        var currentPlayer = _readContext.Players.Find(currentID);
                        sequence = sequence.Where(p => p.Song.MapperId == currentPlayer.MapperId);
                        break;
                }
            }

            if (stars_from != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Stars <= stars_to);
            }

            bool showVoting = false;
            bool showVotingDetails = false;
            if (sortBy == "voting" || sortBy == "votecount" || sortBy == "voteratio") {
                showVoting = true;
                if (currentID != null)
                {
                    var currentPlayer = _readContext.Players.Find(currentID);

                    showVotingDetails = currentPlayer != null && (currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("rankedteam"));
                }
            }

            var result = new ResponseWithMetadata<LeaderboardInfoResponse>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                }
            };

            sequence = sequence.Skip((page - 1) * count)
                .Take(count)
                .Include(lb => lb.Difficulty)
                .ThenInclude(lb => lb.ModifierValues)
                .Include(lb => lb.Song);

            bool showPlays = sortBy == "playcount";

            if (showVoting) {
                result.Data = sequence

                .Include(lb => lb.Scores)
                .ThenInclude(lb => lb.RankVoting)
                .Select(lb => new LeaderboardInfoResponse
                {
                    Id = lb.Id,
                    Song = lb.Song,
                    Difficulty = lb.Difficulty,
                    Qualification = lb.Qualification,
                    MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID).Select(s => new ScoreResponseWithAcc
                    {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
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
                        Timeset = s.Timeset,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight
            }).FirstOrDefault(),
                    Plays = showPlays ? lb.Scores.Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to)).Count() : 0,
                    Votes = lb.Scores
                        .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null)
                        .Select(s => new VotingResponse
                        {
                            Rankability = s.RankVoting.Rankability,
                            Stars = showVotingDetails ? s.RankVoting.Stars : 0,
                            Type = showVotingDetails ? s.RankVoting.Type : 0,
                            Timeset = showVotingDetails ? s.RankVoting.Timeset : 0,
                        })
                }).ToList();
            } else {
                result.Data = sequence
                .Select(lb => new LeaderboardInfoResponse
                {
                    Id = lb.Id,
                    Song = lb.Song,
                    Difficulty = lb.Difficulty,
                    Qualification = lb.Qualification,
                    MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID).Select(s => new ScoreResponseWithAcc
                    {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
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
                        Timeset = s.Timeset,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight
                    }).FirstOrDefault(),
                    Plays = showPlays ? lb.Scores.Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to)).Count() : 0,
                }).ToList();
            }

            return result;
        }

        [HttpGet("~/leaderboards/refresh")]
        public async Task<ActionResult> RefreshLeaderboards()
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var leaderboards = _context.Leaderboards.Include(lb => lb.Scores.Where(s => !s.Banned)).Include(l => l.Difficulty).ToArray();
            int counter = 0;
            var transaction = _context.Database.BeginTransaction();
            foreach (var leaderboard in leaderboards)
            {
                List<Score>? rankedScores;
                var status = leaderboard.Difficulty.Status;
                if (status == DifficultyStatus.ranked || status == DifficultyStatus.qualified || status == DifficultyStatus.nominated) {
                    rankedScores = leaderboard.Scores.OrderByDescending(el => el.Pp).ToList();
                } else {
                    rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
                }
                if (rankedScores.Count > 0) {
                    foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                    }
                }
                counter++;
                if (counter == 100) {
                    counter = 0;
                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception e)
                    {

                        _context.RejectChanges();
                        transaction.Rollback();
                        transaction = _context.Database.BeginTransaction();
                        continue;
                    }
                    transaction.Commit();
                    transaction = _context.Database.BeginTransaction();
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _context.RejectChanges();
            }
            transaction.Commit();


            return Ok();
        }

        public class LeaderboardVoting
        {
            public float Rankability { get; set; }
            public float Stars { get; set; }
            public float[] Type { get; set; } = new float[4];
        }

        public class LeaderboardVotingCounts
        {
            public int Rankability { get; set; }
            public int Stars { get; set; }
            public int Type { get; set; }
        }

        [HttpGet("~/leaderboard/ranking/{id}")]
        public ActionResult<LeaderboardVoting> GetVoting(string id)
        {
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
                    

            if (rankVotings == null || rankVotings.Count == 0)
            {
                return NotFound();
            }

            var result = new LeaderboardVoting();
            var counters = new LeaderboardVotingCounts();

            foreach (var voting in rankVotings)
            {
                counters.Rankability++;
                result.Rankability += voting.Rankability;

                if (voting.Stars != 0) {
                    counters.Stars++;
                    result.Stars += voting.Stars;
                }

                if (voting.Type != 0) {
                    counters.Type++;

                    for (int i = 0; i < 4; i++)
                    {
                        if ((voting.Type & (1 << i)) != 0) {
                            result.Type[i]++;
                        }
                    }
                }
            }
            result.Rankability /= (counters.Rankability != 0 ? (float)counters.Rankability : 1.0f);
            result.Stars /= (counters.Stars != 0 ? (float)counters.Stars : 1.0f);

            for (int i = 0; i < result.Type.Length; i++)
            {
                result.Type[i] /= (counters.Type != 0 ? (float)counters.Type : 1.0f);
            }

            return result;
        }

        [HttpGet("~/leaderboard/statistic/{id}")]
        public async Task<ActionResult<LeaderboardStatistic>> RefreshStatistic(string id)
        {
            var result = _context.LeaderboardStatistics
                .Where(st => st.LeaderboardId == id)
                .Include(st => st.WinTracker)
                .Include(st => st.AccuracyTracker)
                .Include(st => st.ScoreGraphTracker)
                .Include(st => st.HitTracker).FirstOrDefault();

            if (result == null || !result.Relevant) {
                var leaderboard = _context.Leaderboards.Where(lb => lb.Id == id).Include(lb => lb.Scores.Where(s =>
                    !s.Banned
                    && !s.Modifiers.Contains("SS")
                    && !s.Modifiers.Contains("NA")
                    && !s.Modifiers.Contains("NB")
                    && !s.Modifiers.Contains("NF")
                    && !s.Modifiers.Contains("NO"))).FirstOrDefault();
                if (leaderboard == null || leaderboard.Scores.Count == 0)
                {
                    return NotFound();
                }

                var scoreIds = leaderboard.Scores.Select(s => s.Id);

                var statistics = _context.ScoreStatistics
                    .Where(st => scoreIds.Contains(st.ScoreId))
                    .Include(st => st.WinTracker)
                    .Include(st => st.AccuracyTracker)
                    .Include(st => st.ScoreGraphTracker)
                    .Include(st => st.HitTracker).ToList();

                result = new LeaderboardStatistic { Relevant = true, LeaderboardId = leaderboard.Id };

                statistics.ForEach(ReplayStatisticUtils.DecodeArrays);
                ReplayStatisticUtils.AverageStatistic(statistics, result);
                ReplayStatisticUtils.EncodeArrays(result);
                leaderboard.Statistic = result;
                await _context.SaveChangesAsync();
            } else {
                ReplayStatisticUtils.DecodeArrays(result);
            }

            return result;
        }
    }
}
