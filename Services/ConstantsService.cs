using BeatLeader_Server.Bot;
using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Discord;
using Discord.Webhook;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services
{
    public class ConstantsService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        public static int RankedMapCount { get; private set; }

        public ConstantsService(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Calculate global initial ranked map count as of server start
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                await RefreshRankedMapCountAsync(scope.ServiceProvider.GetRequiredService<AppContext>());
            }
        }

        public static async Task RefreshRankedMapCountAsync(AppContext _context)
        {
            RankedMapCount = await _context
                .Leaderboards
                .CountAsync(lb => lb.Difficulty.Status == DifficultyStatus.ranked);
        }
    }
}
