using System.Dynamic;
using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
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
        IWebHostEnvironment _environment;

		public ReplayController(
            AppContext context,
            IOptions<AzureStorageConfig> config, 
            IWebHostEnvironment env, 
            SongController songController, 
            LeaderboardController leaderboardController, 
            PlayerController playerController
            )
		{
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _songController = songController;
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
        public async Task<ActionResult<Score>> PostReplay([FromQuery] string ticket)
        {
            string? authenticatedPlayerID = await GetPlayerIDFromTicket(ticket);

            Replay replay;
            byte[] replayData;

            using (var ms = new MemoryStream(5))
            {
                await Request.Body.CopyToAsync(ms);
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
                if (authenticatedPlayerID == null || authenticatedPlayerID != replay.info.playerID)
                {
                    return Unauthorized("Session ticket is not valid");
                }

                Song? song = (await _songController.GetHash(replay.info.hash)).Value;
                if (song == null) {
                    return NotFound("Such song id not exists");
                }
                Leaderboard? leaderboard = (await _leaderboardController.Get(song.Id + SongUtils.DiffForDiffName(replay.info.difficulty) + SongUtils.ModeForModeName(replay.info.mode))).Value;
                if (leaderboard == null) {
                    return NotFound("Such leaderboard not exists");
                }

                leaderboard = await _context.Leaderboards.Include(lb => lb.Scores).ThenInclude(score => score.Identification).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);
                if (leaderboard == null)
                {
                    return NotFound("Such leaderboard not exists");
                }

                Score? currentScore = leaderboard.Scores.FirstOrDefault(el => el.PlayerId == replay.info.playerID, (Score?)null);
                if (currentScore != null && currentScore.ModifiedScore >= replay.info.score) {
                    return BadRequest("Score is lower than existing one");
                }
                Player? player = (await _playerController.Get(replay.info.playerID)).Value;
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

                if (ReplayUtils.CheckReplay(replayData, leaderboard.Scores)) {
                    (replay, Score score) = ReplayUtils.ProcessReplay(replay, replayData);
                    if (leaderboard.Difficulty.Ranked && leaderboard.Difficulty.Stars != null) {
                        score.Pp = (float)score.ModifiedScore / ((float)score.BaseScore / score.Accuracy) * (float)leaderboard.Difficulty.Stars * 44;
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
                    resultScore.Id = currentScore.Id;
                    _context.Scores.Update(resultScore);
                    player.ScoreStats.TotalScore -= currentScore.ModifiedScore;
                    player.ScoreStats.AverageAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageAccuracy, player.ScoreStats.TotalPlayCount, currentScore.Accuracy);
                    if (leaderboard.Difficulty.Ranked)
                    {
                        player.ScoreStats.AverageRankedAccuracy = MathUtils.RemoveFromAverage(player.ScoreStats.AverageRankedAccuracy, player.ScoreStats.RankedPlayCount, currentScore.Accuracy);
                    }
                }
                else
                {
                    if (leaderboard.Difficulty.Ranked)
                    {
                        player.ScoreStats.RankedPlayCount++;
                    }
                    player.ScoreStats.TotalPlayCount++;

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

                await _context.SaveChangesAsync();

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

                leaderboard.Plays = rankedScores.Count;
                _context.Leaderboards.Update(leaderboard);

                await _context.SaveChangesAsync();
                resultScore.Identification = null;

                return resultScore;
            }
            else {
                return BadRequest("It's not a replay or it has old version.");
            }
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
