using Amazon.S3;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReplayDecoder;
using System.Dynamic;
using System.Linq.Expressions;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ScoreController : Controller
    {
        private readonly AppContext _context;

        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public ScoreController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [HttpGet("~/score/{id}")]
        public async Task<ActionResult<ScoreResponseWithDifficulty>> GetScore(int id, [FromQuery] bool fallbackToRedirect = false)
        {
            var score = await _context
                .Scores
                .AsNoTracking()
                .Where(l => l.Id == id)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .AsSplitQuery()
                .TagWithCallSite()
                .Select(s => new ScoreResponseWithDifficulty {
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
                    Difficulty = s.Leaderboard.Difficulty,
                    ValidContexts = s.ValidContexts,
                    ScoreImprovement = s.ScoreImprovement,
                    Song = new ScoreSongResponse {
                        Id = s.Leaderboard.Song.Id,
                        Hash = s.Leaderboard.Song.Hash,
                        Cover = s.Leaderboard.Song.CoverImage,
                        Name = s.Leaderboard.Song.Name,
                        SubName = s.Leaderboard.Song.SubName,
                        Author = s.Leaderboard.Song.Author,
                        Mapper = s.Leaderboard.Song.Mapper,
                        DownloadUrl = s.Leaderboard.Song.DownloadUrl,
                    },
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Platform = s.Player.Platform,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        Socials = s.Player.Socials,
                        ProfileSettings = s.Player.ProfileSettings,
                        PatreonFeatures = s.Player.PatreonFeatures
                    },
                    ContextExtensions = s.ContextExtensions.Select(ce => new ScoreContextExtensionResponse {
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
                .FirstOrDefaultAsync();

            if (score != null)
            {
                score.Player = PostProcessSettings(score.Player, false);
                return score;
            }
            else
            {
                var redirect = fallbackToRedirect ? await _context.ScoreRedirects.FirstOrDefaultAsync(sr => sr.OldScoreId == id) : null;
                if (redirect != null && redirect.NewScoreId != id) {
                    return await GetScore(redirect.NewScoreId);
                } else {
                    return NotFound();
                }
            }
        }

        [HttpGet("~/score/{leaderboardContext}/{playerID}/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<ScoreResponseWithDifficulty>> GetPlayerScore(
            LeaderboardContexts leaderboardContext,
            string playerID, 
            string hash, 
            string diff, 
            string mode)
        {
            playerID = await _context.PlayerIdToMain(playerID);

            int? score = await _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => s.PlayerId == playerID &&
                                s.ValidContexts.HasFlag(leaderboardContext) &&
                                s.Leaderboard.Song.Hash.ToLower() == hash.ToLower() &&
                                s.Leaderboard.Difficulty.DifficultyName.ToLower() == diff.ToLower() &&
                                s.Leaderboard.Difficulty.ModeName.ToLower() == mode.ToLower())
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync();

            if (score != null)
            {
                return await GetScore((int)score, false);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("~/score/random")]
        public async Task<ActionResult<ScoreResponseWithDifficulty>> GetRandomScore(
            [FromQuery] RandomScoreSource scoreSource = RandomScoreSource.General)
        {
            IQueryable<Score> query = _context.Scores;
            if (scoreSource == RandomScoreSource.Friends) {
                string? userId = HttpContext.CurrentUserID(_context);
                if (userId != null) {
                    var friends = await _context
                        .Friends
                        .AsNoTracking()
                        .Where(f => f.Id == userId)
                        .Include(f => f.Friends)
                        .FirstOrDefaultAsync();

                    var friendsList = new List<string> { userId };
                    if (friends != null) {
                        friendsList.AddRange(friends.Friends.Select(f => f.Id));
                    }

                    var score = Expression.Parameter(typeof(Score), "s");

                    // 1 != 2 is here to trigger `OrElse` further the line.
                    var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                    foreach (var term in friendsList)
                    {
                        exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(score, "PlayerId"), Expression.Constant(term)));
                    }
                    query = query.Where((Expression<Func<Score, bool>>)Expression.Lambda(exp, score));
                }
            }

            var offset = Random.Shared.Next(1, await query.CountAsync());
            int? result = await query
                .AsNoTracking()
                .Skip(offset)
                .Take(1)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();
            if (result == null) {
                return NotFound();
            }

            return await GetScore((int)result, false);
        }

        [HttpGet("~/score/random/ree")]
        public async Task<ActionResult<ScoreResponseWithDifficulty>> GetRandomReeScore(
            [FromQuery] int timeback = 60 * 60 * 24 * 7)
        {
            IQueryable<Score> query = _context.Scores;

            // Filter out scores that are less than a week old
            var treshold = Time.UnixNow() - timeback;
            query = query.Where(s => 
                s.Timepost < treshold &&
                s.Leaderboard.Difficulty.Mode == 1 &&
                !s.Leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) &&
                !s.Leaderboard.Difficulty.Requirements.HasFlag(Requirements.MappingExtensions));

            string? userId = HttpContext.CurrentUserID(_context);
            List<string>? friendsList = null;

            if (userId != null)
            {
                var friends = await _context
                    .Friends
                    .AsNoTracking()
                    .Where(f => f.Id == userId)
                    .Include(f => f.Friends)
                    .FirstOrDefaultAsync();

                friendsList = new List<string> { userId };
                if (friends != null)
                {
                    friendsList.AddRange(friends.Friends.Select(f => f.Id));
                }

                // Select scores based on the defined probabilities
                var randomValue = Random.Shared.NextDouble();
                if (randomValue < 0.10) // 10% chance for a random player's score
                {
                    if (friendsList != null)
                    {
                        // Exclude the user and their friends
                        query = query.Where(s => !friendsList.Contains(s.PlayerId));
                    }
                }
                else if (randomValue < 0.30) // 20% chance for the user's own scores
                {
                    query = query.Where(s => s.PlayerId == userId);
                }
                else // 70% chance for a friend's score
                {
                    if (friendsList != null && friendsList.Count > 1) // Check to ensure friends exist
                    {
                        query = query.Where(s => friendsList.Contains(s.PlayerId));
                    }
                }
            }

            int totalScores = await query.CountAsync();
            if (totalScores == 0)
            {
                return await GetRandomScore(RandomScoreSource.General);
            }

            var offset = Random.Shared.Next(totalScores);
            var scoreId = await query
                .AsNoTracking()
                .Skip(offset)
                .Take(1)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();

            return await GetScore(scoreId, false);
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
            var score = await _context
                .Scores
                .Where(s => s.Id == id)
                .Include(s => s.ContextExtensions)
                .FirstOrDefaultAsync();
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
            
            foreach (var extension in score.ContextExtensions) {
                _context.ScoreContextExtensions.Remove(extension);
            }
            _context.Scores.Remove(score);

            await SocketController.ScoreWasRejected(score, _context);
            await _context.BulkSaveChangesAsync();

            RefreshTaskService.ExtendJob(new MigrationJob {
                PlayerId = score.PlayerId,
                Leaderboards = new List<string> { score.LeaderboardId },
                Throttle = 5
            });

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

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var song = await _context
                .Songs
                .AsNoTracking()
                .Select(s => new { Id = s.Id, Hash = s.Hash })
                .FirstOrDefaultAsync(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = Song.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + Song.DiffForDiffName(diff).ToString() + modeValue.ToString();

            IQueryable<Score> query = _context
                .Scores
                .AsNoTracking()
                .Where(s => !s.Banned && s.LeaderboardId == leaderboardId)
                .OrderBy(p => p.Rank);

            result.Metadata.Total = await query.CountAsync();
            result.Data = await query
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
                .TagWithCallSite()
                .ToListAsync();

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

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var leaderboard = await LeaderboardControllerHelper.GetByHash(_context, hash, diff, mode, false);
            if (leaderboard == null) {
                return result;
            }

            IQueryable<Score> query = _context
                .Scores
                .AsNoTracking()
                .Where(s => !s.Banned && 
                             s.LeaderboardId == leaderboard.Id &&
                             s.ValidContexts.HasFlag(LeaderboardContexts.General))
                .OrderBy(p => p.Rank);

            result.Metadata.Total = await query.CountAsync();
            result.Data = await query
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
                .TagWithCallSite()
                .ToListAsync();

            result.Container.LeaderboardId = leaderboard.Id;
            result.Container.Ranked = leaderboard.Difficulty?.Status == DifficultyStatus.ranked;

            return result;
        }

        [NonAction]
        public async Task<(List<ScoreResponse>?, int)> GeneralScoreList(
            ResponseWithMetadataAndSelection<ScoreResponse> result,
            bool showBots, 
            string leaderboardId, 
            string scope,
            string player,
            string method,
            int page,
            int count,
            PlayerResponse? currentPlayer) {
            IQueryable<Score> query = _context
                .Scores
                .AsNoTracking()
                .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && 
                            (!s.Banned || (s.Bot && showBots)) && 
                            s.LeaderboardId == leaderboardId);

            if (scope.ToLower() == "friends")
            {
                PlayerFriends? friends = await _context.Friends.Include(f => f.Friends).FirstOrDefaultAsync(f => f.Id == player);

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
                    return (null, page);
                }
                query = query.Where(s => s.Player.Country == currentPlayer.Country);
            }

            if (method.ToLower() == "around")
            {
                var playerScore = await query.Select(s => new { s.PlayerId, s.Rank }).FirstOrDefaultAsync(el => el.PlayerId == player);
                if (playerScore != null)
                {
                    int rank = await query.CountAsync(s => s.Rank < playerScore.Rank);
                    page += (int)Math.Floor((double)(rank) / (double)count);
                    result.Metadata.Page = page;
                }
                else
                {
                    return (null, page);
                }
            }
            else
            {
                ScoreResponse? highlightedScore = await query.Where(el => el.PlayerId == player).Select(s => new ScoreResponse
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
                        Alias = s.Player.Alias,
                        Platform = s.Player.Platform,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        Socials = s.Player.Socials,
                        ProfileSettings = s.Player.ProfileSettings,
                        ClanOrder = s.Player.ClanOrder,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement
                })
                    .TagWithCallSite()
                    .FirstOrDefaultAsync();

                if (highlightedScore != null)
                {
                    result.Selection = highlightedScore;
                    result.Selection.Player = PostProcessSettings(result.Selection.Player, false);
                    if (scope.ToLower() == "friends" || scope.ToLower() == "country") {
                        result.Selection.Rank = await query.CountAsync(s => s.Rank < result.Selection.Rank) + 1;
                    } else {
                        result.Selection.Rank += await query.CountAsync(s => s.Bot && (
                            highlightedScore.Pp > 0 ? (s.Pp > highlightedScore.Pp) : (s.ModifiedScore > highlightedScore.ModifiedScore)));
                    }
                }

                if (page < 1) {
                    page = 1;
                }
            }

            result.Metadata.Total = await query.CountAsync();

            var ids = await query
                .OrderBy(p => p.Rank)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => s.Id)
                .ToListAsync();

            List<ScoreResponse> resultList = await query
                .Where(s => ids.Contains(s.Id))
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
                    Priority = s.Priority,
                    LeaderboardId = s.LeaderboardId,
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
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
                        ClanOrder = s.Player.ClanOrder,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement
                })
                .TagWithCallSite()
                .ToListAsync();

            return ((resultList.FirstOrDefault()?.Pp > 0 
                        ? resultList
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                        : resultList
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)).ToList(), page);
        }

        [NonAction]
        public async Task<(List<ScoreResponse>?, int)> ContextScoreList(
            ResponseWithMetadataAndSelection<ScoreResponse> result,
            LeaderboardContexts context,
            bool showBots, 
            string leaderboardId, 
            string scope,
            string player,
            string method,
            int page,
            int count,
            PlayerResponse? currentPlayer) {

            IQueryable<ScoreContextExtension> query = _context
                .ScoreContextExtensions
                .AsNoTracking()
                .Where(s => s.Context == context && (!s.ScoreInstance.Banned || (s.ScoreInstance.Bot && showBots)) && s.LeaderboardId == leaderboardId);

            if (scope.ToLower() == "friends")
            {
                PlayerFriends? friends = await _context.Friends.Include(f => f.Friends).FirstOrDefaultAsync(f => f.Id == player);

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
                    return (null, page);
                }
                query = query.Where(s => s.Player.Country == currentPlayer.Country);
            }

            if (method.ToLower() == "around")
            {
                var playerScore = await query.Select(s => new { s.PlayerId, s.Rank }).FirstOrDefaultAsync(el => el.PlayerId == player);
                if (playerScore != null)
                {
                    int rank = await query.CountAsync(s => s.Rank < playerScore.Rank);
                    page += (int)Math.Floor((double)(rank) / (double)count);
                    result.Metadata.Page = page;
                }
                else
                {
                    return (null, page);
                }
            }
            else
            {
                ScoreResponse? highlightedScore = await query
                    .Where(s => s.Context == context && (!s.ScoreInstance.Banned || (s.ScoreInstance.Bot && showBots)) && s.LeaderboardId == leaderboardId && s.PlayerId == player)
                    .Select(s => new ScoreResponse
                {
                    Id = s.ScoreId != null ? (int)s.ScoreId : 0,
                    PlayerId = s.PlayerId,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.ScoreInstance.FcAccuracy,
                    FcPp = s.ScoreInstance.FcPp,
                    Rank = s.Rank,
                    Replay = s.ScoreInstance.Replay,
                    Modifiers = s.Modifiers,
                    BadCuts = s.ScoreInstance.BadCuts,
                    MissedNotes = s.ScoreInstance.MissedNotes,
                    BombCuts = s.ScoreInstance.BombCuts,
                    WallsHit = s.ScoreInstance.WallsHit,
                    Pauses = s.ScoreInstance.Pauses,
                    FullCombo = s.ScoreInstance.FullCombo,
                    Hmd = s.ScoreInstance.Hmd,
                    Controller = s.ScoreInstance.Controller,
                    MaxCombo = s.ScoreInstance.MaxCombo,
                    Timeset = s.ScoreInstance.Timeset,
                    Timepost = s.ScoreInstance.Timepost,
                    Platform = s.ScoreInstance.Platform,
                    LeaderboardId = s.LeaderboardId,
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Platform = s.Player.Platform,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        Socials = s.Player.Socials,
                        ProfileSettings = s.Player.ProfileSettings,
                        ClanOrder = s.Player.ClanOrder,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement
                })
                    .TagWithCallSite()
                    .FirstOrDefaultAsync();

                if (highlightedScore != null)
                {
                    result.Selection = highlightedScore;
                    result.Selection.Player = PostProcessSettings(result.Selection.Player, false);
                    if (scope.ToLower() == "friends" || scope.ToLower() == "country") {
                        result.Selection.Rank = query.Count(s => s.Rank < result.Selection.Rank) + 1;
                    }
                    var contextPlayer = await _context.PlayerContextExtensions.FirstOrDefaultAsync(ce => ce.PlayerId == player && ce.Context == context);
                    if (contextPlayer != null) {
                        highlightedScore?.Player?.ToContext(contextPlayer);
                    }
                }

                if (page < 1) {
                    page = 1;
                }
            }

            result.Metadata.Total = await query.CountAsync();

            var ids = await query
                .OrderBy(p => p.Rank)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => s.Id)
                .ToListAsync();

            List<ScoreResponse> resultList = await query
                .Where(s => ids.Contains(s.Id))
                .Select(s => new ScoreResponse
                {
                    Id = s.ScoreId != null ? (int)s.ScoreId : 0,
                    PlayerId = s.PlayerId,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.ScoreInstance.FcAccuracy,
                    FcPp = s.ScoreInstance.FcPp,
                    Rank = s.Rank,
                    Replay = s.ScoreInstance.Replay,
                    Modifiers = s.Modifiers,
                    BadCuts = s.ScoreInstance.BadCuts,
                    MissedNotes = s.ScoreInstance.MissedNotes,
                    BombCuts = s.ScoreInstance.BombCuts,
                    WallsHit = s.ScoreInstance.WallsHit,
                    Pauses = s.ScoreInstance.Pauses,
                    FullCombo = s.ScoreInstance.FullCombo,
                    Hmd = s.ScoreInstance.Hmd,
                    Controller = s.ScoreInstance.Controller,
                    MaxCombo = s.ScoreInstance.MaxCombo,
                    Timeset = s.ScoreInstance.Timeset,
                    Timepost = s.ScoreInstance.Timepost,
                    Platform = s.ScoreInstance.Platform,
                    Priority = s.Priority,
                    LeaderboardId = s.LeaderboardId,
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
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
                        ClanOrder = s.Player.ClanOrder,
                        Clans = s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    },
                    ScoreImprovement = s.ScoreImprovement
                })
                .TagWithCallSite()
                .ToListAsync();

            foreach (var score in resultList) {
                var contextPlayer = await _context.PlayerContextExtensions.FirstOrDefaultAsync(ce => ce.PlayerId == score.PlayerId && ce.Context == context);
                
                if (contextPlayer != null) {
                    score?.Player?.ToContext(contextPlayer);
                }
            }
            if (context == LeaderboardContexts.Golf) {
                if (resultList.FirstOrDefault()?.Pp > 0) {
                    resultList = resultList
                        .OrderByDescending(el => Math.Round(el.Pp, 2))
                        .ThenBy(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timeset).ToList();
                } else {
                    resultList = resultList
                        .OrderBy(el => el.Priority)
                        .ThenBy(el => el.ModifiedScore)
                        .ThenBy(el => Math.Round(el.Accuracy, 4))
                        .ThenBy(el => el.Timeset).ToList();
                }
            } else {
                if (resultList.FirstOrDefault()?.Pp > 0) {
                    resultList = resultList
                            .OrderByDescending(el => Math.Round(el.Pp, 2))
                            .ThenBy(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset)
                            .ToList();
                } else {
                    resultList = resultList
                            .OrderBy(el => el.Priority)
                            .ThenByDescending(el => el.ModifiedScore)
                            .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                            .ThenBy(el => el.Timeset).ToList();
                }
            }

            return (resultList, page);
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

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            PlayerResponse? currentPlayer = 
                await _context
                .Players
                .AsNoTracking()
                .Select(p => new PlayerResponse {
                    Id = p.Id,
                    Name = p.Name,
                    Alias = p.Alias,
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
                .TagWithCallSite()
                .FirstOrDefaultAsync(p => p.Id == player);
            var song = await _context
                .Songs
                .AsNoTracking()
                .Select(s => new { Id = s.Id, Hash = s.Hash })
                .TagWithCallSite()
                .FirstOrDefaultAsync(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = Song.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + Song.DiffForDiffName(diff).ToString() + modeValue.ToString();

            bool showBots = currentPlayer?.ProfileSettings?.ShowBots ?? false;
            LeaderboardContexts contexts = LeaderboardContexts.General;
            switch (context) {
                case "standard":
                    contexts = LeaderboardContexts.NoMods;
                    break;
                case "nopause":
                    contexts = LeaderboardContexts.NoPause;
                    break;
                case "golf":
                    contexts = LeaderboardContexts.Golf;
                    break;
                case "scpm":
                    contexts = LeaderboardContexts.SCPM;
                    break;
                default:
                    break;
            }
            
            (var resultList, page) =
                contexts == LeaderboardContexts.General 
                ? await GeneralScoreList(result, showBots, leaderboardId, scope, player, method, page, count, currentPlayer)
                : await ContextScoreList(result, contexts, showBots, leaderboardId, scope, player, method, page, count, currentPlayer);

            if (resultList != null) {
                for (int i = 0; i < resultList.Count; i++)
                {
                    var score = resultList[i];
                    score.Player = PostProcessSettings(score.Player, false);
                    score.Rank = i + (page - 1) * count + 1;

                    if (score.Player.Bot) {
                        score.Player.Name += " [BOT]";
                    }
                }
                result.Data = resultList;
            }

            return result;
        }

        [HttpGet("~/score/{playerID}/{hash}/{diff}/{mode}")]
        public async Task<ActionResult<Score>> GetPlayer(
            string playerID, 
            string hash, 
            string diff, 
            string mode,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
        {
            playerID = await _context.PlayerIdToMain(playerID);

            var score = await _context
                    .Scores
                    .AsNoTracking()
                    .Where(l => 
                        l.Leaderboard.Song.Hash == hash && 
                        l.Leaderboard.Difficulty.DifficultyName == diff && 
                        l.Leaderboard.Difficulty.ModeName == mode && 
                        l.ValidContexts.HasFlag(leaderboardContext) &&
                        l.PlayerId == playerID)
                    .Include(el => el.Player)
                    .ThenInclude(el => el.PatreonFeatures)
                    .Include(el => el.Player)
                    .ThenInclude(el => el.ProfileSettings)
                    .Include(el => el.ContextExtensions.Where(ce => ce.Context == leaderboardContext))
                    .TagWithCallSite()
                    .FirstOrDefaultAsync();

            if (score != null)
            {
                if (leaderboardContext != LeaderboardContexts.General) {
                    score.ToContext(score.ContextExtensions.First());
                }
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
            var statsUrl = await _s3Client.GetPresignedUrl(id + ".json", S3Container.scorestats);
            if (statsUrl == null) {
                return NotFound();
            }
            return Redirect(statsUrl);
        }

        [HttpGet("~/score/calculatestatistic/{id}")]
        public async Task<ActionResult<ScoreStatistic?>> CalculateStatistic(string id)
        {
            Score? score = await _context.Scores.Where(s => s.Id == Int64.Parse(id)).Include(s => s.Leaderboard).ThenInclude(l => l.Song).Include(s => s.Leaderboard).ThenInclude(l => l.Difficulty).FirstOrDefaultAsync();
            if (score == null)
            {
                return NotFound("Score not found");
            }
            (var result, var error) = await ScoreControllerHelper.CalculateStatisticScore(_context, _s3Client, score);
            if (result != null) {
                return result;
            } else {
                return BadRequest(error);
            }
        }

        [HttpPut("~/score/{id}/pin")]
        public async Task<ActionResult<ScoreMetadata>> PinScore(
            int id,
            [FromQuery] bool pin,
            [FromQuery] string? description = null,
            [FromQuery] string? link = null,
            [FromQuery] int? priority = null,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
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

            var scores = await _context
                .Scores
                .Where(s => s.PlayerId == currentPlayer.Id && s.ValidContexts.HasFlag(leaderboardContext) && (s.Id == id || s.Metadata != null))
                .Include(s => s.Metadata)
                .ToListAsync();
            if (scores.Count() == 0 || scores.FirstOrDefault(s => s.Id == id) == null)
            {
                return NotFound("Score not found");
            }

            var pinLimit = 2;
            if (currentPlayer.Role.Contains("tipper") || currentPlayer.Role.Contains("supporter") || currentPlayer.Role.Contains("sponsor"))
            {
                pinLimit = 9;
            }

            var score = scores.First(s => s.Id == id);
            var pinnedScores = scores.Where(s => s.Metadata?.PinnedContexts.HasFlag(leaderboardContext) ?? false);

            if (pinnedScores.Count() > pinLimit 
                && pin 
                && score.Metadata?.PinnedContexts.HasFlag(leaderboardContext) != true)
            {
                return BadRequest("Too many scores pinned");
            }

            ScoreMetadata? metadata = score.Metadata;
            if (metadata == null)
            {
                metadata = new ScoreMetadata
                {
                    Priority = pinnedScores.Count() == 0 ? 1 : pinnedScores.Max(s => s.Metadata?.Priority ?? 0) + 1
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
                metadata.PinnedContexts |= leaderboardContext;
            }
            else
            {
                metadata.PinnedContexts &= ~leaderboardContext;
            }
            if (priority != null)
            {
                if (!(priority <= scores.Count)) return BadRequest("Priority is out of range");

                int priorityValue = (int)priority;

                if (priorityValue <= metadata.Priority)
                {
                    var scoresLower = pinnedScores.Where(s => s.Metadata?.Priority >= priorityValue).ToList();
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
                    var scoresLower = pinnedScores.Where(s => s.Metadata.Priority <= priorityValue).ToList();
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

            var scoresOrdered = pinnedScores.OrderBy(s => s.Metadata?.Priority ?? 0).ToList();
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
                .AsNoTracking()
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
                        Total = await scores.CountAsync()
                    },
                    Data = await scores
                        .Skip((page - 1) * count)
                        .Take(count)
                        .Select(s => s.Replay)
                        .ToListAsync()
            };
        }

        [HttpGet("~/v1/clanScores/{hash}/{diff}/{mode}/page")]
        public async Task<ActionResult<ResponseWithMetadataAndSelection<ClanScoreResponse>>> ClanScoresV1(
            string hash,
            string diff,
            string mode,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            var result = new ResponseWithMetadataAndSelection<ClanScoreResponse>
            {
                Data = new List<ClanScoreResponse>(),
                Metadata =
                    {
                        ItemsPerPage = count,
                        Page = page,
                        Total = 0
                    }
            };

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var song = await _context
                .Songs
                .AsNoTracking()
                .Select(s => new { s.Id, s.Hash })
                .TagWithCallSite()
                .FirstOrDefaultAsync(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = Song.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + Song.DiffForDiffName(diff).ToString() + modeValue.ToString();

            var query = _context
                .ClanRanking
                .AsNoTracking()
                .Where(s => s.LeaderboardId == leaderboardId);

            result.Metadata.Total = await query.CountAsync();

            var resultList = (await query
                .OrderBy(p => p.Rank)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new ClanScoreResponse
                {
                    Id = s.Id,
                    ClanId = s.ClanId ?? 0,
                    ModifiedScore = s.TotalScore,
                    Accuracy = s.AverageAccuracy,
                    Pp = s.Pp,
                    Rank = s.Rank,
                    Timepost = s.LastUpdateTime.ToString(),
                    LeaderboardId = s.LeaderboardId,
                    Clan = new ClanScoreClanResponse
                    {
                        Id = s.Clan.Id,
                        Tag = s.Clan.Tag,
                        Name = s.Clan.Name,
                        Avatar = s.Clan.Icon,
                        Color = s.Clan.Color,

                        Pp = s.Clan.Pp,
                        Rank = s.Clan.Rank,
                    },
                })
                .TagWithCallSite()
                .ToListAsync())
                .OrderByDescending(el => Math.Round(el.Pp, 2))
                .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                .ThenBy(el => el.Timepost)
                .ToList();

            result.Data = resultList;

            return result;
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

            var leaderboards = await _context
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
                .ToListAsync();

            foreach (var item in leaderboards)
            {
                result += $"{item.Count},{item.Count8},{item.Count95},{item.Count8/(float)item.Count},{item.Count95/(float)item.Count},{item.Average},{item.Top250},{item.PPsum},{item.PPAverage},{item.PPAverage2},{item.AccRating},{item.PassRating},{item.TechRating},{item.Name.Replace(",","")},https://stage.beatleader.net/leaderboard/global/{item.Id}/1\n";
            }

            return result;
        }
    }
}

