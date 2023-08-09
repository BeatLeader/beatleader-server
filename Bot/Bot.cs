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

        public const ulong ReviewHubForumID = 1034817071894237194;
        public const ulong NQTRoleId = 1064783598206599258;


        private async Task OnReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                SocketThreadChannel? thread;
                if ((thread = await channel.GetOrDownloadAsync() as SocketThreadChannel) != null) {
                    if (ReviewHubForumID == thread.ParentChannel.Id) {
                        ulong? userId = reaction.User.GetValueOrDefault()?.Id;
                        if (userId != null) {
                            var user = await ((IGuild)BotService.Client.GetGuild(BotService.BLServerID)).GetUserAsync(userId ?? 0, CacheMode.AllowDownload);
                            if (!user.RoleIds.Contains(NQTRoleId)) {
                                var fullmessage = await thread.GetMessageAsync(message.Id);
                                await fullmessage.RemoveReactionAsync(reaction.Emote, user);
                            }
                        }
                    }
                }
            }
        }
    }
}
