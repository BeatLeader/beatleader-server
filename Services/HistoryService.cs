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
                if (DateTime.Now.Hour == 0)
                {
                    Console.WriteLine("SERVICE-STARTED HistoryService");

                    await SetHistories();
                    await SetLastWeek();
                    await SetClanRankingHistories();

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

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                var lastHistory = await _context.PlayerScoreStatsHistory.LastOrDefaultAsync();
                if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 12) {
                    return;
                }

                var _playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                await _playerController.RefreshPlayersStatsSlowly();

                var _playerContextController = scope.ServiceProvider.GetRequiredService<PlayerContextRefreshController>();
                await _playerContextController.RefreshPlayersStatsAllContextsSlowly();

                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                
                shouldSave = true;
                var playersCount = await _context.Players.Where(p => !p.Banned).CountAsync();
                for (int i = 0; i < playersCount; i += 10000)
                {
                    var ranked = await _context
                        .Players
                        .OrderBy(p => p.Id)
                        .Where(p => !p.Banned || p.Bot)
                        .Include(p => p.ScoreStats)
                        .Skip(i)
                        .Take(10000)
                        .Select(p => new { p.Pp, p.AccPp, p.PassPp, p.TechPp, p.ScoreStats, p.Rank, p.CountryRank, p.Id })
                        .ToListAsync();
                    foreach (var p in ranked)
                    {
                        if (p.ScoreStats == null) continue;
                        _context.PlayerScoreStatsHistory.Add(new PlayerScoreStatsHistory {
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

                            ReplaysWatched = p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched,
                            WatchedReplays = p.ScoreStats.WatchedReplays,
                        });
                    }
                    await _context.BulkSaveChangesAsync();
                    if (shouldSave) {
                        await SetContextHistories();
                    }
                }
                
            }
        }

        public async Task SetContextHistories()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                foreach (var context in ContextExtensions.NonGeneral)
                {
                    var lastHistory = await _context.PlayerScoreStatsHistory.Where(ps => ps.Context == context).LastOrDefaultAsync();
                    if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 12) {
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
                        foreach (var p in ranked)
                        {
                            if (p.ScoreStats == null) continue;
                            _context.PlayerScoreStatsHistory.Add(new PlayerScoreStatsHistory {
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

                                ReplaysWatched = p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched,
                                WatchedReplays = p.ScoreStats.WatchedReplays,
                            });
                        }
                        await _context.BulkSaveChangesAsync();
                    }
                }
            }
        }

        public async Task SetLastWeek()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                int timesetFrom = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7 - 60 * 60 * 2;
                int timesetTo = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7 + 60 * 60 * 2;
                
                var ranked = (await _context
                    .PlayerScoreStatsHistory
                    .Where(p => p.Timestamp > timesetFrom && p.Timestamp < timesetTo && p.Context == LeaderboardContexts.General)
                    .Select(p => new { 
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        Pp = p.Pp,
                        Id = p.PlayerId,
                        Timestamp = p.Timestamp,
                        })
                    .ToListAsync())
                    .DistinctBy(p => p.Id)
                    .ToList();
                foreach (var p in ranked)
                {
                    if (p.Id != null) {
                        var player = new Player() { Id = p.Id, LastWeekRank = p.Rank, LastWeekPp = p.Pp, LastWeekCountryRank = p.CountryRank };
                        _context.Players.Attach(player);
                        _context.Entry(player).Property(x => x.LastWeekRank).IsModified = true;
                        _context.Entry(player).Property(x => x.LastWeekPp).IsModified = true;
                        _context.Entry(player).Property(x => x.LastWeekCountryRank).IsModified = true;
                    }
                }

                await _context.BulkSaveChangesAsync();
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

                var clans = await _context.Clans.ToListAsync();
                foreach (var clan in clans)
                {
                    _context.GlobalMapHistory.Add(new GlobalMapHistory {
                        ClanId = clan.Id,
                        GlobalMapCaptured = clan.RankedPoolPercentCaptured,
                        Timestamp = timeset
                    });
                }

                await _context.BulkSaveChangesAsync();
            }
        }
    }
}
