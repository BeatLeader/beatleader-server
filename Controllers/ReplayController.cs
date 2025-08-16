using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Discord;
using Discord.Webhook;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Primitives;
using Prometheus.Client;
using System.Buffers;
using ReplayDecoder;
using static BeatLeader_Server.Utils.ResponseUtils;
using System.Text;
using BeatLeader_Server.ControllerHelpers;
using System.Net;
using beatleader_parser;
using Parser.Utils;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class ReplayController : Controller
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IDbContextFactory<AppContext> _dbFactory;
        private readonly IDbContextFactory<StorageContext> _storageFactory;

        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IServerTiming _serverTiming;
        private readonly IReplayRecalculator? _replayRecalculator;

        private readonly IMetricFamily<IGauge> _replayLocation;
        private readonly Geohash.Geohasher _geoHasher;
        private static IP2Location.Component ipLocation;

        public ReplayController(
            IDbContextFactory<AppContext> dbFactory,
            IDbContextFactory<StorageContext> storageFactory,
            IWebHostEnvironment env,
            IConfiguration configuration,
            IServerTiming serverTiming,
            IMetricFactory metricFactory,
            IReplayRecalculator? replayRecalculator)
        {
            _dbFactory = dbFactory;
            _environment = env;
            _configuration = configuration;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _storageFactory = storageFactory;

            _replayLocation = metricFactory.CreateGauge("replay_position", "Posted replay location", new string[] { "geohash" });
            _geoHasher = new Geohash.Geohasher();

            if (ipLocation == null)
            {
                ipLocation = new IP2Location.Component();
                ipLocation.Open(env.WebRootPath + "/databases/IP2LOCATION-LITE-DB.BIN");
            }
            
            _replayRecalculator = replayRecalculator;
        }

        [HttpPost("~/replay"), DisableRequestSizeLimit]
        public async Task<ActionResult<ScoreResponse>> PostSteamReplay([FromQuery] string ticket)
        {
            // DB Context disposed manually
            var dbContext = _dbFactory.CreateDbContext();
            var storageContext = _storageFactory.CreateDbContext();
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(ticket, _configuration);
            if (id == null && error != null) {
                dbContext.Dispose();
                storageContext.Dispose();
                return Unauthorized(error);
            }
            long intId = long.Parse(id);
            if (intId < 70000000000000000)
            {
                AccountLink? accountLink = await dbContext.AccountLinks.FirstOrDefaultAsync(el => el.PCOculusID == id);
                if (accountLink != null && accountLink.SteamID.Length > 0) {
                    id = accountLink.SteamID;
                }
            }
            var result = await PostReplayFromBody(dbContext, storageContext, id);
            if (result.Value.Status == ScoreUploadStatus.Uploaded) {
                return result.Value.Score;
            } else {
                return BadRequest(result.Value.Description);
            }
        }

        [HttpPut("~/replayoculus"), DisableRequestSizeLimit]
        [Authorize]
        public async Task<ActionResult<ScoreResponse>> PostOculusReplay(
            [FromQuery] float time = 0,
            [FromQuery] EndType type = 0)
        {
            // DB Context disposed manually
            var dbContext = _dbFactory.CreateDbContext();
            StorageContext? storageContext = _storageFactory.CreateDbContext();
            string? userId = HttpContext.CurrentUserID(dbContext);

            if (userId == null)
            {
                dbContext.Dispose();
                if (storageContext != null) {
                    storageContext.Dispose();
                }
                return Unauthorized("User is not authorized");
            }

            try {
                await storageContext.PlayerLeaderboardStats.Select(s => s.Id).FirstAsync();
            } catch (Exception e) {
                Console.WriteLine($"EXCEPTION storage DB: {e}");
                storageContext = null;
            }

            var result = await PostReplayFromBody(dbContext, storageContext, userId, time, type);
            if (result.Value.Status == ScoreUploadStatus.Error) {
                await dbContext.DisposeAsync();
                if (storageContext != null) {
                    await storageContext.DisposeAsync();
                }
            }
            if (Response.Headers.ContainsKey("Set-Cookie")) {
                Response.Headers.Remove("Set-Cookie");
            }
            if (result.Value.Status == ScoreUploadStatus.Uploaded) {
                return result.Value.Score;
            } else if (result.Value.Status == ScoreUploadStatus.Attempt) {
                return Ok();
            } else {
                return BadRequest(result.Value.Description);
            }
        }

        [HttpPut("~/v2/replayoculus"), DisableRequestSizeLimit]
        [Authorize]
        public async Task<ActionResult<ScoreUploadResponse>> PostOculusReplayV2(
            [FromQuery] float time = 0,
            [FromQuery] EndType type = 0)
        {
            // DB Context disposed manually
            var dbContext = _dbFactory.CreateDbContext();
            StorageContext? storageContext = _storageFactory.CreateDbContext();
            string? userId = HttpContext.CurrentUserID(dbContext);

            if (userId == null)
            {
                dbContext.Dispose();
                if (storageContext != null) {
                    storageContext.Dispose();
                }
                return Unauthorized("User is not authorized");
            }

            try {
                await storageContext.PlayerLeaderboardStats.Select(s => s.Id).FirstAsync();
            } catch (Exception e) {
                Console.WriteLine($"EXCEPTION storage DB: {e}");
                storageContext = null;
            }

            var result = await PostReplayFromBody(dbContext, storageContext, userId, time, type);
            if (result.Value.Status == ScoreUploadStatus.Error) {
                await dbContext.DisposeAsync();
                if (storageContext != null) {
                    await storageContext.DisposeAsync();
                }
            }
            if (Response.Headers.ContainsKey("Set-Cookie")) {
                Response.Headers.Remove("Set-Cookie");
            }
            return result;
        }

        //[HttpPut("~/replayoculusadmin/{playerId}"), DisableRequestSizeLimit]
        [NonAction]
        [Authorize]
        public async Task<ActionResult<ScoreUploadResponse>> PostOculusReplayAdmin(
            string playerId,
            [FromQuery] float time = 0,
            [FromQuery] EndType type = 0)
        {
            // DB Context disposed manually
            var dbContext = _dbFactory.CreateDbContext();
            var storageContext = _storageFactory.CreateDbContext();
            string? userId = HttpContext.CurrentUserID(dbContext);

            if (userId == null)
            {
                dbContext.Dispose();
                storageContext.Dispose();
                return Unauthorized("User is not authorized");
            }

            var currentPlayer = await dbContext.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            
            var result = await PostReplay(dbContext, storageContext, playerId, Request.Body, HttpContext, time, type, false, 0);
            if (result.Status == ScoreUploadStatus.Error) {
                await dbContext.DisposeAsync();
                await storageContext.DisposeAsync();
            }
            return result;
        }

        //[HttpPut("~/replayoculusadminfolder/{playerId}"), DisableRequestSizeLimit]
        [NonAction]
        [Authorize]
        public async Task<ActionResult<ScoreResponse>> PostOculusReplayAdminFolder(
            string playerId,
            [FromQuery] string directory,
            [FromQuery] float time = 0,
            [FromQuery] EndType type = 0)
        {
            // DB Context disposed manually
            var dbContext = _dbFactory.CreateDbContext();
            var storageContext = _storageFactory.CreateDbContext();
            string? userId = HttpContext.CurrentUserID(dbContext);

            try {
                await storageContext.PlayerLeaderboardStats.Select(s => s.Id).FirstAsync();
            } catch (Exception e) {
                Console.WriteLine($"EXCEPTION storage DB: {e}");
                storageContext = null;
            }

            if (userId == null)
            {
                dbContext.Dispose();
                storageContext.Dispose();
                return Unauthorized("User is not authorized");
            }

            var currentPlayer = await dbContext.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(directory)) {
                if (file.Contains("exit")) {
                    await PostReplay(dbContext, storageContext, playerId, System.IO.File.OpenRead(file), HttpContext, time, type, false, 0);
                }
            } 

            return Ok();
        }

        [NonAction]
        public async Task<ActionResult<ScoreUploadResponse>> PostReplayFromBody(AppContext dbContext, StorageContext? storage, string authenticatedPlayerID, float time = 0, EndType type = 0)
        {
            return await PostReplay(dbContext, storage, authenticatedPlayerID, Request.Body, HttpContext, time, type);
        }

        [NonAction]
        public async Task<ActionResult<ScoreUploadResponse>> PostReplayFromCDN(string authenticatedPlayerID, string name, bool backup, bool allow, string timeset, HttpContext context)
        {
            // DB Context disposed manually
            var dbContext = _dbFactory.CreateDbContext();
            var storageContext = _storageFactory.CreateDbContext();
            ActionResult<ScoreUploadResponse> result;
            if (backup) {
                string directoryPath = Path.Combine("/root/replays");
                string filePath = Path.Combine(directoryPath, name);

                if (!System.IO.File.Exists(filePath)) {
                    result = NotFound();
                }

                // Use FileMode.Create to overwrite the file if it already exists.
                using (FileStream stream = new FileStream(filePath, FileMode.Open)) {
                    result = await PostReplay(dbContext, storageContext, authenticatedPlayerID, stream, context, allow: allow, timesetForce: int.Parse(timeset));
                }
            } else {
                using (var stream = await _s3Client.DownloadReplay(name)) {
                    
                    if (stream != null) {
                        result = await PostReplay(dbContext, storageContext, authenticatedPlayerID, stream, context, allow: allow, timesetForce: int.Parse(timeset));
                    } else {
                        result = NotFound();
                    }
                }
            }

            if (result.Value == null) {
                dbContext.Dispose();
                storageContext.Dispose();
            }
            return result;
        }

        [NonAction]
        public async Task<ScoreUploadResponse> PostReplay(
            AppContext dbContext,
            StorageContext? storageContext,
            string authenticatedPlayerID, 
            Stream replayStream,
            HttpContext context,
            float time = 0, 
            EndType type = 0,
            bool allow = false,
            int? timesetForce = null)
        {
            Replay? replay;
            ReplayOffsets? offsets;
            byte[] replayData;

            using (var ms = new MemoryStream(5))
            {
                await replayStream.CopyToAsync(ms);
                long length = ms.Length;
                if (length > 200000000)
                {
                    return new() {
                        Status = ScoreUploadStatus.Error,
                        Description = "Replay is too big to save, sorry"
                    };
                }
                replayData = ms.ToArray();
            }

            try
            {
                (replay, offsets) = ReplayDecoder.ReplayDecoder.Decode(replayData);
            }
            catch (Exception)
            {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Error decoding replay"
                };
            }
            var info = replay?.info;

            if (replay == null || info == null) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "It's not a replay or it has old version."
                };
            }
            if (replay.notes.Count == 0 || replay.frames.Count == 0) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Replay is broken, update your mod please."
                };
            }

            if (replay.info.score <= 0) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "The score should be positive"
                };
            }

            var gameversion = replay.info.gameVersion.Split(".");
            if (replay.info.mode.EndsWith("OldDots") || (gameversion.Length == 3 && int.Parse(gameversion[1]) < 20)) {
                replay.info.mode = replay.info.mode.Replace("OldDots", "");
                replay.info.modifiers += replay.info.modifiers.Length > 0 ? ",OD" : "OD";
            }

            var version = info.version.Split(".");
            if (version.Length < 3 || int.Parse(version[1]) < 3) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Please update your mod. v0.3 or higher"
                };
            }

            info.playerID = authenticatedPlayerID;
            if (info.hash.Length >= 40) {
                info.hash = info.hash.Substring(0, 40);
            }

            if (await dbContext.EarthDayMaps.AnyAsync(dm => dm.Hash == info.hash && dm.PlayerId == authenticatedPlayerID)) {
                info.hash = "EarthDay2025";
            }

            Leaderboard? leaderboard;
            using (_serverTiming.TimeAction("ldbrd"))
            {
                leaderboard = await LeaderboardControllerHelper.GetByHash(dbContext, info.hash, info.difficulty, info.mode);
                if (leaderboard == null)
                {
                    return new() {
                        Status = ScoreUploadStatus.Error,
                        Description = "No such leaderboard exists."
                    };
                }
            }

            var gameVersion = info.gameVersion.Split(".");
            if (gameVersion.Length == 3 && int.Parse(gameVersion[1]) < 40 && leaderboard.Difficulty.Requirements.HasFlag(Requirements.VNJS)) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Variable NJS maps supported only on 1.40+"
                };
            }

            var ip = context.Request.HttpContext.GetIpAddress();
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                player = await PlayerControllerHelper.GetLazy(dbContext, _configuration, info.playerID);
                if (player == null)
                {
                    player = new Player();
                    player.Id = info.playerID;
                    player.Name = info.playerName;
                    player.Platform = info.platform;
                    player.ScoreStats = new PlayerScoreStats();
                    player.ContextExtensions = new List<PlayerContextExtension>();
                    foreach (var contxt in ContextExtensions.NonGeneral) {
                        if (player.ContextExtensions.FirstOrDefault(ce => ce.Context == contxt) == null) {
                            player.ContextExtensions.Add(new PlayerContextExtension {
                                Context = contxt,
                                ScoreStats = new PlayerScoreStats(),
                                PlayerId = player.Id,
                                Country = player.Country
                            });
                        }
                    }
                    player.SetDefaultAvatar();
                    player.SanitizeName();

                    dbContext.Players.Add(player);

                    PlayerSearchService.AddNewPlayer(player);
                }

                if (player.Country == "not set")
                {
                    string? country = null;

                    if (context.Request.Headers["cf-ipcountry"] != StringValues.Empty) {
                       country = context.Request.Headers["cf-ipcountry"].ToString();
                    }
                    
                    if (country == null && ip != null)
                    {
                        country = WebUtils.GetCountryByIp(ip.ToString());
                    }
                    if (country != null) {
                        player.Country = country;
                        foreach (var item in player.ContextExtensions) {
                            item.Country = country;
                        }
                    }
                }
            }

            if (ip != null) {
                string hashId = AuthUtils.HashIp(ip);
                if (await dbContext.IpBans.AnyAsync(ib => ib.HashId == hashId)) {
                    return new() {
                        Status = ScoreUploadStatus.Error,
                        Description = "You are banned!"
                    };
                }
            }

            if (!player.Bot && player.Banned) { 
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "You are banned!"
                };
            }

            //if (_replayRecalculator != null && 
            //    leaderboard.Difficulty.Requirements.HasFlag(Requirements.V3Pepega) &&
            //    !leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) &&
            //    !leaderboard.Difficulty.Requirements.HasFlag(Requirements.MappingExtensions)) {
            //    (int? newScore, var fixedReplay) = await _replayRecalculator.RecalculateReplay(replay);

            //    if (newScore != null && (newScore / (float)replay.info.score) > 0.95f && (newScore / (float)replay.info.score) < 1.15f) {
            //        replay = fixedReplay;
            //        replay.info.score = (int)newScore;
            //    }
            //}

            if (type != EndType.Unknown && type != EndType.Clear && (info.hash != "EarthDay2025" || type == EndType.Practice)) {
                int? forcetimeset = null;
                if (timesetForce == 0) {
                    time = replay.info.failTime;
                    forcetimeset = int.Parse(replay.info.timestamp);
                }
                var returnScore = await CollectStats(dbContext, storageContext, replay, offsets, replayData, null, authenticatedPlayerID, leaderboard, time, type, player, null, null, forcetimeset);

                var resultResponse = new ScoreUploadResponse {
                    Status = ScoreUploadStatus.Attempt,
                    Description = "New attempt"
                };
                returnScore.Player = player;
                returnScore.PlayerId = player.Id;
                returnScore.LeaderboardId = leaderboard.Id;
                resultResponse.Score = RemoveLeaderboard(returnScore, returnScore.Rank);
                resultResponse.Score.Player = PostProcessSettings(resultResponse.Score.Player, false);

                return resultResponse;
            }

            

            if (replay.notes.Last().eventTime - replay.frames.Last().time > 2) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Replay missing frame data past note data."
                };
            }

            if (leaderboard.Difficulty.Notes > 0 && replay.notes.Count >= (leaderboard.Difficulty.Notes + leaderboard.Difficulty.Chains) * 1.8) {
                var cleanedNotes = new List<NoteEvent>();
                for (int i = 0; i < replay.notes.Count() - 1; i += 2) {
                    var firstNote = replay.notes[i];
                    var secondNote = replay.notes[i + 1];

                    if (firstNote.spawnTime != secondNote.spawnTime || firstNote.noteID != secondNote.noteID) {
                        cleanedNotes.Add(firstNote);
                        i--;
                        continue;
                    }
                    if (firstNote.noteCutInfo == null || (firstNote.noteCutInfo.cutPoint.x == 0 && firstNote.noteCutInfo.cutPoint.y == 0 && firstNote.noteCutInfo.cutPoint.z == 0)) {
                        cleanedNotes.Add(secondNote);
                    } else {
                        cleanedNotes.Add(firstNote);
                    }
                }
                if (cleanedNotes.Count() >= leaderboard.Difficulty.Notes) {
                    await _s3Client.UploadReplay("backup-" + ReplayUtils.ReplayFilename(replay, null, true), replayData);
                    string? error = ReplayUtils.RemoveDuplicatesWithNotes(replay, cleanedNotes, leaderboard);
                    using (var recalculatedStream = new MemoryStream()) {
                        ReplayEncoder.Encode(replay, new BinaryWriter(recalculatedStream, Encoding.UTF8));

                        replayData = recalculatedStream.ToArray();
                    }
                }
            }

            if ((leaderboard.Difficulty.Notes + leaderboard.Difficulty.Chains) * 0.8 > replay.notes.Count) {
                await RefreshNoteCount(dbContext, leaderboard);
            }

            if ((leaderboard.Difficulty.Status.WithRating()) && leaderboard.Difficulty.Notes != 0 && replay.notes.Count > (leaderboard.Difficulty.Notes + leaderboard.Difficulty.Chains)) {
                string? error = ReplayUtils.RemoveDuplicates(replay, leaderboard);
                if (error != null) {
                    return new() {
                        Status = ScoreUploadStatus.Error,
                        Description = "Failed to delete duplicate note: " + error
                    };
                }
            }

            (Score resultScore, int maxScore) = ReplayUtils.ProcessReplay(replay, leaderboard.Difficulty);

            if (timesetForce != null) {
                if (timesetForce == 0) {
                    resultScore.Timepost = int.Parse(replay.info.timestamp) + (int)(leaderboard.Song?.Duration ?? 0) + 3;
                } else {
                    resultScore.Timepost = (int)timesetForce;
                }
            }

            List<Score> currentScores;
            using (_serverTiming.TimeAction("currS"))
            {
                currentScores = await dbContext
                    .Scores
                    .Where(s =>
                        s.LeaderboardId == leaderboard.Id &&
                        s.PlayerId == info.playerID)
                    .Include(s => s.Player)
                    .ThenInclude(p => p.ScoreStats)
                    .Include(s => s.Player)
                    .ThenInclude(p => p.ContextExtensions)
                    .ThenInclude(ce => ce.ScoreStats)
                    .Include(s => s.Player)
                    .ThenInclude(p => p.Clans)
                    .Include(s => s.RankVoting)
                    .ThenInclude(v => v.Feedbacks)
                    .Include(s => s.ContextExtensions)
                    .Include(s => s.ReplayOffsets)
                    .Include(s => s.ScoreImprovement)
                    .Include(s => s.Metadata)
                    .AsSplitQuery()
                    .TagWithCaller()
                    .ToListAsync();
            }
            

            string[] njsExceptions = ["8bc991", "30a2e91", "620591"];

            if (!leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) && 
                !njsExceptions.Contains(leaderboard.Id) && 
                !ReplayUtils.IsPlayerCuttingNotesOnPlatform(replay)) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Please stay on the platform."
                };
            }

            if (!leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) && 
                ReplayUtils.IsPlayerCheesingPauses(replay)) {
                return new() {
                    Status = ScoreUploadStatus.Error,
                    Description = "Please pause less or stay still on pause."
                };
            }

            if (ipLocation != null && ip != null) {
                var location = ipLocation.IPQuery(ip);
                var hash = _geoHasher.Encode(Math.Round(location.Latitude / 3f) * 3, Math.Round(location.Longitude / 3f) * 3, 3);
                var counter = _replayLocation.WithLabels(new string[] { hash });
                counter.Inc();

                resultScore.HashId = AuthUtils.HashIp(ip);

                Task.Run(async () => {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    counter.Dec();
                });
            }

            resultScore.PlayerId = info.playerID;
            resultScore.LeaderboardId = leaderboard.Id;

            SocketController.ScoreWasUploaded(resultScore, dbContext);
            resultScore.LeaderboardId = null;

            var result = new ScoreUploadResponse {
                Status = ScoreUploadStatus.Error,
                Description = "Exception occured, ping NSGolova"
            };
             
            try {
                result = await UploadScores(
                    dbContext,
                    storageContext,
                    leaderboard, 
                    player,
                    resultScore, 
                    currentScores, 
                    replay,
                    replayData,
                    offsets,
                    context, 
                    maxScore,
                    allow);
                if (result.Status != ScoreUploadStatus.Uploaded) {
                    var returnScore = await CollectStats(dbContext, storageContext, replay, offsets, replayData, null, authenticatedPlayerID, leaderboard, replay.frames.Last().time, EndType.Clear, player, null);
                    returnScore.Player = player;
                    returnScore.PlayerId = player.Id;
                    returnScore.LeaderboardId = leaderboard.Id;
                    result.Score = RemoveLeaderboard(resultScore, resultScore.Rank);
                    result.Score.Player = PostProcessSettings(result.Score.Player, false);
                    try
                    {
                        await dbContext.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await ex.Entries.Single().ReloadAsync();
                        await dbContext.SaveChangesAsync();
                    }
                }
            } catch (Exception e) {

                dbContext.RejectChanges();

                resultScore.Replay = await _s3Client.UploadReplay(ReplayUtils.ReplayFilename(replay, resultScore, true), replayData);

                FailedScore failedScore = new FailedScore {
                    Error = e.StackTrace + "\n" + e.Message + (e.InnerException != null ? (" --- Inner: " + e.InnerException.Message) : ""),
                    Leaderboard = leaderboard,
                    PlayerId = resultScore.PlayerId,
                    Modifiers = resultScore.Modifiers,
                    Replay = resultScore.Replay,
                    Timeset = resultScore.Timeset,
                    BaseScore = resultScore.BaseScore,
                    ModifiedScore = resultScore.ModifiedScore,
                    Rank = resultScore.Rank,
                    Player = player,
                    Hmd = resultScore.Hmd,
                };
                dbContext.FailedScores.Add(failedScore);
                await dbContext.SaveChangesAsync();
            }

            if (result.Status != ScoreUploadStatus.Uploaded) {
                await dbContext.DisposeAsync();
            }

            return result;
        }

        class CurrentScoreWrapper {
            public Score Score { get; set; }
            public ICollection<ScoreContextExtension> ContextExtensions { get; set; }
            public LeaderboardContexts ValidContexts { get; set; }
        }

        [NonAction]
        private async Task<ScoreUploadResponse> UploadScores(
            AppContext dbContext,
            StorageContext? storageContext,
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            List<Score> currentScores,
            Replay replay,
            byte[] replayData,
            ReplayOffsets offsets,
            HttpContext context,
            int maxScore,
            bool allow = false) {

            var wrappedCurrentScores = currentScores.Select(s => new CurrentScoreWrapper {
                Score = s,
                ContextExtensions = s.ContextExtensions,
                ValidContexts = s.ValidContexts,
            }).ToList();

            if (player.ScoreStats == null) {
                player.ScoreStats = new PlayerScoreStats();
            }
            resultScore.Player = player;
            resultScore.Banned = player.Bot;

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && 
                player.SpeedrunStart > resultScore.Timepost - 60 * 60) {

                var lastScore = await dbContext
                    .Scores
                    .Where(s => s.PlayerId == player.Id)
                    .OrderByDescending(s => s.Timepost)
                    .Select(s => new { s.Timepost })
                    .FirstOrDefaultAsync();
                var songDuration = await dbContext
                    .Leaderboards
                    .Where(l => l.Id == leaderboard.Id)
                    .Select(l => new { l.Song.Duration })
                    .FirstOrDefaultAsync();

                if (lastScore == null || (songDuration != null && (resultScore.Timepost - lastScore.Timepost - songDuration.Duration / 2) > 0)) {
                    resultScore.ContextExtensions.Add(ReplayUtils.SpeedrunContextExtension(resultScore));
                }
            }

            foreach (var ce in resultScore.ContextExtensions)
            {
                ce.Banned = resultScore.Banned;
            }
            resultScore.Bot = player.Bot;

            await GeneralContextScore(dbContext, storageContext, leaderboard, player, resultScore, currentScores, replay);

            (ScoreStatistic? statistic, string? statisticError) = ScoreControllerHelper.CalculateStatisticFromReplay(replay, leaderboard, allow);
            if (statistic != null) {
                resultScore.AccLeft = statistic.accuracyTracker.accLeft;
                resultScore.AccRight = statistic.accuracyTracker.accRight;
                resultScore.MaxCombo = statistic.hitTracker.maxCombo;
                resultScore.FcAccuracy = statistic.accuracyTracker.fcAcc;
                resultScore.MaxStreak = statistic.hitTracker.maxStreak;
                resultScore.LeftTiming = statistic.hitTracker.leftTiming;
                resultScore.RightTiming = statistic.hitTracker.rightTiming;
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                    resultScore.FcPp = ReplayUtils.PpFromScore(
                        resultScore.FcAccuracy, 
                        resultScore.ValidContexts,
                        resultScore.Modifiers ?? "", 
                        leaderboard.Difficulty.ModifierValues ?? new ModifiersMap(), 
                        leaderboard.Difficulty.ModifiersRating, 
                        leaderboard.Difficulty.AccRating ?? 0, 
                        leaderboard.Difficulty.PassRating ?? 0, 
                        leaderboard.Difficulty.TechRating ?? 0, 
                        leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard").Item1;
                }
            }

            var oldPlayerStats = CollectPlayerStats(player);

            foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                await ContextScore(dbContext, leaderboardContext, leaderboard, player, resultScore, currentScores, replay);
            }

            if (resultScore.ValidContexts == LeaderboardContexts.None) {
                return new() {
                    Status = ScoreUploadStatus.NonPB,
                    Description = "Score is lower than existing one"
                };
            } else {
                using (_serverTiming.TimeAction("db"))
                {
                    foreach (var scoreToRemove in currentScores) {
                        if (scoreToRemove.ValidContexts == LeaderboardContexts.None) {
                            scoreToRemove.LeaderboardId = null;
                            dbContext.Scores.Remove(scoreToRemove);
                        }
                    }

                    foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                        if (resultScore.ValidContexts.HasFlag(leaderboardContext)) {
                            var ce = await dbContext
                                .ScoreContextExtensions
                                .Where(ce => 
                                    ce.Context == leaderboardContext &&
                                    ce.PlayerId == player.Id &&
                                    ce.LeaderboardId == leaderboard.Id)
                                .FirstOrDefaultAsync();
                            if (ce != null) {
                                dbContext.ScoreContextExtensions.Remove(ce);
                            }
                        }
                    }

                    try
                    {
                        await dbContext.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await ex.Entries.Single().ReloadAsync();
                        await dbContext.SaveChangesAsync();
                    }
                    
                    resultScore.LeaderboardId = leaderboard.Id;
                    dbContext.Scores.Add(resultScore);

                    try
                    {
                        await dbContext.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await ex.Entries.Single().ReloadAsync();
                        await dbContext.SaveChangesAsync();
                    }
                }

                resultScore.Replay = "https://cdn.replays.beatleader.com/" + ReplayUtils.ReplayFilename(replay, resultScore);
                await CollectStats(dbContext, storageContext, replay, offsets, replayData, resultScore.Replay, resultScore.PlayerId, leaderboard, replay.frames.Last().time, EndType.Clear, player, resultScore, statistic);
                await dbContext.SaveChangesAsync();

                await RecalculateRanks(dbContext, resultScore, currentScores, leaderboard, player);
                if (!player.Bot && leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                    try {
                        await dbContext.RecalculatePPAndRankFast(player, resultScore.ValidContexts);
                    } catch {
                        await dbContext.RecalculatePPAndRankFast(player, resultScore.ValidContexts);
                    }
                }
            
                context.Response.OnCompleted(async () => {
                    await PostUploadAction(
                        dbContext,
                        storageContext,
                        replay,
                        replayData,
                        leaderboard, 
                        player,
                        oldPlayerStats,
                        resultScore,
                        wrappedCurrentScores,
                        context, 
                        offsets,
                        statistic,
                        statisticError,
                        allow);
                    await dbContext.DisposeAsync();
                });

                var result = RemoveLeaderboard(resultScore, resultScore.Rank);
                result.Player = PostProcessSettings(result.Player, false);

                return new() {
                    Status = ScoreUploadStatus.Uploaded,
                    Score = result,
                    Description = "PB"
                };
            }
        }

        //[HttpGet("~/fixfailed")]
        //public async Task<ActionResult> FixFailed() {
        //    var fscores = _context.FailedScores.OrderByDescending(f => f.Timeset).Take(1000).Include(fs => fs.Leaderboard).ToList();
        //    foreach (var fscore in fscores)
        //    {
        //        var replayLink = fscore.Replay;
        //        var scoreReplayLink = replayLink.Replace("bsortemp", "bsor");
        //        var resultScore = _context
        //            .Scores
        //            .Include(s => s.Leaderboard)
        //            .ThenInclude(l => l.Song)
        //            .Include(s => s.Leaderboard)
        //            .ThenInclude(l => l.Difficulty)
        //            .Include(s => s.ContextExtensions)
        //            .Include(s => s.Player)
        //            .ThenInclude(l => l.ScoreStats)
        //            .Where(s => s.LeaderboardId == fscore.Leaderboard.Id && s.PlayerId == fscore.PlayerId && s.BaseScore == fscore.BaseScore)
        //            .FirstOrDefault();

        //        if (resultScore != null) {
        //            var player = resultScore.Player;
        //            if (player == null) continue;
        //            var leaderboard = resultScore.Leaderboard;

        //            string? name = replayLink.Split("/").LastOrDefault();
        //            var stream = await _s3Client.DownloadReplay(name);

        //            Replay? replay;
        //            ReplayOffsets? offsets;
        //            byte[] replayData;

        //            int length = 0;
        //            List<byte> replayDataList = new List<byte>(); 
        //            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

        //            while (true)
        //            {
        //                var bytesRemaining = await stream.ReadAsync(buffer, offset: 0, buffer.Length);
        //                if (bytesRemaining == 0)
        //                {
        //                    break;
        //                }
        //                length += bytesRemaining;
        //                replayDataList.AddRange(new Span<byte>(buffer, 0, bytesRemaining).ToArray());
        //            }

        //            ArrayPool<byte>.Shared.Return(buffer);

        //            replayData = replayDataList.ToArray();
        //            try
        //            {
        //                (replay, offsets) = ReplayDecoder.ReplayDecoder.Decode(replayData);
        //            }
        //            catch (Exception)
        //            {
        //                continue;
        //            }

        //            await RecalculateRanks(resultScore, new List<Score>(), leaderboard, player, null);
        //            await PostUploadAction(
        //                replay,
        //                replayData,
        //                leaderboard, 
        //                player,
        //                resultScore,
        //                new List<CurrentScoreWrapper>(),
        //                null, 
        //                offsets,
        //                false);

        //            _context.FailedScores.Remove(fscore);
        //            _context.SaveChanges();
        //        } 
        //        //else {
        //        //    string? name = fscore.Replay.Split("/").LastOrDefault();
        //        //    if (name == null) {
        //        //        continue;
        //        //    }
        //        //    var result = await PostReplayFromCDN(fscore.PlayerId, name, fscore.Replay.Contains("/backup/file"), false, fscore.Timeset, HttpContext);
        //        //    _context.FailedScores.Remove(fscore);
        //        //    await _context.SaveChangesAsync();
        //        //}

                
        //    }

        //    return Ok();
        //}

        [NonAction]
        private async Task GeneralContextScore(
            AppContext dbContext,
            StorageContext? storageContext,
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            List<Score> currentScores,
            Replay replay) {
            
            var info = replay.info;
            var currentScore = currentScores.FirstOrDefault(s => s.ValidContexts.HasFlag(LeaderboardContexts.General));
            
            if (!ReplayUtils.IsNewScoreBetter(currentScore, resultScore)) {
                return;
            }

            if (currentScore != null) {
                currentScore.ValidContexts &= ~LeaderboardContexts.General;
                currentScore.ValidForGeneral = false;
                resultScore.PlayCount = currentScore.PlayCount;
            } else if (storageContext != null) {
                resultScore.PlayCount = await storageContext.PlayerLeaderboardStats.Where(st => st.PlayerId == player.Id && st.LeaderboardId == leaderboard.Id).CountAsync();
            }

            resultScore.ValidContexts |= LeaderboardContexts.General;
            resultScore.ValidForGeneral = true;
            player.ScoreStats.UpdateScoreStats(currentScore, resultScore, player, leaderboard.Difficulty.Status == DifficultyStatus.ranked);

            ScoreImprovement improvement = new ScoreImprovement();
            resultScore.ScoreImprovement = improvement;

            if (currentScore != null)
            {
                improvement.Timeset = currentScore.Timeset;
                improvement.Score = resultScore.ModifiedScore - currentScore.ModifiedScore;
                improvement.Accuracy = resultScore.Accuracy - currentScore.Accuracy;

                improvement.BadCuts = resultScore.BadCuts - currentScore.BadCuts;
                improvement.BombCuts = resultScore.BombCuts - currentScore.BombCuts;
                improvement.MissedNotes = resultScore.MissedNotes - currentScore.MissedNotes;
                improvement.WallsHit = resultScore.WallsHit - currentScore.WallsHit;
                improvement.Modifiers = currentScore.Modifiers;
                var status1 = leaderboard.Difficulty.Status;

                if (!resultScore.IgnoreForStats) { 
                    if (status1 == DifficultyStatus.ranked)
                    {
                        float oldAverageAcc = player.ScoreStats.AverageRankedAccuracy;

                        improvement.AverageRankedAccuracy = player.ScoreStats.AverageRankedAccuracy - oldAverageAcc;
                    }
                }

                if (status1 is DifficultyStatus.ranked or DifficultyStatus.qualified)
                {
                    improvement.Pp = resultScore.Pp - currentScore.Pp;
                }

                if (currentScore.RankVoting != null)
                {
                    resultScore.RankVoting = new RankVoting
                    {
                        PlayerId = currentScore.RankVoting.PlayerId,
                        Hash = currentScore.RankVoting.Hash,
                        Diff = currentScore.RankVoting.Diff,
                        Mode = currentScore.RankVoting.Mode,
                        Rankability = currentScore.RankVoting.Rankability,
                        Stars = currentScore.RankVoting.Stars,
                        Type = currentScore.RankVoting.Type,
                        Timeset = currentScore.RankVoting.Timeset,
                        Feedbacks = currentScore.RankVoting.Feedbacks
                    };
                }
            }

            if (!player.Bot) {
                leaderboard.Plays++;
                leaderboard.LastScoreTime = resultScore.Timepost;
            }
        }

        [NonAction]
        private async Task ContextScore(
            AppContext dbContext,
            LeaderboardContexts context,
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            List<Score> currentScores,
            Replay replay) {

            var resultExtension = resultScore.ContextExtensions.FirstOrDefault(c => c.Context == context);
            if (resultExtension == null) return;
            
            var info = replay.info;
            var currentScore = currentScores.FirstOrDefault(s => s.ValidContexts.HasFlag(context));
            var currentExtension = currentScore?.ContextExtensions.FirstOrDefault(c => c.Context == context);
            
            if (!ReplayUtils.IsNewScoreExtensionBetter(currentExtension, resultExtension))
            {
                resultScore.ContextExtensions.Remove(resultExtension);
                return;
            }

            resultExtension.LeaderboardId = leaderboard.Id;
            resultExtension.PlayerId = player.Id;
            
            ScoreImprovement improvement = new ScoreImprovement();
            resultExtension.ScoreImprovement = improvement;
            if (currentScore != null) {
                currentScore.ValidContexts &= ~context;
                if (currentExtension != null) {
                    improvement.Timeset = currentScore.Timeset;
                    improvement.Score = resultScore.ModifiedScore - currentExtension.ModifiedScore;
                    improvement.Accuracy = resultScore.Accuracy - currentExtension.Accuracy;

                    improvement.BadCuts = resultScore.BadCuts - currentScore.BadCuts;
                    improvement.BombCuts = resultScore.BombCuts - currentScore.BombCuts;
                    improvement.MissedNotes = resultScore.MissedNotes - currentScore.MissedNotes;
                    improvement.WallsHit = resultScore.WallsHit - currentScore.WallsHit;
                    improvement.Modifiers = currentScore.Modifiers;
                    var status1 = leaderboard.Difficulty.Status;

                    if (!resultScore.IgnoreForStats) { 
                        if (status1 == DifficultyStatus.ranked)
                        {
                            float oldAverageAcc = player.ScoreStats.AverageRankedAccuracy;

                            improvement.AverageRankedAccuracy = player.ScoreStats.AverageRankedAccuracy - oldAverageAcc;
                        }
                    }

                    if (status1 is DifficultyStatus.ranked or DifficultyStatus.qualified)
                    {
                        improvement.Pp = resultScore.Pp - currentExtension.Pp;
                    }

                    dbContext.ScoreContextExtensions.Remove(currentExtension);
                }
            }
            resultScore.ValidContexts |= context;

            if (player.ContextExtensions == null) {
                player.ContextExtensions = new List<PlayerContextExtension>();
            }

            var playerContext = player.ContextExtensions.FirstOrDefault(c => c.Context == context);
            if (playerContext == null) {
                playerContext = new PlayerContextExtension {
                    Context = context,
                    PlayerId = player.Id,
                    ScoreStats = new PlayerScoreStats(),
                    Country = player.Country ?? "not set"
                };
                player.ContextExtensions.Add(playerContext);
            }
            playerContext.ScoreStats?.UpdateScoreStats(currentExtension, resultExtension, playerContext, leaderboard.Difficulty.Status == DifficultyStatus.ranked);
        }

        private async Task RecalculateRanks(
            AppContext dbContext,
            Score resultScore, 
            List<Score> currentScores,
            Leaderboard leaderboard, 
            Player player) {
            if (!player.Bot) {
                var isRanked = leaderboard.Difficulty.Status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent;
                await RefreshGeneneralContextRank(dbContext, leaderboard, resultScore, isRanked);
                
                foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                    await RefreshContextRank(dbContext, leaderboardContext, resultScore, leaderboard, isRanked);
                }
            }
        }

        private async Task RefreshGeneneralContextRank(AppContext dbContext, Leaderboard leaderboard, Score resultScore, bool isRanked) {
            if (!resultScore.ValidContexts.HasFlag(LeaderboardContexts.General)) return;

            var rankedScores = await dbContext
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                                !s.Banned && 
                                s.ValidForGeneral)
                    .AsNoTracking()
                    .Select(s => new Score { 
                        Id = s.Id, 
                        Rank = s.Rank, 
                        Priority = s.Priority, 
                        ModifiedScore = s.ModifiedScore,
                        Accuracy = s.Accuracy, 
                        Pp = s.Pp, 
                        Timepost = s.Timepost })
                    .ToListAsync();

            if (isRanked) {
                rankedScores = rankedScores
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenBy(el => el.Timepost)
                    .ToList();
            } else {
                rankedScores = rankedScores
                    .OrderBy(el => el.Priority)
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timepost)
                    .ToList();
            }

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                if (s.Id == resultScore.Id) {
                    resultScore.Rank = i + 1;
                }
                s.Rank = i + 1;
            }

            await dbContext.SafeBulkUpdateAsync(rankedScores, options => options.ColumnInputExpression = s => new { s.Rank });
        }

        class ScoreSelection {
            public int Id { get; set; }
            public int Rank { get; set; }
        }

        private async Task RefreshContextRank(
            AppContext dbContext,
            LeaderboardContexts context,
            Score resultScore,
            Leaderboard leaderboard,
            bool isRanked) {

            var resultContextExtension = resultScore.ContextExtensions.FirstOrDefault(ce => ce.Context == context);
            if (!resultScore.ValidContexts.HasFlag(context) || resultContextExtension == null) return;

            List<ScoreContextExtension> rankedScores;
            if (isRanked) {
                if (context != LeaderboardContexts.Golf) {
                    rankedScores = await dbContext
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenBy(el => el.Timepost)
                    .Select(s => new ScoreContextExtension() { Id = s.Id, Rank = s.Rank, Timepost = s.Timepost })
                    .ToListAsync();
                } else {
                    rankedScores = await dbContext
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenBy(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.ModifiedScore)
                    .ThenBy(el => el.Timepost)
                    .Select(s => new ScoreContextExtension() { Id = s.Id, Rank = s.Rank, Timepost = s.Timepost })
                    .ToListAsync();
                }
            } else {
                if (context != LeaderboardContexts.Golf) {
                    rankedScores = await dbContext
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderBy(el => el.Priority)
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timepost)
                    .Select(s => new ScoreContextExtension() { Id = s.Id, Rank = s.Rank, Timepost = s.Timepost })
                    .ToListAsync();
                } else {
                    rankedScores = await dbContext
                    .ScoreContextExtensions
                    .AsNoTracking()
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderByDescending(el => el.Priority)
                    .ThenBy(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timepost)
                    .Select(s => new ScoreContextExtension() { Id = s.Id, Rank = s.Rank, Timepost = s.Timepost })
                    .ToListAsync();
                }
            }

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                if (s.Id == resultContextExtension.Id) {
                    resultContextExtension.Rank = i + 1;
                }
                s.Rank = i + 1;
            }

            await dbContext.SafeBulkUpdateAsync(rankedScores, options => options.ColumnInputExpression = s => new { s.Rank });
        }

        [NonAction]
        private async Task PostUploadAction(
            AppContext dbContext,
            StorageContext? storageContext,
            Replay replay,
            byte[] replayData,
            Leaderboard leaderboard, 
            Player player,
            List<OldPlayerStats> oldPlayerStats,
            Score resultScore,
            List<CurrentScoreWrapper> currentScores,
            HttpContext? context,
            ReplayOffsets offsets,
            ScoreStatistic? statistic,
            string? statisticError,
            bool allow = false) {

            resultScore.Replay = await _s3Client.UploadReplay(ReplayUtils.ReplayFilename(replay, resultScore), replayData);

            //if (resultScore.ValidContexts.HasFlag(LeaderboardContexts.Funny) && leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
            //    var playerIds = await dbContext
            //        .ScoreContextExtensions
            //        .Where(s => s.LeaderboardId == leaderboard.Id)
            //        .Select(s => s.PlayerId)
            //        .ToListAsync();

            //    await PlayerContextRefreshControllerHelper.RefreshPlayersContext(dbContext, LeaderboardContexts.Funny, playerIds);
            //}

            if (statistic != null) {
                await _s3Client.UploadScoreStats(resultScore.Id + ".json", statistic);
            } else {
                await SaveFailedScore(dbContext, resultScore, leaderboard, statisticError ?? "Can't calculate stats");
                return;
            }

            foreach (var scoreToDelete in currentScores) {
                if (scoreToDelete.Score.ValidContexts == LeaderboardContexts.None) {
                    dbContext.ScoreRedirects.Add(new ScoreRedirect
                    {
                        OldScoreId = scoreToDelete.Score.Id,
                        NewScoreId = resultScore.Id,
                    });
                }
            }

            resultScore.ReplayOffsets = offsets;
            await dbContext.RecalculateEventsPP(player, leaderboard);

            try
            {
                await dbContext.BulkSaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await ex.Entries.Single().ReloadAsync();
                await dbContext.BulkSaveChangesAsync();
            }

            try
            {
                foreach (var scoreToDelete in currentScores) {
                    if (scoreToDelete.Score.ValidContexts == LeaderboardContexts.None) {
                        await MigrateOldReplay(dbContext, storageContext, scoreToDelete.Score, leaderboard.Id);
                    }
                }

                if (statistic == null)
                {
                    await SaveFailedScore(dbContext, resultScore, leaderboard, "Could not recalculate score from replay. Error: " + statisticError);
                    return;
                }

                float maxScore = leaderboard.Difficulty.MaxScore;
                // V3 notes at the start may lead to 100+ accuracy in some cases
                if (leaderboard.Difficulty.Requirements.HasFlag(Requirements.V3)) {
                    maxScore *= 1.1f;
                }
                if (leaderboard.Id != "EarthDay2025") {
                    if (resultScore.BaseScore > maxScore) {
                        await SaveFailedScore(dbContext, resultScore, leaderboard, "Score is bigger than max possible on this map!");
                        return;
                    }

                    if (!leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) && !allow) {
                        double scoreRatio = resultScore.BaseScore / (double)statistic.winTracker.totalScore;
                        if (scoreRatio > 1.01 || scoreRatio < 0.99)
                        {
                            if (_replayRecalculator != null) {
                                (int? newScore, replay) = await _replayRecalculator.RecalculateReplay(replay);
                                if (newScore != null) {
                                    scoreRatio = resultScore.BaseScore / (double)newScore;
                                }
                            }

                            if (scoreRatio > 1.01 || scoreRatio < 0.99)
                            {
                                await SaveFailedScore(dbContext, resultScore, leaderboard, "Calculated on server score is too different: " + statistic.winTracker.totalScore + ". You probably need to update the mod.");

                                return;
                            } else {
                                using (var recalculatedStream = new MemoryStream()) {
                                    ReplayEncoder.Encode(replay, new BinaryWriter(recalculatedStream, Encoding.UTF8));
                                    resultScore.Replay = await _s3Client.UploadReplay("recalculated-" + ReplayUtils.ReplayFilename(replay, resultScore), recalculatedStream.ToArray());
                                }

                                try {
                                    (statistic, statisticError) = await ScoreControllerHelper.CalculateAndSaveStatistic(_s3Client, replay, resultScore);
                
                                } catch (Exception e)
                                {
                                    await SaveFailedScore(dbContext, resultScore, leaderboard, e.ToString());
                                    return;
                                }
                            }
                        }
                    }

                    if (leaderboard.Difficulty.Notes > 30 && !allow && statistic != null)
                    {
                        var sameAccScore = await dbContext
                            .Scores
                            .Where(s => s.LeaderboardId == leaderboard.Id &&
                                                 s.PlayerId != resultScore.PlayerId && 
                                                 s.AccLeft != 0 && 
                                                 s.AccRight != 0 && 
                                                 s.AccLeft == statistic.accuracyTracker.accLeft && 
                                                 s.AccRight == statistic.accuracyTracker.accRight &&
                                                 s.BaseScore == resultScore.BaseScore)
                            .Select(s => s.PlayerId)
                            .FirstOrDefaultAsync();
                        if (sameAccScore != null)
                        {
                            await SaveFailedScore(dbContext, resultScore, leaderboard, "Acc is suspiciously exact same as: " + sameAccScore + "'s score");

                            return;
                        }
                    }
                }

                resultScore.Country = (context?.Request.Headers["cf-ipcountry"] ?? "") == StringValues.Empty ? "not set" : context?.Request.Headers["cf-ipcountry"].ToString();

                if (resultScore.Rank <= 4) {
                    await UpdateTop4(dbContext, leaderboard.Id);
                }
                if (oldPlayerStats.Count > 0) {
                    UpdateImprovements(resultScore, currentScores, player, oldPlayerStats, leaderboard);
                }

                if (resultScore.Hmd == HMD.unknown && (await dbContext.Headsets.FirstOrDefaultAsync(h => h.Name == replay.info.hmd)) == null) {
                    dbContext.Headsets.Add(new Headset {
                        Name = replay.info.hmd,
                        Player = replay.info.playerID,
                    });
                }

                if (resultScore.Controller == ControllerEnum.unknown && (await dbContext.VRControllers.FirstOrDefaultAsync(h => h.Name == replay.info.controller)) == null) {
                    dbContext.VRControllers.Add(new VRController {
                        Name = replay.info.controller,
                        Player = replay.info.playerID,
                    });
                }
                await dbContext.BulkSaveChangesAsync();

                await SocketController.TryPublishNewScore(resultScore, dbContext);
                await SocketController.ScoreWasAccepted(resultScore, dbContext);

                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && 
                    resultScore.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                    resultScore.Rank == 1 && 
                    !player.Bot &&
                    !player.Temporary)
                {
                    var dsClient = top1DSClient();

                    if (dsClient != null)
                    {
                        float? weight = await dbContext.Scores.Where(s => s.Id == resultScore.Id).Select(s => s.Weight).FirstOrDefaultAsync();
                        var song = await dbContext.Leaderboards.Where(lb => lb.Id == leaderboard.Id).Include(lb => lb.Song).Select(lb => lb.Song).FirstOrDefaultAsync();
                        string message = "**" + player.Name + "** has become No 1 on **" + (song != null ? song?.Name : leaderboard.Id) + "** :tada: \n";
                        message += Math.Round(resultScore.Accuracy * 100, 2) + "% " + Math.Round(resultScore.Pp, 2) + "pp (" + Math.Round((weight ?? 0) * resultScore.Pp, 2) + "pp)\n";
                        var secondScore = await dbContext
                            .Scores
                            .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned && s.LeaderboardId != null && s.ValidForGeneral)
                            .OrderByDescending(s => s.Pp)
                            .Skip(1)
                            .Take(1)
                            .Select(s => new { s.Pp, s.Accuracy })
                            .FirstOrDefaultAsync();
                        if (secondScore != null)
                        {
                            message += "This beats previous record by **" + Math.Round(resultScore.Pp - secondScore.Pp, 2) + "pp** and **" + Math.Round((resultScore.Accuracy - secondScore.Accuracy) * 100, 2) + "%** ";
                            if (resultScore.Modifiers.Length > 0)
                            {
                                message += "using **" + resultScore.Modifiers + "**";
                            }
                            message += "\n";
                        }
                        var improvement = resultScore.ScoreImprovement;
                        if (improvement != null) {
                            message += Math.Round(improvement.TotalPp, 2) + " to the personal pp and " + improvement.TotalRank + " to rank \n";
                        }

                        var messageId = await dsClient.SendMessageAsync(message,
                            embeds: new List<Embed> { new EmbedBuilder()
                                    .WithTitle("Open leaderboard ↗")
                                    .WithUrl("https://beatleader.com/leaderboard/global/" + leaderboard.Id)
                                    .WithDescription("[Watch replay with BL](" + "https://replay.beatleader.com?scoreId=" + resultScore.Id + ") | [Watch replay with ArcViewer](" +"https://allpoland.github.io/ArcViewer/?scoreID=" + resultScore.Id + ")" )
                                    .WithImageUrl("https://api.beatleader.com/preview/replay?scoreId=" + resultScore.Id)
                                    .Build()
                            });
                        await Task.Delay(TimeSpan.FromSeconds(30));
                        await Bot.BotService.PublishAnnouncement(1016157747668074627, messageId);
                    }
                }

                // Calculate clan ranking for this leaderboard
                await dbContext.BulkSaveChangesAsync();
                if (!player.Temporary) {
                    (var changes, var playerClans) = await dbContext.UpdateClanRanking(leaderboard, resultScore);
                    await dbContext.BulkSaveChangesAsync();

                    if (changes?.Count > 0) {
                        ClanTaskService.AddJob(new ClanRankingChangesDescription {
                            Score = resultScore,
                            Changes = changes
                        });
                    }
                }
            }
            catch (Exception e)
            {
                await SaveFailedScore(dbContext, resultScore, leaderboard, (e.StackTrace ?? "") + e.ToString());
            }
        }

        

        [NonAction]
        private async Task SaveFailedScore(AppContext dbContext, Score score, Leaderboard leaderboard, string failReason) {
            try {

            await SocketController.ScoreWasRejected(score, dbContext);

            foreach (var ce in score.ContextExtensions) {
                if (score.ValidContexts.HasFlag(ce.Context)) {
                    dbContext.ScoreContextExtensions.Remove(ce);
                }
            }

            score.LeaderboardId = null;

            //if (previousScore != null) {
            //    previousScore.LeaderboardId = leaderboard.Id;
            //    foreach (var ce in previousScore.ContextExtensions) {
            //        if (!previousScore.ValidContexts.HasFlag(ce.Context)) {
            //            _context.ScoreContextExtensions.Add(ce);
            //            previousScore.ValidContexts |= ce.Context;
            //        }
            //    }
            //}
            Player player = score.Player;

            FailedScore failedScore = new FailedScore {
                Error = failReason,
                Leaderboard = leaderboard,
                PlayerId = score.PlayerId,
                Modifiers = score.Modifiers,
                Replay = score.Replay,
                Accuracy = score.Accuracy,
                Timeset = score.Timeset,
                BaseScore = score.BaseScore,
                ModifiedScore = score.ModifiedScore,
                Pp = score.Pp,
                Weight = score.Weight,
                Rank = score.Rank,
                MissedNotes = score.MissedNotes,
                BadCuts = score.BadCuts,
                BombCuts = score.BombCuts,
                Player = score.Player,
                Pauses = score.Pauses,
                Hmd = score.Hmd,
                FullCombo = score.FullCombo,
                
            };
            dbContext.FailedScores.Add(failedScore);
            await dbContext.SaveChangesAsync();

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                await dbContext.RecalculatePPAndRankFast(player, score.ValidContexts);
            }
            await LeaderboardRefreshControllerHelper.RefreshLeaderboardsRankAllContexts(dbContext, leaderboard.Id);

            await PlayerRefreshControllerHelper.RefreshStats(
                dbContext,
                player.ScoreStats, 
                player.Id, 
                player.Rank,
                null,
                null,
                LeaderboardContexts.General);

            foreach (var ce in player.ContextExtensions)
            {
                await PlayerRefreshControllerHelper.RefreshStats(dbContext, ce.ScoreStats, player.Id, player.Rank, null, null, ce.Context);
            }

            await dbContext.BulkSaveChangesAsync();
            
            } catch { }
        }

        [NonAction]
        private async Task<Score> CollectStats(
            AppContext dbContext,
            StorageContext? storageContext,
            Replay replay,
            ReplayOffsets offsets,
            byte[] replayData,
            string? fileName,
            string playerId,
            Leaderboard leaderboard,
            float time = 0, 
            EndType type = 0,
            Player? player = null,
            Score? resultScore = null,
            ScoreStatistic? statistic = null,
            int? forceTimeset = null) {
            int timeset = forceTimeset ?? (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (resultScore == null) {
                (resultScore, int maxScore) = ReplayUtils.ProcessReplay(replay, leaderboard.Difficulty, time);
                string? error = null;
                try
                {
                    (statistic, error) = ReplayStatisticUtils.ProcessReplay(replay, leaderboard, true);
                } catch (Exception e) {
                    Console.WriteLine($"Attemp stats error: {e}");
                }
                if (statistic != null) {
                    resultScore.AccLeft = statistic.accuracyTracker.accLeft;
                    resultScore.AccRight = statistic.accuracyTracker.accRight;
                    resultScore.MaxCombo = statistic.hitTracker.maxCombo;
                    if (float.IsFinite(statistic.accuracyTracker.fcAcc) && statistic.accuracyTracker.fcAcc > 0) {
                        resultScore.FcAccuracy = statistic.accuracyTracker.fcAcc;
                    }
                    resultScore.MaxStreak = statistic.hitTracker.maxStreak;
                    resultScore.LeftTiming = statistic.hitTracker.leftTiming;
                    resultScore.RightTiming = statistic.hitTracker.rightTiming;
                    if (leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                        resultScore.FcPp = ReplayUtils.PpFromScore(
                            resultScore.FcAccuracy, 
                            resultScore.ValidContexts,
                            resultScore.Modifiers, 
                            leaderboard.Difficulty.ModifierValues, 
                            leaderboard.Difficulty.ModifiersRating, 
                            leaderboard.Difficulty.AccRating ?? 0, 
                            leaderboard.Difficulty.PassRating ?? 0, 
                            leaderboard.Difficulty.TechRating ?? 0, 
                            leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard").Item1;
                    }
                }
            }
            resultScore.ReplayOffsets = offsets;

            if (type != EndType.Practice && type != EndType.Unknown && replay.notes.Count >= 20) {
                int baseExp = 500;
                int incExp = 50;

                float accuracy = resultScore.BaseScore / (float)leaderboard.Difficulty.MaxScore;
                float firstNoteTime = replay.notes.FirstOrDefault()?.eventTime ?? 0.0f;
                float lastNoteTime = replay.notes.LastOrDefault()?.eventTime ?? 0.0f;
                if (time < lastNoteTime)
                {
                    lastNoteTime = time;
                }
                float duration = lastNoteTime - firstNoteTime;
                if (resultScore.Modifiers.Contains("SS"))
                {
                    duration /= 0.85f;
                }
                else if (resultScore.Modifiers.Contains("FS"))
                {
                    duration /= 1.2f;
                }
                else if (resultScore.Modifiers.Contains("SF"))
                {
                    duration /= 1.5f;
                }

                float exp;
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    float star = Math.Max(resultScore.ModifiedStars, 0);
                    exp = ReplayUtils.GetCurveVal(0, star);
                }
                else
                {
                    exp = 1000;
                }
                float accMult = ReplayUtils.GetCurveVal(1, accuracy);
                float durMult = ReplayUtils.GetCurveVal(2, duration);

                float gainedExp = exp * accMult * durMult / (resultScore.Modifiers.Contains("NF") ? 2f : 1f);

                resultScore.Experience = gainedExp;
                        
                if (player != null && player.Level < 100)
                {
                    player.Experience += (int)Math.Round(gainedExp);
                    while (player.Experience > 0)
                    {
                        var reqExp = baseExp + (incExp * player.Level);

                        if (player.Prestige != 0)
                        {
                            reqExp = (int)Math.Round(reqExp * Math.Pow(1.33f, player.Prestige));
                        }

                        if (player.Experience >= reqExp)
                        {
                            player.Level++;
                            player.Experience -= reqExp;
                            if (player.Level == 100)
                            {
                                player.Experience = 0;
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            if (player?.ScoreStats != null) {
                player.ScoreStats.ScorePlaytime += (replay.frames.Last().time - replay.frames.First().time) / 60.0 + statistic?.winTracker.totalPauseDuration ?? 0.0;
            }

            if (storageContext != null) {
                await LeaderboardPlayerStatsService.AddJob(new PlayerStatsJob {
                    replayData = fileName != null ? null : replayData,
                    fileName = fileName ?? $"{replay.info.playerID}-{timeset}{(replay.info.speed != 0 ? "-practice" : "")}{(replay.info.failTime != 0 ? "-fail" : "")}-{replay.info.difficulty}-{replay.info.mode}-{replay.info.hash}.bsor",
                    playerId = playerId,
                    leaderboardId = leaderboard.Id,
                    time = statistic?.winTracker.failTime > 0 ? statistic!.winTracker.failTime : time,
                    startTime = replay.info.startTime,
                    speed = replay.info.speed,
                    type = type,
                    score = resultScore,
                    saveReplay = replay.frames.Count > 0 && replay.info.score > 0 
                }, dbContext, storageContext, _s3Client);
            }

            return resultScore;
        }

        private async Task MigrateOldReplay(AppContext dbContext, StorageContext? storageContext, Score score, string leaderboardId) {
            if (score.Replay == null || storageContext == null) return;
            var stats = await storageContext.PlayerLeaderboardStats
                .Where(s => s.LeaderboardId == leaderboardId && s.Score == score.BaseScore && s.PlayerId == score.PlayerId && (s.Replay == null || s.Replay == score.Replay))
                .Include(s => s.Metadata)
                .FirstOrDefaultAsync();
            
            string? name = score.Replay.Split("/").LastOrDefault();
            if (name == null) return;

            string fileName = $"{score.Id}.bsor";
            bool uploaded = false;
            try {
                using (var stream = await _s3Client.DownloadReplay(name)) {
                    if (stream == null) return;
                    using (var ms = new MemoryStream()) {
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        await _s3Client.UploadOtherReplayStream(fileName, ms);

                        uploaded = true;
                    }
                    await _s3Client.DeleteReplay(name);
                }
            } catch {}

            if (uploaded) {
                if (stats != null) {
                    stats.Replay = "https://api.beatleader.com/otherreplays/" + fileName;
                    if (score.Metadata != null) {
                        stats.Metadata = new ScoreMetadata {
                            PinnedContexts = score.Metadata.PinnedContexts,
                            HighlightedInfo = score.Metadata.HighlightedInfo,
                            Priority = score.Metadata.Priority,
                            Description = score.Metadata.Description,

                            LinkService = score.Metadata.LinkService,
                            LinkServiceIcon = score.Metadata.LinkServiceIcon,
                            Link = score.Metadata.Link
                        };
                    }
                    storageContext.SaveChanges();
                } else {
                     await LeaderboardPlayerStatsService.AddJob(new PlayerStatsJob {
                        fileName = "https://api.beatleader.com/otherreplays/" + fileName,
                        playerId = score.PlayerId,
                        leaderboardId = leaderboardId,
                        timeset = score.Timepost > 0 ? score.Timepost : int.Parse(score.Timeset),
                        type = EndType.Clear,
                        time = 0,
                        score = score
                    }, dbContext, storageContext, _s3Client);
                }
            }
        }

        [NonAction]
        private async Task UpdateTop4(
            AppContext dbContext,
            string leaderboardId) {

            {
                var playerIds = await dbContext
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboardId && s.Rank <= 4 && s.ValidForGeneral)
                    .Select(s => s.PlayerId)
                    .ToListAsync();

                foreach (var playerId in playerIds) {
                    var stats = await dbContext.Players.Where(p => p.Id == playerId).Select(p => p.ScoreStats).FirstOrDefaultAsync();
                    if (stats == null) continue;

                    var scores = await dbContext
                        .Scores
                        .AsNoTracking()
                        .Where(s => s.PlayerId == playerId && s.ValidForGeneral)
                        .Select(s => new { s.Rank, s.Leaderboard.Difficulty.Status })
                        .ToListAsync();

                    stats.RankedTop1Count = scores.Count(s => s.Status == DifficultyStatus.ranked && s.Rank == 1);
                    stats.UnrankedTop1Count = scores.Count(s => s.Status != DifficultyStatus.ranked && s.Rank == 1);
                    stats.Top1Count = scores.Count(s => s.Rank == 1);

                    if (scores.Where(s => s.Status == DifficultyStatus.ranked).Count() > 0) {
                        stats.RankedTop1Score = scores.Where(s => s.Status == DifficultyStatus.ranked).Sum(s => ReplayUtils.ScoreForRank(s.Rank));
                    } else {
                        stats.RankedTop1Score = 0;
                    }
                    if (scores.Where(s => s.Status != DifficultyStatus.ranked).Count() > 0) {
                        stats.UnrankedTop1Score = scores.Where(s => s.Status != DifficultyStatus.ranked).Sum(s => ReplayUtils.ScoreForRank(s.Rank));
                    } else {
                        stats.UnrankedTop1Score = 0;
                    }
                    stats.Top1Score = scores.Sum(s => ReplayUtils.ScoreForRank(s.Rank));
                }
            }

            foreach (var context in ContextExtensions.NonGeneral) {
                var playerIds = await dbContext
                .ScoreContextExtensions
                .Where(s => s.LeaderboardId == leaderboardId && s.Rank <= 4 && s.Context == context)
                .Select(s => s.PlayerId)
                .ToListAsync();

                foreach (var playerId in playerIds) {
                    var stats = await dbContext.PlayerContextExtensions.Where(p => p.PlayerId == playerId && p.Context == context).Select(p => p.ScoreStats).FirstOrDefaultAsync();
                    if (stats == null) continue;

                    var scores = await dbContext
                        .ScoreContextExtensions
                        .AsNoTracking()
                        .Where(s => s.PlayerId == playerId && s.Context == context)
                        .Select(s => new { s.Rank, s.Leaderboard.Difficulty.Status })
                        .ToListAsync();

                    stats.RankedTop1Count = scores.Count(s => s.Status == DifficultyStatus.ranked && s.Rank == 1);
                    stats.UnrankedTop1Count = scores.Count(s => s.Status != DifficultyStatus.ranked && s.Rank == 1);
                    stats.Top1Count = scores.Count(s => s.Rank == 1);

                    if (scores.Where(s => s.Status == DifficultyStatus.ranked).Count() > 0) {
                        stats.RankedTop1Score = scores.Where(s => s.Status == DifficultyStatus.ranked).Sum(s => ReplayUtils.ScoreForRank(s.Rank));
                    } else {
                        stats.RankedTop1Score = 0;
                    }

                    if (scores.Where(s => s.Status != DifficultyStatus.ranked).Count() > 0) {
                        stats.UnrankedTop1Score = scores.Where(s => s.Status != DifficultyStatus.ranked).Sum(s => ReplayUtils.ScoreForRank(s.Rank));
                    } else {
                        stats.UnrankedTop1Score = 0;
                    }
                    stats.Top1Score = scores.Sum(s => ReplayUtils.ScoreForRank(s.Rank));
                }
            }
        }

        [NonAction]
        private async Task RefreshNoteCount(AppContext dbContext, Leaderboard leaderboard) {
            try {
                var downloadUrl = dbContext.Songs.Where(s => s.Id == leaderboard.SongId).Select(s => s.DownloadUrl).FirstOrDefault();
                if (downloadUrl == null) return;
                HttpWebResponse res = (HttpWebResponse)await WebRequest.Create(downloadUrl).GetResponseAsync();
                if (res.StatusCode != HttpStatusCode.OK) return;

                var memoryStream = new MemoryStream();
                await res.GetResponseStream().CopyToAsync(memoryStream);
                var parse = new Parse();
                memoryStream.Position = 0;
                var map = parse.TryLoadZip(memoryStream)?.FirstOrDefault();
                var songDiff = map?.Difficulties.FirstOrDefault(d => d.Difficulty == leaderboard.Difficulty.DifficultyName && d.Characteristic == leaderboard.Difficulty.ModeName);
                if (songDiff != null) {
                    leaderboard.Difficulty.Notes = songDiff.Data.Notes.Where(n => n.Seconds >= 0 && n.Seconds <= leaderboard.Difficulty.Duration).Count();
                    leaderboard.Difficulty.Chains = songDiff.Data.Chains.Where(n => n.Seconds >= 0 && n.Seconds <= leaderboard.Difficulty.Duration).Sum(c => c.SliceCount > 1 ? c.SliceCount - 1 : 0);
                    leaderboard.Difficulty.Sliders = songDiff.Data.Arcs.Where(n => n.Seconds >= 0 && n.Seconds <= leaderboard.Difficulty.Duration).Count();

                    leaderboard.Difficulty.MaxScore = songDiff.MaxScore();

                    await dbContext.BulkUpdateAsync(new List<DifficultyDescription> { leaderboard.Difficulty }, options => options.ColumnInputExpression = c => new { c.Notes, c.Chains, c.Sliders, c.MaxScore });
                    await ScoreRefreshControllerHelper.BulkRefreshScores(dbContext, leaderboard.Id);

                }
            } catch (Exception e) {
                Console.WriteLine($"RefreshNoteCount EXCEPTION {e}");
            }
        }

        class OldPlayerStats {
            public LeaderboardContexts Context { get; set; }
            public float Pp { get; set; }
            public int Rank { get; set; }
        }

        private List<OldPlayerStats> CollectPlayerStats(Player player) {
            var result = new List<OldPlayerStats> { new OldPlayerStats {
                Context = LeaderboardContexts.General,
                Pp = player.Pp,
                Rank = player.Rank
            } };

            if (player.ContextExtensions != null) {
                foreach (var ce in player.ContextExtensions)
                {
                    result.Add(new OldPlayerStats {
                        Context = ce.Context,
                        Pp = ce.Pp,
                        Rank = ce.Rank
                    });
                }
            }

            return result;
        }

        private void UpdateImprovements(
                Score resultScore,
                List<CurrentScoreWrapper> currentScores, 
                Player player,
                List<OldPlayerStats> oldPlayerStats,
                Leaderboard leaderboard) {

            var generalCurrentScore = currentScores.FirstOrDefault(s => s.ValidContexts.HasFlag(LeaderboardContexts.General));
            if (resultScore.ScoreImprovement != null) {
                var oldPlayer = oldPlayerStats.First(s => s.Context == LeaderboardContexts.General);
                var improvement = resultScore.ScoreImprovement;
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    improvement.TotalPp = player.Pp - oldPlayer.Pp;
                    improvement.TotalRank = player.Rank - oldPlayer.Rank;
                }
                
                if (generalCurrentScore != null) {
                    improvement.AccLeft = resultScore.AccLeft - generalCurrentScore.Score.AccLeft;
                    improvement.AccRight = resultScore.AccRight - generalCurrentScore.Score.AccRight;
                    improvement.Rank = resultScore.Rank - generalCurrentScore.Score.Rank;
                    if (leaderboard.Difficulty.Status is DifficultyStatus.ranked or DifficultyStatus.qualified)
                    {
                        improvement.Pp = resultScore.Pp - generalCurrentScore.Score.Pp;
                    }
                }
            }

            if (resultScore.ContextExtensions != null) {
                foreach (var ce in resultScore.ContextExtensions)
                {
                    var currentScore = currentScores
                        .FirstOrDefault(s => s.ValidContexts.HasFlag(ce.Context));
                    var currentScoreExtenstion = currentScore?.ContextExtensions
                        ?.FirstOrDefault(sce => sce.Context == ce.Context);

                    if (ce.ScoreImprovement != null) {
                        var oldPlayer = oldPlayerStats.FirstOrDefault(s => s.Context == ce.Context);
                        var newPlayer = player?.ContextExtensions?.FirstOrDefault(pce => pce.Context == ce.Context);
                        var improvement = ce.ScoreImprovement;
                        if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && newPlayer != null && oldPlayer != null)
                        {
                            improvement.TotalPp = newPlayer.Pp - oldPlayer.Pp;
                            improvement.TotalRank = newPlayer.Rank - oldPlayer.Rank;
                        }
                        if (currentScore != null && currentScoreExtenstion != null) {
                            improvement.AccLeft = resultScore.AccLeft - currentScore.Score.AccLeft;
                            improvement.AccRight = resultScore.AccRight - currentScore.Score.AccRight;
                            improvement.Rank = ce.Rank - currentScoreExtenstion.ScoreInstance?.Rank ?? 0;
                            if (leaderboard.Difficulty.Status is DifficultyStatus.ranked or DifficultyStatus.qualified)
                            {
                                improvement.Pp = resultScore.Pp - currentScoreExtenstion.ScoreInstance?.Pp ?? 0;
                            }
                        }
                    }
                }
            }
        }

        [NonAction]
        public DiscordWebhookClient? top1DSClient()
        {
            var link = _configuration.GetValue<string?>("Top1DSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }
    }
}
