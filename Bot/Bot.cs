using BeatLeader_Server.Extensions;
using BeatLeader_Server.Utils;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Timers;

namespace BeatLeader_Server.Bot
{
    public class BotService : BackgroundService
    {
        private const int SpamChannelThreshold = 3;
        private static readonly TimeSpan SpamDetectionWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan SpamTimeoutDuration = TimeSpan.FromHours(24);

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private Dictionary<ulong, int> _originalChannelPositions = new();
        private bool _isRestoringPositions = false;
        private System.Timers.Timer? _debounceTimer;
        private readonly object _timerLock = new object();
        private readonly object _spamLock = new object();
        private bool _autoOrderEnabled = true;
        private readonly HashSet<ulong> _allowedRoles = new() { 961336531442864219, 1273803067912884285 };
        private readonly Dictionary<SpamMessageKey, List<TrackedSpamMessage>> _recentMessages = new();
        private DateTimeOffset _lastSpamCleanup = DateTimeOffset.MinValue;

        public const ulong BLServerID = 921820046345523311;
        public const ulong BLBoosterRoleID = 934229583325175809;
        public const ulong BLWelcomeChannelID = 1042959852261089301;
        public const ulong SpamReportChannelID = 934225461951750254;
        public static DiscordSocketClient Client { get; private set; } = new(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        });

        public BotService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            
            _debounceTimer = new System.Timers.Timer(500); // 500ms debounce time
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += async (sender, e) => await RestoreChannelPositions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Client.LoginAsync(TokenType.Bot, _configuration.GetValue<string>("BotToken"));
            await Client.StartAsync();

            Client.ReactionAdded += OnReactionAdded;
            Client.MessageReceived += OnNewMessage;
            Client.ChannelUpdated += OnChannelUpdated;
            Client.SlashCommandExecuted += OnSlashCommandExecuted;
            Client.Ready += OnClientReady;

            await Task.Delay(-1, stoppingToken);

