using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services
{
    public class HistoryService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public HistoryService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                if (DateTime.Now.Hour == 0)
                {
                    await SetHistories();
                    await SetLastWeek();
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

                var lastHistory = _context.PlayerScoreStatsHistory.FirstOrDefault();
                if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 12) {
                    return;
                }

                var _playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                await _playerController.RefreshPlayersStatsSlowly();

                var _playerContextController = scope.ServiceProvider.GetRequiredService<PlayerContextRefreshController>();
                await _playerContextController.RefreshPlayersStatsAllContextsSlowly();

                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                
                shouldSave = true;
                var playersCount = _context.Players.Where(p => !p.Banned).Count();
                for (int i = 0; i < playersCount; i += 10000)
                {
                    var ranked = _context
                        .Players
                        .OrderBy(p => p.Id)
                        .Where(p => !p.Banned || p.Bot)
                        .Include(p => p.ScoreStats)
                        .Skip(i)
                        .Take(10000)
                        .Select(p => new { 
                            Pp = p.Pp, 
                            ScoreStats = p.ScoreStats, 
                            Rank = p.Rank, 
                            CountryRank = p.CountryRank,
                            Id = p.Id
                            })
                        .ToList();
                    foreach (var p in ranked)
                    {
                        if (p.ScoreStats == null) continue;
                        _context.PlayerScoreStatsHistory.Add(new PlayerScoreStatsHistory {
                            PlayerId = p.Id,
                            Timestamp = timeset,
                            Rank = p.Rank,
                            Pp = p.Pp,
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
                    var lastHistory = _context.PlayerScoreStatsHistory.OrderByDescending(ps => ps.Timestamp).Where(ps => ps.Context == context).FirstOrDefault();
                    if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 12) {
                        return;
                    }
                    var playersCount = _context.PlayerContextExtensions.Where(p => p.Context == context).Count();
                    for (int i = 0; i < playersCount; i += 10000)
                    {
                        var ranked = _context
                            .PlayerContextExtensions
                            .OrderBy(p => p.Id)
                            .Where(p => p.Context == context)
                            .Include(p => p.ScoreStats)
                            .Skip(i)
                            .Take(10000)
                            .Select(p => new { 
                                Pp = p.Pp, 
                                ScoreStats = p.ScoreStats, 
                                Rank = p.Rank, 
                                CountryRank = p.CountryRank,
                                Id = p.PlayerId
                                })
                            .ToList();
                        foreach (var p in ranked)
                        {
                            if (p.ScoreStats == null) continue;
                            _context.PlayerScoreStatsHistory.Add(new PlayerScoreStatsHistory {
                                PlayerId = p.Id,
                                Timestamp = timeset,
                                Rank = p.Rank,
                                Pp = p.Pp,
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
    }
}
