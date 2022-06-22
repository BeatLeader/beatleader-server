using System.Dynamic;
using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ReplayController : Controller
    {
        BlobContainerClient _containerClient;
        AppContext _context;
        LeaderboardController _leaderboardController;
        PlayerController _playerController;
        SongController _songController;
        ScoreController _scoreController;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;
        private readonly IServerTiming _serverTiming;


        public ReplayController(
            AppContext context,
            IOptions<AzureStorageConfig> config, 
            IWebHostEnvironment env,
            IConfiguration configuration,
            SongController songController, 
            LeaderboardController leaderboardController, 
            PlayerController playerController,
            ScoreController scoreController,
            IServerTiming serverTiming
            )
		{
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _songController = songController;
            _scoreController = scoreController;
            _context = context;
            _environment = env;
            _configuration = configuration;
            _serverTiming = serverTiming;

            if (env.IsDevelopment())
			{
				_containerClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
                _containerClient.SetPublicContainerPermissions();
            }
			else
			{
				string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
														config.Value.AccountName,
													   config.Value.ReplaysContainerName);

				_containerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
			}
		}

        [HttpPost("~/replay"), DisableRequestSizeLimit]
        public async Task<ActionResult<ScoreResponse>> PostSteamReplay([FromQuery] string ticket)
        {
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(ticket, _configuration);
            if (id == null && error != null) {
                return Unauthorized(error);
            }
            return await PostReplayFromBody(id);
        }

        [HttpPut("~/replayoculus"), DisableRequestSizeLimit]
        [Authorize]
        public async Task<ActionResult<ScoreResponse>> PostOculusReplay()
        {
            string currentID = HttpContext.CurrentUserID();
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == Int64.Parse(currentID));
            return await PostReplayFromBody(accountLink != null ? accountLink.SteamID : currentID);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplayFromBody(string? authenticatedPlayerID)
        {
            Replay replay;
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
                    replay = ReplayDecoder.Decode(replayData);
                }
                catch (Exception)
                {
                    return BadRequest("Error decoding replay");
                }
            }

            return await PostReplay(authenticatedPlayerID, replay, replayData, HttpContext);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplayFromCDN(string? authenticatedPlayerID, string name, HttpContext context)
        {
            BlobClient blobClient = _containerClient.GetBlobClient(name);
            MemoryStream ms = new MemoryStream(5);
            await blobClient.DownloadToAsync(ms);
            Replay? replay;
            byte[]? replayData;
            try
            {
                replayData = ms.ToArray();
                replay = ReplayDecoder.Decode(replayData);
            }
            catch (Exception)
            {
                return BadRequest();
            }

            return await PostReplay(authenticatedPlayerID, replay, replayData, context);
        }

        [NonAction]
        public async Task<ActionResult<ScoreResponse>> PostReplay(string? authenticatedPlayerID, Replay? replay, byte[] replayData, HttpContext context)
        {
            if (replay == null) {
                return BadRequest("It's not a replay or it has old version.");
            }
            if (authenticatedPlayerID == null)
            {
                return Unauthorized("Session ticket is not valid");
            }

            replay.info.playerID = authenticatedPlayerID;

            if (replay.info.hash.Length < 40)
            {
                return BadRequest("Hash is to short");
            }
            else
            {
                replay.info.hash = replay.info.hash.Substring(0, 40);
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

            FailedScore? failedScore;
            using (_serverTiming.TimeAction("failS"))
            {
                failedScore = _context.FailedScores.Include(s => s.Leaderboard).FirstOrDefault(s => s.PlayerId == authenticatedPlayerID && s.Leaderboard.Id == leaderboard.Id);
                if (failedScore != null) {
                    _context.FailedScores.Remove(failedScore);
                    _context.SaveChanges();
                }
            }

            Score? currentScore;
            using (_serverTiming.TimeAction("currS"))
            {
                currentScore = leaderboard.Scores.FirstOrDefault(el => el.PlayerId == replay.info.playerID);
                if (currentScore != null && currentScore.ModifiedScore >= (int)(((float)replay.info.score) * ReplayUtils.GetTotalMultiplier(replay.info.modifiers)))
                {
                    transaction.Commit();
                    return BadRequest("Score is lower than existing one");
                }
            }

            Player? player;

            using (_serverTiming.TimeAction("player"))
            {
                player = currentScore?.Player ?? (await _playerController.GetLazy(replay.info.playerID)).Value;
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
                        player.Country = GetCountryByIp(ip.ToString());
                    }
                }
            }

            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }
            (replay, Score resultScore) = ReplayUtils.ProcessReplay(replay, leaderboard);

            ScoreImprovement improvement = new ScoreImprovement();

            using (_serverTiming.TimeAction("score"))
            {
                if (leaderboard.Difficulty.Ranked && leaderboard.Difficulty.Stars != null) {
                    resultScore.Pp = (float)resultScore.Accuracy * (float)leaderboard.Difficulty.Stars * 44;
                }

                resultScore.PlayerId = replay.info.playerID;
                resultScore.Player = player;
                resultScore.Leaderboard = leaderboard;
                resultScore.Replay = "";
                resultScore.Timepost = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                if (currentScore != null)
                {
                    improvement.Score = resultScore.ModifiedScore - currentScore.ModifiedScore;
                    improvement.Accuracy = resultScore.Accuracy - currentScore.Accuracy;

                    player.ScoreStats.TotalScore -= currentScore.ModifiedScore;
                    if (player.ScoreStats.TotalPlayCount == 1)
                    {
                        player.ScoreStats.AverageAccuracy = 0.0f;
                    } else
                    {
                        player.ScoreStats.AverageAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, currentScore.Accuracy);
                    }
                    
                    if (leaderboard.Difficulty.Ranked)
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
                        improvement.Pp = resultScore.Pp - currentScore.Pp;
                    }

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
                    if (leaderboard.Difficulty.Ranked)
                    {
                        player.ScoreStats.RankedPlayCount++;
                    }
                    player.ScoreStats.TotalPlayCount++;
                }
                try
                {
                    leaderboard.Scores.Add(resultScore);
                } catch (Exception)
                {
                    leaderboard.Scores = new List<Score>(leaderboard.Scores);
                    leaderboard.Scores.Add(resultScore);
                }

                var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
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
                if (leaderboard.Difficulty.Ranked)
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, resultScore.Accuracy);
                    if (resultScore.Accuracy > player.ScoreStats.TopAccuracy) {
                        player.ScoreStats.TopAccuracy = resultScore.Accuracy;
                    }
                    if (resultScore.Pp > player.ScoreStats.TopPp)
                    {
                        player.ScoreStats.TopPp = resultScore.Pp;
                    }
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

                _context.RecalculatePPAndRankFast(player);
            }

            context.Response.OnCompleted(async () => {

                _context.RecalculatePP(player);
                float resultPP = player.Pp;
                var rankedPlayers = _context.Players.Where(t => t.Pp >= oldPp && t.Pp <= resultPP && t.Id != player.Id && !t.Banned).OrderByDescending(t => t.Pp).ToList();

                if (rankedPlayers.Count() > 0) {

                    var country = player.Country;
                    int topRank = rankedPlayers.First().Rank; int? topCountryRank = rankedPlayers.Where(p => p.Country == country).FirstOrDefault()?.CountryRank;
                    player.Rank = topRank;
                    if (topCountryRank != null) {
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
                    await _containerClient.CreateIfNotExistsAsync();

                    Stream stream = new MemoryStream();
                    ReplayEncoder.Encode(replay, new BinaryWriter(stream));
                    stream.Position = 0;

                    string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsor";
                    resultScore.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/replays/" : "https://cdn.beatleader.xyz/replays/") + fileName;

                    string tempName = fileName + "temp";
                    string replayLink = resultScore.Replay;
                    resultScore.Replay += "temp";

                    await _containerClient.DeleteBlobIfExistsAsync(tempName);
                    await _containerClient.UploadBlobAsync(tempName, stream);

                    (ScoreStatistic? statistic, string? error) = _scoreController.CalculateStatisticReplay(replay, resultScore);
                    if (statistic == null) {
                        SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Could not recalculate score from replay. Error: " + error);

                        return;
                    }

                    double scoreRatio = (double)resultScore.BaseScore / (double)statistic.WinTracker.TotalScore;

                    if (scoreRatio > 1.03 || scoreRatio < 0.97) {
                        SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, "Calculated on server score is too different: " + statistic.WinTracker.TotalScore + ". You probably need to update the mod.");

                        return;
                    }

                    await _containerClient.GetBlobClient(fileName).StartCopyFromUri(_containerClient.GetBlobClient(tempName).Uri).WaitForCompletionAsync();
                    await _containerClient.DeleteBlobIfExistsAsync(tempName);

                    resultScore.Replay = replayLink;
                    resultScore.AccLeft = statistic.AccuracyTracker.AccLeft;
                    resultScore.AccRight = statistic.AccuracyTracker.AccRight;

                    if (currentScore != null) {
                        improvement.AccLeft = resultScore.AccLeft - currentScore.AccLeft;
                        improvement.AccRight = resultScore.AccRight - currentScore.AccRight;
                    }

                    resultScore.ScoreImprovement = improvement;

                    _context.SaveChanges();
                    transaction3.Commit();
                }
                catch (Exception e)
                {
                    SaveFailedScore(transaction3, currentScore, resultScore, leaderboard, e.ToString());
                }
            });

            return RemoveLeaderboard(resultScore, resultScore.Rank);
        }

        [NonAction]
        private void SaveFailedScore(IDbContextTransaction transaction, Score? previousScore, Score score, Leaderboard leaderboard, string failReason) {
            try {
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

            if (leaderboard.Difficulty.Ranked)
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
                if (leaderboard.Difficulty.Ranked)
                {
                    player.ScoreStats.RankedPlayCount--;
                }
                player.ScoreStats.TotalPlayCount--;
            } else {
                try
                {
                    leaderboard.Scores.Add(previousScore);
                }
                catch (Exception)
                {
                    leaderboard.Scores = new List<Score>(leaderboard.Scores);
                    leaderboard.Scores.Add(previousScore);
                }

                player.ScoreStats.TotalScore += previousScore.ModifiedScore;
                player.ScoreStats.AverageAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, previousScore.Accuracy);
                if (leaderboard.Difficulty.Ranked)
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, previousScore.Accuracy);
                }
            }

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

            _context.SaveChanges();
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
        }

        [NonAction]
        public async Task<ActionResult> MigrateReplays()
        {
            var scores = _context.Scores.ToList();
            int migrated = 0;
            var result = "";
            foreach (var score in scores)
            {
                string replayFile = score.Replay;

                var net = new System.Net.WebClient();
                var data = net.DownloadData(replayFile);
                var readStream = new System.IO.MemoryStream(data);

                int arrayLength = (int)readStream.Length;
                byte[] buffer = new byte[arrayLength];
                readStream.Read(buffer, 0, arrayLength);

                try
                {
                    Models.Old.Replay replay = Models.Old.ReplayDecoder.Decode(buffer);

                    migrated++;

                    Stream stream = new MemoryStream();
                    Models.Old.ReplayEncoder.Encode(replay, new BinaryWriter(stream));
                    stream.Position = 0;

                    string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsor";

                    await _containerClient.DeleteBlobIfExistsAsync(fileName);
                    await _containerClient.UploadBlobAsync(fileName, stream);

                    
                }
                catch (Exception e) {
                    result += "\n" + replayFile + "  " + e.ToString();
                }
            }
            return Ok(result + "\nMigrated " + migrated + " out of " + _context.Scores.Count());
        }

        [HttpGet("~/player/{id}/restore")]
        [Authorize]
        public async Task<ActionResult> RestorePlayer(string id) {

            Player? player = _context.Players.Find(id);
            if (player == null)
            {
                return NotFound();
            }

            var resultSegment = _containerClient.GetBlobsAsync();
            await foreach (var file in resultSegment)
            {
                var parsedName = file.Name.Split("-");
                if (parsedName.Length == 4 && parsedName[0] == id)
                {
                    string playerId = parsedName[0];
                    string difficultyName = parsedName[1];
                    string modeName = parsedName[2];
                    string hash = parsedName[3].Split(".").First();

                    Score? score = _context
                            .Scores
                            .Include(s => s.Leaderboard)
                            .ThenInclude(l => l.Song)
                            .Include(s => s.Leaderboard)
                            .ThenInclude(l => l.Difficulty)
                            .FirstOrDefault(s => s.PlayerId == playerId
                            && s.Leaderboard.Song.Hash == hash
                            && s.Leaderboard.Difficulty.DifficultyName == difficultyName
                            && s.Leaderboard.Difficulty.ModeName == modeName);

                    if (score != null) { continue; }

                    await PostReplayFromCDN(playerId, file.Name, HttpContext);
                }
            }

            return Ok();
        }

        [NonAction]
        public async Task<ActionResult> CheckReplay([FromQuery] string link)
        {
            var net = new System.Net.WebClient();
            var data = net.DownloadData(link);
            var readStream = new System.IO.MemoryStream(data);

            int arrayLength = (int)readStream.Length;
            byte[] buffer = new byte[arrayLength];
            readStream.Read(buffer, 0, arrayLength);
            try
            {
                Models.Old.Replay replay = Models.Old.ReplayDecoder.Decode(buffer);
            }
            catch {
                arrayLength = 10;
            }
            return Ok();
        }

        [NonAction]
        private static string GetCountryByIp(string ip)
        {
            string result = "not set";
            try
            {
                string jsonResult = new WebClient().DownloadString("http://ipinfo.io/" + ip);
                dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(jsonResult, new ExpandoObjectConverter());
                if (info != null)
                {
                    result = info.country;
                }
            }
            catch (Exception)
            {
            }

            return result;
        }
    }
}