            await Client.StopAsync();
        }

        private async Task OnClientReady()
        {
            // Register slash commands
            var guild = Client.GetGuild(BLServerID);
            if (guild != null)
            {
                try
                {
                    var commandBuilder = new SlashCommandBuilder()
                        .WithName("autoorder")
                        .WithDescription("Manage channel auto-ordering")
                        .AddOption(new SlashCommandOptionBuilder()
                            .WithName("action")
                            .WithDescription("The action to perform")
                            .WithRequired(true)
                            .WithType(ApplicationCommandOptionType.String)
                            .AddChoice("pause", "pause")
                            .AddChoice("start", "start")
                            .AddChoice("status", "status"));

                    await guild.CreateApplicationCommandAsync(commandBuilder.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating slash command: {ex.Message}");
                }

                await StoreCurrentChannelPositions();
            }
        }

        private async Task OnSlashCommandExecuted(SocketSlashCommand command)
        {
            if (command.CommandName != "autoorder") return;

            var guildUser = await ((IGuild)Client.GetGuild(BLServerID)).GetUserAsync(command.User.Id, CacheMode.AllowDownload);
            bool hasPermission = guildUser.RoleIds.Any(r => _allowedRoles.Contains(r));

            if (!hasPermission)
            {
                await command.RespondAsync("You don't have permission to use this command.", ephemeral: true);
                return;
            }

            string action = command.Data.Options.First().Value.ToString();
            switch (action)
            {
                case "pause":
                    _autoOrderEnabled = false;
                    await command.RespondAsync("Channel auto-ordering has been paused.", ephemeral: true);
                    break;

                case "start":
                    await StoreCurrentChannelPositions();
                    _autoOrderEnabled = true;
                    await command.RespondAsync("Channel auto-ordering has been started with current positions as reference.", ephemeral: true);
                    break;

                case "status":
                    string status = _autoOrderEnabled ? "enabled" : "paused";
                    int trackedChannels = _originalChannelPositions.Count;
                    await command.RespondAsync($"Channel auto-ordering is currently {status}. Tracking {trackedChannels} channels.", ephemeral: true);
                    break;
            }
        }

        private async Task StoreCurrentChannelPositions()
        {
            var guild = Client.GetGuild(BLServerID);
            if (guild != null)
            {
                _originalChannelPositions.Clear();
                foreach (var channel in guild.Channels)
                {
                    if (channel.ChannelType != ChannelType.PublicThread && channel.ChannelType != ChannelType.PrivateThread)
                    {
                        _originalChannelPositions[channel.Id] = channel.Position;
                    }
                }
            }
        }

        private async Task RestoreChannelPositions()
        {
            if (_isRestoringPositions || !_autoOrderEnabled) return;

            try {
                _isRestoringPositions = true;
                var guild = Client.GetGuild(BLServerID);
                if (guild == null) return;

                // Collect all channels that need position updates
                var updates = new List<(IGuildChannel Channel, int Position)>();
                foreach (var channelEntry in _originalChannelPositions)
                {
                    var channel = guild.GetChannel(channelEntry.Key);
                    if (channel != null && channel.Position != channelEntry.Value)
                    {
                        updates.Add((channel, channelEntry.Value));
                    }
                }

                // Apply all updates at once if any are needed
                if (updates.Count > 0)
                {
                    try {
                        await guild.ReorderChannelsAsync(updates.Select(x => new ReorderChannelProperties(x.Channel.Id, x.Position)));
                    }
                    catch (Exception e) {
                        Console.WriteLine($"Failed to restore channel positions: {e.Message}");
                    }
                }
            }
            finally {
                _isRestoringPositions = false;
            }
        }

        private Task OnChannelUpdated(SocketChannel arg1, SocketChannel arg2)
        {
            if (arg1 is SocketGuildChannel && arg2 is SocketGuildChannel)
            {
                var before = (SocketGuildChannel)arg1;
                var after = (SocketGuildChannel)arg2;

                if (_originalChannelPositions.ContainsKey(before.Id) && _originalChannelPositions[before.Id] != after.Position)
                {
                    lock (_timerLock)
                    {
                        _debounceTimer?.Stop(); // Stop any existing timer
                        _debounceTimer?.Start(); // Restart the timer
                    }
                }
            }
            return Task.CompletedTask;
        }

        private async Task OnNewMessage(SocketMessage message)
        {
            if (message.Type == MessageType.UserPremiumGuildSubscription)
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                    await PlayerUtils.UpdateBoosterRole(_context, message.Author.Id);
                }
            }

            if (message is not SocketUserMessage userMessage || message.Source != MessageSource.User)
            {
                return;
            }

            await HandleSpamDetection(userMessage);
        }

        public const ulong ReviewHubForumID = 1034817071894237194;
        public const ulong NQTRoleId = 1064783598206599258;

        private sealed record SpamMessageKey(ulong UserId, string Content);

        private sealed record TrackedSpamMessage(ulong ChannelId, ulong MessageId, DateTimeOffset Timestamp);

        private async Task HandleSpamDetection(SocketUserMessage message)
        {
            if (message.Channel is not SocketGuildChannel guildChannel)
            {
                return;
            }

            var normalizedContent = NormalizeSpamContent(message.Content);
            if (normalizedContent == null)
            {
                return;
            }

            var matchedMessages = TrackPotentialSpam(message.Author.Id, guildChannel.Id, message.Id, normalizedContent);
            if (matchedMessages.Count == 0)
            {
                return;
            }

            await DeleteTrackedMessages(guildChannel.Guild, matchedMessages);
            await TimeoutUser(guildChannel.Guild, message.Author.Id);
            await SendSpamReport(guildChannel.Guild, message.Author.Id, message.Content);
        }

        private List<TrackedSpamMessage> TrackPotentialSpam(ulong userId, ulong channelId, ulong messageId, string normalizedContent)
        {
            lock (_spamLock)
            {
                var now = DateTimeOffset.UtcNow;
                CleanupSpamTracking(now);

                var key = new SpamMessageKey(userId, normalizedContent);
                if (!_recentMessages.TryGetValue(key, out var trackedMessages))
                {
                    trackedMessages = new List<TrackedSpamMessage>();
                    _recentMessages[key] = trackedMessages;
                }

                trackedMessages.Add(new TrackedSpamMessage(channelId, messageId, now));

                if (trackedMessages.Select(entry => entry.ChannelId).Distinct().Count() < SpamChannelThreshold)
                {
                    return new List<TrackedSpamMessage>();
                }

                var result = trackedMessages.ToList();
                _recentMessages.Remove(key);
                return result;
            }
        }

        private void CleanupSpamTracking(DateTimeOffset now)
        {
            if (now - _lastSpamCleanup < TimeSpan.FromSeconds(30))
            {
                return;
            }

            var cutoff = now - SpamDetectionWindow;
            var expiredKeys = new List<SpamMessageKey>();

            foreach (var (key, entries) in _recentMessages)
            {
                entries.RemoveAll(entry => entry.Timestamp < cutoff);
                if (entries.Count == 0)
                {
                    expiredKeys.Add(key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _recentMessages.Remove(key);
            }

            _lastSpamCleanup = now;
        }

        private async Task DeleteTrackedMessages(SocketGuild guild, IReadOnlyCollection<TrackedSpamMessage> trackedMessages)
        {
            foreach (var trackedMessage in trackedMessages)
            {
                if (guild.GetChannel(trackedMessage.ChannelId) is not IMessageChannel channel)
                {
                    continue;
                }

                try
                {
                    await channel.DeleteMessageAsync(trackedMessage.MessageId);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"Failed to delete spam message {trackedMessage.MessageId}: {exception.Message}");
                }
            }
        }

        private async Task TimeoutUser(SocketGuild guild, ulong userId)
        {
            var user = guild.GetUser(userId);
            if (user == null)
            {
                return;
            }

            var timeoutUntil = DateTimeOffset.UtcNow.Add(SpamTimeoutDuration);
            if (user.TimedOutUntil.HasValue && user.TimedOutUntil.Value > timeoutUntil)
            {
                timeoutUntil = user.TimedOutUntil.Value;
            }

            try
            {
                await user.ModifyAsync(properties =>
                {
                    properties.TimedOutUntil = timeoutUntil;
                });
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Failed to time out spammer {userId}: {exception.Message}");
            }
        }

        private async Task SendSpamReport(SocketGuild guild, ulong userId, string messageContent)
        {
            if (SpamReportChannelID == 0 || guild.GetChannel(SpamReportChannelID) is not IMessageChannel reportChannel)
            {
                return;
            }

            var prefix = $"<@{userId}> {userId} Automated spam detection, messages deleted, issued time out. Message content:{Environment.NewLine}";
            var content = messageContent;
            var maxContentLength = Math.Max(0, 2000 - prefix.Length);
            if (content.Length > maxContentLength)
            {
                content = content.Substring(0, maxContentLength);
            }

            try
            {
                await reportChannel.SendMessageAsync(
                    prefix + content,
                    allowedMentions: new AllowedMentions { UserIds = new List<ulong> { userId } });
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Failed to send spam report for {userId}: {exception.Message}");
            }
        }

        private static string? NormalizeSpamContent(string content)
        {
            var normalized = content.Replace("\r\n", "\n").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }


        private async Task OnReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {   
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

        public static async Task PublishAnnouncement(ulong where, ulong what)
        {
            try {
                var message = await (Client.GetGuild(BotService.BLServerID).GetChannel(where) as ISocketMessageChannel)?.GetMessageAsync(what);
                var announcement = message as RestUserMessage;
                if (announcement != null) {
                    await announcement.CrosspostAsync();
                }
            } catch { }
        }

        public static async Task<int?> GetChannelPosition(ulong where) {
            return Client.GetGuild(BotService.BLServerID)?.GetChannel(where)?.Position;
        }

        public static async Task UpdateChannelOrder(ulong where, int order) {
            if (Client.GetGuild(BotService.BLServerID) == null) return;

            var channel = Client.GetGuild(BotService.BLServerID).GetChannel(where);

            try {
                await channel.ModifyAsync(props => {
                    props.Position = order;
                });
            } catch (Exception e) { 
                Console.WriteLine($"UpdateChannelOrder EXCEPTION {e.Message} {e.StackTrace}");
            }
        }
    }
}
