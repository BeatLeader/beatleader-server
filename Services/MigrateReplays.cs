using Amazon.S3;
using Azure.Storage.Blobs;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace BeatLeader_Server.Services
{
    public class MigrateReplays : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private static int lastScoreCount = 0;
        private readonly int concurrencyCount = 20;

        public MigrateReplays(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    int newCount = _context.Scores.Where(s => !s.Migrated).Count();
                    if (newCount == lastScoreCount) {
                        await Task.Delay(TimeSpan.FromHours(1000), stoppingToken);
                    }
                    lastScoreCount = newCount;
                }
                var list = new List<int>();
                for (int i = 0; i < concurrencyCount; i++)
                {
                    list.Add(i);
                }

                var bag = new ConcurrentBag<object>();
                await list.ParallelForEachAsync(async worker =>
                {
                    await Migrate(worker);
                }, maxDegreeOfParallelism: concurrencyCount);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task MigrateScore(
            Score score, 
            AppContext _context,
            BlobContainerClient _replaysClient,
            BlobContainerClient _compactReplaysClient,
            IAmazonS3 _s3Client
            )
        {
            Replay? replay = null;
            ReplayOffsets? offsets = null;

            MemoryStream? memoryStream = null;
            string fileName = score.Replay.Split("/").Last();
            if (score.Replay.Contains("cdn.replay")) {
                using (var replayStream = await _s3Client.DownloadReplay(fileName))
                {
                    if (replayStream == null) return;

                    using (var ms = new MemoryStream(5))
                    {
                        await replayStream.CopyToAsync(ms);
                        long length = ms.Length;
                        try
                        {
                            (replay, offsets) = ReplayDecoder.Decode(ms.ToArray());
                        }
                        catch (Exception)
                        {
                            return;
                        }
                    }
                } 

            } else {
                BlobClient blobClient = _replaysClient.GetBlobClient(fileName);
                if (!await blobClient.ExistsAsync()) return;

                memoryStream = new MemoryStream(5);
                await blobClient.DownloadToAsync(memoryStream);
                try
                {
                    (replay, offsets) = ReplayDecoder.Decode(memoryStream.ToArray());
                }
                catch (Exception)
                {
                    return;
                }
            }

            if (replay == null) {
                return;
            }

            if (score.Leaderboard.Difficulty.Notes != 0 && replay.notes.Count > score.Leaderboard.Difficulty.Notes) {
                string? error2 = ReplayUtils.RemoveDuplicates(replay, score.Leaderboard);
                if (error2 != null) {
                    return;
                }
            }

            if (memoryStream != null) {
                memoryStream.Position = 0;

                score.Replay = "https://cdn.replays.beatleader.xyz/" + fileName;
                await _s3Client.UploadStream(fileName, "replays", memoryStream);
            }

            if (score.ReplayOffsets == null && offsets != null) {
                score.ReplayOffsets = offsets;
            }

            (var statistic, string? error) = ReplayStatisticUtils.ProcessReplay(replay, score.Leaderboard);
            if (statistic != null) {

                score.AccLeft = statistic.accuracyTracker.accLeft;
                score.AccRight = statistic.accuracyTracker.accRight;
                score.MaxCombo = statistic.hitTracker.maxCombo;
                score.FcAccuracy = statistic.accuracyTracker.fcAcc;
                score.MaxStreak = statistic.hitTracker.maxStreak;

                score.LeftTiming = statistic.hitTracker.leftTiming;
                score.RightTiming = statistic.hitTracker.rightTiming;

                if (score.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) {
                    score.FcPp = ReplayUtils.PpFromScore(
                        score.FcAccuracy, 
                        score.Modifiers, 
                        score.Leaderboard.Difficulty.ModifierValues, 
                        score.Leaderboard.Difficulty.Stars ?? 0, score.Leaderboard.Difficulty.ModeName.ToLower() == "rhythmgamestandard").Item1;
                }

                await _s3Client.UploadScoreStats(score.Id + ".json", statistic);
            } else {
                score.Suspicious = true;
            }

            if (score.Hmd == HMD.unknown) {
                score.Hmd = ReplayUtils.HMDFromName(replay.info.hmd);

                if (score.Hmd == HMD.unknown && _context.Headsets.FirstOrDefault(h => h.Name == replay.info.hmd) == null) {
                    _context.Headsets.Add(new Headset {
                        Name = replay.info.hmd,
                        Player = replay.info.playerID,
                    });
                }
            }

            if (score.Controller == ControllerEnum.unknown) {
                score.Controller = ReplayUtils.ControllerFromName(replay.info.controller);

                if (score.Controller == ControllerEnum.unknown && _context.VRControllers.FirstOrDefault(h => h.Name == replay.info.controller) == null) {
                    _context.VRControllers.Add(new VRController {
                        Name = replay.info.controller,
                        Player = replay.info.playerID,
                    });
                }
            }

            replay.frames = new List<Frame>();
            Stream stream = new MemoryStream();
            ReplayEncoder.Encode(replay, new BinaryWriter(stream));
            stream.Position = 0;

            _compactReplaysClient.DeleteBlobIfExists(fileName);
            await _compactReplaysClient.UploadBlobAsync(fileName, stream);

            score.Migrated = true;
            await _context.SaveChangesAsync();
        }

        public async Task Migrate(int worker)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                IOptions<AzureStorageConfig> config = scope.ServiceProvider.GetRequiredService<IOptions<AzureStorageConfig>>();
                IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                var _replaysClient = new BlobContainerClient(config.Value.AccountName, config.Value.ReplaysContainerName);
                var _compactReplaysClient = new BlobContainerClient(config.Value.AccountName, "compactreplays");
                var _s3Client = configuration.GetS3Client();

                int index = 0;
                Score? score = null;
                do
                {
                    score = _context
                        .Scores
                        .Where(s => !s.Migrated)
                        .OrderBy(s => s.Id)
                        .Skip(index * concurrencyCount + worker)
                        .Take(1)
                        .Include(s => s.ReplayOffsets)
                        .Include(s => s.Leaderboard)
                        .ThenInclude(lb => lb.Difficulty)
                        .ThenInclude(d => d.ModifierValues)
                        .FirstOrDefault();
                    if (score != null) {

                        await MigrateScore(
                            score, 
                            _context,
                            _replaysClient,
                            _compactReplaysClient,
                            _s3Client);
                        index++;
                    }

                } while (score != null);
            }
        }
    }
}
