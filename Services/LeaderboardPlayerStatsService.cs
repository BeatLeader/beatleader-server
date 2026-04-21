using Amazon.S3;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services {
    public class PlayerStatsJob {
        public byte[]? replayData { get; set; }
        public bool saveReplay { get; set; }
        public string fileName { get; set; }
        public string playerId { get; set; }
        public string leaderboardId { get; set; }
        public Score score { get; set; }
        public float time { get; set; }
        public float startTime { get; set; }
        public float speed { get; set; }
        public int? timeset { get; set; }
        public EndType type { get; set; }
    }

    public class OldReplayJob {
        public Score Score { get; set; }
        public string LeaderboardId { get; set; }
    }

    public class PlaycountJob {
        public int ScoreId { get; set; }
        public string PlayerId { get; set; }
        public string LeaderboardId { get; set; }
    }

    public class LeaderboardPlayerStatsService : BackgroundService {

        private static List<PlayerStatsJob> StatJobs = new List<PlayerStatsJob>();
        private static List<OldReplayJob> ReplayJobs = new List<OldReplayJob>();
        private static List<PlaycountJob> PlaycountJobs = new List<PlaycountJob>();

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public LeaderboardPlayerStatsService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("SERVICE-STARTED LeaderboardPlayerStatsService");
                try {
                    await ProcessStatsJobs();
                    await ProcessReplayJobs();
                    await ProcessPlaycountJobs();
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }

                Console.WriteLine("SERVICE-DONE LeaderboardPlayerStatsService");

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                try {
                    await ProcessStatsJobs();
                    await ProcessReplayJobs();
                    await ProcessPlaycountJobs();
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public static void AddStatsJob(PlayerStatsJob job) {
            lock (StatJobs) {
                StatJobs.Add(job);
            }
        }

        public static void AddMigrateReplayJob(OldReplayJob job) {
            lock (ReplayJobs) {
                ReplayJobs.Add(job);
            }
        }

        public static void AddRefreshPlaycountJob(PlaycountJob job) {
            lock (PlaycountJobs) {
                PlaycountJobs.Add(job);
            }
        }

        private async Task ProcessStatsJobs() {
            var jobsToProcess = new List<PlayerStatsJob>();

            lock (StatJobs) {
                var leftovers = new List<PlayerStatsJob>();
                foreach (var job in StatJobs) {
                    jobsToProcess.Add(job);
                }
                StatJobs = leftovers;
            }

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();
                var _s3Client = _configuration.GetS3Client();

                foreach (var job in jobsToProcess) {
                    var score = job.score;

                    var leaderboard = await _context.Leaderboards.Where(l => l.Id == job.leaderboardId).Include(lb => lb.Song).FirstOrDefaultAsync();
                    if (leaderboard == null) continue;

                    var playerRole = await _context.Players.Where(p => p.Id == job.playerId).Select(p => p.Role).FirstOrDefaultAsync();
                    var anySupporter = Player.RoleIsAnySupporter(playerRole ?? "");

                    leaderboard.PlayCount++;

                    int timeset = job.timeset ?? (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                    string? replayLink = null;
                    if (job.replayData == null) {
                        replayLink = job.fileName;
                    } else if (anySupporter && job.saveReplay) {
                        try {
                            string fileName = job.fileName;
                            await _s3Client.UploadOtherReplay(fileName, job.replayData);
                            replayLink = $"https://api.beatleader.com/otherreplays/{fileName}";
                        } catch {
                        }
                    }

                    var stats = new PlayerLeaderboardStats {
                        Timeset = timeset,
                        Time = (float)Math.Min(job.time, leaderboard.Song.Duration),
                        Speed = job.speed,
                        StartTime = job.startTime,
                        Score = score.BaseScore,
                        Type = job.type,
                        PlayerId = job.playerId,
                        Replay = replayLink,
                        LeaderboardId = leaderboard.Id
                    };

                    if (job.leaderboardId != null) {
                        stats.LeaderboardId = job.leaderboardId;
                    }
                    stats.FromScore(score);

                    var currentScore = await _context
                            .Scores
                            .Where(s =>
                                s.LeaderboardId == job.leaderboardId &&
                                s.PlayerId == job.playerId)
                            .FirstOrDefaultAsync();
                    if (currentScore != null) {
                        currentScore.PlayCount++;
                        currentScore.LastTryTime = score.Timepost;
                    }

                    if (float.IsNaN(stats.Accuracy) || float.IsNegativeInfinity(stats.Accuracy) || float.IsPositiveInfinity(stats.Accuracy)) {
                        stats.Accuracy = 0;
                    }
                    stats.AttemptsCount = await _storageContext.PlayerLeaderboardStats.Where(s => s.LeaderboardId == stats.LeaderboardId && s.PlayerId == stats.PlayerId).CountAsync();
                    stats.AttemptsCount++;
                    _storageContext.PlayerLeaderboardStats.Add(stats);

                    try {
                        await _storageContext.SaveChangesAsync();
                        await _context.SaveChangesAsync();
                    } catch (Exception e) {
                        Console.WriteLine($"LeaderboardPlayerStatsService EXCEPTION: {e}");
                    }
                }
            }
        }

        private async Task ProcessReplayJobs() {
            var jobsToProcess = new List<OldReplayJob>();

            lock (ReplayJobs) {
                var leftovers = new List<OldReplayJob>();
                foreach (var job in ReplayJobs) {
                    jobsToProcess.Add(job);
                }
                ReplayJobs = leftovers;
            }

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();
                var _s3Client = _configuration.GetS3Client();

                foreach (var job in jobsToProcess) {
                    var score = job.Score;
                    var leaderboardId = job.LeaderboardId;

                    var stats = await _storageContext.PlayerLeaderboardStats
                        .Where(s => s.LeaderboardId == leaderboardId && s.Score == score.BaseScore && s.PlayerId == score.PlayerId && (s.Replay == null || s.Replay == score.Replay))
                        .Include(s => s.Metadata)
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
                            await _s3Client.DeleteReplay(name);
                        }
                    } catch {}

                    if (uploaded) {
                        if (stats != null) {
                            stats.Replay = "https://api.beatleader.com/otherreplays/" + fileName;
                            if (score.Metadata != null) {
                                stats.Metadata = new ScoreMetadata {
                                    PinnedContexts = score.Metadata.PinnedContexts,
                                    HighlightedInfo = score.Metadata.HighlightedInfo,
                                    Priority = score.Metadata.Priority,
                                    Description = score.Metadata.Description,

                                    LinkService = score.Metadata.LinkService,
                                    LinkServiceIcon = score.Metadata.LinkServiceIcon,
                                    Link = score.Metadata.Link
                                };
                            }
                            _storageContext.SaveChanges();
                        } else {
                             AddStatsJob(new PlayerStatsJob {
                                fileName = "https://api.beatleader.com/otherreplays/" + fileName,
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
            }
        }

        private async Task ProcessPlaycountJobs() {
            var jobsToProcess = new List<PlaycountJob>();

            lock (PlaycountJobs) {
                var leftovers = new List<PlaycountJob>();
                foreach (var job in PlaycountJobs) {
                    jobsToProcess.Add(job);
                }
                PlaycountJobs = leftovers;
            }

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();
                var scores = new List<Score>();

                foreach (var job in jobsToProcess) {
                    var scoreToUpdate = new Score {
                        Id = job.ScoreId,
                        PlayCount = await _storageContext.PlayerLeaderboardStats.Where(st => st.PlayerId == job.PlayerId && st.LeaderboardId == job.LeaderboardId).CountAsync()
                    };
                    scores.Add(scoreToUpdate);
                }

                await _context.BulkUpdateAsync(scores, options => options.ColumnInputExpression = c => new { c.PlayCount });
                await _context.BulkSaveChangesAsync();
            }
        }
    }
}

