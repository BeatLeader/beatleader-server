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
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ReplayController : Controller
    {
        private readonly IAmazonS3 _s3Client;
        private readonly AppContext _context;

        private readonly LeaderboardController _leaderboardController;
        private readonly PlayerController _playerController;
        private readonly ScoreController _scoreController;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly IServerTiming _serverTiming;

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
            IServerTiming serverTiming,
            IMetricFactory metricFactory
            )
        {
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _scoreController = scoreController;
            _context = context;
            _environment = env;
            _configuration = configuration;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();

            _replayLocation = metricFactory.CreateGauge("replay_position", "Posted replay location", new string[] { "geohash" });
            _geoHasher = new Geohash.Geohasher();

            if (ipLocation == null) {
                ipLocation = new IP2Location.Component();
                ipLocation.Open(env.WebRootPath + "/databases/IP2LOCATION-LITE-DB.BIN");
            }
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
                AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
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
        public async Task<ActionResult<ScoreResponse>> PostReplayFromCDN(string authenticatedPlayerID, string name, bool backup, bool allow, HttpContext context)
        {
            if (backup) {
                string directoryPath = Path.Combine("/root/replays");
                string filePath = Path.Combine(directoryPath, name);

                if (!System.IO.File.Exists(filePath)) {
                    return NotFound();
                }

                // Use FileMode.Create to overwrite the file if it already exists.
                using (FileStream stream = new FileStream(filePath, FileMode.Open)) {
                    return await PostReplay(authenticatedPlayerID, stream, context, allow: allow);
                }
            } else {
                using (var stream = await _s3Client.DownloadReplay(name)) {
                    if (stream != null) {
                        return await PostReplay(authenticatedPlayerID, stream, context, allow: allow);
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
            bool allow = false)
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
                (replay, offsets) = ReplayDecoder.Decode(replayData);
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
            if (info.hash.Length < 40) return BadRequest("Hash is too short");

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
            info.hash = info.hash.Substring(0, 40);

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

            if ((leaderboard.Difficulty.Status.WithRating()) && leaderboard.Difficulty.Notes != 0 && replay.notes.Count > leaderboard.Difficulty.Notes) {
                string? error = ReplayUtils.RemoveDuplicates(replay, leaderboard);
                if (error != null) {
                    return BadRequest("Failed to delete duplicate note: " + error);
                }
            }

            (Score resultScore, int maxScore) = ReplayUtils.ProcessReplay(replay, leaderboard.Difficulty);

            List<Score> currentScores;
            Score? currentScore;
            using (_serverTiming.TimeAction("currS"))
            {
                currentScores = _context
                    .Scores
                    .Where(s =>
                        s.LeaderboardId == leaderboard.Id &&
                        s.PlayerId == info.playerID)
                    .Include(s => s.Player)
                    .ThenInclude(p => p.ScoreStats)
                    .Include(s => s.Player)
                    .ThenInclude(p => p.ContextExtensions)
                    .ThenInclude(ce => ce.ScoreStats)
                    .Include(s => s.RankVoting)
                    .ThenInclude(v => v.Feedbacks)
                    .Include(s => s.ContextExtensions)
                    .ToList();
                currentScore = currentScores.FirstOrDefault();
            }
            var ip = context.Request.HttpContext.GetIpAddress();
            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                player = currentScore?.Player ?? (await _playerController.GetLazy(info.playerID)).Value;
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
                !leaderboard.Difficulty.Requirements.HasFlag(Requirements.MappingExtensions) && 
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

            GeneralSocketController.ScoreWasUploaded(resultScore, _configuration, _context);
            resultScore.LeaderboardId = null;

            var transaction = await _context.Database.BeginTransactionAsync();
            ActionResult<ScoreResponse> result = BadRequest("Exception occured, ping NSGolova"); 
            bool stats = false;
             
            try {
                (result, stats) = await UploadScores(
                    leaderboard, 
                    player,
                    resultScore, 
                    currentScore,
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
                _context.SaveChanges();

                transaction.Commit();
            }

            return result;
        }

        [NonAction]
        private async Task<(ActionResult<ScoreResponse>, bool)> UploadScores(
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            Score? currentScore,
            List<Score> currentScores,
            Replay replay,
            byte[] replayData,
            ReplayOffsets offsets,
            HttpContext context,
            IDbContextTransaction transaction,
            int maxScore,
            bool allow = false) {

            if (!player.Bot && player.Banned) return (BadRequest("You are banned!"), false);
            if (resultScore.BaseScore > maxScore) return (BadRequest("Score is bigger than max possible on this map!"), false);

            if (player.ScoreStats == null) {
                player.ScoreStats = new PlayerScoreStats();
            }
            resultScore.Player = player;

            resultScore.Banned = player.Bot;
            resultScore.Bot = player.Bot;

            var improvement = await GeneralContextScore(leaderboard, player, resultScore, currentScores, replay);

            await ContextScore(LeaderboardContexts.NoMods, leaderboard, player, resultScore, currentScores, replay);
            await ContextScore(LeaderboardContexts.NoPause, leaderboard, player, resultScore, currentScores, replay);
            await ContextScore(LeaderboardContexts.Golf, leaderboard, player, resultScore, currentScores, replay);

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
                        improvement, 
                        resultScore,
                        currentScore,
                        currentScores,
                        context, 
                        offsets,
                        allow);
                });

                var result = RemoveLeaderboard(resultScore, resultScore.Rank);
                if (!player.Bot && leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                    _context.RecalculatePPAndRankFasterAllContexts(result);
                }

                return (result, false);
            }
        }

        [NonAction]
        private async Task<ScoreImprovement?> GeneralContextScore(
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            List<Score> currentScores,
            Replay replay) {
            
            var info = replay.info;
            var currentScore = currentScores.FirstOrDefault(s => s.ValidContexts.HasFlag(LeaderboardContexts.General));
            
            if (!ReplayUtils.IsNewScoreBetter(currentScore, resultScore)) {
                return null;
            }

            if (currentScore != null) {
                currentScore.ValidContexts &= ~LeaderboardContexts.General;
                resultScore.PlayCount = currentScore.PlayCount;
            } else {
                resultScore.PlayCount = _context.PlayerLeaderboardStats.Where(st => st.PlayerId == player.Id && st.LeaderboardId == leaderboard.Id).Count();
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

            return improvement;
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
            
            if (currentScore != null) {
                currentScore.ValidContexts &= ~context;
                if (currentExtension != null) {
                    currentScore.ContextExtensions.Remove(currentExtension);
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
            IDbContextTransaction transaction) {
            if (player.Bot) return;

            var isRanked = leaderboard.Difficulty.Status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent;
            RefreshGeneneralContextRank(resultScore, currentScores.FirstOrDefault(s => s.ValidContexts.HasFlag(LeaderboardContexts.General)), leaderboard, isRanked);
            
            RefreshContextRank(LeaderboardContexts.NoMods, resultScore, currentScores, leaderboard, isRanked);
            RefreshContextRank(LeaderboardContexts.NoPause, resultScore, currentScores, leaderboard, isRanked);
            RefreshContextRank(LeaderboardContexts.Golf, resultScore, currentScores, leaderboard, isRanked);

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

                await transaction.CommitAsync();
            }
        }

        private void RefreshGeneneralContextRank(
            Score resultScore, 
            Score? currentScore,
            Leaderboard leaderboard,
            bool isRanked) {
            var rankedScores = (isRanked 
                    ?
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                                s.Pp <= resultScore.Pp && 
                                !s.Banned && 
                                s.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                                s.PlayerId != resultScore.PlayerId)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new { s.Id, s.Rank })
                    :
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                               ((s.ModifiedScore <= resultScore.ModifiedScore && s.Priority == resultScore.Priority) || s.Priority > resultScore.Priority) && 
                               !s.Banned &&
                               s.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                               s.PlayerId != resultScore.PlayerId)
                    .OrderBy(el => el.Priority)
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new { s.Id, s.Rank })
            ).ToList();

            int topRank = rankedScores.Count > 0 ? rankedScores[0].Rank : _context
                .Scores.Count(s => s.PlayerId != resultScore.PlayerId && s.ValidContexts.HasFlag(LeaderboardContexts.General) && s.LeaderboardId == leaderboard.Id) + 1;

            if (currentScore?.Rank < topRank) {
                topRank--;
            }

            resultScore.Rank = topRank;
            _context.Entry(resultScore).Property(x => x.Rank).IsModified = true;

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                var score = new Score() { Id = s.Id };
                try {
                    _context.Scores.Attach(score);
                } catch { }
                score.Rank = i + topRank + 1;
                    
                _context.Entry(score).Property(x => x.Rank).IsModified = true;
            }
        }

        class ScoreSelection {
            public int Id { get; set; }
            public int Rank { get; set; }
        }

        private void RefreshContextRank(
            LeaderboardContexts context,
            Score mainResultScore, 
            List<Score> currentScores,
            Leaderboard leaderboard,
            bool isRanked) {

            var resultScore = mainResultScore.ContextExtensions.FirstOrDefault(s => s.Context == context);
            if (resultScore == null) return;

            List<ScoreSelection> rankedScores;
            if (isRanked) {
                rankedScores = _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                                s.Pp <= resultScore.Pp && 
                                s.Context == context &&
                                s.PlayerId != resultScore.PlayerId)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToList();
            } else {
                if (context != LeaderboardContexts.Golf) {
                rankedScores = _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                        ((s.ModifiedScore <= resultScore.ModifiedScore && s.Priority == resultScore.Priority) || s.Priority > resultScore.Priority) && 
                        s.Context == context &&
                        s.PlayerId != resultScore.PlayerId)
                    .OrderBy(el => el.Priority)
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToList();
                } else {
                    rankedScores = _context
                    .ScoreContextExtensions
                    .Where(s => s.LeaderboardId == leaderboard.Id && 
                        ((s.ModifiedScore >= resultScore.ModifiedScore && s.Priority == resultScore.Priority) || s.Priority < resultScore.Priority) && 
                        s.Context == context &&
                        s.PlayerId != resultScore.PlayerId)
                    .OrderByDescending(el => el.Priority)
                    .ThenBy(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new ScoreSelection() { Id = s.Id, Rank = s.Rank })
                    .ToList();
                }
            }

            var currentScore = currentScores
                .FirstOrDefault(s => s.ValidContexts.HasFlag(context))?
                .ContextExtensions
                .FirstOrDefault(c => c.Context == context);

            int topRank = rankedScores.Count > 0 ? rankedScores[0].Rank : _context
                .ScoreContextExtensions.Count(s => s.PlayerId != resultScore.PlayerId && s.LeaderboardId == leaderboard.Id && s.Context == context) + 1;

            if (currentScore?.Rank < topRank) {
                topRank--;
            }

            resultScore.Rank = topRank;
            _context.Entry(resultScore).Property(x => x.Rank).IsModified = true;

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                var score = new ScoreContextExtension() { Id = s.Id };
                try {
                    _context.ScoreContextExtensions.Attach(score);
                } catch { }
                score.Rank = i + topRank + 1;
                    
                _context.Entry(score).Property(x => x.Rank).IsModified = true;
            }
        }

        [NonAction]
        private async Task PostUploadAction(
            Replay replay,
            byte[] replayData,
            Leaderboard leaderboard, 
            Player player, 
            ScoreImprovement? improvement,
            Score resultScore,
            Score? currentScore,
            List<Score> currentScores,
            HttpContext context,
            ReplayOffsets offsets,
            bool allow = false) {

            float oldPp = player.Pp;
            int oldRank = player.Rank;

            var transaction2 = await _context.Database.BeginTransactionAsync();

            if (currentScore != null && improvement != null)
            {
                _context.Entry(improvement).Property(x => x.Rank).IsModified = true;
                improvement.Rank = resultScore.Rank - currentScore.Rank;
            }

            if (!player.Bot && leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                _context.RecalculatePPAndRankFast(player, resultScore.ValidContexts);
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

            await transaction2.CommitAsync();

            _context.ChangeTracker.AutoDetectChangesEnabled = true;

            transaction2 = await _context.Database.BeginTransactionAsync();

            ScoreStatistic? statistic;
            string? statisticError;
            try {
                (statistic, statisticError) = await _scoreController.CalculateAndSaveStatistic(replay, resultScore);
                
            } catch (Exception e)
            {
                SaveFailedScore(transaction2, currentScore, resultScore, leaderboard, e.ToString());
                return;
            }

            if (!resultScore.IgnoreForStats) {
                if (resultScore.Rank < 4) {
                    await UpdateTop4(leaderboard.Id, leaderboard.Difficulty.Status == DifficultyStatus.ranked, player.Id);
                }
            }

            foreach (var scoreToDelete in currentScores) {
                if (scoreToDelete.ValidContexts == LeaderboardContexts.None) {
                    _context.ScoreRedirects.Add(new ScoreRedirect
                    {
                        OldScoreId = scoreToDelete.Id,
                        NewScoreId = resultScore.Id,
                    });
                }
            }

            resultScore.ReplayOffsets = offsets;

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
            {
                if (improvement != null) {
                    improvement.TotalPp = player.Pp - oldPp;
                    improvement.TotalRank = player.Rank - oldRank;
                }

                if (player.Rank < player.ScoreStats.PeakRank)
                {
                    player.ScoreStats.PeakRank = player.Rank;
                }
            }
            _context.RecalculateEventsPP(player, leaderboard);

            try
            {
                await _context.BulkSaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await ex.Entries.Single().ReloadAsync();
                await _context.BulkSaveChangesAsync();
            }

            await transaction2.CommitAsync();

            var transaction3 = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var scoreToDelete in currentScores) {
                    if (scoreToDelete.ValidContexts == LeaderboardContexts.None) {
                        await MigrateOldReplay(scoreToDelete, leaderboard.Id);
                    }
                }

                resultScore.Replay = await _s3Client.UploadReplay(ReplayUtils.ReplayFilename(replay, resultScore), replayData);
                _context.Entry(resultScore).Property(x => x.Replay).IsModified = true;

                if (statistic == null)
                {
                    SaveFailedScore(transaction2, currentScore, resultScore, leaderboard, "Could not recalculate score from replay. Error: " + statisticError);
                    return;
                }

                if (!leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles) && !allow) {
                    double scoreRatio = (double)resultScore.BaseScore / (double)statistic.winTracker.totalScore;
                    if (scoreRatio > 1.01 || scoreRatio < 0.99)
                    {
                        SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Calculated on server score is too different: " + statistic.winTracker.totalScore + ". You probably need to update the mod.");

                        return;
                    }
                }

                if (leaderboard.Difficulty.Notes > 30 && !allow)
                {
                    var sameAccScore = _context
                        .Scores
                        .Where(s => s.LeaderboardId == leaderboard.Id &&
                                             s.PlayerId != resultScore.PlayerId && 
                                             s.AccLeft != 0 && 
                                             s.AccRight != 0 && 
                                             s.AccLeft == statistic.accuracyTracker.accLeft && 
                                             s.AccRight == statistic.accuracyTracker.accRight &&
                                             s.BaseScore == resultScore.BaseScore)
                        .Select(s => s.PlayerId)
                        .FirstOrDefault();
                    if (sameAccScore != null)
                    {
                        SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Acc is suspiciously exact same as: " + sameAccScore + "'s score");

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
                        resultScore.Modifiers, 
                        leaderboard.Difficulty.ModifierValues, 
                        leaderboard.Difficulty.ModifiersRating, 
                        leaderboard.Difficulty.AccRating ?? 0, 
                        leaderboard.Difficulty.PassRating ?? 0, 
                        leaderboard.Difficulty.TechRating ?? 0, 
                        leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard").Item1;
                }

                if (!resultScore.IgnoreForStats && resultScore.MaxStreak > player.ScoreStats.MaxStreak) {
                    player.ScoreStats.MaxStreak = resultScore.MaxStreak ?? 0;
                }
                resultScore.Country = context.Request.Headers["cf-ipcountry"] == StringValues.Empty ? "not set" : context.Request.Headers["cf-ipcountry"].ToString();

                if (currentScore != null && improvement != null)
                {
                    improvement.AccLeft = resultScore.AccLeft - currentScore.AccLeft;
                    improvement.AccRight = resultScore.AccRight - currentScore.AccRight;
                }

                if (resultScore.Hmd == HMD.unknown && _context.Headsets.FirstOrDefault(h => h.Name == replay.info.hmd) == null) {
                    _context.Headsets.Add(new Headset {
                        Name = replay.info.hmd,
                        Player = replay.info.playerID,
                    });
                }

                if (resultScore.Controller == ControllerEnum.unknown && _context.VRControllers.FirstOrDefault(h => h.Name == replay.info.controller) == null) {
                    _context.VRControllers.Add(new VRController {
                        Name = replay.info.controller,
                        Player = replay.info.playerID,
                    });
                }

                await CollectStats(replay, replayData, resultScore.Replay, resultScore.PlayerId, leaderboard, replay.frames.Last().time, EndType.Clear, resultScore);
                
                // Calculate clan ranking for this leaderboard
                //_context.UpdateClanRanking(leaderboard, currentScore, resultScore);

                await _context.BulkSaveChangesAsync();
                await transaction3.CommitAsync();

                await ScoresSocketController.TryPublishNewScore(resultScore, _configuration, _context);
                await GeneralSocketController.ScoreWasAccepted(resultScore, _configuration, _context);

                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && 
                    resultScore.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                    resultScore.Rank == 1 && 
                    !player.Bot)
                {
                    var dsClient = top1DSClient();

                    if (dsClient != null)
                    {
                        var song = _context.Leaderboards.Where(lb => lb.Id == leaderboard.Id).Include(lb => lb.Song).Select(lb => lb.Song).FirstOrDefault();
                        string message = "**" + player.Name + "** has become No 1 on **" + (song != null ? song?.Name : leaderboard.Id) + "** :tada: \n";
                        message += Math.Round(resultScore.Accuracy * 100, 2) + "% " + Math.Round(resultScore.Pp, 2) + "pp (" + Math.Round(resultScore.Weight * resultScore.Pp, 2) + "pp)\n";
                        var secondScore = _context
                            .Scores
                            .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned && s.LeaderboardId != null && s.ValidContexts.HasFlag(LeaderboardContexts.General))
                            .OrderByDescending(s => s.Pp)
                            .Skip(1)
                            .Take(1)
                            .Select(s => new { s.Pp, s.Accuracy })
                            .FirstOrDefault();
                        if (secondScore != null)
                        {
                            message += "This beats previous record by **" + Math.Round(resultScore.Pp - secondScore.Pp, 2) + "pp** and **" + Math.Round((resultScore.Accuracy - secondScore.Accuracy) * 100, 2) + "%** ";
                            if (resultScore.Modifiers.Length > 0)
                            {
                                message += "using **" + resultScore.Modifiers + "**";
                            }
                            message += "\n";
                        }
                        message += Math.Round(improvement.TotalPp, 2) + " to the personal pp and " + improvement.TotalRank + " to rank \n";

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
            }
            catch (Exception e)
            {
                SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, e.ToString());
            }
        }

        [NonAction]
        private void SaveFailedScore(IDbContextTransaction transaction, Score? previousScore, Score score, Leaderboard leaderboard, string failReason) {
            try {
            GeneralSocketController.ScoreWasRejected(score, _configuration, _context);
            RollbackScore(score, previousScore, leaderboard);
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
            _context.SaveChanges();

            transaction.Commit();

            transaction = _context.Database.BeginTransaction();

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            var status = leaderboard.Difficulty.Status;
            var isRanked = status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent;

            var rankedScores = (isRanked 
                    ?
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new { Id = s.Id, Rank = s.Rank })
                    :
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned)
                    .OrderBy(el => el.Priority)
                    .ThenByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new { Id = s.Id, Rank = s.Rank })
            ).ToList();

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                var score1 = _context.Scores.Local.FirstOrDefault(ls => ls.Id == s.Id);
                if (score1 == null) {
                    score1 = new Score() { Id = s.Id };
                    _context.Scores.Attach(score);
                }
                score1.Rank = i + 1;
                    
                _context.Entry(score1).Property(x => x.Rank).IsModified = true;
            }

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                _context.RecalculatePPAndRankFast(player, score.ValidContexts);
            }

            _context.BulkSaveChanges();
            transaction.Commit();
            
            } catch { }
        }

        [NonAction]
        private async void RollbackScore(Score score, Score? previousScore, Leaderboard leaderboard) {
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

            foreach (var ce in score.ContextExtensions) {
                if (score.ValidContexts.HasFlag(ce.Context)) {
                    _context.ScoreContextExtensions.Remove(ce);
                }
            }

            score.LeaderboardId = null;

            if (previousScore == null) {
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    player.ScoreStats.RankedPlayCount--;
                }
                player.ScoreStats.TotalPlayCount--;
            } else {
                previousScore.LeaderboardId = leaderboard.Id;

                player.ScoreStats.TotalScore += previousScore.ModifiedScore;
                player.ScoreStats.AverageAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, previousScore.Accuracy);
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, previousScore.Accuracy);
                }
            }
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
        private async Task GolfScore() {
        }

        [NonAction]
        private async Task NoPauseScore()
        {
        }

        [NonAction]
        private async Task NoModifiersScore()
        {
        }

        [NonAction]
        private async Task PrecisionScore()
        {
        }

        [NonAction]
        private async Task UpdateTop4(
            string leaderboardId, 
            bool ranked,
            string currentPlayerId) {

            var scores = _context
                .Scores
                .Where(s => 
                    s.LeaderboardId == leaderboardId && 
                    (s.Rank == 2 || s.Rank == 3 || s.Rank == 4) &&
                    s.PlayerId != currentPlayerId)
                .Select(s => new {
                    s.Rank,
                    s.Player.ScoreStats
                })
                .ToList();
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

            await _context.SaveChangesAsync();
        }

        [NonAction]
        public DiscordWebhookClient? top1DSClient()
        {
            var link = _configuration.GetValue<string?>("Top1DSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }
    }
}
