using System;
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
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ScoreController : Controller
    {
        private readonly AppContext _context;
        private readonly BlobContainerClient _containerClient;
        private readonly PlayerController _playerController;
        private readonly IServerTiming _serverTiming;

        public ScoreController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            PlayerController playerController,
            IServerTiming serverTiming)
        {
            _context = context;
            _playerController = playerController;
            _serverTiming = serverTiming;
            if (env.IsDevelopment())
            {
                _containerClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.ReplaysContainerName);

                _containerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
        }

        [HttpGet("~/score/{id}")]
        public async Task<ActionResult<Score>> GetScore(int id)
        {
            var score = await _context
                .Scores
                .Where(l => l.Id == id)
                .Include(el => el.Player).ThenInclude(el => el.PatreonFeatures)
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
            string currentId = HttpContext.CurrentUserID();
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

            if (leaderboard.Difficulty.Ranked)
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

            if (leaderboard.Difficulty.Ranked)
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
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID();
                Player? currentPlayer = _context.Players.Find(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var query = _context.Leaderboards.Where(l => l.Difficulty.Ranked).Include(s => s.Scores).Include(l => l.Difficulty);
            var allLeaderboards = (leaderboardId != null ? query.Where(s => s.Id == leaderboardId) : query).Select(l => new { Scores = l.Scores, Difficulty = l.Difficulty }).ToList();

            var transaction = _context.Database.BeginTransaction();

            foreach (var leaderboard in allLeaderboards) {
                var allScores = leaderboard.Scores.Where(s => !s.Banned).ToList();
                foreach (Score s in allScores)
                {
                    s.ModifiedScore = (int)((float)s.BaseScore * ReplayUtils.GetTotalMultiplier(s.Modifiers));
                    if (leaderboard.Difficulty.MaxScore > 0)
                    {
                        s.Accuracy = (float)s.ModifiedScore / (float)leaderboard.Difficulty.MaxScore;
                    }
                    else
                    {
                        s.Accuracy = (float)s.ModifiedScore / (float)ReplayUtils.MaxScoreForNote(leaderboard.Difficulty.Notes);
                    }
                    if (leaderboard.Difficulty.Ranked) {
                        s.Pp = (float)s.Accuracy * (float)leaderboard.Difficulty.Stars * 44;
                    }
                    _context.Scores.Update(s);
                }

                var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
                foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                }
                try {
                    await _context.SaveChangesAsync();
                } catch (Exception e) {
                    _context.RejectChanges();
                }
            }

            return Ok();
        }

        [HttpGet("~/scores/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<IEnumerable<Score>>> GetByHash(string hash, string diff, string mode, [FromQuery] string? country, [FromQuery] string? player, [FromQuery] int page = 1, [FromQuery] int count = 8)
        {
            var leaderboard = _context.Leaderboards.Include(el => el.Song).Include(el => el.Difficulty).FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null)
            {
                IEnumerable<Score> query = _context.Leaderboards.Include(el => el.Scores).ThenInclude(s => s.Player).First(el => el.Id == leaderboard.Id).Scores;
                if (query.Count() == 0)
                {
                    return new List<Score>();
                }
                if (country != null)
                {
                    query = query.Where(s => s.Player.Country == country);
                }
                if (player != null)
                {
                    Score? playerScore = query.FirstOrDefault(el => el.Player.Id == player);
                    if (playerScore != null)
                    {
                        page = (int)Math.Floor((double)(playerScore.Rank - 1) / (double)count) + 1;
                    }
                    else
                    {
                        return new List<Score>();
                    }
                }
                return query.OrderByDescending(p => p.ModifiedScore).Skip((page - 1) * count).Take(count).ToArray();
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("~/failedscores/")]
        public async Task<ActionResult<IEnumerable<FailedScore>>> FailedSсores()
        {
            return _context.FailedScores.OrderByDescending(s => s.Id).Include(el => el.Leaderboard).Include(el => el.Player).ToList();
        }

        public ScoreResponse SetRank(ScoreResponse s, int i)
        {
            s.Rank = i >= 0 ? i + 1 : s.Rank;
            return s;
        }

        private ScoreResponse RemovePositiveModifiers(ScoreResponse s)
        {
            ScoreResponse result = s;

            int maxScore = (int)(result.ModifiedScore / result.Accuracy);
            if (result.Pp > 0)
            {
                result.Pp /= result.Accuracy;
            }

            (string modifiers, float value) = ReplayUtils.GetNegativeMultipliers(s.Modifiers);

            result.ModifiedScore = (int)(result.BaseScore * value);
            result.Accuracy = (float)result.ModifiedScore / (float)maxScore;
            result.Modifiers = modifiers;

            if (result.Pp > 0)
            {
                result.Pp *= result.Accuracy;
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
            var song = await _context.Songs.Select(s => new { Id = s.Id, Hash = s.Hash }).FirstOrDefaultAsync(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            int modeValue = SongUtils.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + SongUtils.DiffForDiffName(diff).ToString() + modeValue.ToString();

            IEnumerable<ScoreResponse> query = _context
                .Scores
                .Where(s => !s.Banned && s.LeaderboardId == leaderboardId)
                .Include(s => s.Player)
                    .ThenInclude(p => p.Clans)
                .Include(s => s.Player)
                .ThenInclude(p => p.PatreonFeatures)
                .Include(s => s.ScoreImprovement)
                .Select(RemoveLeaderboard)
                .ToList();

            if (query.Count() == 0)
            {
                return result;
            }

            if (context.ToLower() == "standard")
            {
                query = query.Select(RemovePositiveModifiers);
            }

            query = query.OrderByDescending(p => p.ModifiedScore);
            //Dictionary<string, int> countries = new Dictionary<string, int>();
            //query = query.Select((s, i) => {
            //    if (s.CountryRank == 0) {
            //        if (!countries.ContainsKey(s.Player.Country))
            //        {
            //            countries[s.Player.Country] = 1;
            //        }

            //        s.CountryRank = countries[s.Player.Country];
            //        countries[s.Player.Country]++;
            //    }
            //    return s;
            //});
            if (scope.ToLower() == "friends")
            {
                PlayerFriends? friends = await _context.Friends.Include(f => f.Friends).FirstOrDefaultAsync(f => f.Id == player);
                if (friends != null) {
                    query = query.Where(s => s.PlayerId == player || friends.Friends.FirstOrDefault(f => f.Id == s.PlayerId) != null);
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

            if (method.ToLower() == "around")
            {
                ScoreResponse? playerScore = query.FirstOrDefault(el => el.PlayerId == player);
                if (playerScore != null)
                {
                    int rank = query.TakeWhile(s => s.PlayerId != player).Count();
                    page += (int)Math.Floor((double)(rank) / (double)count);
                    result.Metadata.Page = page;
                }
                else
                {
                    return result;
                }
            } else
            {
                ScoreResponse? highlightedScore = query.FirstOrDefault(el => el.PlayerId == player);
                if (highlightedScore != null)
                {
                    int rank = query.TakeWhile(s => s.PlayerId != player).Count();
                    result.Selection = SetRank(highlightedScore, rank);
                    result.Selection.Player = currentPlayer ?? ResponseFromPlayer(_context.Players.Find(player));
                }
            }

            List<ScoreResponse> resultList = query
                .Select(SetRank)
                .Skip((page - 1) * count)
                .Take(count)
                .ToList();
            result.Metadata.Total = query.Count();
            result.Data = resultList;

            return result;
        }


        [HttpGet("~/score/{playerID}/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<Score>> GetPlayer(string playerID, string hash, string diff, string mode)
        {
            Int64 oculusId = Int64.Parse(playerID);
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
            string userId = (link != null ? link.SteamID : playerID);

            var score = await _context
                .Scores
                .Where(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId)
                .Include(el => el.Player).ThenInclude(el => el.PatreonFeatures)
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

        [HttpGet("~/user/friendScores")]
        [Authorize]
        public async Task<ActionResult<ResponseWithMetadata<Score>>> FriendsScores(
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
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

            string userId = accountLink != null ? accountLink.SteamID : currentID;
            var player = await _context.Players.FindAsync(userId);
            if (player == null) {
                return NotFound();
            }

            IQueryable<Score> sequence;

            using (_serverTiming.TimeAction("sequence"))
            {
                var friends = await _context.Friends.Where(f => f.Id == player.Id).Include(f => f.Friends).FirstOrDefaultAsync();
                if (friends != null)
                {
                    var friendsList = friends.Friends.Select(f => f.Id).ToList();
                    sequence = _context.Scores.Where(s => s.PlayerId == player.Id || friendsList.Contains(s.PlayerId));
                }
                else
                {
                    sequence = _context.Scores.Where(s => s.PlayerId == player.Id);
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
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Ranked : !p.Leaderboard.Difficulty.Ranked);
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

            ResponseWithMetadata<Score> result;
            using (_serverTiming.TimeAction("db"))
            {
                result = new ResponseWithMetadata<Score>()
                {
                    Metadata = new Metadata()
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = sequence.Count()
                    },
                    Data = await sequence.Skip((page - 1) * count).Take(count).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).ToListAsync()
                };
            }
            return result;
        }

        [HttpGet("~/score/statistic/{id}")]
        public async Task<ActionResult<ScoreStatistic>> GetStatistic(int id)
        {
            ScoreStatistic? scoreStatistic = _context.ScoreStatistics.Where(s => s.ScoreId == id).Include(s => s.AccuracyTracker).Include(s => s.HitTracker).Include(s => s.ScoreGraphTracker).Include(s => s.WinTracker).FirstOrDefault();
            if (scoreStatistic == null)
            {
                return NotFound();
            }
            ReplayStatisticUtils.DecodeArrays(scoreStatistic);
            return scoreStatistic;
        }

        [HttpGet("~/score/calculatestatistic/players")]
        public async Task<ActionResult> CalculateStatisticPlayers()
        {
            var players = _context.Players.ToList();
            foreach (Player p in players)
            {
                await CalculateStatisticPlayer(p.Id);
            }

            return Ok();
        }

        [HttpGet("~/score/calculatestatistic/player/{id}")]
        public async Task<ActionResult> CalculateStatisticPlayer(string id)
        {
           Player? player = _context.Players.Find(id);
            if (player == null)
            {
                return NotFound();
            }

            var scores = _context.Scores.Where(s => s.PlayerId == id).Include(s => s.Leaderboard).ThenInclude(l => l.Song).ToArray();
            foreach (var score in scores)
            {
                await CalculateStatisticScore(score);
            }
            return Ok();
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

            BlobClient blobClient = _containerClient.GetBlobClient(blobName);
            MemoryStream ms = new MemoryStream(5);
            await blobClient.DownloadToAsync(ms);
            Replay replay;
            try
            {
                replay = ReplayDecoder.Decode(ms.ToArray());
            }
            catch (Exception)
            {
                return BadRequest("Error decoding replay");
            }

            (ScoreStatistic? statistic, string? error) = CalculateStatisticReplay(replay, score);
            if (statistic == null) {
                return BadRequest(error);
            }

            return statistic;
        }

        [NonAction]
        public (ScoreStatistic?, string?) CalculateStatisticReplay(Replay replay, Score score)
        {
            ScoreStatistic? statistic = null;

            try
            {
                statistic = ReplayStatisticUtils.ProcessReplay(replay, score.Leaderboard);
                statistic.ScoreId = score.Id;
                ReplayStatisticUtils.EncodeArrays(statistic);
            } catch (Exception e) {
                return (null, e.ToString());
            }

            if (statistic == null)
            {
                return (null, "Could not calculate statistics");
            }

            ScoreStatistic? currentStatistic = _context.ScoreStatistics.FirstOrDefault(s => s.ScoreId == score.Id);
            _context.ScoreStatistics.Add(statistic);
            if (currentStatistic != null)
            {
                _context.ScoreStatistics.Remove(currentStatistic);
            }
            _context.SaveChanges();

            return (statistic, null);
        }
    }
}

