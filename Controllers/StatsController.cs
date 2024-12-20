﻿using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReplayDecoder;
using Swashbuckle.AspNetCore.Annotations;
using System.Buffers;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class StatsController : Controller
    {
        private readonly AppContext _context;
        private readonly StorageContext _storageContext;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly IAmazonS3 _s3Client;

        public StatsController(
            AppContext context,
            StorageContext storageContext,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;
            _storageContext = storageContext;

            _serverTiming = serverTiming;
            _configuration = configuration;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/player/{id}/scoresstats")]
        public async Task<ActionResult<ResponseWithMetadata<AttemptResponseWithMyScore>>> GetScoresStats(
            string id,
            [FromQuery, SwaggerParameter("Sorting criteria for scores, default is by 'date'")] ScoresSortBy sortBy = ScoresSortBy.Pp,
            [FromQuery, SwaggerParameter("Order of sorting, default is descending")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of scores per page, default is 8")] int count = 8,
            [FromQuery, SwaggerParameter("Filter scores by search term in song name, author or mapper. Default is null")] string? search = null,
            [FromQuery, SwaggerParameter("Filter scores by map difficulty(Easy, Expert, Expert+, etc), default is null")] string? diff = null,
            [FromQuery, SwaggerParameter("Filter scores by map characteristic(Standard, OneSaber, etc), default is null")] string? mode = null,
            [FromQuery, SwaggerParameter("Filter scores by map requirements, default is 'None'")] Requirements requirements = Requirements.None,
            [FromQuery, SwaggerParameter("Filter scores by map status, default is null")] DifficultyStatus? type = null,
            [FromQuery, SwaggerParameter("Filter scores by headset, default is null")] HMD? hmd = null,
            [FromQuery, SwaggerParameter("Filter scores by modifiers(GN, SF, etc), default is null")] string? modifiers = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars greater than, default is null")] float? stars_from = null,
            [FromQuery, SwaggerParameter("Filter scores on ranked maps with stars lower than, default is null")] float? stars_to = null,
            [FromQuery, SwaggerParameter("Filter scores made after unix timestamp, default is null")] int? time_from = null,
            [FromQuery, SwaggerParameter("Filter scores made before unix timestamp, default is null")] int? time_to = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] int? eventId = null,
            [FromQuery, SwaggerParameter("Show only scores from the event with ID, default is null")] EndType? endType = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            bool admin = currentID != null ? ((await _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.Role)
                .FirstOrDefaultAsync())
                ?.Contains("admin") ?? false) : false;

            id = await _context.PlayerIdToMain(id);

            var currentProfileSettings = currentID != null ? await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == currentID && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync() : null;
            var profileSettings = await _context
                .Players
                .AsNoTracking()
                .Where(p => p.Id == id && p.ProfileSettings != null)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();

            if (!(currentID == id || admin || (profileSettings != null && profileSettings.ShowStatsPublic))) {
                return Unauthorized();
            }

            if (count > 100 || count < 0) {
                return BadRequest("Please use `count` value in range of 0 to 100");
            }
            if (page < 1) {
                page = 1;
            }

            bool showRatings = currentProfileSettings?.ShowAllRatings ?? false;

            IQueryable<PlayerLeaderboardStats> query = _storageContext.PlayerLeaderboardStats
                   .AsNoTracking()
                   .Where(t => t.PlayerId == id);

            IQueryable<PlayerLeaderboardStats>? sequence = await query.Filter(_context, showRatings, sortBy, order, search, diff, mode, requirements, endType, type, hmd, modifiers, stars_from, stars_to, time_from, time_to, eventId);


            ResponseWithMetadata<AttemptResponseWithMyScore> result;
            using (_serverTiming.TimeAction("db"))
            {
                result = new ResponseWithMetadata<AttemptResponseWithMyScore>()
                {
                    Metadata = new Metadata()
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = await sequence.CountAsync()
                    },
                    Data = await sequence
                        .Skip((page - 1) * count)
                        .Take(count)
                        .Select(s => new AttemptResponseWithMyScore {
                            Id = s.ScoreId,
                            EndType = s.Type,
                            AttemptsCount = s.AttemptsCount,
                            Time = s.Time,
                            StartTime = s.StartTime,
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
                            ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                            Timepost = s.Timepost,
                            LeaderboardId = s.LeaderboardId,
                            Platform = s.Platform,
                            ScoreImprovement = s.ScoreImprovement,
                            Offsets = s.ReplayOffsets,
                            Country = s.Country,
                            Weight = s.Weight,
                            AccLeft = s.AccLeft,
                            AccRight = s.AccRight,
                            MaxStreak = s.MaxStreak
                        })
                        .ToListAsync()
                };
            }

            var leaderboarIds = result.Data.Select(s => s.LeaderboardId).ToList();

            if (currentID != null && currentID != id) {
                var myScores = await _context
                    .Scores
                    .AsSplitQuery()
                    .AsNoTracking()
                    .Where(s => s.PlayerId == currentID && leaderboarIds.Contains(s.LeaderboardId))
                    .Select(s => new ScoreResponseWithMyScore {
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
                            PlayCount = s.PlayCount,
                            LastTryTime = s.LastTryTime,
                            Hmd = s.Hmd,
                            Controller = s.Controller,
                            MaxCombo = s.MaxCombo,
                            Timeset = s.Timeset,
                            ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                            Timepost = s.Timepost,
                            LeaderboardId = s.LeaderboardId,
                            Platform = s.Platform,
                            Weight = s.Weight,
                            AccLeft = s.AccLeft,
                            AccRight = s.AccRight,
                            Player = s.Player != null ? new PlayerResponse {
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
                                PatreonFeatures = s.Player.PatreonFeatures,
                                ProfileSettings = s.Player.ProfileSettings,
                                ContextExtensions = s.Player.ContextExtensions != null ? s.Player.ContextExtensions.Select(ce => new PlayerContextExtension {
                                    Context = ce.Context,
                                    Pp = ce.Pp,
                                    AccPp = ce.AccPp,
                                    TechPp = ce.TechPp,
                                    PassPp = ce.PassPp,
                                    PlayerId = ce.PlayerId,

                                    Rank = ce.Rank,
                                    Country  = ce.Country,
                                    CountryRank  = ce.CountryRank,
                                }).ToList() : null,
                                Clans = s.Player.Clans.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                                        .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                            } : null,
                            ScoreImprovement = s.ScoreImprovement,
                            RankVoting = s.RankVoting,
                            Metadata = s.Metadata,
                            Country = s.Country,
                            Offsets = s.ReplayOffsets,
                            MaxStreak = s.MaxStreak
                        }).ToListAsync();
                foreach (var score in result.Data) {
                    score.MyScore = myScores.FirstOrDefault(s => s.LeaderboardId == score.LeaderboardId);
                }
            }

            var leaderboards = await _context
                .Leaderboards
                .AsSplitQuery()
                .AsNoTracking()
                .Where(l => leaderboarIds.Contains(l.Id))
                .Select(l => new CompactLeaderboardResponse {
                        Id = l.Id,
                        Song = new CompactSongResponse {
                            Id = l.Song.Id,
                            Hash = l.Song.Hash,
                            Name = l.Song.Name,
            
                            SubName = l.Song.SubName,
                            Author = l.Song.Author,
                            Mapper = l.Song.Mapper,
                            MapperId = l.Song.MapperId,
                            CollaboratorIds = l.Song.CollaboratorIds,
                            CoverImage = l.Song.CoverImage,
                            FullCoverImage = l.Song.FullCoverImage,
                            Bpm = l.Song.Bpm,
                            Duration = l.Song.Duration,
                        },
                        Difficulty = new DifficultyResponse {
                            Id = l.Difficulty.Id,
                            Value = l.Difficulty.Value,
                            Mode = l.Difficulty.Mode,
                            DifficultyName = l.Difficulty.DifficultyName,
                            ModeName = l.Difficulty.ModeName,
                            Status = l.Difficulty.Status,
                            ModifierValues = l.Difficulty.ModifierValues != null 
                                ? l.Difficulty.ModifierValues
                                : new ModifiersMap(),
                            ModifiersRating = l.Difficulty.ModifiersRating,
                            NominatedTime  = l.Difficulty.NominatedTime,
                            QualifiedTime  = l.Difficulty.QualifiedTime,
                            RankedTime = l.Difficulty.RankedTime,

                            Stars  = l.Difficulty.Stars,
                            PredictedAcc  = l.Difficulty.PredictedAcc,
                            PassRating  = l.Difficulty.PassRating,
                            AccRating  = l.Difficulty.AccRating,
                            TechRating  = l.Difficulty.TechRating,
                            Type  = l.Difficulty.Type,

                            Njs  = l.Difficulty.Njs,
                            Nps  = l.Difficulty.Nps,
                            Notes  = l.Difficulty.Notes,
                            Bombs  = l.Difficulty.Bombs,
                            Walls  = l.Difficulty.Walls,
                            MaxScore = l.Difficulty.MaxScore,
                            Duration  = l.Difficulty.Duration,

                            Requirements = l.Difficulty.Requirements,
                        }
                    })
                .ToListAsync();
            foreach (var score in result.Data) {
                score.Leaderboard = leaderboards.FirstOrDefault(s => s.Id == score.LeaderboardId);
                if (!showRatings && !score.Leaderboard.Difficulty.Status.WithRating()) {
                    score.Leaderboard.HideRatings();
                }
            }

            return result;
        }

        [HttpGet("~/map/scorestats")]
        public async Task<ActionResult> GetScorestatsOnMap([FromQuery] string playerId, [FromQuery] string leaderboardId) {
            string? currentID = HttpContext.CurrentUserID(_context);
            bool admin = currentID != null ? ((await _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.Role)
                .FirstOrDefaultAsync())
                ?.Contains("admin") ?? false) : false;

            playerId = await _context.PlayerIdToMain(playerId);

            var features = await _context
                .Players
                .Where(p => p.Id == playerId)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();

            if (!(currentID == playerId || admin || (features != null && features.ShowStatsPublic))) {
                return Unauthorized();
            }

            return Ok(
                await _storageContext
                .PlayerLeaderboardStats
                .Where(s => s.PlayerId == playerId && s.LeaderboardId == leaderboardId)
                .ToListAsync());
        }

        [NonAction]
        public async Task<(StatsGraph?, ReplayOffsets?)> CollectGraph(int id, string replayLink, int? start, int? length) {

            List<NoteEvent>? notes = null;
            List<WallEvent>? walls = null;
            ReplayOffsets? offsets = null;

            string filename = replayLink.Split("/").Last();
            
            try {
                if (start != null && length != null) {
                    using (var stream = await _s3Client.DownloadStreamOffset(filename, replayLink.Contains("otherreplays") ? S3Container.otherreplays : S3Container.replays, (int)start, (int)length)) {
                        if (stream == null) return (null, null);

                        byte[] replayData;

                        List<byte> replayDataList = new List<byte>(); 
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

                        while (true)
                        {
                            var bytesRemaining = await stream.ReadAsync(buffer, offset: 0, buffer.Length);
                            if (bytesRemaining == 0)
                            {
                                break;
                            }
                            replayDataList.AddRange(new Span<byte>(buffer, 0, bytesRemaining).ToArray());
                        }

                        ArrayPool<byte>.Shared.Return(buffer);

                        replayData = replayDataList.ToArray();
                        
                        int pointer = 0;
                        notes = ReplayDecoder.ReplayDecoder.DecodeNotes(replayData, ref pointer);
                        pointer++;
                        walls = ReplayDecoder.ReplayDecoder.DecodeWalls(replayData, ref pointer);
                    }
                } else {
                    using (var stream = await _s3Client.DownloadStream(filename, replayLink.Contains("otherreplays") ? S3Container.otherreplays : S3Container.replays)) {
                        if (stream == null) return (null, null);

                        Replay? replay;
                        byte[] replayData;

                        List<byte> replayDataList = new List<byte>(); 
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

                        while (true)
                        {
                            var bytesRemaining = await stream.ReadAsync(buffer, offset: 0, buffer.Length);
                            if (bytesRemaining == 0)
                            {
                                break;
                            }
                            replayDataList.AddRange(new Span<byte>(buffer, 0, bytesRemaining).ToArray());
                        }

                        ArrayPool<byte>.Shared.Return(buffer);

                        replayData = replayDataList.ToArray();
                        (replay, offsets) = ReplayDecoder.ReplayDecoder.Decode(replayData);
                        notes = replay?.notes;
                        walls = replay?.walls;
                    }
                }
            } catch {
            }

            if (notes == null || walls == null) return (null, null);

            (_, var notesStructs, _, _) = ReplayStatistic.Accuracy(notes, walls);

            return (new StatsGraph { 
                AttemptId = id,
                Notes = notesStructs.Select(n => new StatsNote {
                    SpawnTime = n.spawnTime,
                    Score = n.score,
                    Accuracy = n.accuracy
                }).ToList()
            }, offsets);
        }

        [HttpGet("~/map/scorestats/graph")]
        public async Task<ActionResult<StatsRecord>> GetScorestatsGraphOnMap([FromQuery] string playerId, [FromQuery] string leaderboardId) {
            string? currentID = HttpContext.CurrentUserID(_context);
            bool admin = currentID != null ? ((await _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.Role)
                .FirstOrDefaultAsync())
                ?.Contains("admin") ?? false) : false;

            playerId = await _context.PlayerIdToMain(playerId);

            var features = await _context
                .Players
                .Where(p => p.Id == playerId)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();

            if (!(currentID == playerId || admin || (features != null && features.ShowStatsPublic))) {
                return Unauthorized();
            }

            StatsRecord? result = null;
            string filename = playerId + "_" + leaderboardId + ".json";

            using (var stream = await _s3Client.DownloadStream(filename, S3Container.attempts)) {
                if (stream != null) {
                    result = stream.ObjectFromStream<StatsRecord>();
                }
            }

            if (result == null) {
                result = new StatsRecord { Scores = new List<StatsGraph>() };
            }

            var existingStats = result.Scores.Select(s => s.AttemptId).ToList();
            var replays = await _storageContext
                .PlayerLeaderboardStats
                .Where(s => !existingStats.Contains(s.Id) && s.PlayerId == playerId && s.LeaderboardId == leaderboardId && s.Replay != null)
                .Select(s => new { s.Id, s.Replay, s.ReplayOffsets, s.AttemptsCount })
                .ToListAsync();
            if (replays.Count > 0) {
                var tasks = replays.Select(r => CollectGraph(r.Id, r.Replay, r.ReplayOffsets?.Notes, (r.ReplayOffsets?.Heights ?? 0) - (r.ReplayOffsets?.Notes ?? 0)));
                var scores = (await Task.WhenAll(tasks)).Where(t => t.Item1 != null).ToList();

                var offsetsIdsToUpdate = scores.Where(t => t.Item2 != null).Select(t => t.Item1.AttemptId).ToList();
                if (offsetsIdsToUpdate.Count > 0) {
                    var stats = await _storageContext
                    .PlayerLeaderboardStats
                    .Where(s => offsetsIdsToUpdate.Contains(s.Id))
                    .ToListAsync();

                    foreach (var stat in stats) {
                        stat.ReplayOffsets = scores.FirstOrDefault(s => s.Item1.AttemptId == stat.Id).Item2;
                    }
                    _storageContext.SaveChanges();
                }

                if (scores.Count > 0) {
                    foreach (var score in scores) {
                        result.Scores.Add(score.Item1);
                    }

                    await _s3Client.UploadStatsRecord(filename, result);
                }
            }

            return result;
        }

        [HttpGet("~/otherreplays/{name}")]
        public async Task<ActionResult> GetOtherReplay(string name) {

            string? currentID = HttpContext.CurrentUserID(_context);
            bool admin = currentID != null ? ((await _context
                .Players
                .Where(p => p.Id == currentID)
                .Select(p => p.Role)
                .FirstOrDefaultAsync())
                ?.Contains("admin") ?? false) : false;

            var playerId = await _context.PlayerIdToMain(name.Split("-").First());

            var features = await _context
                .Players
                .Where(p => p.Id == playerId)
                .Select(p => p.ProfileSettings)
                .FirstOrDefaultAsync();

            if (!(currentID == playerId || admin || (features != null && features.ShowStatsPublic))) {
                return Unauthorized();
            }

            var stream = await _s3Client.DownloadOtherReplay(name);
            if (stream == null) {
                return NotFound();
            }

            return Ok(stream);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/watched/{scoreId}/")]
        public async Task<ActionResult> Played(
            int scoreId)
        {
            var ip = HttpContext.GetIpAddress();

            if (ip == null) return BadRequest();

            string ipString = ip;
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID != null) {
                if ((await _context.WatchingSessions.FirstOrDefaultAsync(ws => ws.ScoreId == scoreId && ws.Player == currentID)) != null) return Ok();
            } else {
                if ((await _context.WatchingSessions.FirstOrDefaultAsync(ws => ws.ScoreId == scoreId && ws.IP == ipString)) != null) return Ok();
            }

            Score? score = await _context.Scores.FindAsync(scoreId);
            if (score == null) return NotFound();
            if (score.PlayerId == currentID) return Ok();

            Player? scoreMaker = await _context.Players.Where(p => p.Id == score.PlayerId).Include(p => p.ScoreStats).FirstOrDefaultAsync();
            if (scoreMaker == null) return NotFound();
            if (scoreMaker.ScoreStats == null) {
                scoreMaker.ScoreStats = new PlayerScoreStats();
            }

            if (currentID != null)
            {
                score.AuthorizedReplayWatched++;
                scoreMaker.ScoreStats.AuthorizedReplayWatched++;
                var player = await _context.Players.Where(p => p.Id == currentID).Include(p => p.ScoreStats).FirstOrDefaultAsync();
                if (player != null && player.ScoreStats != null) {
                    player.ScoreStats.WatchedReplays++;
                }
            } else {
                score.AnonimusReplayWatched++;
                scoreMaker.ScoreStats.AnonimusReplayWatched++;
            }

            _context.WatchingSessions.Add(new ReplayWatchingSession {
                ScoreId = scoreId,
                IP = currentID == null ? ipString : null,
                Player = currentID
            });
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
