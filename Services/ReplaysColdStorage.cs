using Amazon.S3;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Controllers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace BeatLeader_Server.Services
{
    public class ReplaysColdStorage : BackgroundService
    {
        public const string ColdStorageRoot = "/mnt/replays-box";
        public const string ColdStorageBaseUrl = "https://api.beatleader.com/replays-storage/";
        private const int MigrationConcurrency = 16;

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IAmazonS3 _s3Client;

        public ReplaysColdStorage(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            _s3Client = configuration.GetS3Client();
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                //    int hoursUntil21 = (9 - (int)DateTime.Now.Hour + 24) % 24;

                //    if (hoursUntil21 == 0) {
                Console.WriteLine("SERVICE-STARTED ReplaysColdStorage");

            try {
                        await MoveOldReplays();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION ReplaysColdStorage {e}");
                    }

            //        hoursUntil21 = (9 - (int)DateTime.Now.Hour + 24) % 24;

                    Console.WriteLine("SERVICE-DONE ReplaysColdStorage");
                //    }

                //    await Task.Delay(TimeSpan.FromHours(hoursUntil21), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task MoveOldReplays()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var scores = await _context
                    .Scores
                    .AsNoTracking()
                    .Where(s => s.AnonimusReplayWatched + s.AuthorizedReplayWatched < 2 && s.Rank > 10 && s.StorageTier == 0 && s.Pp < 1)
                    .OrderBy(s => s.Timepost)
                    .Take(1000000)
                    .Select(s => new Score { Id = s.Id, Replay = s.Replay })
                    .ToListAsync();

                var migratedFilenames = new System.Collections.Concurrent.ConcurrentBag<(S3Container container, string filename)>();
                var updatedScores = new System.Collections.Concurrent.ConcurrentBag<Score>();
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MigrationConcurrency };

                await Parallel.ForEachAsync(scores, parallelOptions, async (score, token) => {
                    if (string.IsNullOrEmpty(score.Replay)) {
                        return;
                    }

                    var filename = score.Replay.Split("/").Last();
                    if (string.IsNullOrEmpty(filename)) {
                        return;
                    }

                    if (score.Replay.StartsWith(ColdStorageBaseUrl)) {
                        score.StorageTier = 3;
                        updatedScores.Add(score);
                        return;
                    }

                    var container = score.Replay.Contains("otherreplays") ? S3Container.otherreplays : S3Container.replays;
                    var destinationPath = Path.Combine(ColdStorageRoot, filename);

                    try {
                        using (var stream = await _s3Client.DownloadStream(filename, container)) {
                            if (stream == null) {
                                // Replay missing in S3, nothing to migrate.
                                return;
                            }

                            using (var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                                await stream.CopyToAsync(fs, token);
                            }
                        }
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION ReplaysColdStorage failed to migrate {filename}: {e.Message}");
                        try { if (System.IO.File.Exists(destinationPath)) System.IO.File.Delete(destinationPath); } catch { }
                        return;
                    }

                    score.Replay = ColdStorageBaseUrl + filename;
                    score.StorageTier = 3;
                    updatedScores.Add(score);
                    migratedFilenames.Add((container, filename));
                });

                if (updatedScores.Count > 0) {
                    await _context.BulkUpdateAsync(updatedScores.ToList(), options => options.ColumnInputExpression = c => new { c.Replay, c.StorageTier });
                    await _context.BulkSaveChangesAsync();
                }

                // Only drop the S3 copies after the DB points to the cold location,
                // so a crash mid-run never leaves a score with a dangling link.
                foreach (var (container, filename) in migratedFilenames) {
                    await _s3Client.DeleteFile(filename, container);
                }
            }
        }
    }
}
