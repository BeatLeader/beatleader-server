using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ScoreController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        private readonly LeaderboardController _leaderboardController;

        public ScoreController(
            AppContext context,
            ReadAppContext readContext,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            LeaderboardController leaderboardController)
        {
            _context = context;
            _readContext = readContext;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
            _leaderboardController = leaderboardController;
        }

        [HttpGet("~/score/{id}")]
        public async Task<ActionResult<ScoreResponse>> GetScore(int id)
        {
            var score = _readContext
                .Scores
                .Where(l => l.Id == id)
                .Select(s => new ScoreResponse {
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
                    Offsets = s.ReplayOffsets,
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
                .FirstOrDefault();

            if (score != null)
            {
                score.Player = PostProcessSettings(score.Player);
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

            await GeneralSocketController.ScoreWasRejected(score, _configuration, _context);

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
            if (page < 1) {
                return BadRequest("Page should be greater than zero!");
            }

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

        [HttpGet("~/v5/scores/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<SaverScoreResponse, SaverContainerResponse>>> GetByHash5(
            string hash,
            string diff,
            string mode,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            if (page < 1) {
                return BadRequest("Page should be greater than zero!");
            }

            var result = new ResponseWithMetadataAndContainer<SaverScoreResponse, SaverContainerResponse>
            {
                Data = new List<SaverScoreResponse>(),
                Container = new SaverContainerResponse(),
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

            var leaderboard = (await _leaderboardController.GetByHash(hash, diff, mode, false)).Value;
            if (leaderboard == null) {
                return result;
            }

            IQueryable<Score> query = _context
                .Scores
                .Where(s => !s.Banned && s.LeaderboardId == leaderboard.Id)
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

            result.Container.LeaderboardId = leaderboard.Id;
            result.Container.Ranked = leaderboard.Difficulty.Status == DifficultyStatus.ranked;

            return result;
        }

        public static string FilterOutPositiveModifiers(string modifiers, ModifiersMap modifiersObject)
        {
            List<string> modifierArray = new List<string>();
            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier)) {
                    if (modifiersMap[modifier] < 0) {
                        modifierArray.Add(modifier);
                    }
                }
            }

            return String.Join(",", modifierArray);
        }

        private ScoreResponse RemovePositiveModifiers(ScoreResponse s, DifficultyDescription difficulty) {
            ScoreResponse result = s;

            var status = difficulty.Status;
            var modifiers = difficulty.ModifierValues ?? new ModifiersMap();

            bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
            bool hasPp = status == DifficultyStatus.ranked || qualification;

            s.Modifiers = FilterOutPositiveModifiers(s.Modifiers, modifiers);
            s.ModifiedScore = (int)(s.BaseScore * modifiers.GetNegativeMultiplier(s.Modifiers));

            if (hasPp)
            {
                (s.Pp, s.BonusPp, s.PassPP, s.AccPP, s.TechPP) = ReplayUtils.PpFromScoreResponse(s, difficulty);
            }
            else
            {
                s.Pp = 0;
                s.BonusPp = 0;
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

            PlayerResponse? currentPlayer = 
                await _context
                .Players
                .Select(p => new PlayerResponse {
                    Id = p.Id,
                    Name = p.Name,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    Role = p.Role,
                    Socials = p.Socials,
                    ProfileSettings = p.ProfileSettings,
                    Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                })
                .FirstOrDefaultAsync(p => p.Id == player);
            var song = _readContext.Songs.Select(s => new { Id = s.Id, Hash = s.Hash }).FirstOrDefault(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = SongUtils.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = _readContext.CustomModes.FirstOrDefault(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + SongUtils.DiffForDiffName(diff).ToString() + modeValue.ToString();

            bool showBots = currentPlayer?.ProfileSettings?.ShowBots ?? false;
            IQueryable<Score> query = _readContext
                .Scores
                .Where(s => (!s.Banned || (s.Bot && showBots)) && s.LeaderboardId == leaderboardId)
                .OrderBy(p => p.Rank);

            

            if (scope.ToLower() == "friends")
            {
                PlayerFriends? friends = _readContext.Friends.Include(f => f.Friends).FirstOrDefault(f => f.Id == player);

                if (friends != null) {
                    var idList = friends.Friends.Select(f => f.Id).ToArray();
                    query = query.Where(s => s.PlayerId == player || idList.Contains(s.PlayerId));
                } else {
                    query = query.Where(s => s.PlayerId == player);
                }
            } else if (scope.ToLower() == "country")
            {
                currentPlayer = currentPlayer ?? ResponseFromPlayer(await _readContext.Players.FindAsync(player));
                if (currentPlayer == null)
                {
                    return result;
                }
                query = query.Where(s => s.Player.Country == currentPlayer.Country);
            }

            if (context.ToLower() == "standard") {
                query = query
                    .Where(s => s.ModifiedScore >= s.BaseScore)
                    .OrderByDescending(p => p.Accuracy);
            }

            if (method.ToLower() == "around")
            {
                var playerScore = query.Select(s => new { s.PlayerId, s.Rank }).FirstOrDefault(el => el.PlayerId == player);
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
                    Offsets = s.ReplayOffsets,
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

                if (page < 1) {
                    page = 1;
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
                    Offsets = s.ReplayOffsets,
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

                        Bot = s.Player.Bot,
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

            if (context.ToLower() == "standard") {
                var difficulty = _context
                    .Leaderboards
                    .Where(lb => lb.Id == leaderboardId)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifiersRating)
                    .Include(lb => lb.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Select(lb => lb.Difficulty)
                    .FirstOrDefault();

                if (difficulty != null) {
                    foreach (var item in resultList) {
                        RemovePositiveModifiers(item, difficulty);
                    }
                }
            }

            for (int i = 0; i < resultList.Count; i++)
            {
                var score = resultList[i];

                score.Player = PostProcessSettings(score.Player);
                score.Rank = i + (page - 1) * count + 1;

                if (score.Player.Bot) {
                    score.Player.Name += " [BOT]";
                }
            }
            result.Data = resultList;

            return result;
        }


        [HttpGet("~/score/{playerID}/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<Score>> GetPlayer(string playerID, string hash, string diff, string mode)
        {
            playerID = _context.PlayerIdToMain(playerID);
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

        [HttpGet("~/score/statistic/{id}")]
        public async Task<ActionResult> GetStatistic(int id)
        {
            var replayStream = await _s3Client.DownloadStats(id + ".json");
            if (replayStream == null) {
                return NotFound();
            }
            return File(replayStream, "application/json");
        }

        [HttpGet("~/score/calculatestatistic/{id}")]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatistic(string id)
        {
            Score? score = _context.Scores.Where(s => s.Id == Int64.Parse(id)).Include(s => s.Leaderboard).ThenInclude(l => l.Song).Include(s => s.Leaderboard).ThenInclude(l => l.Difficulty).FirstOrDefault();
            if (score == null)
            {
                return NotFound("Score not found");
            }
            return await CalculateStatisticScore(score);
        }

        [NonAction]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatisticScore(Score score)
        {
            string fileName = score.Replay.Split("/").Last();
            Replay? replay;

            using (var replayStream = await _s3Client.DownloadReplay(fileName))
            {
                if (replayStream == null) return NotFound();

                using (var ms = new MemoryStream(5))
                {
                    await replayStream.CopyToAsync(ms);
                    long length = ms.Length;
                    try
                    {
                        (replay, _) = ReplayDecoder.Decode(ms.ToArray());
                    }
                    catch (Exception)
                    {
                        return BadRequest("Error decoding replay");
                    }
                }
            }

            (ScoreStatistic? statistic, string? error) = await CalculateAndSaveStatistic(replay, score);
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
        public async Task<(ScoreStatistic?, string?)> CalculateAndSaveStatistic(Replay? replay, Score score)
        {
            (ScoreStatistic? statistic, string? error) = CalculateStatisticFromReplay(replay, score.Leaderboard);

            if (statistic == null)
            {
                return (null, error);
            }

            await _s3Client.UploadScoreStats(score.Id + ".json", statistic);

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

        [HttpGet("~/replays/")]
        public async Task<ActionResult<ResponseWithMetadata<string>>> GetReplays(
            int count = 50, 
            int page = 1,
            int? date_from = null,
            int? date_to = null)
        {
            if (count > 1000) {
                return BadRequest("Max count is 1000");
            }
            IQueryable<Score> scores = _context
                .Scores
                .OrderBy(s => s.Id);

            if (date_from != null) {
                scores = scores.Where(s => s.Timepost >= date_from);
            }

            if (date_to != null) {
                scores = scores.Where(s => s.Timepost <= date_to);
            }

            return new ResponseWithMetadata<string>() {
                    Metadata = new Metadata()
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = scores.Count()
                    },
                    Data = scores
                        .Skip((page - 1) * count)
                        .Take(count)
                        .Select(s => s.Replay)
                        .ToList()
            };
        }

        [HttpGet("~/scorestats/")]
        public async Task<ActionResult<string>> GetStats()
        {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            string result = "Count,Count >80%,Count >95%,Count/80,Count/95,Average,Top250,Total PP,PP/topPP filtered,PP/topPP unfiltered,Acc Rating,Pass Rating,Tech Rating,Name,Link\n";
            float weightTreshold = MathF.Pow(0.965f, 40);

            var leaderboards = _context
                .Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                .Select(lb => new { 
                    Average = lb.Scores.Average(s => s.Weight), 
                    Count8 = lb.Scores.Where(s => s.Weight > 0.8).Count(),
                    Count95 = lb.Scores.Where(s => s.Weight > 0.95).Count(),
                    PPsum = lb.Scores.Sum(s => s.Pp * s.Weight),
                    PPAverage = lb
                        .Scores
                        .Where(s => s.Player.ScoreStats.RankedPlayCount >= 50 && s.Player.ScoreStats.TopPp != 0)
                      .Average(s => s.Pp / s.Player.ScoreStats.TopPp),
                    PPAverage2 = lb
                        .Scores
                        .Where(s => s.Player.ScoreStats.TopPp != 0)
                      .Average(s => s.Pp / s.Player.ScoreStats.TopPp),
                    Count = lb.Scores.Count(),
                    Top250 = lb.Scores.Where(s => s.Player.Rank < 250 && s.Weight > weightTreshold).Count(),
                    lb.Id,
                    lb.Song.Name,
                    lb.Difficulty.AccRating,
                    lb.Difficulty.PassRating,
                    lb.Difficulty.TechRating})
                .ToList();

            foreach (var item in leaderboards)
            {
                result += $"{item.Count},{item.Count8},{item.Count95},{item.Count8/(float)item.Count},{item.Count95/(float)item.Count},{item.Average},{item.Top250},{item.PPsum},{item.PPAverage},{item.PPAverage2},{item.AccRating},{item.PassRating},{item.TechRating},{item.Name.Replace(",","")},https://stage.beatleader.net/leaderboard/global/{item.Id}/1\n";
            }

            return result;
        }
    }
}

