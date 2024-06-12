using BeatLeader_Server.Controllers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Dynamic;

namespace BeatLeader_Server.Services
{
    public class ClanRankingChanges {
        public Leaderboard Leaderboard { get; set; }
        public int? PreviousCaptorId { get; set; }
        public int? CurrentCaptorId { get; set; }

        public GlobalMapChange ChangeRecord { get; set; }

        public ClanRankingChanges(
            Leaderboard leaderboard, 
            int? previousCaptorId, 
            int? currentCaptorId,
            AppContext _context,
            List<ClanRanking> clanRankings) {
            Leaderboard = leaderboard;
            PreviousCaptorId = previousCaptorId;
            CurrentCaptorId = currentCaptorId;
            ChangeRecord = new GlobalMapChange {
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            };

            if (clanRankings.Count > 0) {
                var clan1Id = clanRankings[0].ClanId;
                ChangeRecord.OldClan1Id = clan1Id;
                ChangeRecord.OldClan1Capture = _context
                    .Clans
                    .Where(c => c.Id == clan1Id)
                    .Select(c => c.RankedPoolPercentCaptured)
                    .FirstOrDefault();
                ChangeRecord.OldClan1Pp = clanRankings[0].Pp;
            }
            if (clanRankings.Count > 1) {
                var clan2Id = clanRankings[1].ClanId;
                ChangeRecord.OldClan2Id = clan2Id;
                ChangeRecord.OldClan2Capture = _context
                    .Clans
                    .Where(c => c.Id == clan2Id)
                    .Select(c => c.RankedPoolPercentCaptured)
                    .FirstOrDefault();
                ChangeRecord.OldClan2Pp = clanRankings[1].Pp;
            }
            if (clanRankings.Count > 2) {
                var clan3Id = clanRankings[2].ClanId;
                ChangeRecord.OldClan3Id = clan3Id;
                ChangeRecord.OldClan3Capture = _context
                    .Clans
                    .Where(c => c.Id == clan3Id)
                    .Select(c => c.RankedPoolPercentCaptured)
                    .FirstOrDefault();
                ChangeRecord.OldClan3Pp = clanRankings[2].Pp;
            }
        }
    }

    public class ClanRankingChangesDescription {
        public GlobalMapEvent? GlobalMapEvent { get; set; }

        public string? PlayerId { get; set; }
        public int? ClanId { get; set; }
        public Clan? Clan { get; set; }
        public Score? Score { get; set; }
        public List<ClanRankingChanges>? Changes { get; set; }
    }

    public class ClanTaskService : BackgroundService {
        private static List<ClanRankingChangesDescription> jobs = new List<ClanRankingChangesDescription>();

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public ClanTaskService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        private async Task<string?> ActionMessage(AppContext _context, ClanRankingChangesDescription description) {
            if (description.GlobalMapEvent == GlobalMapEvent.ranked) return "New batch of maps were ranked:";
            if (description.PlayerId == null) return null;

            var player = await _context.Players.FindAsync(description.PlayerId);
            if (description.GlobalMapEvent == GlobalMapEvent.priorityChange) return $"{player.Name} switched clan order which";

            if (description.ClanId == null) return null;
            var clan = description.Clan ?? await _context.Clans.FindAsync(description);

            switch (description.GlobalMapEvent)
            {
                case GlobalMapEvent.create:
                    return $"{player.Name} created [{clan.Tag}] which";
                case GlobalMapEvent.dismantle:
                    return $"{player.Name} dismantled [{clan.Tag}] which";
                case GlobalMapEvent.kick:
                    return $"{player.Name} was kicked from [{clan.Tag}] which";
                case GlobalMapEvent.join:
                    return $"{player.Name} joined [{clan.Tag}] which";
                case GlobalMapEvent.leave:
                    return $"{player.Name} left [{clan.Tag}] which";
                default:
                    break;
            }

            return null;
        }

