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

namespace BeatLeader_Server.Controllers
{
    public class ReplayController : Controller
    {
        private readonly IAmazonS3 _s3Client;
        private readonly AppContext _context;

        private readonly LeaderboardController _leaderboardController;
        private readonly PlayerController _playerController;
        private readonly ScoreController _scoreController;
        private readonly LeaderboardRefreshController _leaderboardRefreshController;
        private readonly PlayerContextRefreshController _playerContextRefreshController;
        private readonly PlayerRefreshController _playerRefreshController;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IServerTiming _serverTiming;
        private readonly IReplayRecalculator? _replayRecalculator;

        private readonly IMetricFamily<IGauge> _replayLocation;
        private readonly Geohash.Geohasher _geoHasher;
        private static IP2Location.Component ipLocation;

        public ReplayController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration,
            LeaderboardController leaderboardController, 
            PlayerController playerController,
            ScoreController scoreController,
            LeaderboardRefreshController leaderboardRefreshController,
            IServerTiming serverTiming,
            IMetricFactory metricFactory,
            PlayerContextRefreshController playerContextRefreshController,
            PlayerRefreshController playerRefreshController,
            IReplayRecalculator? replayRecalculator)
        {
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _scoreController = scoreController;
            _leaderboardRefreshController = leaderboardRefreshController;
            _context = context;
            _environment = env;
            _configuration = configuration;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();

            _replayLocation = metricFactory.CreateGauge("replay_position", "Posted replay location", new string[] { "geohash" });
            _geoHasher = new Geohash.Geohasher();

            if (ipLocation == null)
            {
                ipLocation = new IP2Location.Component();
                ipLocation.Open(env.WebRootPath + "/databases/IP2LOCATION-LITE-DB.BIN");
            }
            _playerContextRefreshController = playerContextRefreshController;
            _playerRefreshController = playerRefreshController;
            _replayRecalculator = replayRecalculator;
        }

        [HttpPost("~/replay"), DisableRequestSizeLimit]
        public async Task<ActionResult<ScoreResponse>> PostSteamReplay([FromQuery] string ticket)
        {
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(ticket, _configuration);
            if (id == null && error != null) {
                return Unauthorized(error);
            }
            long intId = long.Parse(id);
            if (intId < 70000000000000000)
            {
                AccountLink? accountLink = await _context.AccountLinks.FirstOrDefaultAsync(el => el.PCOculusID == id);
                if (accountLink != null && accountLink.SteamID.Length > 0) {
                    id = accountLink.SteamID;
                }
            }
            return await PostReplayFromBody(id);
        }

