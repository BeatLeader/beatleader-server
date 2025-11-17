using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services
{

    public class MigrationJob {
        public string PlayerId { get; set; }
        public List<string> Leaderboards { get; set; }
        public int Throttle { get; set; } = 0;
    }

    public class HistoryMigrationJob {
        public string FromPlayerId { get; set; }
        public string ToPlayerId { get; set; }
        public int Throttle { get; set; } = 0;
    }

    public class RefreshTaskService : BackgroundService {
        private static List<MigrationJob> jobs = new List<MigrationJob>();
        private static List<HistoryMigrationJob> historyJobs = new List<HistoryMigrationJob>();

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public RefreshTaskService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public static void AddJob(MigrationJob newJob) {
            lock (jobs) {
                jobs.Add(newJob);
            }
        }

        public static void AddHistoryJob(HistoryMigrationJob newJob) {
            lock (jobs) {
                historyJobs.Add(newJob);
            }
        }

        public static void ExtendJob(MigrationJob newJob) {
            lock (jobs) {
                var sheduled = jobs.FirstOrDefault(j => j.PlayerId == newJob.PlayerId);
                if (sheduled != null) {
                    sheduled.Leaderboards.AddRange(newJob.Leaderboards);
                } else {
                    jobs.Add(newJob);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("SERVICE-STARTED RefreshTaskService");
                try {
                    await ProcessJobs();
                    await ProcessHistoryJobs();
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }

                Console.WriteLine("SERVICE-DONE RefreshTaskService");

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                try {
                    await ProcessJobs();
                    await ProcessHistoryJobs();
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task ProcessJobs() {
            var jobsToProcess = new List<MigrationJob>();

            lock (jobs) {
                var leftovers = new List<MigrationJob>();
                foreach (var job in jobs) {
                    if (job.Throttle == 0) {
                        jobsToProcess.Add(job);
                    } else {
                        job.Throttle--;
                        leftovers.Add(job);
                    }
                }
                jobs = leftovers;
            }

            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                var playerContextController = scope.ServiceProvider.GetRequiredService<PlayerContextRefreshController>();
                var scoreController = scope.ServiceProvider.GetRequiredService<ScoreRefreshController>();

                foreach (var job in jobsToProcess) {
                    await playerController.RefreshPlayerAction(job.PlayerId, false);
                    await playerContextController.RefreshPlayerAllContexts(job.PlayerId, false);

                    try {
                        await _context.BulkSaveChangesAsync();
                    } catch { }

                    foreach (var id in job.Leaderboards) {
                        await scoreController.BulkRefreshScores(id);
                        await scoreController.BulkRefreshScoresAllContexts(id);
                    }
                }

                try {
                    await _context.BulkSaveChangesAsync();
                } catch { }
            }
        }

        private async Task ProcessHistoryJobs() {
            var jobsToProcess = new List<HistoryMigrationJob>();

            lock (jobs) {
                var leftovers = new List<HistoryMigrationJob>();
                foreach (var job in historyJobs) {
                    if (job.Throttle == 0) {
                        jobsToProcess.Add(job);
                    } else {
                        job.Throttle--;
                        leftovers.Add(job);
                    }
                }
                historyJobs = leftovers;
            }

            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();

                foreach (var job in jobsToProcess) {
                    try {
                        var stats = (await _storageContext
                            .PlayerLeaderboardStats
                            .Where(s => s.PlayerId == job.FromPlayerId)
                            .Select(s => s.Id)
                            .ToListAsync())
                            .Select(s => new PlayerLeaderboardStats {
                                Id = s,
                                PlayerId = job.ToPlayerId
                            })
                            .ToList();
                        await _storageContext.BulkUpdateAsync(stats, options => options.ColumnInputExpression = c => new { c.PlayerId });
                    } catch { }

                    var toHistory = await _storageContext.PlayerScoreStatsHistory.Where(sh => sh.PlayerId == job.ToPlayerId).ToListAsync();
                    var fromHistory = await _storageContext.PlayerScoreStatsHistory.Where(sh => sh.PlayerId == job.FromPlayerId).ToListAsync();

                    if (fromHistory.Count > toHistory.Count && fromHistory.OrderByDescending(h => h.Pp).First().Pp > 0) {
                        foreach (var item in fromHistory) {
                            item.PlayerId = job.ToPlayerId;
                        }
                        foreach (var item in toHistory) {
                            _storageContext.PlayerScoreStatsHistory.Remove(item);
                        }
                    } else {
                        foreach (var item in fromHistory) {
                            _storageContext.PlayerScoreStatsHistory.Remove(item);
                        }
                    }

                    var currentPlayer = await _context.Players.Where(p => p.Id == job.FromPlayerId).FirstOrDefaultAsync();
                    if (currentPlayer != null) {
                        PlayerSearchService.RemovePlayer(currentPlayer);
                        _context.Players.Remove(currentPlayer);
                    }

                    try {
                        await _context.BulkSaveChangesAsync();
                        await _storageContext.BulkSaveChangesAsync();
                    } catch { }
                }
            }
        }
    }
}
