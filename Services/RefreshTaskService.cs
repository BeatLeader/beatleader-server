using BeatLeader_Server.Controllers;

namespace BeatLeader_Server.Services
{

    public class MigrationJob {
        public string PlayerId { get; set; }
        public List<string> Leaderboards { get; set; }
    }

    public class RefreshTaskService : BackgroundService {
        private static List<MigrationJob> jobs = new List<MigrationJob>();

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("STARTED RefreshTaskService");
                try {
                    await ProcessJobs();
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }

                Console.WriteLine("DONE RefreshTaskService");

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                await ProcessJobs();
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task ProcessJobs() {
            var jobsToProcess = new List<MigrationJob>();

            lock (jobs) {
                foreach (var job in jobs) {
                    jobsToProcess.Add(job);
                }
                jobs = new List<MigrationJob>();
            }

            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                var playerContextController = scope.ServiceProvider.GetRequiredService<PlayerContextRefreshController>();
                var scoreController = scope.ServiceProvider.GetRequiredService<ScoreRefreshController>();

                foreach (var job in jobsToProcess) {
                    await playerController.RefreshPlayerAction(job.PlayerId);
                    await playerContextController.RefreshPlayerAllContexts(job.PlayerId);

                    foreach (var id in job.Leaderboards) {
                        await scoreController.RefreshScores(id);
                        await scoreController.BulkRefreshScoresAllContexts(id);
                    }

                    await playerController.RefreshPlayerAction(job.PlayerId);
                    await playerContextController.RefreshPlayerAllContexts(job.PlayerId);
                }

                try {
                    await _context.SaveChangesAsync();
                } catch { }
            }
        }
    }
}