        [HttpPut("~/replayoculus"), DisableRequestSizeLimit]
        [Authorize]
        public async Task<ActionResult<ScoreResponse>> PostOculusReplay(
            [FromQuery] float time = 0,
            [FromQuery] EndType type = 0)
        {
            string? userId = HttpContext.CurrentUserID(_context);
            if (userId == null)
            {
                return Unauthorized("User is not authorized");
            }
            
            return await PostReplayFromBody(userId, time, type);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplayFromBody(string authenticatedPlayerID, float time = 0, EndType type = 0)
        {
            return await PostReplay(authenticatedPlayerID, Request.Body, HttpContext, time, type);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplayFromCDN(string authenticatedPlayerID, string name, bool backup, bool allow, string timeset, HttpContext context)
        {
            if (backup) {
                string directoryPath = Path.Combine("/root/replays");
                string filePath = Path.Combine(directoryPath, name);

                if (!System.IO.File.Exists(filePath)) {
                    return NotFound();
                }

                // Use FileMode.Create to overwrite the file if it already exists.
                using (FileStream stream = new FileStream(filePath, FileMode.Open)) {
                    return await PostReplay(authenticatedPlayerID, stream, context, allow: allow, timesetForce: timeset);
                }
            } else {
                using (var stream = await _s3Client.DownloadReplay(name)) {
                    if (stream != null) {
                        return await PostReplay(authenticatedPlayerID, stream, context, allow: allow, timesetForce: timeset);
                    } else {
                        return NotFound();
                    }
                }
            }
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplay(
            string authenticatedPlayerID, 
            Stream replayStream,
            HttpContext context,
            float time = 0, 
            EndType type = 0,
            bool allow = false,
            string? timesetForce = null)
        {
            Replay? replay;
            ReplayOffsets? offsets;
            byte[] replayData;

            int length = 0;
            List<byte> replayDataList = new List<byte>(); 
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

            while (true)
            {
                var bytesRemaining = await replayStream.ReadAsync(buffer, offset: 0, buffer.Length);
                if (bytesRemaining == 0)
                {
                    break;
                }
                length += bytesRemaining;
                if (length > 200000000) {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return BadRequest("Replay is too big to save, sorry");
                }
                replayDataList.AddRange(new Span<byte>(buffer, 0, bytesRemaining).ToArray());
            }

            ArrayPool<byte>.Shared.Return(buffer);

            replayData = replayDataList.ToArray();
            try
            {
                (replay, offsets) = ReplayDecoder.ReplayDecoder.Decode(replayData);
            }
            catch (Exception)
            {
                return BadRequest("Error decoding replay");
            }
            var info = replay?.info;

            if (info == null) return BadRequest("It's not a replay or it has old version.");
            if (replay.notes.Count == 0) {
                return BadRequest("Replay is broken, update your mod please.");
            }

            var gameversion = replay.info.gameVersion.Split(".");
            if (replay.info.mode.EndsWith("OldDots") || (gameversion.Length == 3 && int.Parse(gameversion[1]) < 20)) {
                replay.info.mode = replay.info.mode.Replace("OldDots", "");
                replay.info.modifiers += replay.info.modifiers.Length > 0 ? ",OD" : "OD";
            }

            var version = info.version.Split(".");
            if (version.Length < 3 || int.Parse(version[1]) < 3) {
                Thread.Sleep(8000); // Error may not show if returned too quick
                return BadRequest("Please update your mod. v0.3 or higher");
            }

            info.playerID = authenticatedPlayerID;
            if (info.hash.Length >= 40) {
                info.hash = info.hash.Substring(0, 40);
            }

            Leaderboard? leaderboard;
            using (_serverTiming.TimeAction("ldbrd"))
            {
                leaderboard = (await _leaderboardController.GetByHash(info.hash, info.difficulty, info.mode)).Value;
                if (leaderboard == null)
                {
                    return NotFound("Such leaderboard not exists");
                }
            }

            if (type != EndType.Unknown && type != EndType.Clear) {
                await CollectStats(replay, replayData, null, authenticatedPlayerID, leaderboard, time, type);
                return Ok();
            }

            if (replay.frames.Count == 0) {
                return BadRequest("Replay is broken, update your mod please.");
            }

            if (replay.notes.Last().eventTime - replay.frames.Last().time > 2) {
                return BadRequest("Replay missing frame data past note data.");
            }

            if (replay.info.score <= 0) {
                Thread.Sleep(8000); // Error may not show if returned too quick
                return BadRequest("The score should be positive");
            }

            if ((leaderboard.Difficulty.Status.WithRating()) && leaderboard.Difficulty.Notes != 0 && replay.notes.Count > leaderboard.Difficulty.Notes) {
                string? error = ReplayUtils.RemoveDuplicates(replay, leaderboard);
                if (error != null) {
                    return BadRequest("Failed to delete duplicate note: " + error);
                }
            }

            (Score resultScore, int maxScore) = ReplayUtils.ProcessReplay(replay, leaderboard.Difficulty);

            if (timesetForce != null) {
                resultScore.Timepost = int.Parse(timesetForce);
            }

            List<Score> currentScores;
            using (_serverTiming.TimeAction("currS"))
            {
                currentScores = await _context
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
                    .AsSplitQuery()
                    .TagWithCallSite()
                    .ToListAsync();
            }
            var ip = context.Request.HttpContext.GetIpAddress();
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                player = currentScores.FirstOrDefault()?.Player ?? (await _playerController.GetLazy(info.playerID)).Value;
                if (player == null)
                {
                    player = new Player();
                    player.Id = info.playerID;
                    player.Name = info.playerName;
                    player.Platform = info.platform;
                    player.ScoreStats = new PlayerScoreStats();
                    player.SetDefaultAvatar();
                    player.SanitizeName();

                    _context.Players.Add(player);

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
                    }
                }
            }

            if (!leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) && 
                !ReplayUtils.IsPlayerCuttingNotesOnPlatform(replay)) {
                Thread.Sleep(8000); // Error may not show if returned too quick
                return BadRequest("Please stay on the platform.");
            }

            if (ipLocation != null && ip != null) {
                var location = ipLocation.IPQuery(ip);
                var hash = _geoHasher.Encode(Math.Round(location.Latitude / 3f) * 3, Math.Round(location.Longitude / 3f) * 3, 3);
                var counter = _replayLocation.WithLabels(new string[] { hash });
                counter.Inc();

                Task.Run(async () => {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    counter.Dec();
                });
            }

            resultScore.PlayerId = info.playerID;
            resultScore.LeaderboardId = leaderboard.Id;

            SocketController.ScoreWasUploaded(resultScore, _context);
            resultScore.LeaderboardId = null;

            var transaction = await _context.Database.BeginTransactionAsync();
            ActionResult<ScoreResponse> result = BadRequest("Exception occured, ping NSGolova"); 
            bool stats = false;
             
            try {
                (result, stats) = await UploadScores(
                    leaderboard, 
                    player,
                    resultScore, 
                    currentScores, 
                    replay,
                    replayData,
                    offsets,
                    context, 
                    transaction,
                    maxScore,
                    allow);
                if (stats) {
                    await CollectStats(replay, replayData, resultScore.Replay, authenticatedPlayerID, leaderboard, replay.frames.Last().time, EndType.Clear, resultScore);
                }
            } catch (Exception e) {

                 _context.RejectChanges();

                if (e is SqlException)
                {
                    transaction.Rollback();
                    transaction = await _context.Database.BeginTransactionAsync();
                }

                resultScore.Replay = await _s3Client.UploadReplay(ReplayUtils.ReplayFilename(replay, resultScore, true), replayData);

                FailedScore failedScore = new FailedScore {
                    Error = e.Message,
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
                _context.FailedScores.Add(failedScore);
                await _context.SaveChangesAsync();

                transaction.Commit();
            }

            return result;
        }

        class CurrentScoreWrapper {
            public Score Score { get; set; }
            public ICollection<ScoreContextExtension> ContextExtensions { get; set; }
            public LeaderboardContexts ValidContexts { get; set; }
        }

        [NonAction]
        private async Task<(ActionResult<ScoreResponse>, bool)> UploadScores(
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            List<Score> currentScores,
            Replay replay,
            byte[] replayData,
            ReplayOffsets offsets,
            HttpContext context,
            IDbContextTransaction transaction,
            int maxScore,
            bool allow = false) {

            if (!player.Bot && player.Banned) return (BadRequest("You are banned!"), false);

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
            foreach (var ce in resultScore.ContextExtensions)
            {
                ce.Banned = resultScore.Banned;
            }
            resultScore.Bot = player.Bot;

            await GeneralContextScore(leaderboard, player, resultScore, currentScores, replay);

            foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                await ContextScore(leaderboardContext, leaderboard, player, resultScore, currentScores, replay);
            }

            if (resultScore.ValidContexts == LeaderboardContexts.None) {
                return (BadRequest("Score is lower than existing one"), true);
            } else {
                using (_serverTiming.TimeAction("db"))
                {
                    foreach (var scoreToRemove in currentScores) {
                        if (scoreToRemove.ValidContexts == LeaderboardContexts.None) {
                            scoreToRemove.LeaderboardId = null;
                            _context.Scores.Remove(scoreToRemove);
                        }
                    }

                    foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                        if (resultScore.ValidContexts.HasFlag(leaderboardContext)) {
                            var ce = await _context
                                .ScoreContextExtensions
                                .Where(ce => 
                                    ce.Context == leaderboardContext &&
                                    ce.PlayerId == player.Id &&
                                    ce.LeaderboardId == leaderboard.Id)
                                .FirstOrDefaultAsync();
                            if (ce != null) {
                                _context.ScoreContextExtensions.Remove(ce);
                            }

                        }
                    }

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await ex.Entries.Single().ReloadAsync();
                        await _context.SaveChangesAsync();
                    }
                    
                    resultScore.LeaderboardId = leaderboard.Id;
                    _context.Scores.Add(resultScore);

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await ex.Entries.Single().ReloadAsync();
                        await _context.SaveChangesAsync();
                    }
                }

                resultScore.Replay = "https://cdn.replays.beatleader.xyz/" + ReplayUtils.ReplayFilename(replay, resultScore);
                await _context.SaveChangesAsync();

                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                await RecalculateRanks(resultScore, currentScores, leaderboard, player, transaction);
            
                context.Response.OnCompleted(async () => {
                    await PostUploadAction(
                        replay,
                        replayData,
                        leaderboard, 
                        player,
                        resultScore,
                        wrappedCurrentScores,
                        context, 
                        offsets,
                        allow);
                });

                var result = RemoveLeaderboard(resultScore, resultScore.Rank);
                if (!player.Bot && leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                    await _context.RecalculatePPAndRankFasterAllContexts(result);
                }

                return (result, false);
            }
        }

        [NonAction]
        private async Task GeneralContextScore(
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
                resultScore.PlayCount = currentScore.PlayCount;
            } else {
                resultScore.PlayCount = await _context.PlayerLeaderboardStats.Where(st => st.PlayerId == player.Id && st.LeaderboardId == leaderboard.Id).CountAsync();
            }

            resultScore.ValidContexts |= LeaderboardContexts.General;
            player.ScoreStats.UpdateScoreStats(currentScore, resultScore, leaderboard.Difficulty.Status == DifficultyStatus.ranked);

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
            }
        }

        [NonAction]
        private async Task ContextScore(
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

                    _context.ScoreContextExtensions.Remove(currentExtension);
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
                    ScoreStats = new PlayerScoreStats()
                };
            }
            playerContext.ScoreStats?.UpdateScoreExtensionStats(currentExtension, resultExtension, resultScore.IgnoreForStats, leaderboard.Difficulty.Status == DifficultyStatus.ranked);
        }

        private async Task RecalculateRanks(
            Score resultScore, 
            List<Score> currentScores,
            Leaderboard leaderboard, 
            Player player,
            IDbContextTransaction? transaction) {
            if (!player.Bot) {
                var isRanked = leaderboard.Difficulty.Status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent;
                await RefreshGeneneralContextRank(leaderboard, resultScore, isRanked);
                
                foreach (var leaderboardContext in ContextExtensions.NonGeneral) {
                    await RefreshContextRank(leaderboardContext, resultScore, leaderboard, isRanked);
                }
            }

            using (_serverTiming.TimeAction("db")) {
                try
                {
                    await _context.BulkSaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    await ex.Entries.Single().ReloadAsync();
                    await _context.BulkSaveChangesAsync();
                }

                if (transaction != null) {
                    await transaction.CommitAsync();
                }
            }
        }

        private async Task RefreshGeneneralContextRank(Leaderboard leaderboard, Score resultScore, bool isRanked) {
            if (!resultScore.ValidContexts.HasFlag(LeaderboardContexts.General)) return;

            var rankedScores = (await _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                                !s.Banned && 
                                s.ValidContexts.HasFlag(LeaderboardContexts.General))
                    
                    .Select(s => new { s.Id, s.Rank, s.Priority, s.ModifiedScore, s.Accuracy, s.Pp, s.Timeset })
                    .ToListAsync())
                    .OrderBy(el => !isRanked ? el.Priority : 1)
                    .OrderByDescending(el => !isRanked ? el.ModifiedScore : Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .ToList();

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                if (s.Id == resultScore.Id) {
                    resultScore.Rank = i + 1;
                }
                if (s.Rank == i + 1) continue;

                var score = new Score() { Id = s.Id };
                try {
                    _context.Scores.Attach(score);
                } catch { }
                score.Rank = i + 1;
                    
                _context.Entry(score).Property(x => x.Rank).IsModified = true;
            }
        }

        class ScoreSelection {
            public int Id { get; set; }
            public int Rank { get; set; }
        }

        private async Task RefreshContextRank(
            LeaderboardContexts context,
            Score resultScore,
            Leaderboard leaderboard,
            bool isRanked) {

            var resultContextExtension = resultScore.ContextExtensions.FirstOrDefault(ce => ce.Context == context);
            if (!resultScore.ValidContexts.HasFlag(context) || resultContextExtension == null) return;

            List<ScoreSelection> rankedScores;
            if (isRanked) {
                if (context != LeaderboardContexts.Golf) {
                    rankedScores = await _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToListAsync();
                } else {
                    rankedScores = await _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenBy(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.ModifiedScore)
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToListAsync();
                }
            } else {
                if (context != LeaderboardContexts.Golf) {
                    rankedScores = await _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderBy(el => el.Priority)
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToListAsync();
                } else {
                    rankedScores = await _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Context == context && !s.Banned)
                    .OrderByDescending(el => el.Priority)
                    .ThenBy(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToListAsync();
                }
            }

            

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                if (s.Id == resultContextExtension.Id) {
                    resultContextExtension.Rank = i + 1;
                }
                if (s.Rank == i + 1) continue;

                var score = new ScoreContextExtension() { Id = s.Id };
                try {
                    _context.ScoreContextExtensions.Attach(score);
                } catch { }
                score.Rank = i + 1;
                    
                _context.Entry(score).Property(x => x.Rank).IsModified = true;
            }
        }

        [NonAction]
        private async Task PostUploadAction(
            Replay replay,
            byte[] replayData,
            Leaderboard leaderboard, 
            Player player,
            Score resultScore,
            List<CurrentScoreWrapper> currentScores,
            HttpContext? context,
            ReplayOffsets offsets,
            bool allow = false) {

            var oldPlayerStats = CollectPlayerStats(player);

            var transaction = await _context.Database.BeginTransactionAsync();

            resultScore.Replay = await _s3Client.UploadReplay(ReplayUtils.ReplayFilename(replay, resultScore), replayData);
            _context.Entry(resultScore).Property(x => x.Replay).IsModified = true;

            if (!player.Bot && leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                await _context.RecalculatePPAndRankFast(player, resultScore.ValidContexts);
            }

            try
            {
                await _context.BulkSaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await ex.Entries.Single().ReloadAsync();
                await _context.BulkSaveChangesAsync();
            }

            await transaction.CommitAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            transaction = await _context.Database.BeginTransactionAsync();

            ScoreStatistic? statistic;
            string? statisticError;
            try {
                (statistic, statisticError) = await _scoreController.CalculateAndSaveStatistic(replay, resultScore);
                
            } catch (Exception e)
            {
                await SaveFailedScore(transaction, resultScore, leaderboard, e.ToString());
                return;
            }

            foreach (var scoreToDelete in currentScores) {
                if (scoreToDelete.ValidContexts == LeaderboardContexts.None) {
                    _context.ScoreRedirects.Add(new ScoreRedirect
                    {
                        OldScoreId = scoreToDelete.Score.Id,
                        NewScoreId = resultScore.Id,
                    });
                }
            }

            resultScore.ReplayOffsets = offsets;
            await _context.RecalculateEventsPP(player, leaderboard);

            try
            {
                await _context.BulkSaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await ex.Entries.Single().ReloadAsync();
                await _context.BulkSaveChangesAsync();
            }

            await transaction.CommitAsync();

            transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var scoreToDelete in currentScores) {
                    if (scoreToDelete.ValidContexts == LeaderboardContexts.None) {
                        await MigrateOldReplay(scoreToDelete.Score, leaderboard.Id);
                    }
                }

                if (statistic == null)
                {
                    await SaveFailedScore(transaction, resultScore, leaderboard, "Could not recalculate score from replay. Error: " + statisticError);
                    return;
                }

                float maxScore = leaderboard.Difficulty.MaxScore;
                // V3 notes at the start may lead to 100+ accuracy in some cases
                if (leaderboard.Difficulty.Requirements.HasFlag(Requirements.V3)) {
                    maxScore *= 1.1f;
                }
                if (resultScore.BaseScore > maxScore) {
                    await SaveFailedScore(transaction, resultScore, leaderboard, "Score is bigger than max possible on this map!");
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
                            await SaveFailedScore(transaction, resultScore, leaderboard, "Calculated on server score is too different: " + statistic.winTracker.totalScore + ". You probably need to update the mod.");

                            return;
                        } else {
                            using (var recalculatedStream = new MemoryStream()) {
                                ReplayEncoder.Encode(replay, new BinaryWriter(recalculatedStream, Encoding.UTF8));
                                resultScore.Replay = await _s3Client.UploadReplay("recalculated-" + ReplayUtils.ReplayFilename(replay, resultScore), recalculatedStream.ToArray());
                                _context.Entry(resultScore).Property(x => x.Replay).IsModified = true;
                            }

                            try {
                                (statistic, statisticError) = await _scoreController.CalculateAndSaveStatistic(replay, resultScore);
                
                            } catch (Exception e)
                            {
                                await SaveFailedScore(transaction, resultScore, leaderboard, e.ToString());
                                return;
                            }
                        }
                    }
                }

                if (leaderboard.Difficulty.Notes > 30 && !allow)
                {
                    var sameAccScore = await _context
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
                        await SaveFailedScore(transaction, resultScore, leaderboard, "Acc is suspiciously exact same as: " + sameAccScore + "'s score");

                        return;
                    }
                }

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
                        resultScore.Modifiers, 
                        leaderboard.Difficulty.ModifierValues, 
                        leaderboard.Difficulty.ModifiersRating, 
                        leaderboard.Difficulty.AccRating ?? 0, 
                        leaderboard.Difficulty.PassRating ?? 0, 
                        leaderboard.Difficulty.TechRating ?? 0, 
                        leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard").Item1;
                }

                resultScore.Country = (context?.Request.Headers["cf-ipcountry"] ?? "") == StringValues.Empty ? "not set" : context?.Request.Headers["cf-ipcountry"].ToString();

                await UpdateTop4(resultScore, currentScores, player, oldPlayerStats, leaderboard);
                UpdateImprovements(resultScore, currentScores, player, oldPlayerStats, leaderboard);
                UpdatePlayerStats(resultScore, currentScores, player, oldPlayerStats, leaderboard);

                if (resultScore.Hmd == HMD.unknown && (await _context.Headsets.FirstOrDefaultAsync(h => h.Name == replay.info.hmd)) == null) {
                    _context.Headsets.Add(new Headset {
                        Name = replay.info.hmd,
                        Player = replay.info.playerID,
                    });
                }

                if (resultScore.Controller == ControllerEnum.unknown && (await _context.VRControllers.FirstOrDefaultAsync(h => h.Name == replay.info.controller)) == null) {
                    _context.VRControllers.Add(new VRController {
                        Name = replay.info.controller,
                        Player = replay.info.playerID,
                    });
                }

                await CollectStats(replay, replayData, resultScore.Replay, resultScore.PlayerId, leaderboard, replay.frames.Last().time, EndType.Clear, resultScore);
                
                await _context.BulkSaveChangesAsync();
                await transaction.CommitAsync();

                await SocketController.TryPublishNewScore(resultScore, _context);
                await SocketController.ScoreWasAccepted(resultScore, _context);

                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && 
                    resultScore.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                    resultScore.Rank == 1 && 
                    !player.Bot)
                {
                    var dsClient = top1DSClient();

                    if (dsClient != null)
                    {
                        var song = await _context.Leaderboards.Where(lb => lb.Id == leaderboard.Id).Include(lb => lb.Song).Select(lb => lb.Song).FirstOrDefaultAsync();
                        string message = "**" + player.Name + "** has become No 1 on **" + (song != null ? song?.Name : leaderboard.Id) + "** :tada: \n";
                        message += Math.Round(resultScore.Accuracy * 100, 2) + "% " + Math.Round(resultScore.Pp, 2) + "pp (" + Math.Round(resultScore.Weight * resultScore.Pp, 2) + "pp)\n";
                        var secondScore = await _context
                            .Scores
                            .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned && s.LeaderboardId != null && s.ValidContexts.HasFlag(LeaderboardContexts.General))
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

                        await dsClient.SendMessageAsync(message,
                            embeds: new List<Embed> { new EmbedBuilder()
                                    .WithTitle("Open leaderboard ↗")
                                    .WithUrl("https://beatleader.xyz/leaderboard/global/" + leaderboard.Id)
                                    .WithDescription("[Watch replay with BL](" + "https://replay.beatleader.xyz?scoreId=" + resultScore.Id + ") | [Watch replay with ArcViewer](" +"https://allpoland.github.io/ArcViewer/?scoreID=" + resultScore.Id + ")" )
                                    .WithImageUrl("https://api.beatleader.xyz/preview/replay?scoreId=" + resultScore.Id)
                                    .Build()
                            });
                    }
                }

                transaction = await _context.Database.BeginTransactionAsync();

                // Calculate clan ranking for this leaderboard
                (var changes, var playerClans) = await _context.UpdateClanRanking(leaderboard, resultScore);
                if (changes?.Count > 0) {
                    ClanTaskService.AddJob(new ClanRankingChangesDescription {
                        Score = resultScore,
                        Changes = changes
                    });
                }

                await _context.BulkSaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception e)
            {
                await SaveFailedScore(transaction, resultScore, leaderboard, e.ToString());
            }
        }

        [NonAction]
        private async Task SaveFailedScore(IDbContextTransaction transaction, Score score, Leaderboard leaderboard, string failReason) {
            try {

            await SocketController.ScoreWasRejected(score, _context);

            foreach (var ce in score.ContextExtensions) {
                if (score.ValidContexts.HasFlag(ce.Context)) {
                    _context.ScoreContextExtensions.Remove(ce);
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
            _context.FailedScores.Add(failedScore);
            await _context.SaveChangesAsync();

            transaction.Commit();

            transaction = _context.Database.BeginTransaction();

            _context.ChangeTracker.AutoDetectChangesEnabled = false;

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                await _context.RecalculatePPAndRankFast(player, score.ValidContexts);
            }
            await _leaderboardRefreshController.RefreshLeaderboardsRankAllContexts(leaderboard.Id);

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            await _playerRefreshController.RefreshStats(
                player.ScoreStats, 
                player.Id, 
                player.Rank,
                null,
                null);

            foreach (var ce in player.ContextExtensions)
            {
                await _playerContextRefreshController.RefreshStats(ce.ScoreStats, player.Id, ce.Context);
            }

            await _context.BulkSaveChangesAsync();
            transaction.Commit();
            
            } catch { }
        }

        [NonAction]
        private async Task CollectStats(
            Replay replay,
            byte[] replayData,
            string? fileName,
            string playerId,
            Leaderboard leaderboard,
            float time = 0, 
            EndType type = 0,
            Score? resultScore = null) {

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            if (resultScore == null) {
                (resultScore, int maxScore) = ReplayUtils.ProcessReplay(replay, leaderboard.Difficulty);

                if (type == EndType.Clear) {
                    ScoreStatistic? statistic = null;

                    try
                    {
                        (statistic, string? error) = ReplayStatisticUtils.ProcessReplay(replay, leaderboard);
                    } catch (Exception e) {
                    }
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
            }
            LeaderboardPlayerStatsService.AddJob(new PlayerStatsJob {
                replayData = fileName != null ? null : replayData,
                fileName = fileName ?? $"{replay.info.playerID}-{timeset}{(replay.info.speed != 0 ? "-practice" : "")}{(replay.info.failTime != 0 ? "-fail" : "")}-{replay.info.difficulty}-{replay.info.mode}-{replay.info.hash}.bsor",
                playerId = playerId,
                leaderboardId = leaderboard.Id,
                time = time,
                type = type,
                score = resultScore,
                saveReplay = replay.frames.Count > 0 && replay.info.score > 0 
            });
        }

        private async Task MigrateOldReplay(Score score, string leaderboardId) {
            if (score.Replay == null) return;
            var stats = await _context.PlayerLeaderboardStats
                .Where(s => s.LeaderboardId == leaderboardId && s.Score == score.BaseScore && s.PlayerId == score.PlayerId && (s.Replay == null || s.Replay == score.Replay))
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
                }
            } catch {}

            if (uploaded) {
                if (stats != null) {
                    stats.Replay = "https://api.beatleader.xyz/otherreplays/" + fileName;
                } else {
                     LeaderboardPlayerStatsService.AddJob(new PlayerStatsJob {
                        fileName = "https://api.beatleader.xyz/otherreplays/" + fileName,
                        playerId = score.PlayerId,
                        leaderboardId = leaderboardId,
                        timeset = score.Timepost > 0 ? score.Timepost : int.Parse(score.Timeset),
                        type = EndType.Clear,
                        time = 0,
                        score = score
                    });
                }
            }
        }

        [NonAction]
        private async Task UpdateTop4(
            Score resultScore,
            List<CurrentScoreWrapper> currentScores, 
            Player player,
            List<OldPlayerStats> oldPlayerStats,
            Leaderboard leaderboard) {

            bool ranked = leaderboard.Difficulty.Status == DifficultyStatus.ranked;

            var generalCurrentScore = currentScores.FirstOrDefault(s => s.ValidContexts.HasFlag(LeaderboardContexts.General));
            if (resultScore.ValidContexts.HasFlag(LeaderboardContexts.General) && 
                resultScore.Rank < 4 &&

                (generalCurrentScore == null || generalCurrentScore.Score.Rank != resultScore.Rank)) {
                var scores = await _context
                    .Scores
                    .Where(s =>                                                                                            
                        s.LeaderboardId == leaderboard.Id && 
                        s.Rank >= resultScore.Rank && 
                        s.Rank <= 4 &&
                        s.PlayerId != player.Id)
                    .Select(s => new {
                        s.Rank,
                        s.Player.ScoreStats
                    })
                    .ToListAsync();
                foreach (var score in scores) {
                    if (score.ScoreStats == null) continue;
                    var scoreStats = score.ScoreStats;
                    if (score.Rank == 2) {
                        if (ranked) {
                            scoreStats.RankedTop1Count--;
                        } else {
                            scoreStats.UnrankedTop1Count--;
                        }
                        scoreStats.Top1Count--;
                    }
                    if (ranked) {
                        scoreStats.RankedTop1Score = ReplayUtils.UpdateRankScore(scoreStats.RankedTop1Score, score.Rank - 1, score.Rank);
                    } else {
                        scoreStats.UnrankedTop1Score = ReplayUtils.UpdateRankScore(scoreStats.UnrankedTop1Score, score.Rank - 1, score.Rank);
                    }
                    scoreStats.Top1Score = ReplayUtils.UpdateRankScore(scoreStats.Top1Score, score.Rank - 1, score.Rank);
                }
            }

            if (resultScore.ContextExtensions != null) {
                foreach (var ce in resultScore.ContextExtensions)
                {
                    var currentScore = currentScores
                        .FirstOrDefault(s => s.ValidContexts.HasFlag(ce.Context));
                    var currentScoreExtenstion = currentScore?.ContextExtensions
                        ?.FirstOrDefault(sce => sce.Context == ce.Context);

                    if (ce.Rank < 4 &&
                        (currentScoreExtenstion == null || currentScoreExtenstion.Rank != ce.Rank)) {
                        var scores = await _context
                            .ScoreContextExtensions
                            .Where(s =>                                                                                            
                                s.LeaderboardId == leaderboard.Id && 
                                s.Rank >= resultScore.Rank && 
                                s.Rank <= 4 &&
                                s.PlayerId != player.Id &&
                                s.Context == ce.Context)
                            .Select(s => new {
                                s.Rank,
                                s.Player.ContextExtensions.Where(ce => ce.Context == ce.Context).FirstOrDefault().ScoreStats
                            })
                            .ToListAsync();
                        foreach (var score in scores) {
                            if (score.ScoreStats == null) continue;
                            var scoreStats = score.ScoreStats;
                            if (score.Rank == 2) {
                                if (ranked) {
                                    scoreStats.RankedTop1Count--;
                                } else {
                                    scoreStats.UnrankedTop1Count--;
                                }
                                scoreStats.Top1Count--;
                            }
                            if (ranked) {
                                scoreStats.RankedTop1Score = ReplayUtils.UpdateRankScore(scoreStats.RankedTop1Score, score.Rank - 1, score.Rank);
                            } else {
                                scoreStats.UnrankedTop1Score = ReplayUtils.UpdateRankScore(scoreStats.UnrankedTop1Score, score.Rank - 1, score.Rank);
                            }
                            scoreStats.Top1Score = ReplayUtils.UpdateRankScore(scoreStats.Top1Score, score.Rank - 1, score.Rank);
                        }
                    }

                }
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
                            improvement.Rank = ce.Rank - currentScoreExtenstion.Score.Rank;
                        }
                    }
                }
            }
        }

        private void UpdatePlayerStats(
                Score resultScore,
                List<CurrentScoreWrapper> currentScores, 
                Player player,
                List<OldPlayerStats> oldPlayerStats,
                Leaderboard leaderboard) {

            if (!resultScore.IgnoreForStats && 
                resultScore.ValidContexts.HasFlag(LeaderboardContexts.General) && 
                resultScore.MaxStreak > player.ScoreStats.MaxStreak) {
                    player.ScoreStats.MaxStreak = resultScore.MaxStreak ?? 0;
            }

            if (player.Rank < player.ScoreStats.PeakRank || player.ScoreStats.PeakRank == 0)
            {
                player.ScoreStats.PeakRank = player.Rank;
            }

            foreach (var context in ContextExtensions.NonGeneral)
            {
                var contextPlayer = player.ContextExtensions?.FirstOrDefault(ce => ce.Context == context);
                var contextStats = contextPlayer?.ScoreStats;
                if (contextStats == null || contextPlayer == null) continue;

                if (!resultScore.IgnoreForStats && 
                    resultScore.ValidContexts.HasFlag(context) && 
                    resultScore.MaxStreak > contextStats.MaxStreak) {
                        contextStats.MaxStreak = resultScore.MaxStreak ?? 0;
                }

                if (contextPlayer.Rank < contextStats.PeakRank || contextStats.PeakRank == 0)
                {
                    contextStats.PeakRank = contextPlayer.Rank;
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
