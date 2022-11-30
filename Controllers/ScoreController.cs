using System;
using System.Linq;
using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ScoreController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly BlobContainerClient _replaysClient;
        private readonly BlobContainerClient _scoreStatsClient;

        private readonly IServerTiming _serverTiming;

        public ScoreController(
            AppContext context,
            ReadAppContext readContext,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            IServerTiming serverTiming)
        {
            _context = context;
            _readContext = readContext;
            _serverTiming = serverTiming;
            if (env.IsDevelopment())
            {
                _replaysClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
                _scoreStatsClient = new BlobContainerClient(config.Value.AccountName, config.Value.ScoreStatsContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.ReplaysContainerName);

                _replaysClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());

                string statsEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.ScoreStatsContainerName);

                _scoreStatsClient = new BlobContainerClient(new Uri(statsEndpoint), new DefaultAzureCredential());
            }
        }

        [HttpGet("~/score/{id}")]
        public async Task<ActionResult<Score>> GetScore(int id)
        {
            var score = _readContext
                .Scores
                .Where(l => l.Id == id)
                .Include(el => el.Player).ThenInclude(el => el.PatreonFeatures)
                .Include(el => el.Player).ThenInclude(el => el.ProfileSettings)
                .FirstOrDefault();

            if (score != null)
            {
                return score;
            }
            else
            {
                var redirect = _readContext.ScoreRedirects.FirstOrDefault(sr => sr.OldScoreId == id);
                if (redirect != null && redirect.NewScoreId != id) {
                    return await GetScore(redirect.NewScoreId);
                } else {
                    return NotFound();
                }
            }
        }

        [HttpGet("~/score/random")]
        public async Task<ActionResult<Score>> GetRandomScore()
        {
            var offset = Random.Shared.Next(1, await _context.Scores.CountAsync());
            var score = await _context
                .Scores
                .OrderBy(s => s.Id)
                .Skip(offset)
                .Take(1)
                .FirstOrDefaultAsync();

            if (score != null)
            {
                return score;
            }
            else
            {
                return NotFound();
            }
        }

        [HttpDelete("~/score/{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(int id)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentId != null ? await _context.Players.FindAsync(currentId) : null;
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var score = _context.Scores
                .Where(s => s.Id == id)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Scores)
                .Include(s => s.Player)
                .ThenInclude(p => p.ScoreStats)
                .FirstOrDefault();
            if (score == null)
            {
                return NotFound();
            }

            var log = new ScoreRemovalLog {
                Replay = score.Replay,
                AdminId = currentId,
                Timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            };
            _context.ScoreRemovalLogs.Add(log);

            var leaderboard = score.Leaderboard;

            Player player = score.Player;

            player.ScoreStats.TotalScore -= score.ModifiedScore;
            if (player.ScoreStats.TotalPlayCount == 1)
            {
                player.ScoreStats.AverageAccuracy = 0.0f;
            }
            else
            {
                player.ScoreStats.AverageAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, score.Accuracy);
            }

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
            {
                if (player.ScoreStats.RankedPlayCount == 1)
                {
                    player.ScoreStats.AverageRankedAccuracy = 0.0f;
                }
                else
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, score.Accuracy);
                }
            }
            try
            {
                leaderboard.Scores.Remove(score);
            }
            catch (Exception)
            {
                leaderboard.Scores = new List<Score>(leaderboard.Scores);
                leaderboard.Scores.Remove(score);
            }

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
            {
                player.ScoreStats.RankedPlayCount--;
            }
            player.ScoreStats.TotalPlayCount--;

            var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
            foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
            {
                if (s.Rank != i + 1)
                {
                    s.Rank = i + 1;
                }
            }

            _context.Leaderboards.Update(leaderboard);
            _context.Players.Update(player);

            leaderboard.Plays = rankedScores.Count;

            await _context.SaveChangesAsync();
            _context.RecalculatePP(player);

            var ranked = _context.Players.OrderByDescending(t => t.Pp).ToList();
            var country = player.Country; var countryRank = 1;
            foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                if (p.Country == country)
                {
                    p.CountryRank = countryRank;
                    countryRank++;
                }
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/v4/scores/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<ResponseWithMetadata<SaverScoreResponse>>> GetByHash4(
            string hash,
            string diff,
            string mode,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            ResponseWithMetadata<SaverScoreResponse> result = new ResponseWithMetadata<SaverScoreResponse>
            {
                Data = new List<SaverScoreResponse>(),
                Metadata =
                    {
                        ItemsPerPage = count,
                        Page = page,
                        Total = 0
                    }
            };

            if (hash.Length < 40) {
                return BadRequest("Hash is to short");
            } else {
                hash = hash.Substring(0, 40);
            }

            var song = _context.Songs.Select(s => new { Id = s.Id, Hash = s.Hash }).FirstOrDefault(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = SongUtils.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = _context.CustomModes.FirstOrDefault(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + SongUtils.DiffForDiffName(diff).ToString() + modeValue.ToString();

            IQueryable<Score> query = _context
                .Scores
                .Where(s => !s.Banned && s.LeaderboardId == leaderboardId)
                .OrderBy(p => p.Rank);

            result.Metadata.Total = query.Count();
            result.Data = query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new SaverScoreResponse
                {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    Rank = s.Rank,
                    Modifiers = s.Modifiers,
                    Timeset = s.Timeset,
                    Timepost = s.Timepost,
                    LeaderboardId = s.LeaderboardId,
                    Player = s.Player.Name
                })
                .ToList();

            return result;
        }

        private Score RemovePositiveModifiers(Score s, ModifiersMap? modifiersObject, float? stars)
        {
            Score result = s;

            if (modifiersObject == null) {
                modifiersObject = new ModifiersMap();
            }

            int maxScore = (int)(result.ModifiedScore / result.Accuracy);

            (string modifiers, float value) = modifiersObject.GetNegativeMultipliers(s.Modifiers);

            result.ModifiedScore = (int)(result.BaseScore * value);
            result.Accuracy = (float)result.ModifiedScore / (float)maxScore;
            result.Modifiers = modifiers;
            
            if (result.Pp > 0) {

                result.Pp = ReplayUtils.PpFromScore(s.Accuracy, s.Modifiers, modifiersObject, (float)stars).Item1;
            }

            return result;
        }

        [HttpGet("~/v3/scores/{hash}/{diff}/{mode}/{context}/{scope}/{method}")]
        public async Task<ActionResult<ResponseWithMetadataAndSelection<ScoreResponse>>> GetByHash3(
            string hash,
            string diff,
            string mode,
            string context,
            string scope,
            string method,
            [FromQuery] string player,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            ResponseWithMetadataAndSelection<ScoreResponse> result = new ResponseWithMetadataAndSelection<ScoreResponse>
            {
                Data = new List<ScoreResponse>(),
                Metadata =
                    {
                        ItemsPerPage = count,
                        Page = page,
                        Total = 0
                    }
            };

            if (hash.Length < 40) {
                return BadRequest("Hash is to short");
            } else {
                hash = hash.Substring(0, 40);
            }

            PlayerResponse? currentPlayer = null;
            var song = _context.Songs.Select(s => new { Id = s.Id, Hash = s.Hash }).FirstOrDefault(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = SongUtils.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = _context.CustomModes.FirstOrDefault(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + SongUtils.DiffForDiffName(diff).ToString() + modeValue.ToString();

            IQueryable<Score> query = _context
                .Scores
                .Where(s => !s.Banned && s.LeaderboardId == leaderboardId)
                .OrderBy(p => p.Rank);

            

            if (scope.ToLower() == "friends")
            {
                PlayerFriends? friends = _context.Friends.Include(f => f.Friends).FirstOrDefault(f => f.Id == player);

                if (friends != null) {
                    var idList = friends.Friends.Select(f => f.Id).ToArray();
                    query = query.Where(s => s.PlayerId == player || idList.Contains(s.PlayerId));
                } else {
                    query = query.Where(s => s.PlayerId == player);
                }
            } else if (scope.ToLower() == "country")
            {
                currentPlayer = currentPlayer ?? ResponseFromPlayer(await _context.Players.FindAsync(player));
                if (currentPlayer == null)
                {
                    return result;
                }
                query = query.Where(s => s.Player.Country == currentPlayer.Country);
            }

            //if (context.ToLower() == "standard")
            //{
            //    var modifiers = _context.Leaderboards.Where(lb => lb.Id == leaderboardId).Include(lb => lb.Difficulty).ThenInclude(d => d.ModifierValues).Select(lb => new { ModifierValues = lb.Difficulty.ModifierValues, Stars = lb.Difficulty.Stars }).FirstOrDefault();
            //    query = query.ToList().AsQueryable().Select(s => RemovePositiveModifiers(s, modifiers.ModifierValues, modifiers.Stars)).OrderByDescending(p => p.Accuracy);
            //}

            if (method.ToLower() == "around")
            {
                var playerScore = query.Select(s => new { PlayerId = s.PlayerId, Rank = s.Rank }).FirstOrDefault(el => el.PlayerId == player);
                if (playerScore != null)
                {
                    int rank = query.Count(s => s.Rank < playerScore.Rank);
                    page += (int)Math.Floor((double)(rank) / (double)count);
                    result.Metadata.Page = page;
                }
                else
                {
                    return result;
                }
            }
            else
            {
                ScoreResponse? highlightedScore = query.Where(el => el.PlayerId == player).Select(s => new ScoreResponse
                {
                    Id = s.Id,
                    PlayerId = s.PlayerId,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
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
                    Timepost = s.Timepost,
                    Platform = s.Platform,
                    LeaderboardId = s.LeaderboardId,
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
                        ProfileSettings = s.Player.ProfileSettings,
                        PatreonFeatures = s.Player.PatreonFeatures,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement
                }).FirstOrDefault();
                if (highlightedScore != null)
                {
                    result.Selection = highlightedScore;
                    result.Selection.Player = PostProcessSettings(result.Selection.Player);
                    if (scope.ToLower() == "friends" || scope.ToLower() == "country") {
                        result.Selection.Rank = query.Count(s => s.Rank < result.Selection.Rank) + 1;
                    }
                }
            }

            result.Metadata.Total = query.Count();

            List<ScoreResponse> resultList = query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new ScoreResponse
                {
                    Id = s.Id,
                    PlayerId = s.PlayerId,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
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
                    Timepost = s.Timepost,
                    Platform = s.Platform,
                    LeaderboardId = s.LeaderboardId,
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
                        ProfileSettings = s.Player.ProfileSettings,
                        PatreonFeatures = s.Player.PatreonFeatures,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement
                })
                .ToList();

            foreach (var item in resultList)
            {
                item.Player = PostProcessSettings(item.Player);
            }

            for (int i = 0; i < resultList.Count; i++)
            {
                resultList[i].Rank = i + (page - 1) * count + 1;
            }
            result.Data = resultList;

            return result;
        }


        [HttpGet("~/score/{playerID}/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<Score>> GetPlayer(string playerID, string hash, string diff, string mode)
        {
            var score = _readContext
                .Scores
                .Where(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == playerID)
                .Include(el => el.Player).ThenInclude(el => el.PatreonFeatures)
                .Include(el => el.Player).ThenInclude(el => el.ProfileSettings)
                .FirstOrDefault();

            if (score != null)
            {
                return score;
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("~/user/friendScores")]
        [Authorize]
        public async Task<ActionResult<ResponseWithMetadata<ScoreResponseWithMyScore>>> FriendsScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] string order = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null)
        {
            string userId = HttpContext.CurrentUserID(_readContext);
            var player = await _readContext.Players.FindAsync(userId);
            if (player == null) {
                return NotFound();
            }

            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence"))
            {
                var friends = _readContext.Friends.Where(f => f.Id == player.Id).Include(f => f.Friends).FirstOrDefault();

                if (friends != null)
                {
                    var friendsList = friends.Friends.Select(f => f.Id).ToList();
                    sequence = _readContext.Scores.Where(s => s.LeaderboardId != null && (s.PlayerId == player.Id || friendsList.Contains(s.PlayerId)));
                }
                else
                {
                    sequence = _readContext.Scores.Where(s => s.LeaderboardId != null && s.PlayerId == player.Id);
                }
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
                    case "stars":
                        sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Order(order, t => t.Leaderboard.Difficulty.Stars);
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
                        .Include(lb => lb.Leaderboard)
                        .ThenInclude(lb => lb.Song)
                        .ThenInclude(lb => lb.Difficulties)
                        .Include(lb => lb.Leaderboard)
                        .ThenInclude(lb => lb.Difficulty)
                        .Select(ScoreWithMyScore)
                        .ToList()
                };
            }
            return result;
        }

        [HttpGet("~/score/statistic/{id}")]
        public async Task<ActionResult> GetStatistic(int id)
        {
            var blob = _scoreStatsClient.GetBlobClient(id + ".json");
            return (await blob.ExistsAsync()) ? File(await blob.OpenReadAsync(), "application/json") : NotFound();
        }

        [HttpGet("~/score/calculatestatistic/{id}")]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatistic(string id)
        {
            Score? score = _context.Scores.Where(s => s.Id == Int64.Parse(id)).Include(s => s.Leaderboard).ThenInclude(l => l.Song).FirstOrDefault();
            if (score == null)
            {
                return NotFound("Score not found");
            }
            return await CalculateStatisticScore(score);
        }

        [NonAction]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatisticScore(Score score)
        {
            string blobName = score.Replay.Split("/").Last();

            BlobClient blobClient = _replaysClient.GetBlobClient(blobName);
            if (!await blobClient.ExistsAsync()) return NotFound();
            MemoryStream ms = new MemoryStream(5);
            await blobClient.DownloadToAsync(ms);
            Replay? replay;
            try
            {
                (replay, ReplayOffsets _) = ReplayDecoder.Decode(ms.ToArray());
            }
            catch (Exception)
            {
                return BadRequest("Error decoding replay");
            }

            (ScoreStatistic? statistic, string? error) = CalculateAndSaveStatistic(replay, score);
            if (statistic == null) {
                return BadRequest(error);
            }

            return statistic;
        }

        [NonAction]
        public (ScoreStatistic?, string?) CalculateStatisticFromReplay(Replay? replay, Leaderboard leaderboard)
        {
            ScoreStatistic? statistic;

            if (replay == null)
            {
                return (null, "Could not calculate statistics");
            }

            try
            {
                (statistic, string? error) = ReplayStatisticUtils.ProcessReplay(replay, leaderboard);
                if (statistic == null && error != null) {
                    return (null, error);
                }
            } catch (Exception e) {
                return (null, e.ToString());
            }

            if (statistic == null)
            {
                return (null, "Could not calculate statistics");
            }

            return (statistic, null);
        }

        [NonAction]
        public (ScoreStatistic?, string?) CalculateAndSaveStatistic(Replay? replay, Score score)
        {
            (ScoreStatistic? statistic, string? error) = CalculateStatisticFromReplay(replay, score.Leaderboard);

            if (statistic == null)
            {
                return (null, error);
            }

            _scoreStatsClient.DeleteBlobIfExists(score.Id + ".json");
            _scoreStatsClient.UploadBlob(score.Id + ".json", new BinaryData(JsonConvert.SerializeObject(statistic)));

            return (statistic, null);
        }

        [HttpPut("~/score/{id}/pin")]
        public async Task<ActionResult<ScoreMetadata>> PinScore(
            int id,
            [FromQuery] bool pin,
            [FromQuery] string? description = null,
            [FromQuery] string? link = null,
            [FromQuery] int? priority = null)
        {
            string? currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null)
            {
                return NotFound("Player not found");
            }

            bool hasDescription = Request.Query.ContainsKey("description");
            bool hasLink = Request.Query.ContainsKey("link");

            if (description != null && description.Length > 300)
            {
                return BadRequest("The description is too long");
            }

            var scores = _context
                .Scores
                .Where(s => s.PlayerId == currentPlayer.Id && (s.Id == id || s.Metadata != null))
                .Include(s => s.Metadata)
                .ToList();
            if (scores.Count() == 0 || scores.FirstOrDefault(s => s.Id == id) == null)
            {
                return NotFound("Score not found");
            }

            var score = scores.First(s => s.Id == id);

            if (currentPlayer.Role.Contains("tipper") || currentPlayer.Role.Contains("supporter") || currentPlayer.Role.Contains("sponsor"))
            {
                if (scores.Count(s => s.Metadata is { Status: ScoreStatus.pinned }) > 9 && pin && score.Metadata is not { Status: ScoreStatus.pinned })
                {
                    return BadRequest("Too many scores pinned");
                }
            }
            else
            {
                if (scores.Count(s => s.Metadata is { Status: ScoreStatus.pinned }) > 2 && pin && score.Metadata is not { Status: ScoreStatus.pinned })
                {
                    return BadRequest("Too many scores pinned");
                }
            }
            ScoreMetadata? metadata = score.Metadata;
            if (metadata == null)
            {
                metadata = new ScoreMetadata
                {
                    Priority = scores.Count(s => s.Metadata is { Status: ScoreStatus.pinned }) == 0 ? 1 : scores.Where(s => s.Metadata is { Status: ScoreStatus.pinned }).Max(s => s.Metadata.Priority) + 1
                };
                score.Metadata = metadata;
            }

            if (hasDescription)
            {
                metadata.Description = description;
            }
            if (hasLink)
            {
                if (link != null)
                {
                    (string? service, string? icon) = LinkUtils.ServiceAndIconFromLink(link);
                    if (service == null)
                    {
                        return BadRequest("Unsupported link");
                    }

                    metadata.LinkServiceIcon = icon;
                    metadata.LinkService = service;
                    metadata.Link = link;
                }
                else
                {
                    metadata.LinkServiceIcon = null;
                    metadata.LinkService = null;
                    metadata.Link = null;
                }
            }

            if (pin)
            {
                metadata.Status = ScoreStatus.pinned;
            }
            else
            {
                metadata.Status = ScoreStatus.normal;
            }
            if (priority != null)
            {
                if (!(priority <= scores.Count)) return BadRequest("Priority is out of range");

                int priorityValue = (int)priority;

                if (priorityValue <= metadata.Priority)
                {
                    var scoresLower = scores.Where(s => s.Metadata is { Status: ScoreStatus.pinned } && s.Metadata.Priority >= priorityValue).ToList();
                    if (scoresLower.Count > 0)
                    {
                        foreach (var item in scoresLower)
                        {
                            item.Metadata.Priority++;
                        }
                    }
                }
                else
                {
                    var scoresLower = scores.Where(s => s.Metadata is { Status: ScoreStatus.pinned } && s.Metadata.Priority <= priorityValue).ToList();
                    if (scoresLower.Count > 0)
                    {
                        foreach (var item in scoresLower)
                        {
                            item.Metadata.Priority--;
                        }
                    }
                }

                metadata.Priority = priorityValue;
            }

            var scoresOrdered = scores.Where(s => s.Metadata is { Status: ScoreStatus.pinned }).OrderBy(s => s.Metadata.Priority).ToList();
            if (scoresOrdered.Count > 0)
            {
                foreach ((int i, Score p) in scoresOrdered.Select((value, i) => (i, value)))
                {
                    p.Metadata.Priority = i + 1;
                }
            }

            await _context.SaveChangesAsync();

            return metadata;
        }
    }
}