        public static void AddJob(ClanRankingChangesDescription newJob) {
            lock (jobs) {
                jobs.Add(newJob);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                Console.WriteLine("STARTED ClanTaskService");
                try {
                    await ProcessJobs(stoppingToken);
                } catch { }
                Console.WriteLine("DONE ClanTaskService");

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                await ProcessJobs(stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task AddCustomHooks(AppContext _context, List<string> hooks, List<ClanRankingChanges> changes) {
            foreach (var change in changes)
            {
               if (change.CurrentCaptorId != null) {
                    var customHooks = await _context
                        .Clans
                        .Where(cl => cl.Id == change.CurrentCaptorId)
                        .Select(cl => cl.ClanRankingDiscordHook)
                        .FirstOrDefaultAsync();
                    if (customHooks != null) {
                        hooks.AddRange(customHooks.Split(","));
                    }
                }

                if (change.PreviousCaptorId != null) {
                    var customHooks = await _context
                        .Clans
                        .Where(cl => cl.Id == change.PreviousCaptorId)
                        .Select(cl => cl.ClanRankingDiscordHook)
                        .FirstOrDefaultAsync();
                    if (customHooks != null) {
                        hooks.AddRange(customHooks.Split(","));
                    }
                }
            }
        }

        private async Task PopulateRecords(AppContext _context, dynamic? globalMap, ClanRankingChangesDescription job) {
            if (job.Changes == null || job.Changes.Count == 0) return;

            foreach (var change in job.Changes)
            {
                var changeRecord = change.ChangeRecord;
                changeRecord.LeaderboardId = change.Leaderboard?.Id;
                changeRecord.PlayerId = job.PlayerId;
                changeRecord.PlayerAction = job.GlobalMapEvent;
                changeRecord.ScoreId = job.Score?.Id;

                if (change.Leaderboard != null) {
                    var clanRankings = await _context
                    .ClanRanking
                    .Where(cr => cr.LeaderboardId == change.Leaderboard.Id)
                    .OrderByDescending(cr => cr.Pp)
                    .Take(3)
                    .Select(cr => new {
                        cr.ClanId, cr.Pp, cr.Clan.RankedPoolPercentCaptured
                    })
                    .ToListAsync();

                    if (clanRankings.Count > 0) {
                        var clan1Id = clanRankings[0].ClanId;
                        changeRecord.NewClan1Id = clan1Id;
                        changeRecord.NewClan1Capture = await _context
                            .Clans
                            .Where(c => c.Id == clan1Id)
                            .Select(c => c.RankedPoolPercentCaptured)
                            .FirstOrDefaultAsync();
                        changeRecord.NewClan1Pp = clanRankings[0].Pp;
                    }
                    if (clanRankings.Count > 1) {
                        var clan2Id = clanRankings[1].ClanId;
                        changeRecord.NewClan2Id = clan2Id;
                        changeRecord.NewClan2Capture = await _context
                            .Clans
                            .Where(c => c.Id == clan2Id)
                            .Select(c => c.RankedPoolPercentCaptured)
                            .FirstOrDefaultAsync();
                        changeRecord.NewClan2Pp = clanRankings[1].Pp;
                    }
                    if (clanRankings.Count > 2) {
                        var clan3Id = clanRankings[2].ClanId;
                        changeRecord.NewClan3Id = clan3Id;
                        changeRecord.NewClan3Capture = await _context
                            .Clans
                            .Where(c => c.Id == clan3Id)
                            .Select(c => c.RankedPoolPercentCaptured)
                            .FirstOrDefaultAsync();
                        changeRecord.NewClan3Pp = clanRankings[2].Pp;
                    }

                    if (globalMap != null) {
                        try {
                            var circles = (IDictionary<string, dynamic>)globalMap.circles;
                            changeRecord.OldX = (float)circles[change.Leaderboard.Id].x;
                            changeRecord.OldY = (float)circles[change.Leaderboard.Id].y;
                        } catch { }
                    }
                }
            }
        }

        private async Task PopulateNewLocation(dynamic? globalMap, ClanRankingChangesDescription job) {
            if (job.Changes == null || job.Changes.Count == 0) return;

            foreach (var change in job.Changes)
            {
                var changeRecord = change.ChangeRecord;

                if (change.Leaderboard != null) {
                    if (globalMap != null) {
                        try {
                            var circles = (IDictionary<string, dynamic>)globalMap.circles;
                            changeRecord.NewX = (float)circles[change.Leaderboard.Id].x;
                            changeRecord.NewY = (float)circles[change.Leaderboard.Id].y;
                        } catch { }
                    }
                }

                try {
                    var message = JsonConvert.SerializeObject(changeRecord, new JsonSerializerSettings 
                    { 
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                    });
                } catch { }
            }
        }

        private async Task ProcessJobs(CancellationToken stoppingToken) {
            var jobsToProcess = new List<ClanRankingChangesDescription>();

            Console.WriteLine("1 ClanTaskService");

            lock (jobs) {
                foreach (var job in jobs) {
                    jobsToProcess.Add(job);
                }
                jobs = new List<ClanRankingChangesDescription>();
            }

            Console.WriteLine("2 ClanTaskService");

            if (jobsToProcess.Count == 0) return;

            using (var scope = _serviceScopeFactory.CreateScope()) {
                try {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                    var _clanController = scope.ServiceProvider.GetRequiredService<ClanAdminController>();
                    var _httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                    var s3 = _configuration.GetS3Client();
                    var blhook = _configuration.GetValue<string?>("ClanWarsHook");

                    Console.WriteLine("3 ClanTaskService");

                    await ClanUtils.UpdateClanRankingRanks(_context);

                    Console.WriteLine("4 ClanTaskService");
                    ExpandoObject? globalMap = null;
                    using (var stream = await s3.DownloadAsset("clansmap-globalcache.json")) {
                       globalMap = stream?.ObjectFromStream();
                    }

                    Console.WriteLine("5 ClanTaskService");

                    foreach (var job in jobsToProcess) {

                        await PopulateRecords(_context, globalMap, job);

                        Console.WriteLine("6 ClanTaskService");

                        var message = await ActionMessage(_context, job);
                        if (job.Changes != null && job.Changes.Count > 0 && message != null) {
                            var hooks = new List<string> { blhook };
                            await AddCustomHooks(_context, hooks, job.Changes);
                            foreach (var hook in hooks.Distinct())
                            {
                                await ClanUtils.PostChangesWithMessage(_context, stoppingToken, job.Changes, message, hook);
                            }
                        }

                        Console.WriteLine("7 ClanTaskService");

                        if (job.GlobalMapEvent != null && job.ClanId != null) {
                            var clan = await _context.Clans.FirstOrDefaultAsync(c => c.Id == job.ClanId);
                            if (clan?.PlayerChangesCallback != null) {
                                var httpClient = new HttpClient();
                                var callbacks = clan.PlayerChangesCallback.Split(",");
                                try {
                                    foreach (var callback in callbacks.Distinct())
                                    {
                                        await httpClient.GetStringAsync($"{callback}?action={job.GlobalMapEvent}&player={job.PlayerId}");
                                    }
                                } catch (Exception e)
                                {
                                    Console.WriteLine($"EXCEPTION: {e}");
                                }
                            }
                        }
                    }

                    Console.WriteLine("8 ClanTaskService");

                    var file = (await _clanController.RefreshGlobalMap()).Value;
                    var newGlobalMap = file?.ObjectFromArray();
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromMinutes(5);

                    foreach (var job in jobsToProcess) {
                        if (job.Changes != null && job.Changes.Count > 0) {
                            await PopulateNewLocation(newGlobalMap, job);

                            if (job.Score != null) {
                                var hooks = new List<string> { blhook };
                                await AddCustomHooks(_context, hooks, job.Changes);
                                foreach (var hook in hooks.Distinct())
                                {
                                    string url = $"https://render.beatleader.xyz/animatedscreenshot/620x280/clansmapchange/general/clansmap/leaderboard/{job.Score.LeaderboardId}";

                                    try {
                                        HttpResponseMessage response = await client.GetAsync(url);

                                        if (response.IsSuccessStatusCode) {
                                            var gif = await response.Content.ReadAsByteArrayAsync();
                                            string path = $"/root/assets/clansmap-change-{(int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds}-{job.Score.LeaderboardId}.gif";
                                            File.WriteAllBytes(path, gif);
                                            await ClanUtils.PostChangesWithScore(_context, stoppingToken, job.Changes, job.Score, path, hook);
                                            File.Delete(path);
                                        }
                                    } catch (Exception e) {
                                    }
                                }
                            }

                            Console.WriteLine("9 ClanTaskService");

                            foreach (var change in job.Changes)
                            {
                                _context.GlobalMapChanges.Add(change.ChangeRecord);
                            }
                        }

                        await SocketController.ClanRankingChanges(job);
                    }

                    await _context.SaveChangesAsync();
                } catch (Exception e)
                {
                    Console.WriteLine($"EXCEPTION: {e}");
                }
            }
        }
    }
}
