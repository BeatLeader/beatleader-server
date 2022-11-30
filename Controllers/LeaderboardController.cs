using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Migrations;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Discord;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class LeaderboardController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;
        private readonly SongController _songController;

        private readonly BlobContainerClient _scoreStatsClient;

        public LeaderboardController(AppContext context, ReadAppContext readContext, SongController songController, IOptions<AzureStorageConfig> config, IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;
            _songController = songController;

            if (env.IsDevelopment())
            {
                _scoreStatsClient = new BlobContainerClient(config.Value.AccountName, config.Value.ScoreStatsContainerName);
            }
            else
            {

                string statsEndpoint = $"https://{config.Value.AccountName}.blob.core.windows.net/{config.Value.ScoreStatsContainerName}";

                _scoreStatsClient = new BlobContainerClient(new Uri(statsEndpoint), new DefaultAzureCredential());
            }
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
            var currentContext = _readContext;

            LeaderboardResponse? leaderboard = currentContext
                    .Leaderboards
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
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
                    .ThenInclude(glb => glb.Difficulty)
                    .Select(l => new LeaderboardResponse {
                        Id = l.Id,
                        Song = l.Song,
                        Difficulty = l.Difficulty,
                        Plays = l.Plays,
                        Qualification = l.Qualification,
                        Reweight = l.Reweight,
                        Changes = l.Changes,
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

            if (leaderboard != null) {

                var scoreQuery = currentContext.Scores.Where(s => s.LeaderboardId == leaderboard.Id);
                bool showVoters = false;

                if (voters)
                {
                    string currentID = HttpContext.CurrentUserID(currentContext);
                    var currentPlayer = currentContext.Players.Find(currentID);

                    if (currentPlayer != null && (currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("rankedteam")))
                    {
                        showVoters = true;
                    } else if (currentPlayer?.MapperId != 0 && leaderboard.Song.MapperId == currentPlayer.MapperId) {
                        showVoters = true;
                    }
                }

                List<string>? friendsList = null;

                if (friends)
                {
                    string? currentID = HttpContext.CurrentUserID(currentContext);
                    if (currentID == null)
                    {
                        return NotFound();
                    }
                    var friendsContainer = currentContext
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => f.Friends.Select(fs => fs.Id))
                        .FirstOrDefault();
                    if (friendsContainer != null)
                    {
                        friendsList = friendsContainer.ToList();
                        friendsList.Add(currentID);
                    }
                    else
                    {
                        friendsList = new List<string> { currentID };
                    }
                }

                if (countries == null)
                {
                    if (friendsList != null) {
                        scoreQuery = scoreQuery.Where(s => !s.Banned && friendsList.Contains(s.PlayerId));
                    } else if (voters) {
                        scoreQuery = scoreQuery.Where(s => !s.Banned && s.RankVoting != null);
                    }
                    else {
                        scoreQuery = scoreQuery.Where(s => !s.Banned);
                    }
                } else {
                    if (friendsList != null) {
                        scoreQuery = scoreQuery.Where(s => !s.Banned && friendsList.Contains(s.PlayerId) && countries.ToLower().Contains(s.Player.Country.ToLower()));
                    } else {
                        scoreQuery = scoreQuery.Where(s => !s.Banned && countries.ToLower().Contains(s.Player.Country.ToLower()));
                    }
                }

                leaderboard.Scores = scoreQuery
                    .OrderBy(s => s.Rank)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Include(sc => sc.Player)
                    .ThenInclude(p => p.ProfileSettings)
                    .Include(s => s.Player)
                    .ThenInclude(s => s.Clans)
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
                    .ToList();
                foreach (var score in leaderboard.Scores)
                {
                    score.Player = PostProcessSettings(score.Player);
                }
            }

            if (leaderboard == null) {
                Song? song = currentContext.Songs.Include(s => s.Difficulties).FirstOrDefault(s => s.Difficulties.FirstOrDefault(d => s.Id + d.Value + d.Mode == id) != null);
                if (song == null) {
                    return NotFound();
                } else {
                    DifficultyDescription difficulty = song.Difficulties.First(d => song.Id + d.Value + d.Mode == id);
                    return ResponseFromLeaderboard((await GetByHash(song.Hash, difficulty.DifficultyName, difficulty.ModeName)).Value);
                }
            } else if (leaderboard.Reweight != null && !leaderboard.Reweight.Finished) {
                string currentID = HttpContext.CurrentUserID(currentContext);
                var currentPlayer = await currentContext.Players.FindAsync(currentID);

                if (currentPlayer != null && (currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("rankedteam")))
                {
                    var reweight = leaderboard.Reweight;
                    var recalculated = leaderboard.Scores.Select(s => {

                        s.ModifiedScore = (int)(s.BaseScore * reweight.Modifiers.GetNegativeMultiplier(s.Modifiers));

                        if (leaderboard.Difficulty.MaxScore > 0)
                        {
                            s.Accuracy = (float)s.BaseScore / (float)leaderboard.Difficulty.MaxScore;
                        }
                        else
                        {
                            s.Accuracy = (float)s.BaseScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        }
                        (s.Pp, s.BonusPp) = ReplayUtils.PpFromScoreResponse(s, reweight);

                        return s;
                    });

                    var rankedScores = recalculated.OrderByDescending(el => el.Pp).ToList();
                    foreach ((int i, ScoreResponse s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1 + ((page - 1) * count);
                    }

                    leaderboard.Scores = recalculated;
                }
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/hash/{hash}")]
        public ActionResult<LeaderboardsResponse> GetLeaderboardsByHash(string hash) {
           var leaderboards = _readContext.Leaderboards
                .Where(lb => lb.Song.Hash == hash)
                .Include(lb => lb.Song)
                .ThenInclude(s => s.Difficulties)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Qualification)
                .Include(lb => lb.Reweight)
                .Select(lb => new {
                    Song = lb.Song,
                    Id = lb.Id,
                    Qualification = lb.Qualification,
                    Difficulty = lb.Difficulty,
                    Reweight = lb.Reweight
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
                    Reweight = lb.Reweight
                }).ToList()
            };
        }


        [NonAction]
        public async Task<ActionResult<Leaderboard>> GetByHash(string hash, string diff, string mode) {
            Leaderboard? leaderboard;

            leaderboard = _context
                .Leaderboards
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .FirstOrDefault(lb => lb.Song.Hash == hash && lb.Difficulty.ModeName == mode && lb.Difficulty.DifficultyName == diff);

            if (leaderboard == null) {
                (Song? song, Song? baseSong) = await _songController.GetOrAddSong(hash);
                if (song == null)
                {
                    return NotFound();
                }

                var difficulty = song.Difficulties.FirstOrDefault(d => d.DifficultyName == diff && d.ModeName == mode);
                // Song migrated leaderboards
                if (difficulty is { Status: DifficultyStatus.nominated }) {
                    return await GetByHash(hash, diff, mode);
                } else {
                    leaderboard = await _songController.NewLeaderboard(song, baseSong, diff, mode);
                }
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
            [FromQuery] string? sortBy = null,
            [FromQuery] string? order = null,
            [FromQuery] string? search = null,
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
            sequence = sequence.Filter(_readContext, sortBy, order, search, type, mapType, allTypes, mytype, stars_from, stars_to, date_from, date_to, currentID);

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
                .Include(lb => lb.Song)
                .Include(lb => lb.Reweight)
                .ThenInclude(rew => rew.Modifiers);

            bool showPlays = sortBy == "playcount";

            result.Data = sequence
                .Select(lb => new LeaderboardInfoResponse
                {
                    Id = lb.Id,
                    Song = lb.Song,
                    Difficulty = lb.Difficulty,
                    Qualification = lb.Qualification,
                    Reweight = lb.Reweight,
                    PositiveVotes = lb.PositiveVotes,
                    NegativeVotes = lb.NegativeVotes,
                    VoteStars = lb.VoteStars,
                    StarVotes = lb.StarVotes,
                    MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID).Select(s => new ScoreResponseWithAcc
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
                        Timeset = s.Timeset,
                        Timepost = s.Timepost,
                        ReplaysWatched = s.AuthorizedReplayWatched + s.AnonimusReplayWatched,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight
                    }).FirstOrDefault(),
                    Plays = showPlays ? lb.Scores.Count(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to)) : 0
                });
            return result;
        }

        [HttpGet("~/leaderboards/refresh")]
        public async Task<ActionResult> RefreshLeaderboards()
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var leaderboards = _context.Leaderboards.Include(lb => lb.Scores.Where(s => !s.Banned && s.LeaderboardId != null)).Include(l => l.Difficulty).ToArray();
            int counter = 0;
            var transaction = await _context.Database.BeginTransactionAsync();
            foreach (var leaderboard in leaderboards)
            {
                List<Score>? rankedScores;
                var status = leaderboard.Difficulty.Status;
                if (status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.nominated) {
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
                        await transaction.RollbackAsync();
                        transaction = await _context.Database.BeginTransactionAsync();
                        continue;
                    }
                    await transaction.CommitAsync();
                    transaction = await _context.Database.BeginTransactionAsync();
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
            await transaction.CommitAsync();


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
        public async Task<ActionResult<Models.ScoreStatistic>> RefreshStatistic(string id)
        {
            var blobClient = _scoreStatsClient.GetBlobClient(id + "-leaderboard.json");
            if (await blobClient.ExistsAsync()) {
                return File(await blobClient.OpenReadAsync(), "application/json");
            }
            
            var leaderboard = _context.Leaderboards.Where(lb => lb.Id == id).Include(lb => lb.Scores.Where(s =>
                !s.Banned && s.LeaderboardId != null
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

            var statistics = scoreIds.Select(async id =>
            {
                BlobClient blobClient = _scoreStatsClient.GetBlobClient(id + ".json");
                MemoryStream stream = new MemoryStream(5);
                if (!(await blobClient.ExistsAsync()))
                {
                    return null;
                }
                await blobClient.DownloadToAsync(stream);
                stream.Position = 0;

                return stream.ObjectFromStream<Models.ScoreStatistic>();
            });

            var result = new Models.ScoreStatistic();

            await ReplayStatisticUtils.AverageStatistic(statistics, result);

            await _scoreStatsClient.DeleteBlobIfExistsAsync(id + "-leaderboard.json");
            await _scoreStatsClient.UploadBlobAsync(id + "-leaderboard.json", new BinaryData(JsonConvert.SerializeObject(result)));

            return result;
        }
    }
}
