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
    public class PinnedScoreController : Controller
    {
        private readonly AppContext _context;
        private readonly StorageContext _storageContext;

        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public PinnedScoreController(
            AppContext context,
            StorageContext storageContext,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;
            _storageContext = storageContext;

            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [HttpPut("~/score/{id}/pin")]
        public async Task<ActionResult<ScoreMetadata>> PinScore(
            int id,
            [FromQuery] bool pin,
            [FromQuery] string? description = null,
            [FromQuery] string? link = null,
            [FromQuery] int? priority = null,
            [FromQuery] bool attempt = false,
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

            // Get scores from both regular scores and player leaderboard stats
            var scores = await _context
                .Scores
                .Where(s => s.PlayerId == currentPlayer.Id && s.ValidContexts.HasFlag(leaderboardContext) && (s.Id == id || s.Metadata != null))
                .Include(s => s.Metadata)
                .ToListAsync();

            var attempts = new List<PlayerLeaderboardStats>();
            if (attempt)
            {
                attempts = await _storageContext
                    .PlayerLeaderboardStats
                    .Where(s => s.PlayerId == currentPlayer.Id && (s.Id == id || s.Metadata != null))
                    .Include(s => s.Metadata)
                    .ToListAsync();
            }

            // Check if the score exists in either regular scores or attempts
            bool scoreFound = scores.Any(s => s.Id == id) || attempts.Any(s => s.Id == id);
            if (!scoreFound)
            {
                return NotFound("Score not found");
            }

            var pinLimit = 3;
            if (currentPlayer.AnySupporter())
            {
                pinLimit = 9;
            }

            // Get the score to pin from either regular scores or attempts
            Score? scoreToPin = scores.FirstOrDefault(s => s.Id == id);
            PlayerLeaderboardStats? attemptToPin = null;
            bool isAttempt = false;
            
            if (scoreToPin == null)
            {
                attemptToPin = attempts.First(s => s.Id == id);
                isAttempt = true;
            }

            // Get all pinned scores from both regular scores and attempts
            var pinnedScores = scores.Where(s => s.Metadata?.PinnedContexts.HasFlag(leaderboardContext) ?? false).ToList();
            var pinnedAttempts = attempts.Where(s => s.Metadata?.PinnedContexts.HasFlag(leaderboardContext) ?? false).ToList();
            int totalPinnedCount = pinnedScores.Count + pinnedAttempts.Count;

            // Check if the score is already pinned
            bool isAlreadyPinned = false;
            if (isAttempt)
            {
                isAlreadyPinned = attemptToPin?.Metadata?.PinnedContexts.HasFlag(leaderboardContext) ?? false;
            }
            else
            {
                isAlreadyPinned = scoreToPin?.Metadata?.PinnedContexts.HasFlag(leaderboardContext) ?? false;
            }

            if (totalPinnedCount >= pinLimit 
                && pin 
                && !isAlreadyPinned)
            {
                return currentPlayer.AnySupporter() ? BadRequest("Too many scores pinned") : BadRequest("Support us on Patreon to pin more scores!");
            }

            ScoreMetadata? metadata;
            if (isAttempt)
            {
                metadata = attemptToPin.Metadata;
                if (metadata == null)
                {
                    metadata = new ScoreMetadata
                    {
                        Priority = totalPinnedCount == 0 ? 1 : 
                            Math.Max(
                                pinnedScores.Count > 0 ? pinnedScores.Max(s => s.Metadata?.Priority ?? 0) : 0,
                                pinnedAttempts.Count > 0 ? pinnedAttempts.Max(s => s.Metadata?.Priority ?? 0) : 0
                            ) + 1
                    };
                    attemptToPin.Metadata = metadata;
                }
            }
            else
            {
                metadata = scoreToPin.Metadata;
                if (metadata == null)
                {
                    metadata = new ScoreMetadata
                    {
                        Priority = totalPinnedCount == 0 ? 1 : 
                            Math.Max(
                                pinnedScores.Count > 0 ? pinnedScores.Max(s => s.Metadata?.Priority ?? 0) : 0,
                                pinnedAttempts.Count > 0 ? pinnedAttempts.Max(s => s.Metadata?.Priority ?? 0) : 0
                            ) + 1
                    };
                    scoreToPin.Metadata = metadata;
                }
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
                if (!(priority <= totalPinnedCount)) return BadRequest("Priority is out of range");

                int priorityValue = (int)priority;
                
                // Combine all pinned items for priority adjustment
                var allPins = new List<(ScoreMetadata Metadata, bool IsAttempt)>();
                foreach (var score in pinnedScores)
                {
                    if (score.Metadata != null)
                    {
                        allPins.Add((score.Metadata, false));
                    }
                }
                foreach (var att in pinnedAttempts)
                {
                    if (att.Metadata != null)
                    {
                        allPins.Add((att.Metadata, true));
                    }
                }

                if (priorityValue <= metadata.Priority)
                {
                    var itemsToAdjust = allPins.Where(item => item.Metadata.Priority >= priorityValue).ToList();
                    foreach (var item in itemsToAdjust)
                    {
                        item.Metadata.Priority++;
                    }
                }
                else
                {
                    var itemsToAdjust = allPins.Where(item => item.Metadata.Priority <= priorityValue).ToList();
                    foreach (var item in itemsToAdjust)
                    {
                        item.Metadata.Priority--;
                    }
                }

                metadata.Priority = priorityValue;
            }

            // Reorder all pinned items
            var allPinnedItems = new List<(ScoreMetadata Metadata, bool IsAttempt)>();
            foreach (var score in pinnedScores)
            {
                if (score.Metadata != null)
                {
                    allPinnedItems.Add((score.Metadata, false));
                }
            }
            foreach (var att in pinnedAttempts)
            {
                if (att.Metadata != null)
                {
                    allPinnedItems.Add((att.Metadata, true));
                }
            }

            var orderedItems = allPinnedItems.OrderBy(item => item.Metadata.Priority).ToList();
            for (int i = 0; i < orderedItems.Count; i++)
            {
                orderedItems[i].Metadata.Priority = i + 1;
            }

            await _storageContext.SaveChangesAsync();
            await _context.SaveChangesAsync();

            return metadata;
        }

        [HttpGet("~/player/{id}/pinnedScores")]
        [SwaggerOperation(Summary = "Retrieve player's pinned scores", Description = "Fetches a paginated list of scores pinned by player for their ID.")]
        [SwaggerResponse(200, "Scores retrieved successfully")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Scores not found for the given player ID")]
        public async Task<ActionResult<ICollection<AttemptResponseWithMyScore>>> GetPinnedScores(
            [FromRoute, SwaggerParameter("Player's unique identifier")] string id,
            [FromQuery, SwaggerParameter("Filter scores by leaderboard context, default is 'General'")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {

            id = await _context.PlayerIdToMain(id);

            var profileSettings = await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == id && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();
            bool publicHistory = profileSettings?.ShowStatsPublicPinned ?? false;

            var resultList = await _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => 
                        s.PlayerId == id && 
                        s.ValidContexts.HasFlag(leaderboardContext) &&
                        s.Metadata != null && 
                        s.Metadata.PinnedContexts.HasFlag(leaderboardContext))
                    .Select(s => new AttemptResponseWithMyScore {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        PassPP = s.PassPP,
                        AccPP = s.AccPP,
                        TechPP = s.TechPP,
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
                        PlayCount = publicHistory ? s.PlayCount : 0,
                        Hmd = s.Hmd,
                        Controller = s.Controller,
                        MaxCombo = s.MaxCombo,
                        Timeset = s.Timeset,
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        Metadata = s.Metadata,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak
                    })
                    .ToListAsync();

            resultList.AddRange(await _storageContext
                    .PlayerLeaderboardStats
                    .AsNoTracking()
                    .TagWithCaller()
                    .AsSplitQuery()
                    .Where(s => 
                        s.PlayerId == id && 
                        s.Metadata != null && 
                        s.Metadata.PinnedContexts.HasFlag(leaderboardContext))
                    .Select(s => new AttemptResponseWithMyScore {
                        Id = s.Id,
                        BaseScore = s.BaseScore,
                        ModifiedScore = s.ModifiedScore,
                        PlayerId = s.PlayerId,
                        Accuracy = s.Accuracy,
                        Pp = s.Pp,
                        PassPP = s.PassPP,
                        AccPP = s.AccPP,
                        TechPP = s.TechPP,
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
                        ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                        Timepost = s.Timepost,
                        LeaderboardId = s.LeaderboardId,
                        Platform = s.Platform,
                        ScoreImprovement = s.ScoreImprovement,
                        Metadata = s.Metadata,
                        Country = s.Country,
                        Offsets = s.ReplayOffsets,
                        Weight = s.Weight,
                        AccLeft = s.AccLeft,
                        AccRight = s.AccRight,
                        MaxStreak = s.MaxStreak,

                        EndType = s.Type,
                        AttemptsCount = s.AttemptsCount,
                        Time = s.Time,
                        StartTime = s.StartTime,
                    })
                    .ToListAsync());

            bool showRatings = HttpContext.ShouldShowAllRatings(_context);

            var lbIds = resultList.Select(s => s.LeaderboardId).ToList();
            var leaderboards = (await _context.Leaderboards
                .AsNoTracking()
                .TagWithCallerS()
                .Where(lb => lbIds.Contains(lb.Id))
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
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
                        ModifierValues = lb.Difficulty.ModifierValues,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        NominatedTime  = lb.Difficulty.NominatedTime,
                        QualifiedTime  = lb.Difficulty.QualifiedTime,
                        RankedTime = lb.Difficulty.RankedTime,

                        Stars  = lb.Difficulty.Stars,
                        PredictedAcc  = lb.Difficulty.PredictedAcc,
                        PassRating  = lb.Difficulty.PassRating,
                        AccRating  = lb.Difficulty.AccRating,
                        TechRating  = lb.Difficulty.TechRating,
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
                }).ToListAsync()).ToDictionary(lb => lb.Id, lb => lb);

            foreach (var resultScore in resultList) {
                resultScore.Leaderboard = leaderboards[resultScore.LeaderboardId];

                if (!showRatings && !resultScore.Leaderboard.Difficulty.Status.WithRating()) {
                    resultScore.Leaderboard.HideRatings();
                }
            }

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None)
            {
                var scoreIds = resultList.Select(s => s.Id).ToList();

                var contexts = await _context
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Where(s => s.Context == leaderboardContext && s.ScoreId != null && scoreIds.Contains((int)s.ScoreId))
                    .ToListAsync();

                for (int i = 0; i < resultList.Count && i < contexts.Count; i++)
                {
                    var ce = contexts.FirstOrDefault(c => c.ScoreId == resultList[i].Id);
                    if (ce != null)
                    {
                        resultList[i].ToContext(ce);
                    }
                }

            }
            return resultList.OrderBy(s => s.Metadata.Priority).ToList();
        }
    }
}
