using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeatLeader_Server.Controllers
{
    public class ReplayUtilsController : Controller
    {
        private readonly BlobContainerClient _containerClient;
        private readonly BlobContainerClient _scoreStatsClient;

        AppContext _context;
        LeaderboardController _leaderboardController;
        ReplayController _replayController;
        PlayerController _playerController;
        ScoreController _scoreController;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;


        public ReplayUtilsController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env,
            IConfiguration configuration,
            LeaderboardController leaderboardController,
            PlayerController playerController,
            ScoreController scoreController,
            ReplayController replayController
            )
        {
            _leaderboardController = leaderboardController;
            _playerController = playerController;
            _scoreController = scoreController;
            _replayController = replayController;
            _context = context;
            _environment = env;
            _configuration = configuration;

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

            if (env.IsDevelopment())
            {
                _scoreStatsClient = new BlobContainerClient(config.Value.AccountName, config.Value.ScoreStatsContainerName);
            }
            else
            {

                string statsEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.ScoreStatsContainerName);

                _scoreStatsClient = new BlobContainerClient(new Uri(statsEndpoint), new DefaultAzureCredential());
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
                catch (Exception e)
                {
                    result += "\n" + replayFile + "  " + e.ToString();
                }
            }
            return Ok(result + "\nMigrated " + migrated + " out of " + _context.Scores.Count());
        }
    }
}
