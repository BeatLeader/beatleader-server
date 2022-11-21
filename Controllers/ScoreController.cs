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
            Player? currentPlayer = currentId != null ? _context.Players.Find(currentId) : null;
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

            _context.SaveChanges();
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

        [HttpGet("~/scores/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshScores([FromQuery] string? leaderboardId = null)
        {
            if (HttpContext != null)
            {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = _context.Players.Find(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            //var count = await _context.Leaderboards.CountAsync();

            //for (int iii = 0; iii < count; iii += 1000) 
            //{
                var query = _context.Leaderboards.Include(s => s.Scores).Include(l => l.Difficulty).ThenInclude(d => d.ModifierValues);
                var allLeaderboards = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query).Select(l => new { Scores = l.Scores, Difficulty = l.Difficulty }).ToList(); // .Skip(iii).Take(1000).ToList();

                int counter = 0;
                var transaction = _context.Database.BeginTransaction();

                foreach (var leaderboard in allLeaderboards)
                {
                    var allScores = leaderboard.Scores.Where(s => !s.Banned && s.LeaderboardId != null).ToList();
                    var status = leaderboard.Difficulty.Status;
                    var modifiers = leaderboard.Difficulty.ModifierValues;
                    bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.nominated || status == DifficultyStatus.inevent;
                    bool hasPp = status == DifficultyStatus.ranked || qualification;

                    foreach (Score s in allScores)
                    {
                        if (hasPp)
                        {
                            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));
                        }
                        else
                        {
                            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetTotalMultiplier(s.Modifiers));
                        }

                        if (leaderboard.Difficulty.MaxScore > 0)
                        {
                            s.Accuracy = (float)s.BaseScore / (float)leaderboard.Difficulty.MaxScore;
                        }
                        else
                        {
                            s.Accuracy = (float)s.BaseScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                        }

                        if (s.Accuracy > 1.29f)
                        {
                            s.Accuracy = 1.29f;
                        }
                        if (hasPp)
                        {
                            (s.Pp, s.BonusPp) = ReplayUtils.PpFromScore(s, leaderboard.Difficulty);
                        }
                        else
                        {
                            s.Pp = 0;
                            s.BonusPp = 0;
                        }

                        s.Qualification = qualification;

                        if (float.IsNaN(s.Pp))
                        {
                            s.Pp = 0.0f;
                        }
                        if (float.IsNaN(s.BonusPp))
                        {
                            s.BonusPp = 0.0f;
                        }
                        if (float.IsNaN(s.Accuracy))
                        {
                            s.Accuracy = 0.0f;
                        }
                        counter++;
                    }

                    var rankedScores = hasPp ? allScores.OrderByDescending(el => el.Pp).ToList() : allScores.OrderByDescending(el => el.ModifiedScore).ToList();
                    foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                    {
                        s.Rank = i + 1;
                    }

                    if (counter >= 5000)
                    {
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
            //}

            return Ok();
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

                result.Pp = ReplayUtils.PpFromScore(s, modifiersObject, (float)stars).Item1;
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
                .Where(s => !s.Banned && s.LeaderboardId != null && s.LeaderboardId == leaderboardId)
                .Include(s => s.Player)
                    .ThenInclude(p => p.Clans)
                .Include(s => s.Player)
                    .ThenInclude(p => p.PatreonFeatures)
                .Include(s => s.Player)
                    .ThenInclude(p => p.ProfileSettings)
                .Include(s => s.Player)
                    .ThenInclude(p => p.Socials)
                .Include(s => s.ScoreImprovement)
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
                currentPlayer = currentPlayer ?? ResponseFromPlayer(_context.Players.Find(player));
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
                    int rank = query.Where(s => s.Rank < playerScore.Rank).Count();
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
                Score? highlightedScore = query.FirstOrDefault(el => el.PlayerId == player);
                if (highlightedScore != null)
                {
                    result.Selection = RemoveLeaderboard(highlightedScore, 0);
                    result.Selection.Player = currentPlayer ?? ResponseFromPlayer(_context.Players.Find(player));
                    if (scope.ToLower() == "friends" || scope.ToLower() == "country") {
                        result.Selection.Rank = query.Where(s => s.Rank < result.Selection.Rank).Count() + 1;
                    }
                }
            }

            result.Metadata.Total = query.Count();

            List<ScoreResponse> resultList = query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(RemoveLeaderboard)
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
            var player = _readContext.Players.Find(userId);
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
            Player? currentPlayer = _context.Players.Find(currentId);
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
                if (scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned).Count() > 9 && pin && (score.Metadata == null || score.Metadata.Status != ScoreStatus.pinned))
                {
                    return BadRequest("Too many scores pinned");
                }
            }
            else
            {
                if (scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned).Count() > 2 && pin && (score.Metadata == null || score.Metadata.Status != ScoreStatus.pinned))
                {
                    return BadRequest("Too many scores pinned");
                }
            }
            ScoreMetadata? metadata = score.Metadata;
            if (metadata == null)
            {
                metadata = new ScoreMetadata
                {
                    Priority = scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned).Count() == 0 ? 1 : scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned).Max(s => s.Metadata.Priority) + 1
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
                    var scoresLower = scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned && s.Metadata.Priority >= priorityValue).ToList();
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
                    var scoresLower = scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned && s.Metadata.Priority <= priorityValue).ToList();
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

            var scoresOrdered = scores.Where(s => s.Metadata != null && s.Metadata.Status == ScoreStatus.pinned).OrderBy(s => s.Metadata.Priority).ToList();
            if (scoresOrdered.Count > 0)
            {
                foreach ((int i, Score p) in scoresOrdered.Select((value, i) => (i, value)))
                {
                    p.Metadata.Priority = i + 1;
                }
            }

            _context.SaveChanges();

            return metadata;
        }
    }
}

