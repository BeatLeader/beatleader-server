using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;

namespace BeatLeader_Server.Services
{
    public class ChangesWithMessage {
        public List<string> Hooks { get; set; }
        public string Message { get; set; }
        public List<ClanRankingChanges>? Changes { get; set; }
    }

    public class ChangesWithScore {
        public List<string> Hooks { get; set; }
        public string GifPath { get; set; }
        public Score Score { get; set; }
        public List<ClanRankingChanges>? Changes { get; set; }
    }

    public class ChangesForCallback {
        public List<string> Callbacks { get; set; }
        public GlobalMapEvent GlobalMapEvent { get; set; }
        public string PlayerId { get; set; }
    }

    public class ClanMessageService : BackgroundService {

        private static List<ChangesWithMessage> messageJobs = new List<ChangesWithMessage>();
        private static List<ChangesWithScore> scoreJobs = new List<ChangesWithScore>();

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public ClanMessageService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public static void AddMessageJob(ChangesWithMessage newJob) {
            lock (messageJobs) {
                messageJobs.Add(newJob);
            }
        }

        public static void AddScoreJob(ChangesWithScore newJob) {
            lock (scoreJobs) {
                scoreJobs.Add(newJob);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("STARTED ClanMessageServices");
                try {
                    await ProcessMessageJobs(stoppingToken);
                    await ProcessScoreJobs(stoppingToken);
                } catch { }
                Console.WriteLine("DONE ClanMessageServices");

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                try {
                    await ProcessMessageJobs(stoppingToken);
                    await ProcessScoreJobs(stoppingToken);
                } catch { }
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task ProcessMessageJobs(CancellationToken stoppingToken) {
            var jobsToProcess = new List<ChangesWithMessage>();

            lock (messageJobs) {
                foreach (var job in messageJobs) {
                    jobsToProcess.Add(job);
                }
                messageJobs = new List<ChangesWithMessage>();
            }

            if (jobsToProcess.Count == 0) return;

            using (var scope = _serviceScopeFactory.CreateScope()) {
                try {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    foreach (var job in jobsToProcess) {
                        foreach (var hook in job.Hooks) {
                            await ClanUtils.PostChangesWithMessage(_context, stoppingToken, job.Changes, job.Message, hook);
                        }
                    }
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
        }

        private async Task ProcessScoreJobs(CancellationToken stoppingToken) {
            var jobsToProcess = new List<ChangesWithScore>();

            lock (scoreJobs) {
                foreach (var job in scoreJobs) {
                    jobsToProcess.Add(job);
                }
                scoreJobs = new List<ChangesWithScore>();
            }

            if (jobsToProcess.Count == 0) return;

            using (var scope = _serviceScopeFactory.CreateScope()) {
                try {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    foreach (var job in jobsToProcess) {
                        foreach (var hook in job.Hooks) {
                            await ClanUtils.PostChangesWithScore(_context, stoppingToken, job.Changes, job.Score, job.GifPath, hook);
                        }
                        File.Delete(job.GifPath);
                    }
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
        }
    }
    public class ClanCallbackService : BackgroundService {

        private static List<ChangesForCallback> callbackJobs = new List<ChangesForCallback>();

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public ClanCallbackService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public static void AddCallbackJob(ChangesForCallback newJob) {
            lock (callbackJobs) {
                callbackJobs.Add(newJob);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("STARTED ClanMessageServices");
                try {
                    await ProcessCallbackJobs(stoppingToken);
                } catch { }
                Console.WriteLine("DONE ClanMessageServices");

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                try {
                    await ProcessCallbackJobs(stoppingToken);
                } catch { }
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task ProcessCallbackJobs(CancellationToken stoppingToken) {
            var jobsToProcess = new List<ChangesForCallback>();

            lock (callbackJobs) {
                foreach (var job in callbackJobs) {
                    jobsToProcess.Add(job);
                }
                callbackJobs = new List<ChangesForCallback>();
            }

            if (jobsToProcess.Count == 0) return;

            using (var scope = _serviceScopeFactory.CreateScope()) {
                try {
                    var _httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                    foreach (var job in jobsToProcess) {
                        var httpClient = new HttpClient();
                        foreach (var callback in job.Callbacks) {
                            try {
                                await httpClient.GetStringAsync($"{callback}?action={job.GlobalMapEvent}&player={job.PlayerId}");
                            } catch (Exception e)
                            {
                                Console.WriteLine($"EXCEPTION: {e}");
                            }
                        }
                    }
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
        }
    }
}
