using Discord;
using Discord.WebSocket;

namespace BeatLeader_Server.Bot
{
    public class BotService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public const ulong BLServerID = 921820046345523311;
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

            await Task.Delay(-1, stoppingToken);

            await Client.StopAsync();
        }

        private async Task OnReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _nominationForum = scope.ServiceProvider.GetRequiredService<NominationsForum>();

                await _nominationForum.OnReactionAdded(_context, message, channel, reaction);
            }
        }
        
    }
}
