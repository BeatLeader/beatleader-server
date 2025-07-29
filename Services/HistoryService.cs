using Amazon.S3;
using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services
{
    public class HistoryService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public HistoryService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                if (DateTime.Now.Hour == 0) {
                    Console.WriteLine("SERVICE-STARTED HistoryService");

                    try {
                        await SetHistories();
                    } catch (Exception e) {
                        Console.WriteLine($"SERVICE-EXCEPTION HistoryService {e}");
                    }

                    try {
                        await SetLastWeek();
                    } catch (Exception e) {
                        Console.WriteLine($"SERVICE-EXCEPTION HistoryService {e}");
                    }

                    try {
                        await SetClanRankingHistories();
                    } catch (Exception e) {
                        Console.WriteLine($"SERVICE-EXCEPTION HistoryService {e}");
                    }

                    Console.WriteLine("SERVICE-DONE HistoryService");
                }

                await Task.Delay(DateTime.Now.Date.AddDays(1) - DateTime.Now, stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task SetHistories()
        {
            bool shouldSave = false;
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                var lastHistory = await _storageContext.PlayerScoreStatsHistory.Where(h => h.Context == LeaderboardContexts.General).OrderBy(ps => ps.Id).LastOrDefaultAsync();
                if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 10) {
                    return;
                }
                
                shouldSave = true;
                var playersCount = await _context.Players.Where(p => !p.Banned).CountAsync();
                for (int i = 0; i < playersCount; i += 10000) {
                    var ranked = await _context
                        .Players
                        .AsNoTracking()
                        .OrderBy(p => p.Id)
                        .Where(p => !p.Banned || p.Bot)
                        .Include(p => p.ScoreStats)
                        .Skip(i)
                        .Take(10000)
                        .Select(p => new { p.Pp, p.AccPp, p.PassPp, p.TechPp, p.ScoreStats, p.Rank, p.CountryRank, p.Id })
                        .ToListAsync();

                    await _storageContext.BulkInsertAsync(ranked
                        .Where(p => p.ScoreStats != null)
                        .Select(p => new PlayerScoreStatsHistory {
                            PlayerId = p.Id,
                            Timestamp = timeset,
                            Rank = p.Rank,

                            Pp = p.Pp,
                            AccPp = p.AccPp,
                            PassPp = p.PassPp,
                            TechPp = p.TechPp,

                            CountryRank = p.CountryRank,
                            Context = LeaderboardContexts.General,

                            TotalScore = p.ScoreStats.TotalScore,
                            TotalUnrankedScore = p.ScoreStats.TotalUnrankedScore,
                            TotalRankedScore = p.ScoreStats.TotalRankedScore,

                            LastScoreTime = p.ScoreStats.LastScoreTime,
                            LastUnrankedScoreTime = p.ScoreStats.LastUnrankedScoreTime,
                            LastRankedScoreTime = p.ScoreStats.LastRankedScoreTime,

                            AverageRankedAccuracy = p.ScoreStats.AverageRankedAccuracy,
                            AverageWeightedRankedAccuracy = p.ScoreStats.AverageWeightedRankedAccuracy,
                            AverageUnrankedAccuracy = p.ScoreStats.AverageUnrankedAccuracy,
                            AverageAccuracy = p.ScoreStats.AverageAccuracy,

                            MedianRankedAccuracy = p.ScoreStats.MedianRankedAccuracy,
                            MedianAccuracy = p.ScoreStats.MedianAccuracy,

                            TopRankedAccuracy = p.ScoreStats.TopRankedAccuracy,
                            TopUnrankedAccuracy = p.ScoreStats.TopUnrankedAccuracy,
                            TopAccuracy = p.ScoreStats.TopAccuracy,

                            TopPp = p.ScoreStats.TopPp,
                            TopBonusPP = p.ScoreStats.TopBonusPP,

                            PeakRank = p.ScoreStats.PeakRank,
                            MaxStreak = p.ScoreStats.MaxStreak,
                            AverageLeftTiming = p.ScoreStats.AverageLeftTiming,
                            AverageRightTiming = p.ScoreStats.AverageRightTiming,

                            RankedPlayCount = p.ScoreStats.RankedPlayCount,
                            UnrankedPlayCount = p.ScoreStats.UnrankedPlayCount,
                            TotalPlayCount = p.ScoreStats.TotalPlayCount,

                            RankedImprovementsCount = p.ScoreStats.RankedImprovementsCount,
                            UnrankedImprovementsCount = p.ScoreStats.UnrankedImprovementsCount,
                            TotalImprovementsCount = p.ScoreStats.TotalImprovementsCount,

                            AverageRankedRank = p.ScoreStats.AverageRankedRank,
                            AverageWeightedRankedRank = p.ScoreStats.AverageWeightedRankedRank,
                            AverageUnrankedRank = p.ScoreStats.AverageUnrankedRank,
                            AverageRank = p.ScoreStats.AverageRank,

                            SSPPlays = p.ScoreStats.SSPPlays,
                            SSPlays = p.ScoreStats.SSPlays,
                            SPPlays = p.ScoreStats.SPPlays,
                            SPlays = p.ScoreStats.SPlays,
                            APlays = p.ScoreStats.APlays,

                            TopPlatform = p.ScoreStats.TopPlatform,
                            TopHMD = p.ScoreStats.TopHMD,

                            ScorePlaytime = p.ScoreStats.ScorePlaytime,
                            SteamPlaytime2Weeks = p.ScoreStats.SteamPlaytime2Weeks,
                            SteamPlaytimeForever = p.ScoreStats.SteamPlaytimeForever,

                            ReplaysWatched = p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched,
                            WatchedReplays = p.ScoreStats.WatchedReplays,
                        }));
                }
                if (shouldSave) {
                    await SetContextHistories();
                }
            }
        }

        public async Task SetContextHistories()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();
                _storageContext.ChangeTracker.AutoDetectChangesEnabled = false;

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                foreach (var context in ContextExtensions.NonGeneral)
                {
                    var lastHistory = await _storageContext.PlayerScoreStatsHistory.Where(ps => ps.Context == context).OrderBy(ps => ps.Id).LastOrDefaultAsync();
                    if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 10) {
                        return;
                    }
                    var playersCount = await _context.PlayerContextExtensions.Where(p => p.Context == context).CountAsync();
                    for (int i = 0; i < playersCount; i += 10000)
                    {
                        var ranked = await _context
                            .PlayerContextExtensions
                            .OrderBy(p => p.Id)
                            .Where(p => p.Context == context)
                            .Include(p => p.ScoreStats)
                            .Skip(i)
                            .Take(10000)
                            .Select(p => new { p.Pp, p.AccPp, p.PassPp, p.TechPp, p.ScoreStats, p.Rank, p.CountryRank, Id = p.PlayerId })
                            .ToListAsync();

                        await _storageContext.BulkInsertAsync(ranked
                            .Where(p => p.ScoreStats != null)
                            .Select(p => new PlayerScoreStatsHistory {
                                PlayerId = p.Id,
                                Timestamp = timeset,
                                Rank = p.Rank,

                                Pp = p.Pp,
                                AccPp = p.AccPp,
                                PassPp = p.PassPp,
                                TechPp = p.TechPp,

                                CountryRank = p.CountryRank,
                                Context = context,

                                TotalScore = p.ScoreStats.TotalScore,
                                TotalUnrankedScore = p.ScoreStats.TotalUnrankedScore,
                                TotalRankedScore = p.ScoreStats.TotalRankedScore,

                                LastScoreTime = p.ScoreStats.LastScoreTime,
                                LastUnrankedScoreTime = p.ScoreStats.LastUnrankedScoreTime,
                                LastRankedScoreTime = p.ScoreStats.LastRankedScoreTime,

                                AverageRankedAccuracy = p.ScoreStats.AverageRankedAccuracy,
                                AverageWeightedRankedAccuracy = p.ScoreStats.AverageWeightedRankedAccuracy,
                                AverageUnrankedAccuracy = p.ScoreStats.AverageUnrankedAccuracy,
                                AverageAccuracy = p.ScoreStats.AverageAccuracy,

                                MedianRankedAccuracy = p.ScoreStats.MedianRankedAccuracy,
                                MedianAccuracy = p.ScoreStats.MedianAccuracy,

                                TopRankedAccuracy = p.ScoreStats.TopRankedAccuracy,
                                TopUnrankedAccuracy = p.ScoreStats.TopUnrankedAccuracy,
                                TopAccuracy = p.ScoreStats.TopAccuracy,

                                TopPp = p.ScoreStats.TopPp,
                                TopBonusPP = p.ScoreStats.TopBonusPP,

                                PeakRank = p.ScoreStats.PeakRank,
                                MaxStreak = p.ScoreStats.MaxStreak,
                                AverageLeftTiming = p.ScoreStats.AverageLeftTiming,
                                AverageRightTiming = p.ScoreStats.AverageRightTiming,

                                RankedPlayCount = p.ScoreStats.RankedPlayCount,
                                UnrankedPlayCount = p.ScoreStats.UnrankedPlayCount,
                                TotalPlayCount = p.ScoreStats.TotalPlayCount,

                                RankedImprovementsCount = p.ScoreStats.RankedImprovementsCount,
                                UnrankedImprovementsCount = p.ScoreStats.UnrankedImprovementsCount,
                                TotalImprovementsCount = p.ScoreStats.TotalImprovementsCount,

                                AverageRankedRank = p.ScoreStats.AverageRankedRank,
                                AverageWeightedRankedRank = p.ScoreStats.AverageWeightedRankedRank,
                                AverageUnrankedRank = p.ScoreStats.AverageUnrankedRank,
                                AverageRank = p.ScoreStats.AverageRank,

                                SSPPlays = p.ScoreStats.SSPPlays,
                                SSPlays = p.ScoreStats.SSPlays,
                                SPPlays = p.ScoreStats.SPPlays,
                                SPlays = p.ScoreStats.SPlays,
                                APlays = p.ScoreStats.APlays,

                                TopPlatform = p.ScoreStats.TopPlatform,
                                TopHMD = p.ScoreStats.TopHMD,

                                ScorePlaytime = p.ScoreStats.ScorePlaytime,
                                SteamPlaytime2Weeks = p.ScoreStats.SteamPlaytime2Weeks,
                                SteamPlaytimeForever = p.ScoreStats.SteamPlaytimeForever,

                                ReplaysWatched = p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched,
                                WatchedReplays = p.ScoreStats.WatchedReplays,
                            }));
                    }
                }
            }
        }

        public async Task SetLastWeek()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();

                int timesetFrom = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7 - 60 * 60 * 2;
                int timesetTo = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7 + 60 * 60 * 2;
                
                var ranked = (await _storageContext
                    .PlayerScoreStatsHistory
                    .Where(p => p.Timestamp > timesetFrom && p.Timestamp < timesetTo && p.Context == LeaderboardContexts.General)
                    .Select(p => new Player { 
                        LastWeekRank = p.Rank,
                        LastWeekCountryRank = p.CountryRank,
                        LastWeekPp = p.Pp,
                        Id = p.PlayerId
                        })
                    .ToListAsync())
                    .DistinctBy(p => p.Id)
                    .ToList();
                await _context.BulkUpdateAsync(ranked, options => options.ColumnInputExpression = c => new { c.LastWeekRank, c.LastWeekPp, c.LastWeekCountryRank });
            }
        }

        public async Task SetClanRankingHistories()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var s3 = _configuration.GetS3Client();

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                using (var gcstream = await s3.DownloadAsset("clansmap-globalcache.json")) {
                    if (gcstream != null) {
                        using (var ms = new MemoryStream(5)) {
                            await gcstream.CopyToAsync(ms);
                            await s3.UploadAsset($"clansmap-globalcache-{timeset}.json", ms);
                        }
                    }
                }

                using (var gmstream = await s3.DownloadAsset("global-map-file.json")) {
                    if (gmstream != null) {
                        using (var ms = new MemoryStream(5)) {
                            await gmstream.CopyToAsync(ms);
                            await s3.UploadAsset($"global-map-file-{timeset}.json", ms);
                        }
                    }
                }

                var previousTimeset = _context.GlobalMapHistory.Where(c => c.ClanId == 461).OrderByDescending(c => c.Timestamp).FirstOrDefault()?.Timestamp ?? 1724717131;

                var clans = await _context.Clans.ToListAsync();
                foreach (var clan in clans)
                {
                    _context.GlobalMapHistory.Add(new GlobalMapHistory {
                        ClanId = clan.Id,
                        GlobalMapCaptured = clan.RankedPoolPercentCaptured,
                        PlayersCount = clan.PlayersCount,
                        MainPlayersCount = clan.MainPlayersCount,
                        Pp = clan.Pp,
                        Rank = clan.Rank,
                        AverageRank = clan.AverageRank,
                        AverageAccuracy = clan.AverageAccuracy,
                        CaptureLeaderboardsCount = clan.CaptureLeaderboardsCount,
                        Timestamp = timeset
                    });
                }

                await _context.BulkSaveChangesAsync();

                var _httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                var _s3Client = _configuration.GetS3Client();
                var client = _httpClientFactory.CreateClient();

                string url = $"https://render.beatleader.com/animatedscreenshot/600x600/clansmapchange/general/clansmap/history/{previousTimeset}/{timeset}";
                string? path = null;
                try {
                    HttpResponseMessage response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode) {
                        var gif = await response.Content.ReadAsByteArrayAsync();
                        path = $"/root/assets/clansmap-change-daily-{previousTimeset}-{timeset}.gif";
                        File.WriteAllBytes(path, gif);

                        await _s3Client.UploadAsset("clansmap-change-daily-latest.gif", gif);

                        var blhook = _configuration.GetValue<string?>("ClanWarsHook") ?? "";
                        var chhook = _configuration.GetValue<string?>("ClansHubHook") ?? "";
                        var hooks = clans
                            .Where(c => c.ClanRankingDiscordHook?.Length > 0)
                            .SelectMany(c => c.ClanRankingDiscordHook.Split(","))
                            .Append(blhook)
                            .Append(chhook);

                        ClanMessageService.AddDailyJob(new DailyChanges {
                            GifPath = path,
                            Hooks = hooks.Distinct().ToList()
                        });
                    }
                    
                } catch (Exception e) {
                    Console.WriteLine($"SetClanRankingHistories {e}");
                }
            }
        }
    }
}
