using BeatLeader_Server.ControllerHelpers;
using Prometheus.Client;

namespace BeatLeader_Server.Services
{
    public class FunnyRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public FunnyRefresh(IServiceScopeFactory serviceScopeFactory, IMetricFactory metricFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                try {
                    await RefreshFunny();
                } catch (Exception e) {
                    Console.WriteLine($"EXCEPTION FunnyRefresh {e}");
                }

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshFunny() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                await PlayerContextRefreshControllerHelper.RefreshPlayersContext(_context, Models.LeaderboardContexts.Funny);
            }
        }
    }
}
