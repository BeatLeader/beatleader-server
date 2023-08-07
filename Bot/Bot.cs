using BeatLeader_Server.Controllers;
using BeatLeader_Server.Utils;
using Discord;
using Discord.WebSocket;

namespace BeatLeader_Server.Bot
{
    public class BotService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public const ulong BLServerID = 921820046345523311;
        public const ulong BLBoosterRoleID = 934229583325175809;
        public static DiscordSocketClient Client { get; private set; } = new();

        public BotService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Client.LoginAsync(TokenType.Bot, _configuration.GetValue<string>("BotToken"));
            await Client.StartAsync();

            Client.ReactionAdded += OnReactionAdded;

            Client.UserUpdated += OnUserUpdated;

            await Task.Delay(-1, stoppingToken);

            await Client.StopAsync();
        }

        private async Task OnUserUpdated(SocketUser oldUser, SocketUser newUser) {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                await PlayerUtils.UpdateBoosterRole(_context, newUser.Id);
            }
        }

        private async Task OnReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _nominationForum = scope.ServiceProvider.GetRequiredService<NominationsForum>();

                var qualification = await _nominationForum.OnReactionAdded(_context, message, channel, reaction);
                if (qualification != null && qualification.QualityVote == -3) {
                    var _rankController = scope.ServiceProvider.GetRequiredService<RankController>();
                    await _rankController.QualityUnnominate(qualification);
                }
            }
        }
    }
}
