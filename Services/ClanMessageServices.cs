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
        public string? GifPath { get; set; }
        public Score Score { get; set; }
        public List<ClanRankingChanges>? Changes { get; set; }
    }

    public class ChangesForCallback {
        public List<string> Callbacks { get; set; }
        public GlobalMapEvent GlobalMapEvent { get; set; }
        public string PlayerId { get; set; }
    }

    public class DailyChanges {
        public List<string> Hooks { get; set; }
        public string GifPath { get; set; }
    }

    public class ClanMessageService : BackgroundService {

        private static List<ChangesWithMessage> messageJobs = new List<ChangesWithMessage>();
        private static List<ChangesWithScore> scoreJobs = new List<ChangesWithScore>();
        private static List<DailyChanges> dailyJobs = new List<DailyChanges>();

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

        public static void AddDailyJob(DailyChanges newJob) {
            lock (dailyJobs) {
                dailyJobs.Add(newJob);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("STARTED ClanMessageServices");
                try {
                    await ProcessMessageJobs(stoppingToken);
                    await ProcessScoreJobs(stoppingToken);
                    await ProcessDailyJobs(stoppingToken);
                } catch { }
                Console.WriteLine("DONE ClanMessageServices");

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                try {
                    await ProcessMessageJobs(stoppingToken);
                    await ProcessScoreJobs(stoppingToken);
                    await ProcessDailyJobs(stoppingToken);
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
            var jobGroups = jobsToProcess.GroupBy(j => (j.Changes?.FirstOrDefault()?.Leaderboard?.Id ?? "") + (j.Changes?.FirstOrDefault()?.CurrentCaptorId ?? 0));

            using (var scope = _serviceScopeFactory.CreateScope()) {
                try {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    foreach (var jobGroup in jobGroups) {
                        var group = jobGroup.ToList();
                        for (int i = 0; i < group.Count; i++) {
                            var job = group[i];
                            if (i == 0) {
                                if (group.Count > 1) {
                                    await ClanUtils.PostChangesWithScores(_context, stoppingToken, group);
                                } else {
                                    foreach (var hook in job.Hooks) {
                                        await ClanUtils.PostChangesWithScore(_context, stoppingToken, job.Changes, job.Score, job.GifPath, hook);
                                    }
                                }
                            }
                            if (job.GifPath != null) {
                                File.Delete(job.GifPath);
                            }
                        }
                        
                    }
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
        }

        private async Task ProcessDailyJobs(CancellationToken stoppingToken) {
            var jobsToProcess = new List<DailyChanges>();

            lock (dailyJobs) {
                foreach (var job in dailyJobs) {
                    jobsToProcess.Add(job);
                }
                dailyJobs = new List<DailyChanges>();
            }

            if (jobsToProcess.Count == 0) return;

            using (var scope = _serviceScopeFactory.CreateScope()) {
                try {
                    foreach (var job in jobsToProcess) {
                        if (job.GifPath != null) {
                            foreach (var hook in job.Hooks) {
                                await ClanUtils.PostDailyChanges(stoppingToken, job.GifPath, hook);
                            }
                        
                            File.Delete(job.GifPath);
                        }
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
