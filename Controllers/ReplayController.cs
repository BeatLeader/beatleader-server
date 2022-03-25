using System.Dynamic;
using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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

		public ReplayController(
            AppContext context,
            IOptions<AzureStorageConfig> config, 
            IWebHostEnvironment env, 
            SongController songController, 
            LeaderboardController leaderboardController, 
            PlayerController playerController,
            ScoreController scoreController
            )
		{
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _songController = songController;
            _scoreController = scoreController;
            _context = context;
            _environment = env;
			if (env.IsDevelopment())
			{
				_containerClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
                SetPublicContainerPermissions(_containerClient);
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
        public async Task<ActionResult<Score>> PostSteamReplay([FromQuery] string ticket)
        {
            return await PostReplay(await GetPlayerIDFromTicket(ticket));
        }

        [HttpPut("~/replayoculus"), DisableRequestSizeLimit]
        [Authorize]
        public async Task<ActionResult<Score>> PostOculusReplay()
        {
            string currentID = HttpContext.CurrentUserID();
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == Int64.Parse(currentID));
            return await PostReplay(accountLink != null ? accountLink.SteamID : currentID);
        }

        public async Task<ActionResult<Score>> PostReplay(string? authenticatedPlayerID)
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

            if (replay != null) {
                if (authenticatedPlayerID == null)
                {
                    return Unauthorized("Session ticket is not valid");
                }

                replay.info.playerID = authenticatedPlayerID;

                Song? song = (await _songController.GetHash(replay.info.hash)).Value;
                if (song == null) {
                    return NotFound("Such song id not exists");
                }
                Leaderboard? leaderboard = (await _leaderboardController.Get(song.Id + SongUtils.DiffForDiffName(replay.info.difficulty) + SongUtils.ModeForModeName(replay.info.mode))).Value;
                if (leaderboard == null) {
                    return NotFound("Such leaderboard not exists");
                }

                leaderboard = await _context.Leaderboards.Include(lb => lb.Scores).ThenInclude(score => score.Identification).Include(lb => lb.Scores).ThenInclude(score => score.Player).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);
                if (leaderboard == null)
                {
                    return NotFound("Such leaderboard not exists");
                }

                Score? currentScore = leaderboard.Scores.FirstOrDefault(el => el.PlayerId == replay.info.playerID, (Score?)null);
                if (currentScore != null && currentScore.ModifiedScore >= replay.info.score * ReplayUtils.GetTotalMultiplier(replay.info.modifiers)) {
                    return BadRequest("Score is lower than existing one");
                }
                
                Player? player = (await _playerController.GetLazy(replay.info.playerID)).Value;
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
                    var ip = Request.HttpContext.Connection.RemoteIpAddress;
                    if (ip != null)
                    {
                        player.Country = GetCountryByIp(ip.ToString());
                    }
                }
                Score? resultScore;

                if (ReplayUtils.CheckReplay(replayData, leaderboard.Scores, currentScore)) {
                    (replay, Score score) = ReplayUtils.ProcessReplay(replay, replayData, leaderboard);
                    if (leaderboard.Difficulty.Ranked && leaderboard.Difficulty.Stars != null) {
                        score.Pp = (float)score.Accuracy * (float)leaderboard.Difficulty.Stars * 44;
                    }
                    
                    score.PlayerId = replay.info.playerID;
                    score.Player = player;
                    score.Leaderboard = leaderboard;
                    resultScore = score;
                } else {
                    return Unauthorized("Another's replays posting is forbidden");
                }

                string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsor";
                try
			    {
                    resultScore.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/replays/" : "https://cdn.beatleader.xyz/replays/") + fileName;
                    
				    await _containerClient.CreateIfNotExistsAsync();

                    Stream stream = new MemoryStream();
                    ReplayEncoder.Encode(replay, new BinaryWriter(stream));
                    stream.Position = 0;

                    await _containerClient.DeleteBlobIfExistsAsync(fileName);
				    await _containerClient.UploadBlobAsync(fileName, stream);
			    }
			    catch (Exception)
			    {
				    return BadRequest("Error saving replay");
			    }

                if (currentScore != null)
                {   
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
                        if (player.ScoreStats.RankedPlayCount == 1)
                        {
                            player.ScoreStats.AverageRankedAccuracy = 0.0f;
                        } else
                        {
                            player.ScoreStats.AverageRankedAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, currentScore.Accuracy);
                        }
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
                
                player.ScoreStats.TotalScore += resultScore.ModifiedScore;
                player.ScoreStats.AverageAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, resultScore.Accuracy);
                if (leaderboard.Difficulty.Ranked)
                {
                    player.ScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, resultScore.Accuracy);
                }

                var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
                foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                    _context.Scores.Update(s);
                }
                
                leaderboard.Plays = rankedScores.Count;
                _context.Leaderboards.Update(leaderboard);

                try
                {
                    await _context.SaveChangesAsync();
                } catch (Exception e)
                {
                    return BadRequest(e.ToString());
                }

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
                    _context.Players.Update(p);
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    return BadRequest(e.ToString());
                }
                await _scoreController.CalculateStatisticReplay(replay, resultScore);
                
                resultScore.Identification = null;
                resultScore.Leaderboard = null;

                return resultScore;
            }
            else {
                return BadRequest("It's not a replay or it has old version.");
            }
        }

        [HttpPost("~/replays/migrate")]
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

            //var scores = _context.Scores.Where(s => s.PlayerId == id).ToList();
            //foreach (var score in scores)
            //{
            //    if (!score.Replay.Split("/").Last().StartsWith(id + "-"))
            //    {
            //        _context.Scores.Remove(score);
            //    }
            //}

            //_context.SaveChanges();

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

                    BlobClient blobClient = _containerClient.GetBlobClient(file.Name);
                    MemoryStream ms = new MemoryStream(5);
                    await blobClient.DownloadToAsync(ms);
                    Replay? replay = null;
                    byte[]? replayData = null;
                    try
                    {
                        replayData = ms.ToArray();
                        replay = ReplayDecoder.Decode(replayData);
                    }
                    catch (Exception)
                    {
                    }

                    if (replay != null)
                    {
                        
                        if (score == null)
                        {
                            Song? song = (await _songController.GetHash(replay.info.hash)).Value;
                            if (song == null)
                            {
                                continue;
                            }
                            Leaderboard? leaderboard = (await _leaderboardController.Get(song.Id + SongUtils.DiffForDiffName(replay.info.difficulty) + SongUtils.ModeForModeName(replay.info.mode))).Value;
                            if (leaderboard == null)
                            {
                                continue;
                            }

                            leaderboard = await _context.Leaderboards.Include(lb => lb.Scores).ThenInclude(score => score.Identification).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);
                            if (leaderboard == null)
                            {
                                continue;
                            }

                                (replay, Score newScore) = ReplayUtils.ProcessReplay(replay, replayData, leaderboard);
                                if (leaderboard.Difficulty.Ranked && leaderboard.Difficulty.Stars != null)
                                {
                                newScore.Pp = (float)newScore.Accuracy * (float)leaderboard.Difficulty.Stars * 44;
                                }

                            newScore.PlayerId = replay.info.playerID;
                            newScore.Player = player;
                            newScore.Leaderboard = leaderboard;
                            newScore.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/replays/" : "https://cdn.beatleader.xyz/replays/") + file.Name;

                            try
                            {
                                leaderboard.Scores.Add(newScore);
                            }
                            catch (Exception)
                            {
                                leaderboard.Scores = new List<Score>(leaderboard.Scores);
                                leaderboard.Scores.Add(newScore);
                            }
                            _context.Leaderboards.Update(leaderboard);
                            var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
                            foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                            {
                                s.Rank = i + 1;
                                _context.Scores.Update(s);
                            }
                            try
                            {
                                _context.SaveChanges();
                            } catch { }
                        }
                    }
                }
            }

            _context.SaveChanges();

            await _playerController.RefreshPlayer(id);

            return Ok();
        }

    [HttpGet("~/replay/check")]
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

            private static void SetPublicContainerPermissions(BlobContainerClient container)
        {
            container.SetAccessPolicy(accessType: Azure.Storage.Blobs.Models.PublicAccessType.BlobContainer);
        }

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

        public Task<string?> GetPlayerIDFromTicket(string ticket)
        {
            string url = "https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v0001?appid=620980&key=B0A7AF33E804D0ABBDE43BA9DD5DAB48&ticket=" + ticket;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse response = null;
            string? playerID = null;
            var stream =
            Task<(WebResponse, string)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    playerID = null;
                }

                return (response, playerID);
            }, request);

            return stream.ContinueWith(t => ReadStreamFromResponse(t.Result));
        }

        private string? ReadStreamFromResponse((WebResponse, string?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (!string.IsNullOrEmpty(results))
                    {

                    }
                    try
                    {
                        var info = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(results);

                        return info["response"]["params"]["steamid"];
                    } catch
                    {
                        return null;
                    }
                    
                }
            }
            else
            {
                return response.Item2;
            }

        }
    }
}
