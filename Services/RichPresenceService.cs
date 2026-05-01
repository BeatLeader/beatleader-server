using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_Server.Services
{
    public class RichPresenceService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RichPresenceService> _logger;
        private readonly SemaphoreSlim _wsSendLock = new(1, 1);

        public RichPresenceService(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            ILogger<RichPresenceService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await MarkAllOffline();

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await ConnectAndListen(stoppingToken);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Rich presence socket disconnected, reconnecting in 10s");
                }

                if (!stoppingToken.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }

        private async Task MarkAllOffline()
        {
            try {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppContext>();

                await context.Players
                    .Where(p => p.RichPresence != null && p.RichPresence.ActivityStatus != RichPresenceActivityStatus.Offline)
                    .Select(p => p.RichPresence!)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ActivityStatus, RichPresenceActivityStatus.Offline));

                _logger.LogInformation("Marked all players as offline");
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to mark all players offline");
            }
        }

        private async Task ConnectAndListen(CancellationToken stoppingToken)
        {
            string? socketUrl = _configuration.GetValue<string>("ReplaySocketsUrl");
            string? secret = _configuration.GetValue<string>("PresenceSecret");

            if (string.IsNullOrEmpty(socketUrl) || string.IsNullOrEmpty(secret)) {
                _logger.LogWarning("ReplaySocketsUrl or PresenceSecret not configured, rich presence disabled");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                return;
            }

            var wsUrl = socketUrl.TrimEnd('/') + "/stream/richpresence/listen?secret=" + Uri.EscapeDataString(secret);
            wsUrl = wsUrl.Replace("https://", "wss://").Replace("http://", "ws://");

            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await ws.ConnectAsync(new Uri(wsUrl), stoppingToken);

            _logger.LogInformation("Connected to replay sockets for rich presence");

            var buffer = new byte[64 * 1024];
            using var messageStream = new MemoryStream();

            while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested) {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);

                if (result.MessageType == WebSocketMessageType.Close) break;

                if (result.MessageType == WebSocketMessageType.Text) {
                    messageStream.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage) continue;

                    var json = Encoding.UTF8.GetString(messageStream.ToArray());
                    messageStream.SetLength(0);

                    await ProcessMessage(ws, json);
                }
            }
        }

        private async Task ProcessMessage(ClientWebSocket ws, string json)
        {
            try {
                var obj = JObject.Parse(json);
                var type = obj["Type"]?.ToString();

                switch (type) {
                    case "presence":
                        await ProcessPresenceUpdate(obj);
                        break;
                    case "authRequest":
                        await ProcessAuthRequest(ws, obj);
                        break;
                    case "mapRequest":
                        await ProcessMapRequest(ws, obj);
                        break;
                    case "playerIdToMain":
                        await ProcessPlayerIdToMain(ws, obj);
                        break;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to process socket message");
            }
        }

        private async Task ProcessPresenceUpdate(JObject obj)
        {
            var updates = obj["Updates"]?.ToObject<List<PresenceUpdate>>();
            if (updates == null || updates.Count == 0) return;

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppContext>();

            foreach (var update in updates) {
                if (string.IsNullOrEmpty(update.PlayerId)) continue;
                if (!Enum.TryParse<RichPresenceActivityStatus>(update.Status, out var activityStatus)) continue;

                var player = await context.Players
                    .Include(p => p.RichPresence)
                    .FirstOrDefaultAsync(p => p.Id == update.PlayerId);

                if (player == null) continue;

                if (player.RichPresence == null) {
                    player.RichPresence = new RichPresence { ActivityStatus = activityStatus };
                } else {
                    player.RichPresence.ActivityStatus = activityStatus;
                }
            }

            await context.SaveChangesAsync();
        }

        private async Task ProcessAuthRequest(ClientWebSocket ws, JObject obj)
        {
            var requestId = obj["RequestId"]?.ToString();
            var playerId = obj["PlayerId"]?.ToString();
            var viewerId = obj["ViewerId"]?.ToString();
            var action = obj["Action"]?.ToString();

            bool allowed = true;
            string? error = null;

            try {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var player = await context.Players
                    .Include(p => p.ProfileSettings)
                    .FirstOrDefaultAsync(p => p.Id == playerId);

                if (player?.ProfileSettings != null) {
                    if (action == "status") {
                        allowed = player.ProfileSettings.RichPresenceEnabled;
                        if (!allowed) error = "Rich presence is disabled for this player";
                    } else if (action == "replay") {
                        switch (player.ProfileSettings.StreamingViewPermissions) {
                            case ReplayStreaming.Public:
                                allowed = true;
                                break;
                            case ReplayStreaming.Friends:
                                if (string.IsNullOrEmpty(viewerId)) {
                                    allowed = false;
                                    error = "Authentication required to view this stream";
                                } else {
                                    var isFriend = await context.Friends
                                        .Where(f => f.Id == playerId)
                                        .SelectMany(f => f.Friends)
                                        .AnyAsync(f => f.Id == viewerId);
                                    allowed = isFriend;
                                    if (!allowed) error = "Only friends can view this stream";
                                }
                                break;
                            case ReplayStreaming.Private:
                                allowed = false;
                                error = "Streaming is set to private";
                                break;
                        }
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to check auth for player {PlayerId}", playerId);
                allowed = true;
            }

            var response = JsonConvert.SerializeObject(new {
                Type = "authResponse",
                RequestId = requestId,
                Allowed = allowed,
                Error = error
            });
            var bytes = Encoding.UTF8.GetBytes(response);

            await _wsSendLock.WaitAsync();
            try {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            } finally {
                _wsSendLock.Release();
            }
        }

        private async Task ProcessMapRequest(ClientWebSocket ws, JObject obj)
        {
            var requestId = obj["RequestId"]?.ToString();
            var hash = obj["Hash"]?.ToString();
            var difficulty = obj["Difficulty"]?.ToString();
            var mode = obj["Mode"]?.ToString();

            object? map = null;

            try {
                if (!string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(difficulty) && !string.IsNullOrEmpty(mode)) {
                    if (hash.Length >= 40) {
                        hash = hash.Substring(0, 40);
                    }

                    using var scope = _serviceScopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    map = await context
                        .Leaderboards
                        .Where(l => l.Song.LowerHash == hash.ToLower()
                            && l.Difficulty.ModeName == mode
                            && l.Difficulty.DifficultyName == difficulty)
                        .Select(l => new {
                            Id = l.Id,
                            Song = new {
                                Id = l.Song.Id,
                                Hash = l.Song.LowerHash,
                                Name = l.Song.Name,
                                SubName = l.Song.SubName,
                                Author = l.Song.Author,
                                Mapper = l.Song.Mapper,
                                MapperId = l.Song.MapperId,
                                CollaboratorIds = l.Song.CollaboratorIds,
                                CoverImage = l.Song.CoverImage,
                                FullCoverImage = l.Song.FullCoverImage,
                                Bpm = l.Song.Bpm,
                                Duration = l.Song.Duration,
                            },
                            Difficulty = new {
                                Id = l.Difficulty.Id,
                                Value = l.Difficulty.Value,
                                Mode = l.Difficulty.Mode,
                                DifficultyName = l.Difficulty.DifficultyName,
                                ModeName = l.Difficulty.ModeName,
                                Status = l.Difficulty.Status,
                                Stars = l.Difficulty.Stars,
                                PassRating = l.Difficulty.PassRating,
                                AccRating = l.Difficulty.AccRating,
                                TechRating = l.Difficulty.TechRating,
                                Njs = l.Difficulty.Njs,
                                Nps = l.Difficulty.Nps,
                                Notes = l.Difficulty.Notes,
                                Bombs = l.Difficulty.Bombs,
                                Walls = l.Difficulty.Walls,
                                MaxScore = l.Difficulty.MaxScore,
                                Duration = l.Difficulty.Duration,
                            }
                        })
                        .FirstOrDefaultAsync();
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to fetch map for {Hash}/{Difficulty}/{Mode}", hash, difficulty, mode);
            }

            var response = JsonConvert.SerializeObject(new {
                Type = "mapResponse",
                RequestId = requestId,
                Map = map
            });
            var bytes = Encoding.UTF8.GetBytes(response);

            await _wsSendLock.WaitAsync();
            try {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            } finally {
                _wsSendLock.Release();
            }
        }

        private async Task ProcessPlayerIdToMain(ClientWebSocket ws, JObject obj)
        {
            var requestId = obj["RequestId"]?.ToString();
            var localId = obj["LocalId"]?.ToString();

            string? mainId = localId;

            try {
                if (!string.IsNullOrEmpty(localId)) {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    mainId = await context.PlayerIdToMain(localId);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to resolve player ID {LocalId}", localId);
            }

            var response = JsonConvert.SerializeObject(new {
                Type = "playerIdToMainResponse",
                RequestId = requestId,
                MainId = mainId
            });
            var bytes = Encoding.UTF8.GetBytes(response);

            await _wsSendLock.WaitAsync();
            try {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            } finally {
                _wsSendLock.Release();
            }
        }
    }

    public class PresenceUpdate
    {
        public string PlayerId { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
