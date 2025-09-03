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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReplayDecoder;
using Swashbuckle.AspNetCore.Annotations;
using System.Data;
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
                .TagWithCaller()
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
                    SotwNominations = s.SotwNominations,
                    Status = s.Status,
                    ExternalStatuses = s.ExternalStatuses,
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
                    return await GetScore(redirect.NewScoreId, true);
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
            int? result = null;
            
            if (scoreSource == RandomScoreSource.Friends || scoreSource == RandomScoreSource.Self) {
                IQueryable<Score> query = _context.Scores;
                string? userId = HttpContext.CurrentUserID(_context);
                if (userId != null) {
                    if (scoreSource == RandomScoreSource.Friends) {
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
                    } else if (scoreSource == RandomScoreSource.Self) {
                        query = query.Where(s => s.PlayerId == userId);
                    }
                    var count = await query.CountAsync();
                    if (count == 0) return NotFound();
                    var offset = Random.Shared.Next(1, count);
                    result = await query
                        .TagWithCaller()
                        .AsNoTracking()
                        .OrderBy(s => s.Id)
                        .Skip(offset)
                        .Select(s => s.Id)
                        .FirstOrDefaultAsync();
                }
            } 

            if (result == null) {
                var offset = Random.Shared.Next(0, ScoreSearch.AvailableScores.Count);
                result = offset != 0 ? ScoreSearch.AvailableScores[offset] : Random.Shared.Next(10593474);
            }

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

            string? userId = HttpContext.CurrentUserID(_context);
            List<string>? friendsList = null;

            if (userId != null)
            {

                // Select scores based on the defined probabilities
                var randomValue = Random.Shared.NextDouble();
                if (randomValue < 0.40) // 20% chance for the user's own scores
                {
                    return await GetRandomScore(RandomScoreSource.General);
                }
                else // 70% chance for a friend's score
                {
                    return await GetRandomScore(RandomScoreSource.Friends);
                }
            }

            var idParam = new SqlParameter("Id", SqlDbType.Int) { Direction = ParameterDirection.Output };
                await _context.Database.ExecuteSqlRawAsync(
                    """
                    SELECT TOP 1 @Id = [s].[Id]
                    FROM [Scores] AS [s]
                    TABLESAMPLE (1 PERCENT);
                    """, idParam);
            int? scoreId = (int?)idParam.Value;
            var score = _context.Scores.Where(s => s.Id == scoreId).Include(s => s.Leaderboard).ThenInclude(s => s.Difficulty).FirstOrDefault();
            if (score?.Leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) == true || score?.Leaderboard.Difficulty.Requirements.HasFlag(Requirements.MappingExtensions) == true) {
                return await GetRandomScore(RandomScoreSource.General);
            }

            return await GetScore(scoreId ?? 1, false);
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
                .TagWithCaller()
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
                             s.ValidForGeneral)
                .OrderBy(p => p.Rank);

            result.Metadata.Total = await query.CountAsync();
            result.Data = await query
                .TagWithCaller()
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
                .ToListAsync();

            result.Container.LeaderboardId = leaderboard.Id;
            result.Container.Ranked = leaderboard.Difficulty?.Status == DifficultyStatus.ranked;

            return result;
        }

        [NonAction]
        public async Task<(List<ScoreResponseWithHeadsets>?, int)> GeneralScoreList(
            ResponseWithMetadataAndSelection<ScoreResponseWithHeadsets> result,
            bool showBots, 
            string leaderboardId, 
            string scope,
            string player,
            string method,
            int page,
            int count,
            PlayerResponse? currentPlayer,
            bool primaryClan = false) {
            IQueryable<Score> query = _context
                .Scores
                .AsNoTracking()
                .TagWithCaller()
                .Where(s => s.ValidForGeneral && 
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
            } else if (scope.ToLower().StartsWith("clan_")) {
                var clanTag = scope.ToLower().Replace("clan_", "").ToUpper();
                var clanId = await _context.Clans.Where(c => c.Tag == clanTag).Select(c => c.Id).FirstOrDefaultAsync();
                if (clanId != 0) {
                    if (primaryClan) {
                        query = query.Where(s => s.Player.Clans.OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                        .ThenBy(c => c.Id)
                        .Take(1)
                        .Any(c => c.Id == clanId));
                    } else {
                        query = query.Where(s => s.Player.Clans.Any(c => c.Id == clanId));
                    }
                }
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
                ScoreResponseWithHeadsets? highlightedScore = await query.TagWithCaller().Where(el => el.PlayerId == player).Select(s => new ScoreResponseWithHeadsets
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

                        Bot = s.Player.Bot,
                        Temporary = s.Player.Temporary,

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
                .FirstOrDefaultAsync();

                if (highlightedScore != null)
                {
                    result.Selection = highlightedScore;
                    result.Selection.Player = PostProcessSettings(result.Selection.Player, false);
                    result.Selection.FillNames();
                    if (scope.ToLower() == "friends" || scope.ToLower() == "country" || scope.ToLower().StartsWith("clan_")) {
                        result.Selection.Rank = await query.CountAsync(s => s.Rank < result.Selection.Rank) + 1;
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

            List<ScoreResponseWithHeadsets> resultList = await query
                .TagWithCaller()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new ScoreResponseWithHeadsets
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
                        Temporary = s.Player.Temporary,

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
                .ToListAsync();

            return ((resultList.Any(s => s.Pp > 0)
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
        public async Task<(List<ScoreResponseWithHeadsets>?, int)> ContextScoreList(
            ResponseWithMetadataAndSelection<ScoreResponseWithHeadsets> result,
            LeaderboardContexts context,
            bool showBots, 
            string leaderboardId, 
            string scope,
            string player,
            string method,
            int page,
            int count,
            PlayerResponse? currentPlayer,
            bool primaryClan = false) {

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
            } else if (scope.ToLower().StartsWith("clan_")) {
                var clanTag = scope.ToLower().Replace("clan_", "").ToUpper();
                var clanId = await _context.Clans.Where(c => c.Tag == clanTag).Select(c => c.Id).FirstOrDefaultAsync();
                if (clanId != 0) {
                    if (primaryClan) {
                        query = query.Where(s => s.Player.Clans.OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                        .ThenBy(c => c.Id)
                        .Take(1)
                        .Any(c => c.Id == clanId));
                    } else {
                        query = query.Where(s => s.Player.Clans.Any(c => c.Id == clanId));
                    }
                }
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
                ScoreResponseWithHeadsets? highlightedScore = await query
                    .TagWithCaller()
                    .AsNoTracking()
                    .Where(s => s.Context == context && (!s.ScoreInstance.Banned || (s.ScoreInstance.Bot && showBots)) && s.LeaderboardId == leaderboardId && s.PlayerId == player)
                    .Select(s => new ScoreResponseWithHeadsets
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
                .FirstOrDefaultAsync();

                if (highlightedScore != null)
                {
                    result.Selection = highlightedScore;
                    result.Selection.Player = PostProcessSettings(result.Selection.Player, false);
                    result.Selection.FillNames();
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
                .TagWithCaller()
                .AsNoTracking()
                .OrderBy(p => p.Rank)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => s.Id)
                .ToListAsync();

            List<ScoreResponseWithHeadsets> resultList = await query
                .TagWithCaller()
                .AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new ScoreResponseWithHeadsets
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
        public async Task<ActionResult<ResponseWithMetadataAndSelection<ScoreResponseWithHeadsets>>> GetByHash3(
            string hash,
            string diff,
            string mode,
            string context,
            string scope,
            string method,
            [FromQuery] string player,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] bool primaryClan = false)
        {
            var result = new ResponseWithMetadataAndSelection<ScoreResponseWithHeadsets>
            {
                Data = new List<ScoreResponseWithHeadsets>(),
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

            if (await _context.EarthDayMaps.AnyAsync(dm => dm.Hash == hash)) {
                hash = "EarthDay2025";
            }

            PlayerResponse? currentPlayer = 
                await _context
                .Players
                .TagWithCallerS()
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
                .FirstOrDefaultAsync(p => p.Id == player);
            var song = await _context
                .Songs
                .TagWithCallerS()
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
            if (modeValue == 0 && mode != "Legacy") {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = hash == "EarthDay2025" ? hash : song.Id + Song.DiffForDiffName(diff).ToString() + modeValue.ToString();

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
                ? await GeneralScoreList(result, showBots, leaderboardId, scope, player, method, page, count, currentPlayer, primaryClan)
                : await ContextScoreList(result, contexts, showBots, leaderboardId, scope, player, method, page, count, currentPlayer, primaryClan);

            if (resultList != null) {
                int shift = 0;
                for (int i = 0; i < resultList.Count; i++)
                {
                    var score = resultList[i];
                    score.Player = PostProcessSettings(score.Player, false);
                    score.FillNames();

                    if (scope.ToLower() == "friends" || scope.ToLower() == "country" || scope.ToLower().StartsWith("clan_")) {
                        if (score.Player.Bot) {
                            shift++;
                        } else {
                            score.Rank = i + (page - 1) * count + 1 - shift;
                            if (result.Selection?.PlayerId == score.PlayerId) {
                                result.Selection.Rank = i + (page - 1) * count + 1 - shift;
                            }
                        }
                    }

                    if (score.Player.Bot) {
                        score.Player.Name += " [BOT]";
                    }

                    if (score.Player.Temporary) {
                        score.Player.Name += " [TMP]";
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
                    .TagWithCaller()
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
        

        [HttpGet("~/scorestats/")]
        public async Task<ActionResult<string>> GetStats()
        {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            string result = "Count,Count >80%,Count >95%,Count/80,Count/95,95/80,Average,Percentile,Megametric,Megametric125,Megametric75,Megametric40,Top250,Total PP,PP/topPP filtered,PP/topPP unfiltered,Acc Rating,Pass Rating,Tech Rating,Name,Link\n";
            float weightTreshold = MathF.Pow(0.965f, 40);

            var leaderboards = _context
                .Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                .Select(lb => new
                {
                    Average = lb.Scores.Average(s => s.Weight),
                    Megametric = lb.Scores.Where(s => s.Player.ScoreStats.TopPp != 0).Select(s => new { s.Weight, s.Player.ScoreStats.RankedPlayCount, s.Pp, s.Player.ScoreStats.TopPp }),
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
                    lb.Difficulty.TechRating
                })
                .ToList();

            foreach (var item in leaderboards)
            {
                var l = item.Megametric.OrderByDescending(s => s.Weight).Take((int)(((double)item.Megametric.Count()) * 0.33));
                var ll = l.Count() > 10 ? l.Average(s => s.Weight) : 0;

                var m = item.Megametric.OrderByDescending(s => s.Weight).Take((int)(((double)item.Megametric.Count()) * 0.33)).Where(s => s.RankedPlayCount > 75);
                var mm = m.Count() > 10 ? m.Average(s => (s.Pp / s.TopPp) * s.Weight) : 0;

                var m2 = item.Megametric.Where(s => s.RankedPlayCount > 125).OrderByDescending(s => s.Weight).Take((int)(((double)item.Megametric.Count()) * 0.33));
                var mm2 = m2.Count() > 10 ? m2.Average(s => (s.Pp / s.TopPp) * s.Weight) : 0;

                var m3 = item.Megametric.Where(s => s.RankedPlayCount > 75).OrderByDescending(s => s.Weight).Take((int)(((double)item.Megametric.Count()) * 0.33));
                var mm3 = m3.Count() > 10 ? m3.Average(s => (s.Pp / s.TopPp) * s.Weight) : 0;

                var m4 = item.Megametric.Where(s => s.RankedPlayCount > 40).OrderByDescending(s => s.Weight).Take((int)(((double)item.Megametric.Count()) * 0.33));
                var mm4 = m4.Count() > 10 ? m4.Average(s => (s.Pp / s.TopPp) * s.Weight) : 0;


                result += $"{item.Count},{item.Count8},{item.Count95},{item.Count8 / (float)item.Count},{item.Count95 / (float)item.Count},{item.Count95 / (float)item.Count8},{item.Average},{ll},{mm},{mm2},{mm3},{mm4},{item.Top250},{item.PPsum},{item.PPAverage},{item.PPAverage2},{item.AccRating},{item.PassRating},{item.TechRating},{item.Name.Replace(",", "")},https://stage5.beatleader.net/leaderboard/global/{item.Id}/1\n";
            }

            return result;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/scores/common/players")]
        public async Task<ActionResult<ResponseWithMetadata<List<CommonScores>>>> GetCommonScores(
            [FromQuery] string players,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 50")] int count = 50)
        {
            if (page < 1) page = 1;
            if (count < 0 || count > 100)
            {
                return BadRequest("Please use count between 0 and 100");
            }

            var playerIds = new List<string>();

            foreach (var item in players.Split(",")) {
                playerIds.Add(await _context.PlayerIdToMain(item));
            }
            playerIds = playerIds.Distinct().ToList();

            if (playerIds.Count < 2 || playerIds.Count > 50) {
                return BadRequest("Please use 2-50 different players");
            }

            var result = new ResponseWithMetadata<CommonScores>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count
                }
            };

            var allPlayersScores = await (leaderboardContext == LeaderboardContexts.General
                        ? _context.Scores
                           .AsNoTracking()
                           .Where(s => playerIds.Contains(s.PlayerId) && s.ValidContexts.HasFlag(LeaderboardContexts.General))
                           .Select(s => new { s.LeaderboardId, s.Id, s.Timepost })
                        : _context.ScoreContextExtensions
                           .AsNoTracking()
                           .Where(s => playerIds.Contains(s.PlayerId) && s.Context == leaderboardContext)
                           .Select(ce => new { ce.LeaderboardId, ce.Id, ce.Timepost })).ToListAsync();

            var common = allPlayersScores
                .GroupBy(s => s.LeaderboardId)
                .Where(g => g.Count() > 1);

            var pagedCommon = common.OrderByDescending(g => g.Max(s => s.Timepost))
                .Skip((page - 1) * count)
                .Take(count)
                .ToList();

            var allScoreIds = pagedCommon.SelectMany(g => g.Select(s => s.Id)).ToList();

            IQueryable<IScore> scoresQuery = leaderboardContext == LeaderboardContexts.General
                        ? _context.Scores
                           .AsNoTracking()
                           .TagWithCaller()
                           .Where(s => allScoreIds.Contains(s.Id))
                        : _context.ScoreContextExtensions
                           .AsNoTracking()
                           .TagWithCaller()
                           .Include(ce => ce.ScoreInstance)
                           .Where(s => allScoreIds.Contains(s.Id));

            var scores = await scoresQuery.Select(s => new ScoreResponse {
                Id = s.Id,
                LeaderboardId = s.LeaderboardId,
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
                Timepost = s.Timepost,
                Hmd = s.Hmd,
                Replay = s.Replay,
                Offsets = s.ReplayOffsets,
                Player = new PlayerResponse
                {
                    Id = s.Player.Id,
                    Name = s.Player.Name,
                    Alias = s.Player.Alias,
                    Avatar = s.Player.Avatar,
                    Country = s.Player.Country,
                    Pp = s.Player.Pp,
                    Rank = s.Player.Rank,
                    CountryRank = s.Player.CountryRank,
                    Role = s.Player.Role,
                    ProfileSettings = s.Player.ProfileSettings,
                    Clans = s.Player.Clans.OrderBy(c => ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") >= 0 ? ("," + s.Player.ClanOrder + ",").IndexOf("," + c.Tag + ",") : 1000)
                        .ThenBy(c => c.Id).Take(1)
                        .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                }
            }).ToListAsync();

            var allLeaderboardIds = pagedCommon.Select(g => g.Key).ToList();

            var leaderboards = await _context
                .Leaderboards
                .AsNoTracking()
                .TagWithCallerS()
                .Where(lb => allLeaderboardIds.Contains(lb.Id))
                .Select(lb => new CompactLeaderboardResponse {
                    Id = lb.Id,
                    Song = new CompactSongResponse {
                        Id = lb.Song.Id,
                        Hash = lb.Song.Hash,
                        Name = lb.Song.Name,
            
                        SubName = lb.Song.SubName,
                        Author = lb.Song.Author,
                        Mapper = lb.Song.Mapper,
                        MapperId = lb.Song.MapperId,
                        CollaboratorIds = lb.Song.CollaboratorIds,
                        CoverImage = lb.Song.CoverImage,
                        FullCoverImage = lb.Song.FullCoverImage,
                        Bpm = lb.Song.Bpm,
                        Duration = lb.Song.Duration,
                        Explicity = lb.Song.Explicity
                    },
                    Difficulty = new DifficultyResponse {
                        Id = lb.Difficulty.Id,
                        Value = lb.Difficulty.Value,
                        Mode = lb.Difficulty.Mode,
                        DifficultyName = lb.Difficulty.DifficultyName,
                        ModeName = lb.Difficulty.ModeName,
                        Status = lb.Difficulty.Status,
                        NominatedTime  = lb.Difficulty.NominatedTime,
                        QualifiedTime  = lb.Difficulty.QualifiedTime,
                        RankedTime = lb.Difficulty.RankedTime,

                        Stars  = lb.Difficulty.Stars,
                        PredictedAcc  = lb.Difficulty.PredictedAcc,
                        PassRating  = lb.Difficulty.PassRating,
                        AccRating  = lb.Difficulty.AccRating,
                        TechRating  = lb.Difficulty.TechRating,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        ModifierValues = lb.Difficulty.ModifierValues,
                        Type  = lb.Difficulty.Type,

                        Njs  = lb.Difficulty.Njs,
                        Nps  = lb.Difficulty.Nps,
                        Notes  = lb.Difficulty.Notes,
                        Bombs  = lb.Difficulty.Bombs,
                        Walls  = lb.Difficulty.Walls,
                        MaxScore = lb.Difficulty.MaxScore,
                        Duration  = lb.Difficulty.Duration,

                        Requirements = lb.Difficulty.Requirements,
                    }
                })
                .ToListAsync();


            result.Metadata.Total = common.Count();

            result.Data = leaderboards.Select(lb => new CommonScores {
                Leaderboard = lb,
                Scores = scores.Where(s => s.LeaderboardId == lb.Id).OrderByDescending(s => s.Timepost).ToList()
            })
            .OrderByDescending(lb => lb.Scores.Max(s => s.Timepost))
            .ToList();

            return Ok(result);
        }

        [HttpGet("~/score/sotw")]
        public async Task<ActionResult<ScoreExternalStatus>> ScoreOfTheWeek()
        {
            var score = await _context
                .ScoreExternalStatus
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();
            if (score == null)
            {
                return NotFound();
            }

            return score;
        }

        public enum NominmationStatus {
            CantNominate,
            CanNominate,
            Nominated
        }

        [HttpGet("~/score/nominations/{id}")]
        [Authorize]
        public async Task<ActionResult<NominmationStatus>> ScoreNomations(int id)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentId != null ? await _context.Players.FindAsync(currentId) : null;
            if (currentPlayer == null)
            {
                return Unauthorized();
            }
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            var score = await _context
                .Scores
                .Where(s => s.Id == id && s.Status == ScoreStatus.None && s.Timepost > (timestamp - 60 * 60 * 24 * 14))
                .FirstOrDefaultAsync();
            if (score == null)
            {
                return NotFound();
            }

            var nominationsCount = await _context.ScoreNominations.CountAsync(n => n.PlayerId == currentId && n.Timestamp > (timestamp - 60 * 60 * 24 * 14));
            if (nominationsCount >= 10) {
                return NominmationStatus.CantNominate;
            }

            var existingNomination = await _context.ScoreNominations.FirstOrDefaultAsync(n => n.ScoreId == id && n.PlayerId == currentId);
            if (existingNomination != null) {
                return NominmationStatus.Nominated;
            }

            return NominmationStatus.CanNominate;
        }

        [HttpPost("~/score/nominate/{id}")]
        [Authorize]
        public async Task<ActionResult> NominateScore(
            int id,
            [FromQuery] string? description = null)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentId != null ? await _context.Players.FindAsync(currentId) : null;
            if (currentPlayer == null)
            {
                return Unauthorized();
            }
            var score = await _context
                .Scores
                .Where(s => s.Id == id)
                .FirstOrDefaultAsync();
            if (score == null)
            {
                return NotFound();
            }

            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (timestamp - score.Timepost > (60 * 60 * 24 * 14 + 60 * 60)) {
                return BadRequest("This score is too old");
            }

            var existingNomination = await _context.ScoreNominations.FirstOrDefaultAsync(n => n.ScoreId == id && n.PlayerId == currentId);
            if (existingNomination != null) {
                return BadRequest("You already nominated this score");
            }

            var nominationsCount = await _context.ScoreNominations.CountAsync(n => n.PlayerId == currentId && n.Timestamp > (timestamp - 60 * 60 * 24 * 14));
            if (nominationsCount >= 10) {
                return BadRequest("You can nominate up to 10 scores of the last 2 weeks");
            }

            _context.ScoreNominations.Add(new ScoreNomination {
                ScoreId = id,
                PlayerId = currentId,
                Timestamp = timestamp,
                Description = description
            });
            score.SotwNominations++;
            
            await _context.BulkSaveChangesAsync();

            return Ok();
        }
    }
}

