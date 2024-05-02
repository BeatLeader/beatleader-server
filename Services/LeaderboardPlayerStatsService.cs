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
        public int? timeset { get; set; }
        public EndType type { get; set; }
    }

    public class LeaderboardPlayerStatsService : BackgroundService {
        private static List<PlayerStatsJob> jobs = new List<PlayerStatsJob>();
        private static readonly object _lock = new object();

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public LeaderboardPlayerStatsService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public static void AddJob(PlayerStatsJob newJob) {
            lock (_lock) {
                jobs.Add(newJob);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("SERVICE-STARTED LeaderboardPlayerStatsService");
                try {
                    await ProcessJobs();
                } catch (Exception e)
                {
                    Console.WriteLine($"LeaderboardPlayerStatsService EXCEPTION: {e}");
                }

                Console.WriteLine("SERVICE-DONE LeaderboardPlayerStatsService");

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task ProcessJobs() {
            var jobsToProcess = new List<PlayerStatsJob>();

            lock (_lock) {
                foreach (var job in jobs) {
                    jobsToProcess.Add(job);
                }
                jobs = new List<PlayerStatsJob>();
            }

            using (var scope = _serviceScopeFactory.CreateScope())
            using (var _context = scope.ServiceProvider.GetRequiredService<AppContext>()) {
                var _s3Client = _configuration.GetS3Client();
                foreach (var job in jobsToProcess) {
                    var score = job.score;

                    var leaderboard = await _context.Leaderboards.Where(l => l.Id == job.leaderboardId).Include(lb => lb.PlayerStats).FirstOrDefaultAsync();
                    var playerRole = await _context.Players.Where(p => p.Id == job.playerId).Select(p => p.Role).FirstOrDefaultAsync();
                    var anySupporter = Player.RoleIsAnySupporter(playerRole);

                    if (leaderboard.PlayerStats == null) {
                        leaderboard.PlayerStats = new List<PlayerLeaderboardStats>();
                    }

                    leaderboard.PlayCount++;

                    int timeset = job.timeset ?? (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                    string? replayLink = null;
                    if (job.replayData == null) {
                        replayLink = job.fileName;
                    } else if (anySupporter && job.saveReplay) {
                        try {
                            string fileName = job.fileName;
                            await _s3Client.UploadOtherReplay(fileName, job.replayData);
                            replayLink = $"https://api.beatleader.xyz/otherreplays/{fileName}";
                        } catch {
                        }
                    }

                    var stats = new PlayerLeaderboardStats {
                        Timeset = timeset,
                        Time = job.time,
                        Score = score.BaseScore,
                        Type = job.type,
                        PlayerId = job.playerId,
                        Replay = replayLink
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
                    }

                    leaderboard.PlayerStats.Add(stats);

                    try {
                        await _context.SaveChangesAsync();
                    } catch (Exception e) {
                        Console.WriteLine($"LeaderboardPlayerStatsService EXCEPTION: {e}");
                        _context.RejectChanges();
                    }
                }
            }
        }
    }
}

