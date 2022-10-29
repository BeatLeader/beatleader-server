using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Discord;
using Discord.Webhook;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Threading;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ReplayController : Controller
    {
        private readonly BlobContainerClient _replaysClient;
        //private readonly BlobContainerClient _otherReplaysClient;
        private readonly BlobContainerClient _scoreStatsClient;

        AppContext _context;
        LeaderboardController _leaderboardController;
        PlayerController _playerController;
        PreviewController _previewController;
        ScoreController _scoreController;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;
        private readonly IServerTiming _serverTiming;

        private static BlobContainerClient ContainerWithName(IOptions<AzureStorageConfig> config, string name) {
            string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       name);

            return new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
        }

        public ReplayController(
            AppContext context,
            IOptions<AzureStorageConfig> config, 
            IWebHostEnvironment env,
            IConfiguration configuration,
            LeaderboardController leaderboardController, 
            PlayerController playerController,
            ScoreController scoreController,
            PreviewController previewController,
            IServerTiming serverTiming
            )
		{
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _scoreController = scoreController;
            _previewController = previewController;
            _context = context;
            _environment = env;
            _configuration = configuration;
            _serverTiming = serverTiming;

            if (env.IsDevelopment())
			{
				_replaysClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
                _replaysClient.SetPublicContainerPermissions();
                //_otherReplaysClient = new BlobContainerClient(config.Value.AccountName, config.Value.OtherReplaysContainerName);

                _scoreStatsClient = new BlobContainerClient(config.Value.AccountName, config.Value.ScoreStatsContainerName);
            }
			else
			{
				_replaysClient = ContainerWithName(config, config.Value.ReplaysContainerName);
                //_otherReplaysClient = ContainerWithName(config, config.Value.OtherReplaysContainerName);
                _scoreStatsClient = ContainerWithName(config, config.Value.ScoreStatsContainerName);
            }
        }

        [HttpPost("~/replay"), DisableRequestSizeLimit]
        public async Task<ActionResult<ScoreResponse>> PostSteamReplay([FromQuery] string ticket)
        {
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(ticket, _configuration);
            if (id == null && error != null) {
                return Unauthorized(error);
            }
            long intId = Int64.Parse(id);
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
            if (type != EndType.Unknown && type != EndType.Clear)
            {
                return Ok();
            }
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
            Replay? replay;
            ReplayOffsets? offsets;
            byte[] replayData;

            using (var ms = new MemoryStream(5))
            {
                await Request.Body.CopyToAsync(ms);
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

            return await PostReplay(authenticatedPlayerID, replay, offsets, replayData, HttpContext, time, type);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplayFromCDN(string authenticatedPlayerID, string name, HttpContext context)
        {
            BlobClient blobClient = _replaysClient.GetBlobClient(name);
            MemoryStream ms = new MemoryStream(5);
            await blobClient.DownloadToAsync(ms);
            Replay? replay;
            ReplayOffsets? offsets;
            byte[]? replayData;
            try
            {
                replayData = ms.ToArray();
                (replay, offsets) = ReplayDecoder.Decode(replayData);
            }
            catch (Exception)
            {
                return BadRequest();
            }

            return await PostReplay(authenticatedPlayerID, replay, offsets, replayData, context);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplay(
            string authenticatedPlayerID, 
            Replay? replay,
            ReplayOffsets? offsets,
            byte[] replayData, 
            HttpContext context,
            float time = 0, 
            EndType type = 0)
        {
            if (replay == null) return BadRequest("It's not a replay or it has old version.");
            if (replay.notes.Count == 0 || (replay.frames.Count == 0 && type == EndType.Unknown)) return BadRequest("Replay is broken, update your mode please.");
            if (replay.info.hash.Length < 40) return BadRequest("Hash is to short");

            var version = replay.info.version.Split(".");
            if (version.Length < 3 || int.Parse(version[1]) < 3) {
                Thread.Sleep(8000); // Error may not show if returned too quick
                return BadRequest("Please update your mod. v0.3 or higher");
            }

            replay.info.playerID = authenticatedPlayerID;
            replay.info.hash = replay.info.hash.Substring(0, 40);

            if (type != EndType.Unknown && type != EndType.Clear) {

                await CollectStats(replay, replayData, null, true, time, type);
                return Ok();
            }

            var transaction = _context.Database.BeginTransaction();

            Leaderboard? leaderboard;
            using (_serverTiming.TimeAction("ldbrd"))
            {
                leaderboard = (await _leaderboardController.GetByHash(replay.info.hash, replay.info.difficulty, replay.info.mode)).Value;
                if (leaderboard == null) {
                    return NotFound("Such leaderboard not exists");
                }
            }

            (replay, Score resultScore, int maxScore) = ReplayUtils.ProcessReplay(replay, leaderboard);
            resultScore.ReplayOffsets = offsets;

            Score? currentScore;
            using (_serverTiming.TimeAction("currS"))
            {
                currentScore = leaderboard.Scores.FirstOrDefault(el => el.PlayerId == replay.info.playerID);
                if (currentScore != null && ((currentScore.Pp != 0 && currentScore.Pp >= resultScore.Pp) || (currentScore.Pp == 0 && currentScore.ModifiedScore >= resultScore.ModifiedScore)))
                {
                    await CollectStats(replay, replayData, null, true, time, EndType.Clear);
                    transaction.Commit();
                    return BadRequest("Score is lower than existing one");
                }
            }

            Player? player;
            using (_serverTiming.TimeAction("player"))
            {
                player = (await _playerController.GetLazy(replay.info.playerID)).Value;
                if (player == null) {
                    player = new Player();
                    player.Id = replay.info.playerID;
                    player.Name = replay.info.playerName;
                    player.Platform = replay.info.platform;
                    player.SetDefaultAvatar();

                    _context.Players.Add(player);
                }

                if (player.Country == "not set")
                {
                    var ip = context.Request.HttpContext.Connection.RemoteIpAddress;
                    if (ip != null)
                    {
                        player.Country = WebUtils.GetCountryByIp(ip.ToString());
                    }
                }
            }

            if (player.Banned) return BadRequest("You are banned!");
            if (resultScore.BaseScore > maxScore) return BadRequest("Score is bigger than max possible on this map!");

            ScoreImprovement improvement = new ScoreImprovement();
            List<Score> rankedScores;
            PlayerLeaderboardStats? stats = null;

            using (_serverTiming.TimeAction("score"))
            {
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                resultScore.PlayerId = replay.info.playerID;
                resultScore.Player = player;
                resultScore.Leaderboard = leaderboard;
                resultScore.Replay = "";
                resultScore.Timepost = timeset;

                if (currentScore != null)
                {
                    stats = CollectStatsFromOldScore(currentScore, leaderboard);

                    improvement.Timeset = currentScore.Timeset;
                    improvement.Score = resultScore.ModifiedScore - currentScore.ModifiedScore;
                    improvement.Accuracy = resultScore.Accuracy - currentScore.Accuracy;

                    improvement.BadCuts = resultScore.BadCuts - currentScore.BadCuts;
                    improvement.BombCuts = resultScore.BombCuts - currentScore.BombCuts;
                    improvement.MissedNotes = resultScore.MissedNotes - currentScore.MissedNotes;
                    improvement.WallsHit = resultScore.WallsHit - currentScore.WallsHit;

                    player.ScoreStats.TotalScore -= currentScore.ModifiedScore;
                    if (player.ScoreStats.TotalPlayCount == 1)
                    {
                        player.ScoreStats.AverageAccuracy = 0.0f;
                    } else
                    {
                        player.ScoreStats.AverageAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, currentScore.Accuracy);
                    }
                    
                    var status1 = leaderboard.Difficulty.Status;
                    if (status1 == DifficultyStatus.ranked)
                    {
                        float oldAverageAcc = player.ScoreStats.AverageRankedAccuracy;
                        if (player.ScoreStats.RankedPlayCount == 1)
                        {
                            player.ScoreStats.AverageRankedAccuracy = 0.0f;
                        } else
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

                    if (status1 == DifficultyStatus.ranked || status1 == DifficultyStatus.qualified || status1 == DifficultyStatus.nominated) {
                        improvement.Pp = resultScore.Pp - currentScore.Pp;
                    }
                    
                    if (currentScore.RankVoting != null)
                    {
                        resultScore.RankVoting = new RankVoting {
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

                    try
                    {
                        leaderboard.Scores.Remove(currentScore);
                    }
                    catch (Exception)
                    {
                        leaderboard.Scores = new List<Score>(leaderboard.Scores);
                        leaderboard.Scores.Remove(currentScore);
                    }

                    int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    if ((timestamp - UInt32.Parse(currentScore.Timeset)) > 60 * 60 * 24) {
                        player.ScoreStats.DailyImprovements++;
                    }
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
                } else {
                    player.ScoreStats.LastUnrankedScoreTime = timeset;
                }
                player.ScoreStats.LastScoreTime = timeset;
                leaderboard.Scores.Add(resultScore);

                _scoreStatsClient.DeleteBlobIfExistsAsync(leaderboard.Id + "-leaderboard.json");

                var status = leaderboard.Difficulty.Status;
                var isRanked = status == DifficultyStatus.ranked || status == DifficultyStatus.qualified || status == DifficultyStatus.nominated || status == DifficultyStatus.inevent;
                rankedScores = (isRanked ? leaderboard.Scores.OrderByDescending(el => el.Pp) : leaderboard.Scores.OrderByDescending(el => el.ModifiedScore)).ToList();
                foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                {
                    if (s.Rank != i + 1) {
                        s.Rank = i + 1;
                    }
                }

                if (currentScore != null) {
                    improvement.Rank = resultScore.Rank - currentScore.Rank;
                }
                
                leaderboard.Plays = rankedScores.Count;
            }


            using (_serverTiming.TimeAction("db"))
            {
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    ex.Entries.Single().Reload();
                    await _context.SaveChangesAsync();
                }

                transaction.Commit();
            }

            var transaction2 = _context.Database.BeginTransaction();
            float oldPp = player.Pp;
            int oldRank = player.Rank;
            using (_serverTiming.TimeAction("pp"))
            {
                player.ScoreStats.TotalScore += resultScore.ModifiedScore;
                player.ScoreStats.AverageAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, resultScore.Accuracy);
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, resultScore.Accuracy);
                    if (resultScore.Accuracy > player.ScoreStats.TopAccuracy) {
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

                if (currentScore != null) {
                    _context.ScoreRedirects.Add(new ScoreRedirect
                    {
                        OldScoreId = currentScore.Id,
                        NewScoreId = resultScore.Id,
                    });
                }
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    _context.RecalculatePPAndRankFast(player);
                }
            }

            context.Response.OnCompleted(async () => {
                await PostUploadAction(replay, replayData, leaderboard, player, improvement, resultScore, currentScore, rankedScores, oldPp, oldRank, context, stats, transaction2);
            });

            return RemoveLeaderboard(resultScore, resultScore.Rank);
        }

        [NonAction]
        private async Task PostUploadAction(
            Replay replay,
            byte[] replayData,
            Leaderboard leaderboard, 
            Player player, 
            ScoreImprovement improvement,
            Score resultScore,
            Score? currentScore,
            List<Score> rankedScores,
            float oldPp,
            int oldRank,
            HttpContext context,
            PlayerLeaderboardStats? stats,
            IDbContextTransaction transaction2) {
            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
            {
                _context.RecalculatePP(player);

                float resultPP = player.Pp;
                var rankedPlayers = _context.Players.Where(t => t.Pp >= oldPp && t.Pp <= resultPP && t.Id != player.Id && !t.Banned).OrderByDescending(t => t.Pp).ToList();

                if (rankedPlayers.Count() > 0)
                {

                    var country = player.Country;
                    int topRank = rankedPlayers.First().Rank; int? topCountryRank = rankedPlayers.Where(p => p.Country == country).FirstOrDefault()?.CountryRank;
                    player.Rank = topRank;
                    if (topCountryRank != null)
                    {
                        player.CountryRank = (int)topCountryRank;
                        topCountryRank++;
                    }

                    topRank++;

                    foreach ((int i, Player p) in rankedPlayers.Select((value, i) => (i, value)))
                    {
                        p.Rank = i + topRank;
                        if (p.Country == country && topCountryRank != null)
                        {
                            p.CountryRank = (int)topCountryRank;
                            topCountryRank++;
                        }
                    }
                }

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
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                ex.Entries.Single().Reload();
                await _context.SaveChangesAsync();
            }

            transaction2.Commit();

            var transaction3 = _context.Database.BeginTransaction();
            try
            {
                //if (currentScore != null && stats != null) {
                //    await MigrateOldReplay(currentScore, stats);
                //}

                await _replaysClient.CreateIfNotExistsAsync();
                string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsor";
                resultScore.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/replays/" : "https://cdn.beatleader.xyz/replays/") + fileName;
                string tempName = fileName + "temp";
                string replayLink = resultScore.Replay;
                resultScore.Replay += "temp";
                await _replaysClient.DeleteBlobIfExistsAsync(tempName);
                await _replaysClient.UploadBlobAsync(tempName, new BinaryData(replayData));
                (ScoreStatistic? statistic, string? error) = _scoreController.CalculateStatisticReplay(replay, resultScore);
                if (statistic == null)
                {
                    SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Could not recalculate score from replay. Error: " + error);
                    return;
                }
                double scoreRatio = (double)resultScore.BaseScore / (double)statistic.winTracker.totalScore;

                if (scoreRatio > 1.02 || scoreRatio < 0.98)
                {
                    SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Calculated on server score is too different: " + statistic.winTracker.totalScore + ". You probably need to update the mod.");

                    return;
                }

                if (leaderboard.Difficulty.Notes > 30)
                {
                    var sameAccScore = leaderboard
                        .Scores
                        .FirstOrDefault(s => s.PlayerId != resultScore.PlayerId && 
                                             s.AccLeft != 0 && 
                                             s.AccRight != 0 && 
                                             s.AccLeft == statistic.accuracyTracker.accLeft && 
                                             s.AccRight == statistic.accuracyTracker.accRight &&
                                             s.BaseScore == resultScore.BaseScore);
                    if (sameAccScore != null)
                    {
                        SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Acc is suspiciously exact same as: " + sameAccScore.PlayerId + "'s score");

                        return;
                    }
                }

                await _replaysClient.GetBlobClient(fileName).StartCopyFromUri(_replaysClient.GetBlobClient(tempName).Uri).WaitForCompletionAsync();
                await _replaysClient.DeleteBlobIfExistsAsync(tempName);

                resultScore.Replay = replayLink;
                resultScore.AccLeft = statistic.accuracyTracker.accLeft;
                resultScore.AccRight = statistic.accuracyTracker.accRight;
                var ip = context.Request.HttpContext.Connection.RemoteIpAddress;
                if (ip != null)
                {
                    resultScore.Country = WebUtils.GetCountryByIp(ip.ToString());
                }

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

                resultScore.ScoreImprovement = improvement;

                _context.SaveChanges();
                transaction3.Commit();

                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked && resultScore.Rank == 1)
                {
                    var dsClient = top1DSClient();

                    if (dsClient != null)
                    {
                        await _previewController.Get(scoreId: resultScore.Id);
                        var song = _context.Leaderboards.Where(lb => lb.Id == leaderboard.Id).Include(lb => lb.Song).Select(lb => lb.Song).FirstOrDefault();
                        string message = "**" + player.Name + "** has become No 1 on **" + (song != null ? song?.Name : leaderboard.Id) + "** :tada: \n";
                        message += Math.Round(resultScore.Accuracy * 100, 2) + "% " + Math.Round(resultScore.Pp, 2) + "pp (" + Math.Round(resultScore.Weight * resultScore.Pp, 2) + "pp)\n";
                        if (rankedScores.Count > 1)
                        {
                            message += "This beats previous record by **" + Math.Round(resultScore.Pp - rankedScores[1].Pp, 2) + "pp** and **" + Math.Round((resultScore.Accuracy - rankedScores[1].Accuracy) * 100, 2) + "%** ";
                            if (resultScore.Modifiers.Length > 0)
                            {
                                message += "using **" + resultScore.Modifiers + "**";
                            }
                            message += "\n";
                        }
                        message += Math.Round(improvement.TotalPp, 2) + " to the personal pp and " + improvement.TotalRank + " to rank \n";

                        dsClient.SendMessageAsync(message,
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
            //try {
            RollbackScore(score, previousScore, leaderboard);

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
            //} catch { }
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
            try
            {
                leaderboard.Scores.Remove(score);
            }
            catch (Exception)
            {
                leaderboard.Scores = new List<Score>(leaderboard.Scores);
                leaderboard.Scores.Remove(score);
            }

            if (previousScore == null) {
                if (leaderboard.Difficulty.Status == DifficultyStatus.ranked)
                {
                    player.ScoreStats.RankedPlayCount--;
                }
                player.ScoreStats.TotalPlayCount--;
            } else {
                //try
                //{
                //    leaderboard.Scores.Add(previousScore);
                //}
                //catch (Exception)
                //{
                //    leaderboard.Scores = new List<Score>(leaderboard.Scores);
                //    leaderboard.Scores.Add(previousScore);
                //}

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
            Leaderboard? withLeaderboard, 
            bool saveReplay,
            float time = 0, 
            EndType type = 0) {

            var leaderboard = withLeaderboard ?? (await _leaderboardController.GetByHash(replay.info.hash, replay.info.difficulty, replay.info.mode)).Value;
            if (leaderboard == null) return;

            if (leaderboard.PlayerStats == null)
            {
                leaderboard.PlayerStats = new List<PlayerLeaderboardStats>();
            }

            if (leaderboard.PlayerStats.Count > 0 && 
                leaderboard.PlayerStats.FirstOrDefault(s => s.PlayerId == replay.info.playerID && s.Score != replay.info.score) != null) {
                return;
            }

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var stats = new PlayerLeaderboardStats
            {
                Timeset = timeset,
                Time = time > 0 ? time : replay.frames.Last().time,
                Score = replay.info.score,
                Type = type,
                PlayerId = replay.info.playerID,
                Replay = ""
            };

            try {
                //if (saveReplay) {
                //    await _otherReplaysClient.CreateIfNotExistsAsync();
                //    string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + "-" + timeset + ".bsor";
                //    stats.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/otherreplays/" : "https://cdn.beatleader.xyz/otherreplays/") + fileName;
                //    await _otherReplaysClient.DeleteBlobIfExistsAsync(fileName);
                //    await _otherReplaysClient.UploadBlobAsync(fileName, new BinaryData(replayData));
                //}

                leaderboard.PlayerStats.Add(stats);
                _context.SaveChanges();
            } catch { }
        }

        [NonAction]
        private PlayerLeaderboardStats? CollectStatsFromOldScore(
            Score oldScore,
            Leaderboard leaderboard)
        {
            if (leaderboard.PlayerStats == null)
            {
                leaderboard.PlayerStats = new List<PlayerLeaderboardStats>();
            }

            if (leaderboard.PlayerStats.Count > 0 &&
            leaderboard.PlayerStats.FirstOrDefault(s => s.PlayerId == oldScore.PlayerId && s.Score != oldScore.BaseScore) != null)
            {
                return null;
            }

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var stats = new PlayerLeaderboardStats
            {
                Timeset = timeset,
                Time = 0,
                Score = oldScore.BaseScore,
                Type = EndType.Clear,
                PlayerId = oldScore.PlayerId,
                Replay = "",
                OldScore = oldScore
            };

            leaderboard.PlayerStats.Add(stats);
            return stats;
        }

        //[NonAction]
        //private async Task MigrateOldReplay(
        //    Score oldScore,
        //    PlayerLeaderboardStats stats)
        //{
        //    try
        //    {
        //        if (oldScore.Replay.Length > 0)
        //        {
        //            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        //            string oldFileName = oldScore.Replay.Split("/").Last();
        //            string newFileName = oldFileName.Split(".").First() + "-" + timeset + ".bsor";
        //            await _otherReplaysClient.GetBlobClient(newFileName).StartCopyFromUri(_replaysClient.GetBlobClient(oldFileName).Uri).WaitForCompletionAsync();
        //            stats.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/otherreplays/" : "https://cdn.beatleader.xyz/otherreplays/") + newFileName;
        //        }
        //    }
        //    catch { }
        //}

        [NonAction]
        public DiscordWebhookClient? top1DSClient()
        {
            var link = _configuration.GetValue<string?>("Top1DSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }
    }
}
