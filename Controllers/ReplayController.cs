using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        public async Task<ActionResult<Score>> PostReplay() //, [FromQuery] bool shared)
        {
            if (!ModelState.IsValid)
			{
				return NotFound();
			}

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
                Song? song = (await _songController.GetHash(replay.info.hash)).Value;
                if (song == null) {
                    return NotFound("Such song id not exists");
                }
                Leaderboard? leaderboard = (await _leaderboardController.Get(song.Id + SongUtils.DiffForDiffName(replay.info.difficulty) + SongUtils.ModeForModeName(replay.info.mode))).Value;
                if (leaderboard == null) {
                    return NotFound("Such leaderboard not exists");
                }

                leaderboard = await _context.Leaderboards.Include(lb => lb.Scores).ThenInclude(score => score.Identification).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);

                Score? currentScore = leaderboard.Scores.FirstOrDefault(el => el.PlayerId == replay.info.playerID, (Score?)null);
                if (currentScore != null && currentScore.ModifiedScore > replay.info.score) {
                    return BadRequest("Score is lower than existing one");
                }
                Player? player = (await _playerController.Get(replay.info.playerID)).Value;
                if (player == null) {
                    return NotFound("Such player not exists");
                }
                Score? resultScore;

                if (ReplayUtils.CheckReplay(replayData, leaderboard.Scores)) {
                    (replay, Score score) = ReplayUtils.ProcessReplay(replay, replayData);
                    if (leaderboard.Difficulty.Ranked) {
                        score.Pp = (float)score.ModifiedScore / ((float)score.BaseScore / score.Accuracy) * (float)leaderboard.Difficulty.Stars * 50;
                        player.Pp += score.Pp;
                    }
                    
                    score.Rank = leaderboard.Scores.OrderBy(el => el.ModifiedScore).ToList().IndexOf(score) + 1;
                    score.PlayerId = replay.info.playerID;
                    score.player = player;
                    resultScore = score;
                } else {
                    return Unauthorized("Another's replays posting is forbidden");
                }

                string fileName = replay.info.playerID + (replay.info.speed != 0 ? "-practice" : "") + (replay.info.failTime != 0 ? "-fail" : "") + "-" + replay.info.difficulty + "-" + replay.info.mode + "-" + replay.info.hash + ".bsor";
                try
			    {
                    resultScore.Replay = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/replays/" : "https://www.cdn.beatleader.xyz/replays/") + fileName;
                    
				    await _containerClient.CreateIfNotExistsAsync();

                    Stream stream = new MemoryStream();
                    ReplayEncoder.Encode(replay, new BinaryWriter(stream));
                    stream.Position = 0;

				    await _containerClient.UploadBlobAsync(fileName, stream);


                    leaderboard.Scores.Add(resultScore);
                    await _context.SaveChangesAsync();
                    resultScore.Identification = null;

                    return resultScore;
			    }
			    catch (Exception)
			    {
				    return BadRequest("Error saving replay");
			    }
            }
            else {
                return BadRequest("It's not a replay or it has old version.");
            }

			
        }
    }
}
