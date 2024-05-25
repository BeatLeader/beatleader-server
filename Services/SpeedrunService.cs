using System;
using System.Threading;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Utils;
using Microsoft.Extensions.Logging;

namespace BeatLeader_Server.Services {
    public class SpeedrunService : BackgroundService {
        private Dictionary<string, Timer> _timers = new Dictionary<string, Timer>();
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public SpeedrunService(IServiceScopeFactory scopeFactory)
        {
            _serviceScopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var timeset = Time.UnixNow();

                var lastSpeedrun = _context.Speedruns.OrderByDescending(s => s.FinishTimeset).FirstOrDefault()?.FinishTimeset ?? 0;

                var speedrunners = _context.Players.Where(p => p.SpeedrunStart > lastSpeedrun - 60 * 60).Select(p => new { p.SpeedrunStart, p.Id }).ToList();
                foreach (var item in speedrunners) {
                    if (item.SpeedrunStart > timeset - 60 * 60) {
                        _timers[item.Id] = new Timer(SpeedrunEnd, item.Id, TimeSpan.FromSeconds(60 * 60 - (timeset - item.SpeedrunStart)), TimeSpan.Zero);
                    } else {
                        SpeedrunUtils.FinishSpeedrun(_context, item.Id);
                    }
                }
            }
        }

        public void SpeedrunStarted(string playerId)
        {
            if (_timers.ContainsKey(playerId)) {
                _timers[playerId].Dispose();
            }

            _timers[playerId] = new Timer(SpeedrunEnd, playerId, TimeSpan.FromHours(1), TimeSpan.Zero);
        }

        private void SpeedrunEnd(object? state) {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                SpeedrunUtils.FinishSpeedrun(_context, (string)state);
            }
        }
    }
}
