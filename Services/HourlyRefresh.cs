using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services
{
    public class HourlyRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public HourlyRefresh(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                 int minuteSpan = 60 - DateTime.Now.Minute;
                 int numberOfMinutes = minuteSpan;

                 if (minuteSpan == 60)
                 {
                    await RefreshClans();

                    minuteSpan = 60 - DateTime.Now.Minute;
                    numberOfMinutes = minuteSpan;
                 }

                 await Task.Delay(TimeSpan.FromMinutes(numberOfMinutes), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshClans()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var clans = _context
                    .Clans
                    .Include(c => c.Players.Where(p => !p.Banned))
                    .ThenInclude(p => p.ScoreStats)
                    .ToList();
                foreach (var clan in clans)
                {
                    if (clan.Players.Count > 0) {
                        clan.AverageAccuracy = clan.Players.Average(p => p.ScoreStats.AverageRankedAccuracy);
                        clan.AverageRank = (float)clan.Players.Average(p => p.Rank);
                        clan.PlayersCount = clan.Players.Count();
                        clan.Pp = _context.RecalculateClanPP(clan.Id);
                    }
                }
            
                await _context.SaveChangesAsync();
            }
        }
    }
}
