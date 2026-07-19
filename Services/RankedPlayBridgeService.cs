using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_Server.Services {
    // Bridge between the main server and BeatLeader-RankedPlay-Sockets.
    // Speaks the message set described in docs/RankedPlay.md §4.1.C / §8 — keep both in sync.
    public class RankedPlayBridgeService : BackgroundService {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RankedPlayBridgeService> _logger;
        private readonly SemaphoreSlim _wsSendLock = new(1, 1);

        private static int _lastPlayersInQueue;
        private static int _lastActiveMatches;
        private static readonly ConcurrentDictionary<string, string> _activeRankedPlayMatches = new();

        public static int PlayersInQueue => _lastPlayersInQueue;
        public static int ActiveMatches => _lastActiveMatches;
        public static bool IsPlayerInActiveRankedPlay(string playerId) =>
            !string.IsNullOrEmpty(playerId) && _activeRankedPlayMatches.ContainsKey(playerId);

        public RankedPlayBridgeService(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            ILogger<RankedPlayBridgeService> logger) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await ConnectAndListen(stoppingToken);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Ranked play bridge disconnected, reconnecting in 10s");
                }

                if (!stoppingToken.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }

        private async Task ConnectAndListen(CancellationToken stoppingToken) {
            string? socketUrl = _configuration.GetValue<string>("RankedPlaySocketsUrl");
            string? secret = _configuration.GetValue<string>("RankedPlayBridgeSecret");

            if (string.IsNullOrEmpty(socketUrl) || string.IsNullOrEmpty(secret)) {
                _logger.LogWarning("RankedPlaySocketsUrl or RankedPlayBridgeSecret not configured");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                return;
            }

            var wsUrl = socketUrl.TrimEnd('/') + "/rankedplay/bridge?secret=" + Uri.EscapeDataString(secret);
            wsUrl = wsUrl.Replace("https://", "wss://").Replace("http://", "ws://");

            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await ws.ConnectAsync(new Uri(wsUrl), stoppingToken);

            _logger.LogInformation("Connected to ranked play socket server bridge");

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

        private async Task ProcessMessage(ClientWebSocket ws, string json) {
            try {
                var obj = JObject.Parse(json);
                var type = obj["Type"]?.ToString();

                switch (type) {
                    case "playerProfileRequest":
                        await ProcessPlayerProfileRequest(ws, obj);
                        break;
                    case "handDealtRequest":
                        await ProcessHandDealtRequest(ws, obj);
                        break;
                    case "mapReplacementRequest":
                        await ProcessMapReplacementRequest(ws, obj);
                        break;
                    case "dodgePenalty":
                        await ProcessDodgePenalty(obj);
                        break;
                    case "matchResult":
                        await ProcessMatchResult(ws, obj);
                        break;
                    case "queueStatus":
                        _lastPlayersInQueue = obj["PlayersInQueue"]?.Value<int>() ?? 0;
                        _lastActiveMatches = obj["ActiveMatches"]?.Value<int>() ?? 0;
                        break;
                    case "rankedPlayVisibility":
                        await ProcessRankedPlayVisibility(ws, obj);
                        break;
                    default:
                        _logger.LogDebug("Unhandled ranked play bridge message type: {Type}", type);
                        break;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to process ranked play bridge message");
            }
        }

        // ===== Player profile lookup (matchmaking + queue join) =====

        private async Task ProcessPlayerProfileRequest(ClientWebSocket ws, JObject obj) {
            var requestId = obj["RequestId"]?.ToString();
            var playerId = obj["PlayerId"]?.ToString();

            if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(playerId)) return;

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppContext>();

            var mainId = await context.PlayerIdToMain(playerId);
            var player = await context.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == mainId);

            if (player == null) {
                player = await PlayerControllerHelper.GetLazy(context, _configuration, mainId);
            }

            if (player == null) {
                await SendResponse(ws, new {
                    Type = "playerProfileResponse",
                    RequestId = requestId,
                    PlayerId = playerId,
                    MMR = RankedPlayMMRService.SeedMMRFromGlobalRank(0),
                    Tier = nameof(RankedPlayTier.Unranked),
                    TierDivision = 0,
                    PlayerName = "Unknown",
                    IsCalibrated = false,
                    GlobalRank = 0,
                    IsBot = false
                });
                return;
            }

            // Test-bot detection (§4.2 follow-up). Bots use their login-encoded rank
            // for seeding and are matchmaker-restricted (no bot-vs-bot pairings).
            bool isBot = RankedPlayBotUtils.IsBotName(player.Name);
            int effectiveRank = RankedPlayBotUtils.GetDesignatedRank(player.Name) ?? player.Rank;

            var season = await RankedPlayControllerHelper.GetActiveSeason(context);
            if (season == null) {
                // No active season — we still owe matchmaking a seed estimate so its expanding
                // window has something to work with; this is the same value GetOrCreateProfile
                // would use once a season exists.
                await SendResponse(ws, new {
                    Type = "playerProfileResponse",
                    RequestId = requestId,
                    PlayerId = player.Id,
                    MMR = RankedPlayMMRService.SeedMMRFromGlobalRank(effectiveRank),
                    Tier = nameof(RankedPlayTier.Unranked),
                    TierDivision = 0,
                    PlayerName = player.Name,
                    IsCalibrated = false,
                    GlobalRank = effectiveRank,
                    IsBot = isBot
                });
                return;
            }

            var previousSeasonId = await RankedPlayControllerHelper.GetPreviousSeasonId(context, season.Id);
            var profile = await RankedPlayMMRService.GetOrCreateProfile(
                context, player.Id, season.Id, previousSeasonId);

            // We send raw MMR even for uncalibrated players — the socket server's
            // matchmaker uses it for the expanding-window pairing (§6.1).
            // The mod hides it in the UI via the REST profile endpoint, not via this bridge.
            await SendResponse(ws, new {
                Type = "playerProfileResponse",
                RequestId = requestId,
                PlayerId = player.Id,
                MMR = profile.MMR,
                Tier = profile.Tier.ToString(),
                TierDivision = profile.TierDivision,
                PlayerName = player.Name,
                IsCalibrated = profile.IsCalibrated,
                CalibrationMatchesPlayed = profile.CalibrationMatchesPlayed,
                GlobalRank = effectiveRank,
                IsBot = isBot
            });
        }

        // ===== Map pool — initial 5-card hand (round 1) =====

        private async Task ProcessHandDealtRequest(ClientWebSocket ws, JObject obj) {
            var requestId = obj["RequestId"]?.ToString();
            var playerAId = obj["PlayerAId"]?.ToString();
            var playerBId = obj["PlayerBId"]?.ToString();
            var averageMMR = obj["AverageMMR"]?.Value<float>() ?? RankedPlayMMRService.BaseSeed;

            if (string.IsNullOrEmpty(requestId)) return;

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppContext>();

            var season = await RankedPlayControllerHelper.GetActiveSeason(context);
            if (season == null) {
                await SendResponse(ws, new {
                    Type = "handDealtResponse",
                    RequestId = requestId,
                    Maps = Array.Empty<object>()
                });
                return;
            }

            var candidates = await RankedPlayMapPoolService.DealInitialHand(
                context, averageMMR, season.Id,
                playerAId ?? "", playerBId ?? "");

            await SendResponse(ws, new {
                Type = "handDealtResponse",
                RequestId = requestId,
                Maps = ToMapResponseList(candidates)
            });
        }

        // ===== Map pool — refill draws (between rounds + after discards) =====

        private async Task ProcessMapReplacementRequest(ClientWebSocket ws, JObject obj) {
            var requestId = obj["RequestId"]?.ToString();
            var averageMMR = obj["AverageMMR"]?.Value<float>() ?? RankedPlayMMRService.BaseSeed;
            var count = obj["Count"]?.Value<int>() ?? 0;
            var excludedLeaderboardIds = obj["ExcludedLeaderboardIds"]?.ToObject<List<string>>() ?? new List<string>();
            var excludedMapperIds = obj["ExcludedMapperIds"]?.ToObject<List<int>>() ?? new List<int>();

            if (string.IsNullOrEmpty(requestId)) return;

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppContext>();

            var season = await RankedPlayControllerHelper.GetActiveSeason(context);
            if (season == null) {
                await SendResponse(ws, new {
                    Type = "mapReplacementResponse",
                    RequestId = requestId,
                    Maps = Array.Empty<object>()
                });
                return;
            }

            var candidates = await RankedPlayMapPoolService.DrawReplacements(
                context, averageMMR, season.Id, count,
                excludedLeaderboardIds, excludedMapperIds);

            await SendResponse(ws, new {
                Type = "mapReplacementResponse",
                RequestId = requestId,
                Maps = ToMapResponseList(candidates)
            });
        }

        // ===== Dodge penalty (anti-abuse hook, §6.3) =====

        private async Task ProcessDodgePenalty(JObject obj) {
            var playerId = obj["PlayerId"]?.ToString();
            var penalty = obj["Penalty"]?.Value<float>() ?? 10f;

            if (string.IsNullOrEmpty(playerId)) return;

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppContext>();

            var season = await RankedPlayControllerHelper.GetActiveSeason(context);
            if (season == null) return;

            var previousSeasonId = await RankedPlayControllerHelper.GetPreviousSeasonId(context, season.Id);
            var profile = await RankedPlayMMRService.GetOrCreateProfile(context, playerId, season.Id, previousSeasonId);
            RankedPlayMMRService.ApplyDodgePenalty(profile, penalty);
            await context.SaveChangesAsync();
        }

        // ===== Match result (best-of-3) =====

        private async Task ProcessMatchResult(ClientWebSocket ws, JObject obj) {
            var matchId = obj["MatchId"]?.ToString() ?? "";
            var playerAId = obj["PlayerAId"]?.ToString() ?? "";
            var playerBId = obj["PlayerBId"]?.ToString() ?? "";
            var resultStr = obj["Result"]?.ToString() ?? nameof(RankedPlayMatchResult.Completed);
            var duration = obj["Duration"]?.Value<float>() ?? 0;

            var games = obj["Games"]?.ToObject<List<BridgeGameDto>>() ?? new List<BridgeGameDto>();

            if (string.IsNullOrEmpty(playerAId) || string.IsNullOrEmpty(playerBId)) {
                _logger.LogWarning("matchResult {MatchId} missing player IDs", matchId);
                return;
            }

            if (!Enum.TryParse<RankedPlayMatchResult>(resultStr, out var matchResult)) {
                _logger.LogWarning("matchResult {MatchId} has unrecognised Result value '{Value}', defaulting to Completed",
                    matchId, resultStr);
                matchResult = RankedPlayMatchResult.Completed;
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppContext>();

            var season = await RankedPlayControllerHelper.GetActiveSeason(context);
            if (season == null) {
                _logger.LogError("No active season when processing match result {MatchId}", matchId);
                return;
            }

            var previousSeasonId = await RankedPlayControllerHelper.GetPreviousSeasonId(context, season.Id);
            var profileA = await RankedPlayMMRService.GetOrCreateProfile(context, playerAId, season.Id, previousSeasonId);
            var profileB = await RankedPlayMMRService.GetOrCreateProfile(context, playerBId, season.Id, previousSeasonId);

            // Per-game winnerId values come from the socket server (which has the live
            // score state). We recount the totals locally so the aggregate is always
            // self-consistent with the games we're about to persist.
            int playerAGamesWon = games.Count(g => g.WinnerId == playerAId);
            int playerBGamesWon = games.Count(g => g.WinnerId == playerBId);
            int drawnGames = games.Count(g => string.IsNullOrEmpty(g.WinnerId));

            var mmrResult = RankedPlayMMRService.ApplyMatchResult(
                profileA, profileB, playerAGamesWon, playerBGamesWon, matchResult);

            var match = new RankedPlayMatch {
                SeasonId = season.Id,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                PlayerAPreMatchMMR = mmrResult.PlayerAPreMatchMMR,
                PlayerBPreMatchMMR = mmrResult.PlayerBPreMatchMMR,
                PlayerAGamesWon = playerAGamesWon,
                PlayerBGamesWon = playerBGamesWon,
                DrawnGames = drawnGames,
                WinnerId = mmrResult.WinnerId,
                Result = matchResult,
                PlayerAMMRChange = mmrResult.PlayerAMMRChange,
                PlayerBMMRChange = mmrResult.PlayerBMMRChange,
                ProfileA = profileA,
                ProfileB = profileB,
                Timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Duration = duration,
                Games = games.Select(g => new RankedPlayGame {
                    RoundNumber = g.RoundNumber,
                    HandJson = g.HandJson ?? "[]",
                    PlayerADiscardLeaderboardId = g.PlayerADiscardLeaderboardId,
                    PlayerBDiscardLeaderboardId = g.PlayerBDiscardLeaderboardId,
                    ReplacementsJson = g.ReplacementsJson ?? "[]",
                    PickerId = g.PickerId,
                    LeaderboardId = g.LeaderboardId,
                    PlayerAScore = g.PlayerAScore,
                    PlayerBScore = g.PlayerBScore,
                    PlayerAFinalScore = g.PlayerAFinalScore,
                    PlayerBFinalScore = g.PlayerBFinalScore,
                    PlayerAAccuracy = g.PlayerAAccuracy,
                    PlayerBAccuracy = g.PlayerBAccuracy,
                    PlayerAFailed = g.PlayerAFailed,
                    PlayerBFailed = g.PlayerBFailed,
                    PlayerAForfeit = g.PlayerAForfeit,
                    PlayerBForfeit = g.PlayerBForfeit,
                    PlayerAReplay = g.PlayerAReplay,
                    PlayerBReplay = g.PlayerBReplay,
                    WinnerId = string.IsNullOrEmpty(g.WinnerId) ? null : g.WinnerId
                }).ToList()
            };

            context.RankedPlayMatches.Add(match);
            await context.SaveChangesAsync();

            await SendResponse(ws, new {
                Type = "matchResultAck",
                MatchId = matchId,
                PlayerAMMRChange = mmrResult.PlayerAMMRChange,
                PlayerBMMRChange = mmrResult.PlayerBMMRChange,
                PlayerANewMMR = profileA.MMR,
                PlayerBNewMMR = profileB.MMR,
                PlayerAGamesWon = playerAGamesWon,
                PlayerBGamesWon = playerBGamesWon,
                // Post-match calibration state (§5.6) so the mod can decide
                // whether to surface the new MMR or render a placement-progress
                // indicator on the results screen instead.
                PlayerAIsCalibrated = profileA.IsCalibrated,
                PlayerACalibrationMatchesPlayed = profileA.CalibrationMatchesPlayed,
                PlayerBIsCalibrated = profileB.IsCalibrated,
                PlayerBCalibrationMatchesPlayed = profileB.CalibrationMatchesPlayed
            });
        }

        // ===== Active-match visibility (used by other features to avoid stepping on RP players) =====

        private async Task ProcessRankedPlayVisibility(ClientWebSocket ws, JObject obj) {
            var requestId = obj["RequestId"]?.ToString();
            var matchId = obj["MatchId"]?.ToString();
            var playerIds = obj["PlayerIds"]?.ToObject<List<string>>() ?? new List<string>();
            var isActive = obj["IsActive"]?.Value<bool>() ?? false;

            if (!string.IsNullOrEmpty(matchId)) {
                foreach (var playerId in playerIds.Where(id => !string.IsNullOrEmpty(id))) {
                    if (isActive) {
                        _activeRankedPlayMatches[playerId] = matchId;
                    } else if (_activeRankedPlayMatches.TryGetValue(playerId, out var currentMatchId) && currentMatchId == matchId) {
                        _activeRankedPlayMatches.TryRemove(playerId, out _);
                    }
                }
            }

            if (!string.IsNullOrEmpty(requestId)) {
                await SendResponse(ws, new {
                    Type = "rankedPlayVisibilityAck",
                    RequestId = requestId
                });
            }
        }

        // ===== Helpers =====

        private static object[] ToMapResponseList(List<MapCandidate> candidates) {
            return candidates.Select(c => (object)new {
                LeaderboardId = c.LeaderboardId,
                SongName = c.SongName,
                SongAuthor = c.SongAuthor,
                Mapper = c.SongMapper,
                MapperId = c.MapperId,
                CoverImage = c.CoverImage,
                DownloadUrl = c.DownloadUrl,
                Hash = c.SongHash,
                Difficulty = c.DifficultyName,
                Mode = c.ModeName,
                Stars = c.Stars,
                Duration = c.Duration
            }).ToArray();
        }

        private async Task SendResponse(ClientWebSocket ws, object response) {
            var json = JsonConvert.SerializeObject(response);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _wsSendLock.WaitAsync();
            try {
                if (ws.State == WebSocketState.Open) {
                    await ws.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text, true,
                        CancellationToken.None);
                }
            } finally {
                _wsSendLock.Release();
            }
        }

        // Bridge DTO mirroring RankedPlayGame for incoming matchResult deserialization.
        // Names match the socket server's protocol — keep in sync with
        // BeatLeader-RankedPlay-Sockets/Models/ProtocolMessages.cs.
        private class BridgeGameDto {
            public int RoundNumber { get; set; }
            public string? HandJson { get; set; }
            public string? PlayerADiscardLeaderboardId { get; set; }
            public string? PlayerBDiscardLeaderboardId { get; set; }
            public string? ReplacementsJson { get; set; }
            public string? PickerId { get; set; }
            public string? LeaderboardId { get; set; }
            public int PlayerAScore { get; set; }
            public int PlayerBScore { get; set; }
            public int PlayerAFinalScore { get; set; }
            public int PlayerBFinalScore { get; set; }
            public float PlayerAAccuracy { get; set; }
            public float PlayerBAccuracy { get; set; }
            public bool PlayerAFailed { get; set; }
            public bool PlayerBFailed { get; set; }
            public bool PlayerAForfeit { get; set; }
            public bool PlayerBForfeit { get; set; }
            public string? PlayerAReplay { get; set; }
            public string? PlayerBReplay { get; set; }
            public string? WinnerId { get; set; }
        }
    }
}
