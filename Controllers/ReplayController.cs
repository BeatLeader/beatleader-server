using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Discord;
using Discord.Webhook;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Prometheus.Client;
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
        public async Task<ActionResult<ScoreResponse>> PostReplayFromCDN(string authenticatedPlayerID, string name, HttpContext context)
        {
            using (var stream = await _s3Client.DownloadReplay(name)) {
                if (stream != null) {
                    MemoryStream ms = new MemoryStream(5);
                    await stream.CopyToAsync(ms);
                    ms.Position = 0;

                    return await PostReplay(authenticatedPlayerID, ms, context);
                } else {
                    return NotFound();
                }
            }
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplay(
            string authenticatedPlayerID, 
            Stream replayStream,
            HttpContext context,
            float time = 0, 
            EndType type = 0)
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
                    return BadRequest("Replay is too big to save, sorry");
                }
                replayData = ms.ToArray();
                try
                {
                    (replay, offsets) = ReplayDecoder.Decode(replayData);
                }
                catch (Exception)
                {
                    return BadRequest("Error decoding replay");
                }
            }
            //AsyncReplayDecoder replayDecoder = new AsyncReplayDecoder();
            //(var info, var continuing) = await replayDecoder.StartDecodingStream(replayStream);
            var info = replay?.info;

            if (info == null) return BadRequest("It's not a replay or it has old version.");
            if (replay.notes.Count == 0) {
                return BadRequest("Replay is broken, update your mod please.");
            }
            if (info.hash.Length < 40) return BadRequest("Hash is to short");

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
                await CollectStats(info.score, authenticatedPlayerID, leaderboard, time, type);
                return Ok();
            } else {
                await CollectStats(replay.info.score, authenticatedPlayerID, leaderboard, replay.frames.Last().time, EndType.Clear);
            }

            if (replay.frames.Count == 0) {
                return BadRequest("Replay is broken, update your mod please.");
            }

            if ((leaderboard.Difficulty.Status == DifficultyStatus.ranked || leaderboard.Difficulty.Status == DifficultyStatus.qualified) && leaderboard.Difficulty.Notes != 0 && replay.notes.Count > leaderboard.Difficulty.Notes) {
                string? error = ReplayUtils.RemoveDuplicates(replay, leaderboard);
                if (error != null) {
                    return BadRequest("Failed to delete duplicate note: " + error);
                }
            }

            (Score resultScore, int maxScore) = ReplayUtils.ProcessReplayInfo(info, leaderboard.Difficulty);
            ReplayUtils.PostProcessReplay(resultScore, replay);

            Score? currentScore;
            using (_serverTiming.TimeAction("currS"))
            {
                currentScore = _context
                    .Scores
                    .Where(s =>
                        s.LeaderboardId == leaderboard.Id &&
                        s.PlayerId == info.playerID)
                    .Include(s => s.Player)
                    .ThenInclude(p => p.ScoreStats)
                    .Include(s => s.RankVoting)
                    .ThenInclude(v => v.Feedbacks)
                    .FirstOrDefault();
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

                    _context.Players.Add(player);

                    SearchService.PlayerAdded(player.Id, player.Name);
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
                (result, stats) = await StandardScore(
                    leaderboard, 
                    player,
                    resultScore, 
                    currentScore, 
                    replay,
                    replayData,
                    offsets,
                    //replayDecoder, 
                    //continuing, 
                    context, 
                    transaction,
                    maxScore);
            } catch (Exception e) {

                 _context.RejectChanges();

                if (e is SqlException)
                {
                    transaction.Rollback();
                    transaction = await _context.Database.BeginTransactionAsync();
                }

                string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsortemp";
                //resultScore.Replay = "https://cdn.replays.beatleader.xyz/" + fileName;
                resultScore.Replay = "https://ssnowy-beatleader-testing.s3.us-east-2.amazonaws.com/" + fileName;
                
                await _s3Client.UploadReplay(fileName, replayData);

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
        private async Task<(ActionResult<ScoreResponse>, bool)> StandardScore(
            Leaderboard leaderboard,
            Player player,
            Score resultScore,
            Score? currentScore,
            Replay replay,
            byte[] replayData,
            ReplayOffsets offsets,
            //AsyncReplayDecoder replayDecoder,
            //Task<Replay?> continuing,
            HttpContext context,
            IDbContextTransaction transaction,
            int maxScore) {
            
            var info = replay.info; // replayDecoder.replay.info;

            if (player.Banned) return (BadRequest("You are banned!"), false);
            if (resultScore.BaseScore > maxScore) return (BadRequest("Score is bigger than max possible on this map!"), false);
            if (currentScore != null && !currentScore.Modifiers.Contains("OP") &&
                    ((currentScore.Pp != 0 && currentScore.Pp >= resultScore.Pp) ||
                    (currentScore.Pp == 0 && currentScore.ModifiedScore >= resultScore.ModifiedScore)))
            {
                return (BadRequest("Score is lower than existing one"), true);
            }

            if (player.ScoreStats == null) {
                player.ScoreStats = new PlayerScoreStats();
            }
            
            resultScore.Player = player;
            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            resultScore.Replay = "";
            resultScore.Timepost = timeset;
            if (currentScore != null) {
                resultScore.PlayCount = currentScore.PlayCount;
            } else {
                resultScore.PlayCount = _context.PlayerLeaderboardStats.Where(st => st.PlayerId == player.Id && st.LeaderboardId == leaderboard.Id).Count();
            }

            ScoreImprovement improvement = new ScoreImprovement();
            resultScore.ScoreImprovement = improvement;

            using (_serverTiming.TimeAction("score"))
            {
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
                        player.ScoreStats.TotalScore -= currentScore.ModifiedScore;

                        if (player.ScoreStats.TotalPlayCount == 1)
                        {
                            player.ScoreStats.AverageAccuracy = 0.0f;
                        }
                        else
                        {
                            player.ScoreStats.AverageAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, currentScore.Accuracy);
                        }

                        
                        if (status1 == DifficultyStatus.ranked)
                        {
                            float oldAverageAcc = player.ScoreStats.AverageRankedAccuracy;
                            if (player.ScoreStats.RankedPlayCount == 1)
                            {
                                player.ScoreStats.AverageRankedAccuracy = 0.0f;
                            }
                            else
                            {
                                player.ScoreStats.AverageRankedAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, currentScore.Accuracy);
                            }

                            improvement.AverageRankedAccuracy = player.ScoreStats.AverageRankedAccuracy - oldAverageAcc;

                            switch (currentScore.Accuracy)
                            {
                                case > 0.95f:
                                    player.ScoreStats.SSPPlays--;
                                    break;
                                case > 0.9f:
                                    player.ScoreStats.SSPlays--;
                                    break;
                                case > 0.85f:
                                    player.ScoreStats.SPPlays--;
                                    break;
                                case > 0.8f:
                                    player.ScoreStats.SPlays--;
                                    break;
                                default:
                                    player.ScoreStats.APlays--;
                                    break;
                            }
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
                    currentScore.LeaderboardId = null;
                    _context.Scores.Remove(currentScore);
                }
                else
                {
                    if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                    {
                        player.ScoreStats.RankedPlayCount++;
                    }
                    player.ScoreStats.TotalPlayCount++;
                }

                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    player.ScoreStats.LastRankedScoreTime = timeset;
                }
                else
                {
                    player.ScoreStats.LastUnrankedScoreTime = timeset;
                }
                player.ScoreStats.LastScoreTime = timeset;
                leaderboard.Plays++;

                using (_serverTiming.TimeAction("db"))
                {
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
                }
            }

            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            var status = leaderboard.Difficulty.Status;
            var isRanked = status is DifficultyStatus.ranked or DifficultyStatus.qualified or DifficultyStatus.inevent;

            var rankedScores = (isRanked 
                    ?
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.Pp <= resultScore.Pp && !s.Banned)
                    .OrderByDescending(el => Math.Round(el.Pp, 2))
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new { Id = s.Id, Rank = s.Rank })
                    :
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && s.ModifiedScore <= resultScore.ModifiedScore && !s.Banned)
                    .OrderByDescending(el => el.ModifiedScore)
                    .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                    .ThenBy(el => el.Timeset)
                    .Select(s => new { Id = s.Id, Rank = s.Rank })
            ).ToList();

            int topRank = rankedScores.Count > 0 ? rankedScores[0].Rank : _context
                .Scores.Count(s => s.LeaderboardId == leaderboard.Id) + 1;

            if (currentScore?.Rank < topRank) {
                topRank--;
            }

            resultScore.Rank = topRank;
            _context.Entry(resultScore).Property(x => x.Rank).IsModified = true;

            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                var score = _context.Scores.Local.FirstOrDefault(ls => ls.Id == s.Id);
                if (score == null) {
                    score = new Score() { Id = s.Id };
                    _context.Scores.Attach(score);
                }
                score.Rank = i + topRank + 1;
                    
                _context.Entry(score).Property(x => x.Rank).IsModified = true;
            }

            // Calculate clan ranking for this leaderboard
            if (isRanked)
            {
                leaderboard.ClanRanking = _context.CalculateClanRanking(leaderboard);
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

                await transaction.CommitAsync();
            }

            (float pp, int rank, int countryRank) = _context.RecalculatePPAndRankFaster(player);

            context.Response.OnCompleted(async () => {
                await PostUploadAction(
                    replay,
                    replayData,
                    //replayDecoder, 
                    leaderboard, 
                    player, 
                    improvement, 
                    resultScore, 
                    currentScore,
                    context, 
                    offsets);
            });

            var result = RemoveLeaderboard(resultScore, resultScore.Rank);
            result.Player.Rank = rank;
            result.Player.CountryRank = countryRank;
            result.Player.Pp = pp;

            return (result, false);
        }

        [NonAction]
        private async Task PostUploadAction(
            Replay replay,
            byte[] replayData,
            //AsyncReplayDecoder replayDecoder,
            Leaderboard leaderboard, 
            Player player, 
            ScoreImprovement improvement,
            Score resultScore,
            Score? currentScore,
            HttpContext context,
            ReplayOffsets offsets) {

            await _s3Client.DeleteStats(leaderboard.Id + "-leaderboard.json");

            float oldPp = player.Pp;
            int oldRank = player.Rank;

            var transaction2 = await _context.Database.BeginTransactionAsync();

            if (currentScore != null)
            {
                _context.Entry(improvement).Property(x => x.Rank).IsModified = true;
                improvement.Rank = resultScore.Rank - currentScore.Rank;
            }

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                _context.RecalculatePPAndRankFast(player);
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

            if (!resultScore.IgnoreForStats) {
                player.ScoreStats.TotalScore += resultScore.ModifiedScore;
                player.ScoreStats.AverageAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, resultScore.Accuracy);
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, resultScore.Accuracy);
                    if (resultScore.Accuracy > player.ScoreStats.TopAccuracy)
                    {
                        player.ScoreStats.TopAccuracy = resultScore.Accuracy;
                    }
                    if (resultScore.Pp > player.ScoreStats.TopPp)
                    {
                        player.ScoreStats.TopPp = resultScore.Pp;
                    }

                    if (resultScore.BonusPp > player.ScoreStats.TopBonusPP)
                    {
                        player.ScoreStats.TopBonusPP = resultScore.BonusPp;
                    }

                    if (resultScore.PassPP > player.ScoreStats.TopPassPP)
                    {
                        player.ScoreStats.TopPassPP = resultScore.PassPP;
                    }
                    if (resultScore.AccPP > player.ScoreStats.TopAccPP)
                    {
                        player.ScoreStats.TopAccPP = resultScore.AccPP;
                    }
                    if (resultScore.TechPP > player.ScoreStats.TopTechPP)
                    {
                        player.ScoreStats.TopTechPP = resultScore.TechPP;
                    }

                    switch (resultScore.Accuracy)
                    {
                        case > 0.95f:
                            player.ScoreStats.SSPPlays++;
                            break;
                        case > 0.9f:
                            player.ScoreStats.SSPlays++;
                            break;
                        case > 0.85f:
                            player.ScoreStats.SPPlays++;
                            break;
                        case > 0.8f:
                            player.ScoreStats.SPlays++;
                            break;
                        default:
                            player.ScoreStats.APlays++;
                            break;
                    }
                }
            }

            if (currentScore != null)
            {
                _context.ScoreRedirects.Add(new ScoreRedirect
                {
                    OldScoreId = currentScore.Id,
                    NewScoreId = resultScore.Id,
                });
            }

            resultScore.ReplayOffsets = offsets;

            //await continuing;

            //resultScore.ReplayOffsets = replayDecoder.offsets;
            //var replay = replayDecoder.replay;

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
            {
                improvement.TotalPp = player.Pp - oldPp;
                improvement.TotalRank = player.Rank - oldRank;

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
                //if (currentScore != null && stats != null) {
                //    await MigrateOldReplay(currentScore, stats);
                //}

                string fileName = replay.info.playerID + "-" + resultScore.Id.ToString() + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsor";
                resultScore.Replay = "https://cdn.replays.beatleader.xyz/" + fileName;
                
                string replayLink = resultScore.Replay;
                await _s3Client.UploadReplay(fileName, replayData);
                (ScoreStatistic? statistic, string? error) = await _scoreController.CalculateAndSaveStatistic(replay, resultScore);
                if (statistic == null)
                {
                    SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Could not recalculate score from replay. Error: " + error);
                    return;
                }

                if (!leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles)) {
                    double scoreRatio = (double)resultScore.BaseScore / (double)statistic.winTracker.totalScore;
                    if (scoreRatio > 1.02 || scoreRatio < 0.98)
                    {
                        SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Calculated on server score is too different: " + statistic.winTracker.totalScore + ". You probably need to update the mod.");

                        return;
                    }
                }

                if (leaderboard.Difficulty.Notes > 30)
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

                resultScore.Replay = replayLink;
                resultScore.AccLeft = statistic.accuracyTracker.accLeft;
                resultScore.AccRight = statistic.accuracyTracker.accRight;
                resultScore.MaxCombo = statistic.hitTracker.maxCombo;
                resultScore.FcAccuracy = statistic.accuracyTracker.fcAcc;
                resultScore.MaxStreak = statistic.hitTracker.maxStreak;
                if (!resultScore.IgnoreForStats && resultScore.MaxStreak > player.ScoreStats.MaxStreak) {
                    player.ScoreStats.MaxStreak = resultScore.MaxStreak;
                }

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
                resultScore.Country = context.Request.Headers["cf-ipcountry"] == StringValues.Empty ? "not set" : context.Request.Headers["cf-ipcountry"].ToString();

                if (currentScore != null)
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

                await _context.SaveChangesAsync();
                await transaction3.CommitAsync();

                await ScoresSocketController.TryPublishNewScore(resultScore, _configuration, _context);
                await GeneralSocketController.ScoreWasAccepted(resultScore, _configuration, _context);

                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && resultScore.Rank == 1)
                {
                    var dsClient = top1DSClient();

                    if (dsClient != null)
                    {
                        var song = _context.Leaderboards.Where(lb => lb.Id == leaderboard.Id).Include(lb => lb.Song).Select(lb => lb.Song).FirstOrDefault();
                        string message = "**" + player.Name + "** has become No 1 on **" + (song != null ? song?.Name : leaderboard.Id) + "** :tada: \n";
                        message += Math.Round(resultScore.Accuracy * 100, 2) + "% " + Math.Round(resultScore.Pp, 2) + "pp (" + Math.Round(resultScore.Weight * resultScore.Pp, 2) + "pp)\n";
                        var secondScore = _context
                            .Scores
                            .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned && s.LeaderboardId != null)
                            .OrderByDescending(s => s.Pp)
                            .Skip(1)
                            .Take(1)
                            .Select(s => new { Pp = s.Pp, Accuracy = s.Accuracy })
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
                                    .WithTitle("Leaderboard")
                                    .WithUrl("https://beatleader.xyz/leaderboard/global/" + leaderboard.Id)
                                    .Build(),
                                new EmbedBuilder()
                                    .WithTitle("Watch Replay")
                                    .WithUrl("https://replay.beatleader.xyz?scoreId=" + resultScore.Id)
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
                    .OrderByDescending(el => el.Pp)
                    .Select(s => new { Id = s.Id, Rank = s.Rank })
                    :
                _context
                    .Scores
                    .Where(s => s.LeaderboardId == leaderboard.Id && !s.Banned)
                    .OrderByDescending(el => el.ModifiedScore)
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
                _context.RecalculatePPFast(player);
                _context.BulkSaveChanges();

                Dictionary<string, int> countries = new Dictionary<string, int>();
                var ranked = _context.Players
                    .Where(p => p.Pp > 0)
                    .OrderByDescending(t => t.Pp)
                    .Select(p => new { Id = p.Id, Country = p.Country })
                    .ToList();
                foreach ((int i, var pp) in ranked.Select((value, i) => (i, value)))
                {
                    var p = _context.Players.Local.FirstOrDefault(lp => lp.Id == pp.Id);
                    if (p == null) {
                        p = new Player { Id = pp.Id, Country = pp.Country };
                        _context.Players.Attach(p);
                    }

                    p.Rank = i + 1;
                    _context.Entry(p).Property(x => x.Rank).IsModified = true;
                    if (!countries.ContainsKey(p.Country))
                    {
                        countries[p.Country] = 1;
                    }

                    p.CountryRank = countries[p.Country];
                    _context.Entry(p).Property(x => x.CountryRank).IsModified = true;

                    countries[p.Country]++;
                }
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
            int score,
            string playerId,
            Leaderboard leaderboard,
            float time = 0, 
            EndType type = 0) {

            if (leaderboard.PlayerStats == null)
            {
                leaderboard.PlayerStats = new List<PlayerLeaderboardStats>();
            }

            if (leaderboard.PlayerStats.Count > 0 && 
                leaderboard.PlayerStats.FirstOrDefault(s => s.PlayerId == playerId && s.Score == score) != null) {
                return;
            }
            leaderboard.PlayCount++;

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var stats = new PlayerLeaderboardStats
            {
                Timeset = timeset,
                Time = time,
                Score = score,
                Type = type,
                PlayerId = playerId,
            };

            var currentScore = _context
                    .Scores
                    .Where(s =>
                        s.LeaderboardId == leaderboard.Id &&
                        s.PlayerId == playerId)
                    .FirstOrDefault();
            if (currentScore != null) {
                currentScore.PlayCount++;
            }

            try {
                leaderboard.PlayerStats.Add(stats);
                await _context.SaveChangesAsync();
            } catch { }
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
        public DiscordWebhookClient? top1DSClient()
        {
            var link = _configuration.GetValue<string?>("Top1DSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }

        [HttpGet("~/removeduplicatenotes")]
        public async Task<ActionResult> removeduplicatenotes()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var scores = _context
                .Scores
                .OrderBy(s => s.Id)
                .Where(s => s.Pp > 0).Include(s => s.ReplayOffsets).Include(s => s.Leaderboard).ThenInclude(l => l.Difficulty);

            var totalc = scores.Count();

            for (int i = 0; i < totalc; i+=50000)
            {
                await scores.Skip(i).Take(50000).ParallelForEachAsync(async score => {
                    try {
                        string? name = score.Replay.Split("/").LastOrDefault();
                        if (name == null || score.ReplayOffsets == null)
                        {
                            return;
                        }

                        var leaderboard = score.Leaderboard;

                        using (var stream = await _s3Client.DownloadStreamOffset(name, S3Container.replays, score.ReplayOffsets.Notes, 4)) {
                            if (stream == null) {
                                return;
                            }

                            MemoryStream ms = new MemoryStream(5);
                            await stream.CopyToAsync(ms);
                            ms.Position = 0;

                            var noteCountData = ms.ToArray();

                            int noteCount = BitConverter.ToInt32(noteCountData, 0);

                            if (leaderboard.Difficulty.Notes == 0 || noteCount <= leaderboard.Difficulty.Notes) {
                                return;
                            }
                        }

                        Replay replay;
                        using (var stream = await _s3Client.DownloadReplay(name))
                        {
                            if (stream != null)
                            {
                                try
                                {
                                    MemoryStream ms = new MemoryStream(5);
                                    await stream.CopyToAsync(ms);
                                    ms.Position = 0;

                                    var replayData = ms.ToArray();
                                    (replay, _) = ReplayDecoder.Decode(replayData);
                                }
                                catch (Exception)
                                {
                                    return;
                                }
                            }
                            else
                            {
                                return;
                            }
                        }

                        if (leaderboard.Difficulty.Notes != 0 && replay.notes.Count > leaderboard.Difficulty.Notes)
                        {
                            string? error = ReplayUtils.RemoveDuplicates(replay, leaderboard);
                            if (error != null)
                            {
                                return;
                            }
                        }

                        (Score resultScore, int maxScore) = ReplayUtils.ProcessReplayInfo(replay.info, leaderboard.Difficulty);

                        if (score.Accuracy != resultScore.Accuracy) {

                            score.Accuracy = resultScore.Accuracy;
                            score.Pp = resultScore.Pp;
                            score.ModifiedScore = resultScore.ModifiedScore;
                            score.BaseScore = resultScore.BaseScore;
                            (ScoreStatistic? statistic, _) = await _scoreController.CalculateAndSaveStatistic(replay, score);
                        }
                    } catch (Exception e) {
                        return;
                    }
                }, maxDegreeOfParallelism: 20);

                _context.SaveChanges();
            }

            return Ok();
        }
    }
}
