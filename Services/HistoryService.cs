using Azure.Identity;
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
                 int hourSpan = 24 - DateTime.Now.Hour;
                 int numberOfHours = hourSpan;

                 if (hourSpan == 24)
                 {
                    await SetHistories();

                    hourSpan = 24 - DateTime.Now.Hour;
                    numberOfHours = hourSpan;
                 }

                 await Task.Delay(TimeSpan.FromHours(numberOfHours), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
        public async Task SetHistories()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                var playersCount = _context.Players.Where(p => !p.Banned).Count();
                for (int i = 0; i < playersCount; i += 2000)
                {
                    var ranked = _context
                        .Players
                        .Where(p => !p.Banned)
                        .Include(p => p.ScoreStats)
                        .Skip(i)
                        .Take(2000)
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
                        _context.PlayerScoreStatsHistory.Add(new PlayerScoreStatsHistory {
                            PlayerId = p.Id,
                            Timestamp = timeset,
                            Rank = p.Rank,
                            Pp = p.Pp,
                            CountryRank = p.CountryRank,

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

                            RankedPlayCount = p.ScoreStats.RankedPlayCount,
                            UnrankedPlayCount = p.ScoreStats.UnrankedPlayCount,
                            TotalPlayCount = p.ScoreStats.TotalPlayCount,

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

                            DailyImprovements = p.ScoreStats.DailyImprovements,
                            ReplaysWatched = p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched,
                            WatchedReplays = p.ScoreStats.WatchedReplays,
                        });
                    }

                    _context.SaveChanges();
                }
            }
        }
    }
}
