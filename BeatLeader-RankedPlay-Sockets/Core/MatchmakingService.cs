using BeatLeader_RankedPlay_Sockets.Models;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_RankedPlay_Sockets.Core {
    // Orchestrates best-of-3 ranked play matches (docs/RankedPlay.md §4.1.C / §8).
    //
    // Architecture overview:
    //   - Tick loop (every 5s) runs matchmaking + phase-timeout checks.
    //   - Per-player message handlers drive most state transitions inline.
    //   - When a state change requires bridge I/O (initial hand, replacement draws,
    //     match result), the matchmaking service awaits the bridge round-trip and
    //     uses Interlocked-style transition guards on MatchSession so two paths
    //     racing the same transition (e.g. both-discards-submitted vs timeout) don't
    //     both fire the side effects.
    public class MatchmakingService : BackgroundService {

        private readonly object _queueLock = new();
        private readonly List<QueueEntry> _queue = new();
        private readonly ConcurrentDictionary<string, MatchSession> _activeMatches = new();
        private readonly ConcurrentDictionary<string, string> _playerToMatch = new();
        private readonly ConcurrentDictionary<string, ConnectedPlayer> _connectedPlayers = new();
        private int _lastSnapshotBroadcastUnix;

        private const int QueueSnapshotSampleSize = 10;
        private const int QueueSnapshotBroadcastIntervalSeconds = 10;

        private static readonly SemaphoreSlim _bridgeSendLock = new(1, 1);
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<BridgePlayerProfileMessage>> _pendingProfileRequests = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeHandDealtMessage>> _pendingHandRequests = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeMapReplacementMessage>> _pendingReplacementRequests = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeMatchResultAck>> _pendingMatchResultAcks = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeRankedPlayVisibilityAckMessage>> _pendingVisibilityAcks = new();

        private WebSocket? _bridgeSocket;
        private readonly object _bridgeLock = new();

        private readonly ConcurrentDictionary<string, int> _queueCooldowns = new();
        private readonly ConcurrentDictionary<string, int> _dodgeBans = new();

        private const int QueueCooldownSeconds = 5;
        private const int DodgeBanSeconds = 120;

        private readonly ILogger<MatchmakingService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ReplaySocketSubscriber _replaySubscriber;

        public int QueueCount {
            get { lock (_queueLock) return _queue.Count; }
        }

        public int ActiveMatchCount => _activeMatches.Count;

        public MatchmakingService(
            ILogger<MatchmakingService> logger,
            IConfiguration configuration,
            ReplaySocketSubscriber replaySubscriber) {
            _logger = logger;
            _configuration = configuration;
            _replaySubscriber = replaySubscriber;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _logger.LogInformation("MatchmakingService started");

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await RunMatchmakingCycle();
                    await CheckPhaseTimeouts();
                    await MaybeBroadcastQueueSnapshot();
                } catch (Exception ex) {
                    _logger.LogError(ex, "Matchmaking cycle error");
                }

                await Task.Delay(5000, stoppingToken);
            }
        }

        // ── Connection Registry ──

        public async Task RegisterConnection(BridgePlayerProfileMessage profile, WebSocket socket) {
            _connectedPlayers[profile.PlayerId] = new ConnectedPlayer {
                PlayerId = profile.PlayerId,
                PlayerName = profile.PlayerName,
                Tier = profile.Tier,
                TierDivision = profile.TierDivision,
                Socket = socket
            };

            await MatchSession.SendMessage(socket, BuildQueueSnapshot());
        }

        public void UnregisterConnection(string playerId, WebSocket socket) {
            if (_connectedPlayers.TryGetValue(playerId, out var existing) && existing.Socket == socket) {
                _connectedPlayers.TryRemove(playerId, out _);
            }
        }

        // ── Queue Management ──

        public async Task<bool> JoinQueue(QueueEntry entry) {
            lock (_queueLock) {
                if (_queue.Any(e => e.PlayerId == entry.PlayerId)) return false;
            }

            if (_playerToMatch.ContainsKey(entry.PlayerId)) return false;

            var now = Time.UnixNow();

            if (_dodgeBans.TryGetValue(entry.PlayerId, out int banExpiry) && now < banExpiry) {
                await MatchSession.SendMessage(entry.Socket, new ErrorMessage {
                    Type = "error",
                    Error = $"Queue dodge ban active for {banExpiry - now} more seconds"
                });
                return false;
            }

            if (_queueCooldowns.TryGetValue(entry.PlayerId, out int cooldownExpiry) && now < cooldownExpiry) {
                await MatchSession.SendMessage(entry.Socket, new ErrorMessage {
                    Type = "error",
                    Error = "Please wait a few seconds before re-queuing"
                });
                return false;
            }

            lock (_queueLock) {
                _queue.Add(entry);
            }

            await MatchSession.SendMessage(entry.Socket, new QueueStatusMessage {
                Type = "queueStatus",
                Status = "joined",
                QueuePosition = GetQueuePosition(entry.PlayerId),
                EstimatedWaitSeconds = 30
            });

            await BroadcastQueueStatus();
            await BroadcastQueueSnapshot();
            return true;
        }

        public async Task LeaveQueue(string playerId) {
            bool wasInQueue;
            lock (_queueLock) {
                wasInQueue = _queue.RemoveAll(e => e.PlayerId == playerId) > 0;
            }
            if (wasInQueue) {
                _queueCooldowns[playerId] = Time.UnixNow() + QueueCooldownSeconds;
            }
            await BroadcastQueueStatus();
            if (wasInQueue) {
                await BroadcastQueueSnapshot();
            }
        }

        public bool IsInQueue(string playerId) {
            lock (_queueLock) {
                return _queue.Any(e => e.PlayerId == playerId);
            }
        }

        public string? GetMatchIdForPlayer(string playerId) {
            _playerToMatch.TryGetValue(playerId, out var matchId);
            return matchId;
        }

        public MatchSession? GetMatch(string matchId) {
            _activeMatches.TryGetValue(matchId, out var match);
            return match;
        }

        private int GetQueuePosition(string playerId) {
            lock (_queueLock) {
                return _queue.FindIndex(e => e.PlayerId == playerId) + 1;
            }
        }

        // ── Matchmaking ──

        private async Task RunMatchmakingCycle() {
            List<QueueEntry> snapshot;
            lock (_queueLock) {
                snapshot = _queue
                    .Where(e => e.Socket.State == WebSocketState.Open)
                    .OrderBy(e => e.JoinedAt)
                    .ToList();

                _queue.RemoveAll(e => e.Socket.State != WebSocketState.Open);
            }

            // Bot-pairing rules (§4.2 — test bots only exist to fill the queue
            // for real players):
            //   - Bot vs bot is NEVER allowed.
            //   - Real vs real is preferred — try it first.
            //   - Real vs bot is only a fallback for real players who didn't
            //     find a real partner this cycle. Bots that don't find a real
            //     partner just stay in queue.
            var matched = new HashSet<string>();

            // Pass 1: real-vs-real.
            await PairAcrossQueue(
                snapshot,
                matched,
                acceptCandidate: (a, b) => !a.IsBot && !b.IsBot);

            // Pass 2: real-vs-bot, only for real players still unmatched.
            await PairAcrossQueue(
                snapshot,
                matched,
                acceptCandidate: (a, b) => !a.IsBot && b.IsBot);

            lock (_queueLock) {
                _queue.RemoveAll(e => matched.Contains(e.PlayerId));
            }

            if (matched.Count > 0) {
                await BroadcastQueueStatus();
                await BroadcastQueueSnapshot();
            }
        }

        /// <summary>
        /// Walks <paramref name="snapshot"/> and pairs each anchor with the
        /// closest-MMR opponent that passes <paramref name="acceptCandidate"/>
        /// AND mutually-accepts via the expanding-window check (<see cref="QueueEntry.EffectiveMinMMR"/>).
        /// Pairs are committed to <paramref name="matched"/> as they're created.
        /// </summary>
        private async Task PairAcrossQueue(
            List<QueueEntry> snapshot,
            HashSet<string> matched,
            System.Func<QueueEntry, QueueEntry, bool> acceptCandidate) {
            for (int i = 0; i < snapshot.Count; i++) {
                var anchor = snapshot[i];
                if (matched.Contains(anchor.PlayerId)) continue;

                QueueEntry? bestMatch = null;
                float bestDiff = float.MaxValue;

                for (int j = 0; j < snapshot.Count; j++) {
                    if (i == j) continue;
                    var candidate = snapshot[j];
                    if (matched.Contains(candidate.PlayerId)) continue;
                    if (!acceptCandidate(anchor, candidate)) continue;

                    bool anchorAccepts = candidate.MMR >= anchor.EffectiveMinMMR && candidate.MMR <= anchor.EffectiveMaxMMR;
                    bool candidateAccepts = anchor.MMR >= candidate.EffectiveMinMMR && anchor.MMR <= candidate.EffectiveMaxMMR;
                    if (!anchorAccepts || !candidateAccepts) continue;

                    float diff = Math.Abs(anchor.MMR - candidate.MMR);
                    if (diff < bestDiff) {
                        bestDiff = diff;
                        bestMatch = candidate;
                    }
                }

                if (bestMatch != null) {
                    bool success = await CreateMatch(anchor, bestMatch);
                    if (success) {
                        matched.Add(anchor.PlayerId);
                        matched.Add(bestMatch.PlayerId);
                    }
                }
            }
        }

        private async Task<bool> CreateMatch(QueueEntry playerA, QueueEntry playerB) {
            var hand = await RequestInitialHand(playerA, playerB);
            if (hand == null || hand.Count == 0) {
                _logger.LogWarning("No initial hand for {A} vs {B}", playerA.PlayerId, playerB.PlayerId);
                return false;
            }

            var match = new MatchSession(playerA, playerB);

            _activeMatches[match.MatchId] = match;
            _playerToMatch[playerA.PlayerId] = match.MatchId;
            _playerToMatch[playerB.PlayerId] = match.MatchId;

            var playerAInfo = MakePlayerInfo(playerA);
            var playerBInfo = MakePlayerInfo(playerB);

            await MatchSession.SendMessage(playerA.Socket, new MatchFoundMessage {
                Type = "matchFound",
                MatchId = match.MatchId,
                Opponent = playerBInfo,
                IsLowerRated = match.IsLowerRated(playerA.PlayerId)
            });

            await MatchSession.SendMessage(playerB.Socket, new MatchFoundMessage {
                Type = "matchFound",
                MatchId = match.MatchId,
                Opponent = playerAInfo,
                IsLowerRated = match.IsLowerRated(playerB.PlayerId)
            });

            match.StartRound(1, hand);
            await EmitHandDealt(match);
            match.OpenDiscardPhase();
            await EmitDiscardPhase(match);

            _logger.LogInformation("Match created: {MatchId} — {A} ({AMMR}) vs {B} ({BMMR})",
                match.MatchId, playerA.PlayerId, playerA.MMR, playerB.PlayerId, playerB.MMR);

            return true;
        }

        private static MatchPlayerInfo MakePlayerInfo(QueueEntry entry) {
            return new MatchPlayerInfo {
                PlayerId = entry.PlayerId,
                PlayerName = entry.PlayerName,
                MMR = entry.MMR,
                Tier = entry.Tier,
                TierDivision = entry.TierDivision,
                IsCalibrated = entry.IsCalibrated,
                CalibrationMatchesPlayed = entry.CalibrationMatchesPlayed
            };
        }

        // ── Tick-driven phase timeouts ──

        private async Task CheckPhaseTimeouts() {
            foreach (var kvp in _activeMatches) {
                var match = kvp.Value;

                try {
                    if (match.Phase == MatchPhase.DiscardPhase && match.DiscardDeadlinePassed && match.TryBeginDiscardReveal()) {
                        _ = AdvanceToDiscardReveal(match);
                        continue;
                    }

                    if (match.Phase == MatchPhase.PickPhase && match.PickDeadlinePassed && !match.PickSubmitted) {
                        match.AutoPick();
                        await EmitMapDecided(match);
                        continue;
                    }

                    if (match.Phase == MatchPhase.MapDownload && match.MapDownloadDeadlinePassed && match.TryBeginRoundResolution()) {
                        // Treat as a drawn round per §8.2 (download grace expired);
                        // ResolveRound on un-played state produces a 0-0 draw.
                        await ResolveAndAdvance(match);
                        continue;
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Phase timeout handler error for match {MatchId}", match.MatchId);
                }
            }
        }

        // ── Round flow ──

        private async Task AdvanceToDiscardReveal(MatchSession match) {
            try {
                var discards = match.GetDiscardsToApply();

                // Replacements have to honour the current hand's mappers + the per-match
                // played-set so we don't re-deal a song the players already saw.
                var excludeLeaderboards = match.Hand
                    .Select(m => m.LeaderboardId)
                    .Concat(match.PlayedLeaderboardIds)
                    .Distinct()
                    .ToList();
                var excludeMappers = match.Hand.Select(m => m.MapperId).Distinct().ToList();

                List<MapCandidateInfo> replacements = new();
                if (discards.Count > 0) {
                    var fetched = await RequestMapReplacements(match, discards.Count, excludeLeaderboards, excludeMappers);
                    if (fetched != null) replacements = fetched;
                }

                match.ApplyDiscardReveal(replacements);
                await EmitDiscardRevealed(match);

                match.OpenPickPhase();
                await EmitPickPhase(match);
            } catch (Exception ex) {
                _logger.LogError(ex, "Discard-reveal flow failed for match {MatchId}", match.MatchId);
                await CancelMatch(match, "Internal error during discard reveal");
            }
        }

        private async Task BeginNextRound(MatchSession match) {
            try {
                // Carry the surviving 4-card hand over (minus the map we just played).
                var lastRound = match.CompletedRounds[^1];
                var carryover = match.Hand
                    .Where(m => m.LeaderboardId != lastRound.LeaderboardId)
                    .ToList();

                int needed = 5 - carryover.Count;
                List<MapCandidateInfo> replacements = new();
                if (needed > 0) {
                    var excludeLeaderboards = carryover
                        .Select(m => m.LeaderboardId)
                        .Concat(match.PlayedLeaderboardIds)
                        .Distinct()
                        .ToList();
                    var excludeMappers = carryover.Select(m => m.MapperId).Distinct().ToList();

                    var fetched = await RequestMapReplacements(match, needed, excludeLeaderboards, excludeMappers);
                    if (fetched != null) replacements = fetched;
                }

                var newHand = new List<MapCandidateInfo>(carryover);
                newHand.AddRange(replacements);

                int nextRound = match.CurrentRound + 1;
                match.StartRound(nextRound, newHand);
                await EmitHandDealt(match);

                // Round 3 skips the discard step — by the time the series is
                // tied 1-1, the players have already seen 4 maps slip through
                // the hand, so we go straight to the lower-MMR pick.
                if (nextRound >= 3) {
                    match.OpenPickPhase();
                    await EmitPickPhase(match);
                } else {
                    match.OpenDiscardPhase();
                    await EmitDiscardPhase(match);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to begin next round for match {MatchId}", match.MatchId);
                await CancelMatch(match, "Internal error advancing rounds");
            }
        }

        private async Task ResolveAndAdvance(MatchSession match) {
            var round = match.ResolveRound();
            await EmitRoundResult(match, round);
            await EmitSeriesUpdate(match);

            if (match.IsSeriesOver) {
                match.MarkSeriesResolved();
                if (match.TryBeginSeriesFinalization()) {
                    await FinalizeMatch(match);
                }
            } else {
                await BeginNextRound(match);
            }
        }

        // ── Client → Server message handlers ──

        public async Task HandleDiscardMap(string playerId, string? leaderboardId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            bool accepted = match.ProcessDiscard(playerId, leaderboardId);
            if (!accepted) return;

            if (match.BothDiscardsSubmitted && match.TryBeginDiscardReveal()) {
                await AdvanceToDiscardReveal(match);
            }
        }

        public async Task HandlePickMap(string playerId, string leaderboardId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            bool accepted = match.ProcessPick(playerId, leaderboardId);
            if (!accepted) return;

            await EmitMapDecided(match);
        }

        public async Task HandleMapReady(string playerId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            match.MarkMapReady(playerId);

            if (match.Phase == MatchPhase.Countdown && match.TryBeginCountdown()) {
                _ = RunCountdown(match);
            }
        }

        public void HandlePlayerScore(string playerId, float accuracy, int score, float time, int combo, int mistakes, bool failed) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            if (match.Phase != MatchPhase.Playing) return;

            match.ApplyScore(playerId, accuracy, score, time, combo, mistakes, failed);

            // Read failed from the session — it's sticky (set once per round, cleared by
            // StartRound). The per-message bool is treated as "did this update report a
            // new fail?" but the HUD wants the cumulative state.
            bool cumulativeFailed = match.IsPlayerA(playerId) ? match.PlayerAFailed : match.PlayerBFailed;

            var opponentMsg = new OpponentScoreMessage {
                Type = "opponentScore",
                Accuracy = accuracy,
                Score = score,
                Time = time,
                Combo = combo,
                Mistakes = mistakes,
                Failed = cumulativeFailed
            };

            _ = MatchSession.SendMessage(match.GetOpponentSocket(playerId), opponentMsg);
        }

        public async Task HandlePlayerFailed(string playerId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            if (match.Phase != MatchPhase.Playing) return;

            // NF means the level keeps running — record the fail flag so the
            // score gets halved at resolution time, but DO NOT advance the round.
            match.MarkPlayerFailed(playerId);

            // Notify the opponent so their HUD can render the fail badge even if
            // they haven't received a score update yet this tick.
            var opponentMsg = new OpponentScoreMessage {
                Type = "opponentScore",
                Failed = true,
                // Mirror the last cached values so the message is self-contained.
                Accuracy = match.IsPlayerA(playerId) ? match.PlayerAAccuracy : match.PlayerBAccuracy,
                Score = match.IsPlayerA(playerId) ? match.PlayerAScore : match.PlayerBScore,
                Time = match.IsPlayerA(playerId) ? match.PlayerATime : match.PlayerBTime,
                Combo = 0,
                Mistakes = match.IsPlayerA(playerId) ? match.PlayerAMistakes : match.PlayerBMistakes
            };
            await MatchSession.SendMessage(match.GetOpponentSocket(playerId), opponentMsg);
        }

        public async Task HandlePlayerFinished(string playerId, float accuracy, int score) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            match.MarkPlayerFinished(playerId, accuracy, score);

            if (match.RoundReadyToResolve && match.TryBeginRoundResolution()) {
                await ResolveAndAdvance(match);
            }
        }

        public async Task HandleForfeitGame(string playerId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            // Only valid during an active round (Playing). Outside that, treat as
            // a no-op / silently ignore.
            if (match.Phase != MatchPhase.Playing) return;

            match.MarkPlayerForfeitGame(playerId);

            await MatchSession.SendMessage(match.GetPlayerSocket(playerId), new ForfeitRequestedMessage {
                Type = "forfeitRequested",
                ForfeitType = "game",
                RoundNumber = match.CurrentRound
            });
            await MatchSession.SendMessage(match.GetOpponentSocket(playerId), new ForfeitConfirmedMessage {
                Type = "forfeitConfirmed",
                OpponentForfeitType = "game",
                RoundNumber = match.CurrentRound
            });

            // Opponent may still be playing — round resolves only when they finish too
            // (or also forfeit). RoundReadyToResolve fires from MarkPlayerForfeitGame
            // only if the opponent was already finished.
            if (match.RoundReadyToResolve && match.TryBeginRoundResolution()) {
                await ResolveAndAdvance(match);
            }
        }

        public async Task HandleForfeitMatch(string playerId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            match.MarkPlayerForfeitMatch(playerId);

            await MatchSession.SendMessage(match.GetPlayerSocket(playerId), new ForfeitRequestedMessage {
                Type = "forfeitRequested",
                ForfeitType = "match",
                RoundNumber = match.CurrentRound
            });
            await MatchSession.SendMessage(match.GetOpponentSocket(playerId), new ForfeitConfirmedMessage {
                Type = "forfeitConfirmed",
                OpponentForfeitType = "match",
                RoundNumber = match.CurrentRound
            });

            // MarkPlayerForfeitMatch sets both sides as finished, so we can resolve
            // the current round + finalize immediately.
            if (match.TryBeginRoundResolution()) {
                await ResolveAndAdvance(match);
            }
        }

        public async Task HandlePlayerDisconnect(string playerId) {
            if (!_playerToMatch.TryGetValue(playerId, out var matchId)) return;
            if (!_activeMatches.TryGetValue(matchId, out var match)) return;

            bool noRoundsCompleted = match.CompletedRounds.Count == 0;
            bool inPlay = match.Phase == MatchPhase.Playing;

            // Dodge penalty when a player leaves during pre-play (HandDealt / Discard /
            // Pick / MapDownload / Countdown) without any round having been played. This
            // is the clear bad-faith case — actual mid-play network drops fall through to
            // the match-forfeit branch below.
            if (!inPlay && noRoundsCompleted) {
                _dodgeBans[playerId] = Time.UnixNow() + DodgeBanSeconds;
                await SendDodgePenalty(playerId, match.GetOpponent(playerId).PlayerId);

                var opponent = match.GetOpponent(playerId);
                await CancelMatch(match, "Opponent disconnected");
                await RequeuePlayerWithPriority(opponent);
                return;
            }

            // Any other disconnect = forfeit the whole series. We don't currently support
            // session reconnect, so partial-round disconnects would otherwise stall the
            // series waiting for messages from a gone player. Refinement TODO: §8.3's
            // < 30% in-play "cancel current round, continue" rule needs reconnect support.
            match.MarkPlayerForfeitMatch(playerId);
            await MatchSession.SendMessage(match.GetOpponentSocket(playerId), new ForfeitConfirmedMessage {
                Type = "forfeitConfirmed",
                OpponentForfeitType = "match",
                RoundNumber = match.CurrentRound
            });
            if (match.TryBeginRoundResolution()) {
                await ResolveAndAdvance(match);
            }
        }

        // ── Emit helpers (server → client) ──

        private async Task EmitHandDealt(MatchSession match) {
            var msgA = new HandDealtMessage {
                Type = "handDealt",
                RoundNumber = match.CurrentRound,
                Hand = new List<MapCandidateInfo>(match.Hand),
                YourGamesWon = match.PlayerAGamesWon,
                OpponentGamesWon = match.PlayerBGamesWon
            };
            var msgB = new HandDealtMessage {
                Type = "handDealt",
                RoundNumber = match.CurrentRound,
                Hand = new List<MapCandidateInfo>(match.Hand),
                YourGamesWon = match.PlayerBGamesWon,
                OpponentGamesWon = match.PlayerAGamesWon
            };
            await MatchSession.SendMessage(match.PlayerA.Socket, msgA);
            await MatchSession.SendMessage(match.PlayerB.Socket, msgB);
        }

        private async Task EmitDiscardPhase(MatchSession match) {
            int deadline = MatchSession.DiscardPhaseSeconds;
            var msg = new DiscardPhaseMessage {
                Type = "discardPhase",
                RoundNumber = match.CurrentRound,
                DeadlineSeconds = deadline
            };
            await MatchSession.SendMessage(match.PlayerA.Socket, msg);
            await MatchSession.SendMessage(match.PlayerB.Socket, msg);
        }

        private async Task EmitDiscardRevealed(MatchSession match) {
            var msgA = new DiscardRevealedMessage {
                Type = "discardRevealed",
                RoundNumber = match.CurrentRound,
                YourDiscardLeaderboardId = match.PlayerADiscard,
                OpponentDiscardLeaderboardId = match.PlayerBDiscard,
                Replacements = new List<MapCandidateInfo>(match.Replacements),
                NewHand = new List<MapCandidateInfo>(match.Hand)
            };
            var msgB = new DiscardRevealedMessage {
                Type = "discardRevealed",
                RoundNumber = match.CurrentRound,
                YourDiscardLeaderboardId = match.PlayerBDiscard,
                OpponentDiscardLeaderboardId = match.PlayerADiscard,
                Replacements = new List<MapCandidateInfo>(match.Replacements),
                NewHand = new List<MapCandidateInfo>(match.Hand)
            };
            await MatchSession.SendMessage(match.PlayerA.Socket, msgA);
            await MatchSession.SendMessage(match.PlayerB.Socket, msgB);
        }

        private async Task EmitPickPhase(MatchSession match) {
            int deadline = MatchSession.PickPhaseSeconds;
            await MatchSession.SendMessage(match.PlayerA.Socket, new PickPhaseMessage {
                Type = "pickPhase",
                RoundNumber = match.CurrentRound,
                PickerId = match.PickerId,
                IsYou = match.PickerId == match.PlayerA.PlayerId,
                DeadlineSeconds = deadline
            });
            await MatchSession.SendMessage(match.PlayerB.Socket, new PickPhaseMessage {
                Type = "pickPhase",
                RoundNumber = match.CurrentRound,
                PickerId = match.PickerId,
                IsYou = match.PickerId == match.PlayerB.PlayerId,
                DeadlineSeconds = deadline
            });
        }

        private async Task EmitMapDecided(MatchSession match) {
            if (match.DecidedMap == null) return;
            var msg = new MapDecidedMessage {
                Type = "mapDecided",
                RoundNumber = match.CurrentRound,
                Map = match.DecidedMap
            };
            await MatchSession.SendMessage(match.PlayerA.Socket, msg);
            await MatchSession.SendMessage(match.PlayerB.Socket, msg);
        }

        private async Task EmitRoundResult(MatchSession match, RoundData round) {
            var msgA = new RoundResultMessage {
                Type = "roundResult",
                RoundNumber = round.RoundNumber,
                LeaderboardId = round.LeaderboardId,
                YourScore = round.PlayerAScore,
                OpponentScore = round.PlayerBScore,
                YourFinalScore = round.PlayerAFinalScore,
                OpponentFinalScore = round.PlayerBFinalScore,
                YourAccuracy = round.PlayerAAccuracy,
                OpponentAccuracy = round.PlayerBAccuracy,
                YourFailed = round.PlayerAFailed,
                OpponentFailed = round.PlayerBFailed,
                YourForfeit = round.PlayerAForfeit,
                OpponentForfeit = round.PlayerBForfeit,
                WinnerId = round.WinnerId,
                IsYouWinner = round.WinnerId == match.PlayerA.PlayerId
            };
            var msgB = new RoundResultMessage {
                Type = "roundResult",
                RoundNumber = round.RoundNumber,
                LeaderboardId = round.LeaderboardId,
                YourScore = round.PlayerBScore,
                OpponentScore = round.PlayerAScore,
                YourFinalScore = round.PlayerBFinalScore,
                OpponentFinalScore = round.PlayerAFinalScore,
                YourAccuracy = round.PlayerBAccuracy,
                OpponentAccuracy = round.PlayerAAccuracy,
                YourFailed = round.PlayerBFailed,
                OpponentFailed = round.PlayerAFailed,
                YourForfeit = round.PlayerBForfeit,
                OpponentForfeit = round.PlayerAForfeit,
                WinnerId = round.WinnerId,
                IsYouWinner = round.WinnerId == match.PlayerB.PlayerId
            };
            await MatchSession.SendMessage(match.PlayerA.Socket, msgA);
            await MatchSession.SendMessage(match.PlayerB.Socket, msgB);
        }

        private async Task EmitSeriesUpdate(MatchSession match) {
            bool over = match.IsSeriesOver;
            await MatchSession.SendMessage(match.PlayerA.Socket, new SeriesUpdateMessage {
                Type = "seriesUpdate",
                YourGamesWon = match.PlayerAGamesWon,
                OpponentGamesWon = match.PlayerBGamesWon,
                DrawnGames = match.DrawnGames,
                IsSeriesOver = over
            });
            await MatchSession.SendMessage(match.PlayerB.Socket, new SeriesUpdateMessage {
                Type = "seriesUpdate",
                YourGamesWon = match.PlayerBGamesWon,
                OpponentGamesWon = match.PlayerAGamesWon,
                DrawnGames = match.DrawnGames,
                IsSeriesOver = over
            });
        }

        // ── Countdown ──

        private async Task RunCountdown(MatchSession match) {
            // 30s window — the mod uses the first ~25s to let the player adjust their
            // settings panel, the last 3 ticks as a real "3, 2, 1, GO" countdown.
            for (int i = MatchSession.CountdownSeconds; i > 0; i--) {
                var msg = new CountdownMessage { Type = "countdown", SecondsRemaining = i };
                await MatchSession.SendMessage(match.PlayerA.Socket, msg);
                await MatchSession.SendMessage(match.PlayerB.Socket, msg);
                await Task.Delay(1000);
            }

            match.StartPlaying();

            if (!await SetRankedPlayVisibility(match, true)) {
                _logger.LogWarning(
                    "Ranked-play visibility was not confirmed before subscribing to replay status for match {MatchId}",
                    match.MatchId);
            }

            await _replaySubscriber.SubscribeToPlayer(match.PlayerA.PlayerId);
            await _replaySubscriber.SubscribeToPlayer(match.PlayerB.PlayerId);

            var startMsg = new SocketMessage { Type = "startPlaying" };
            await MatchSession.SendMessage(match.PlayerA.Socket, startMsg);
            await MatchSession.SendMessage(match.PlayerB.Socket, startMsg);
        }

        // ── Finalize ──

        private async Task FinalizeMatch(MatchSession match) {
            var bridgeResult = match.ToBridgeResult();
            var ack = await SendMatchResultToBridge(bridgeResult);

            float playerAChange = ack?.PlayerAMMRChange ?? 0;
            float playerBChange = ack?.PlayerBMMRChange ?? 0;
            float playerANewMMR = ack?.PlayerANewMMR ?? match.PlayerA.MMR;
            float playerBNewMMR = ack?.PlayerBNewMMR ?? match.PlayerB.MMR;

            // Post-match calibration state (§5.6). Falls back to the QueueEntry
            // pre-match flags if the ack didn't arrive (bridge timeout) — at
            // least the mod won't crash, just shows stale placement copy.
            bool playerAIsCalibrated = ack?.PlayerAIsCalibrated ?? match.PlayerA.IsCalibrated;
            int playerACalibrationMatchesPlayed = ack?.PlayerACalibrationMatchesPlayed ?? match.PlayerA.CalibrationMatchesPlayed;
            bool playerBIsCalibrated = ack?.PlayerBIsCalibrated ?? match.PlayerB.IsCalibrated;
            int playerBCalibrationMatchesPlayed = ack?.PlayerBCalibrationMatchesPlayed ?? match.PlayerB.CalibrationMatchesPlayed;

            string? seriesWinner = match.GetSeriesWinnerId();
            string result = match.GetResultString();

            var rounds = match.CompletedRounds;

            var msgA = new MatchResultMessage {
                Type = "matchResult",
                WinnerId = seriesWinner ?? "",
                Result = result,
                YourGamesWon = match.PlayerAGamesWon,
                OpponentGamesWon = match.PlayerBGamesWon,
                DrawnGames = match.DrawnGames,
                IsYouWinner = seriesWinner == match.PlayerA.PlayerId,
                MMRChange = playerAChange,
                NewMMR = playerANewMMR,
                OpponentMMRChange = playerBChange,
                OpponentNewMMR = playerBNewMMR,
                IsCalibrated = playerAIsCalibrated,
                CalibrationMatchesPlayed = playerACalibrationMatchesPlayed,
                OpponentIsCalibrated = playerBIsCalibrated,
                OpponentCalibrationMatchesPlayed = playerBCalibrationMatchesPlayed,
                Rounds = rounds.Select(r => new RoundSummary {
                    RoundNumber = r.RoundNumber,
                    LeaderboardId = r.LeaderboardId,
                    YourScore = r.PlayerAScore,
                    OpponentScore = r.PlayerBScore,
                    YourFinalScore = r.PlayerAFinalScore,
                    OpponentFinalScore = r.PlayerBFinalScore,
                    YourAccuracy = r.PlayerAAccuracy,
                    OpponentAccuracy = r.PlayerBAccuracy,
                    YourFailed = r.PlayerAFailed,
                    OpponentFailed = r.PlayerBFailed,
                    YourForfeit = r.PlayerAForfeit,
                    OpponentForfeit = r.PlayerBForfeit,
                    WinnerId = r.WinnerId
                }).ToList()
            };

            var msgB = new MatchResultMessage {
                Type = "matchResult",
                WinnerId = seriesWinner ?? "",
                Result = result,
                YourGamesWon = match.PlayerBGamesWon,
                OpponentGamesWon = match.PlayerAGamesWon,
                DrawnGames = match.DrawnGames,
                IsYouWinner = seriesWinner == match.PlayerB.PlayerId,
                MMRChange = playerBChange,
                NewMMR = playerBNewMMR,
                OpponentMMRChange = playerAChange,
                OpponentNewMMR = playerANewMMR,
                IsCalibrated = playerBIsCalibrated,
                CalibrationMatchesPlayed = playerBCalibrationMatchesPlayed,
                OpponentIsCalibrated = playerAIsCalibrated,
                OpponentCalibrationMatchesPlayed = playerACalibrationMatchesPlayed,
                Rounds = rounds.Select(r => new RoundSummary {
                    RoundNumber = r.RoundNumber,
                    LeaderboardId = r.LeaderboardId,
                    YourScore = r.PlayerBScore,
                    OpponentScore = r.PlayerAScore,
                    YourFinalScore = r.PlayerBFinalScore,
                    OpponentFinalScore = r.PlayerAFinalScore,
                    YourAccuracy = r.PlayerBAccuracy,
                    OpponentAccuracy = r.PlayerAAccuracy,
                    YourFailed = r.PlayerBFailed,
                    OpponentFailed = r.PlayerAFailed,
                    YourForfeit = r.PlayerBForfeit,
                    OpponentForfeit = r.PlayerAForfeit,
                    WinnerId = r.WinnerId
                }).ToList()
            };

            await MatchSession.SendMessage(match.PlayerA.Socket, msgA);
            await MatchSession.SendMessage(match.PlayerB.Socket, msgB);

            CleanupMatch(match);
        }

        private async Task CancelMatch(MatchSession match, string reason) {
            match.Cancel(reason);

            var msg = new MatchCancelledMessage { Type = "matchCancelled", Reason = reason };
            await MatchSession.SendMessage(match.PlayerA.Socket, msg);
            await MatchSession.SendMessage(match.PlayerB.Socket, msg);

            CleanupMatch(match);
        }

        private async Task RequeuePlayerWithPriority(QueueEntry entry) {
            if (entry.Socket.State != WebSocketState.Open) return;

            lock (_queueLock) {
                if (_queue.Any(e => e.PlayerId == entry.PlayerId)) return;
                entry.JoinedAt = Math.Min(entry.JoinedAt, Time.UnixNow() - 30);
                _queue.Insert(0, entry);
            }

            await MatchSession.SendMessage(entry.Socket, new QueueStatusMessage {
                Type = "queueStatus",
                Status = "joined",
                QueuePosition = GetQueuePosition(entry.PlayerId),
                EstimatedWaitSeconds = 5
            });

            await BroadcastQueueStatus();
            await BroadcastQueueSnapshot();
        }

        private async Task SendDodgePenalty(string playerId, string? opponentPlayerId) {
            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return;

            var json = JsonConvert.SerializeObject(new BridgeDodgePenaltyMessage {
                Type = "dodgePenalty",
                MatchId = Guid.NewGuid().ToString(),
                PlayerId = playerId,
                OpponentPlayerId = opponentPlayerId,
                Penalty = 10f
            });
            await BridgeSend(ws, json);
        }

        private void CleanupMatch(MatchSession match) {
            _ = SetRankedPlayVisibility(match, false);
            _activeMatches.TryRemove(match.MatchId, out _);
            _playerToMatch.TryRemove(match.PlayerA.PlayerId, out _);
            _playerToMatch.TryRemove(match.PlayerB.PlayerId, out _);

            _ = _replaySubscriber.UnsubscribeFromPlayer(match.PlayerA.PlayerId);
            _ = _replaySubscriber.UnsubscribeFromPlayer(match.PlayerB.PlayerId);
        }

        // ── Bridge Communication ──

        public void SetBridgeSocket(WebSocket socket) {
            lock (_bridgeLock) {
                _bridgeSocket = socket;
            }

            _ = BroadcastQueueStatus();

            foreach (var match in _activeMatches.Values.Where(m => m.Phase == MatchPhase.Playing)) {
                _ = SetRankedPlayVisibility(match, true);
            }
        }

        public void ClearBridgeSocket(WebSocket socket) {
            lock (_bridgeLock) {
                if (_bridgeSocket == socket) _bridgeSocket = null;
            }
        }

        public void HandleBridgeMessage(string json) {
            try {
                var msg = JsonConvert.DeserializeObject<SocketMessage>(json);
                if (msg == null) return;

                switch (msg.Type) {
                    case "playerProfileResponse":
                        var profile = JsonConvert.DeserializeObject<BridgePlayerProfileMessage>(json);
                        if (profile?.RequestId != null && _pendingProfileRequests.TryRemove(profile.RequestId, out var profileTcs)) {
                            profileTcs.TrySetResult(profile);
                        }
                        break;

                    case "handDealtResponse":
                        var hand = JsonConvert.DeserializeObject<BridgeHandDealtMessage>(json);
                        if (hand?.RequestId != null && _pendingHandRequests.TryRemove(hand.RequestId, out var handTcs)) {
                            handTcs.TrySetResult(hand);
                        }
                        break;

                    case "mapReplacementResponse":
                        var replacements = JsonConvert.DeserializeObject<BridgeMapReplacementMessage>(json);
                        if (replacements?.RequestId != null && _pendingReplacementRequests.TryRemove(replacements.RequestId, out var replacementTcs)) {
                            replacementTcs.TrySetResult(replacements);
                        }
                        break;

                    case "matchResultAck":
                        var ackMsg = JsonConvert.DeserializeObject<BridgeMatchResultAck>(json);
                        if (ackMsg?.MatchId != null && _pendingMatchResultAcks.TryRemove(ackMsg.MatchId, out var ackTcs)) {
                            ackTcs.TrySetResult(ackMsg);
                        }
                        break;

                    case "rankedPlayVisibilityAck":
                        var visibilityAck = JsonConvert.DeserializeObject<BridgeRankedPlayVisibilityAckMessage>(json);
                        if (visibilityAck?.RequestId != null && _pendingVisibilityAcks.TryRemove(visibilityAck.RequestId, out var visibilityTcs)) {
                            visibilityTcs.TrySetResult(visibilityAck);
                        }
                        break;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling bridge message");
            }
        }

        public async Task<BridgePlayerProfileMessage?> RequestPlayerProfile(string playerId) {
            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return null;

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<BridgePlayerProfileMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingProfileRequests[requestId] = tcs;

            try {
                var request = JsonConvert.SerializeObject(new {
                    Type = "playerProfileRequest",
                    RequestId = requestId,
                    PlayerId = playerId
                });
                await BridgeSend(ws, request);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                cts.Token.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            } catch (TaskCanceledException) {
                return null;
            } finally {
                _pendingProfileRequests.TryRemove(requestId, out _);
            }
        }

        private async Task<List<MapCandidateInfo>?> RequestInitialHand(QueueEntry playerA, QueueEntry playerB) {
            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return null;

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<BridgeHandDealtMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingHandRequests[requestId] = tcs;

            try {
                var request = JsonConvert.SerializeObject(new {
                    Type = "handDealtRequest",
                    RequestId = requestId,
                    PlayerAId = playerA.PlayerId,
                    PlayerBId = playerB.PlayerId,
                    AverageMMR = (playerA.MMR + playerB.MMR) / 2f
                });
                await BridgeSend(ws, request);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                cts.Token.Register(() => tcs.TrySetCanceled());
                var response = await tcs.Task;
                return response?.Maps;
            } catch (TaskCanceledException) {
                return null;
            } finally {
                _pendingHandRequests.TryRemove(requestId, out _);
            }
        }

        private async Task<List<MapCandidateInfo>?> RequestMapReplacements(
            MatchSession match,
            int count,
            List<string> excludedLeaderboardIds,
            List<int> excludedMapperIds) {

            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return null;

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<BridgeMapReplacementMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingReplacementRequests[requestId] = tcs;

            try {
                var request = JsonConvert.SerializeObject(new {
                    Type = "mapReplacementRequest",
                    RequestId = requestId,
                    AverageMMR = (match.PlayerA.MMR + match.PlayerB.MMR) / 2f,
                    Count = count,
                    ExcludedLeaderboardIds = excludedLeaderboardIds,
                    ExcludedMapperIds = excludedMapperIds
                });
                await BridgeSend(ws, request);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                cts.Token.Register(() => tcs.TrySetCanceled());
                var response = await tcs.Task;
                return response?.Maps;
            } catch (TaskCanceledException) {
                return null;
            } finally {
                _pendingReplacementRequests.TryRemove(requestId, out _);
            }
        }

        private async Task<BridgeMatchResultAck?> SendMatchResultToBridge(BridgeMatchResultMessage result) {
            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return null;

            var tcs = new TaskCompletionSource<BridgeMatchResultAck>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingMatchResultAcks[result.MatchId] = tcs;

            try {
                var json = JsonConvert.SerializeObject(result);
                await BridgeSend(ws, json);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                cts.Token.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            } catch (TaskCanceledException) {
                _logger.LogWarning("Timed out waiting for matchResultAck for {MatchId}", result.MatchId);
                return null;
            } finally {
                _pendingMatchResultAcks.TryRemove(result.MatchId, out _);
            }
        }

        private async Task<bool> SetRankedPlayVisibility(MatchSession match, bool isActive) {
            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return false;

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<BridgeRankedPlayVisibilityAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingVisibilityAcks[requestId] = tcs;

            try {
                var json = JsonConvert.SerializeObject(new BridgeRankedPlayVisibilityMessage {
                    Type = "rankedPlayVisibility",
                    RequestId = requestId,
                    MatchId = match.MatchId,
                    PlayerIds = new List<string> {
                        match.PlayerA.PlayerId,
                        match.PlayerB.PlayerId
                    },
                    IsActive = isActive
                });
                await BridgeSend(ws, json);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                cts.Token.Register(() => tcs.TrySetCanceled());
                await tcs.Task;
                return true;
            } catch (TaskCanceledException) {
                _logger.LogWarning(
                    "Timed out waiting for rankedPlayVisibilityAck for match {MatchId} (active={IsActive})",
                    match.MatchId, isActive);
                return false;
            } finally {
                _pendingVisibilityAcks.TryRemove(requestId, out _);
            }
        }

        private async Task BridgeSend(WebSocket ws, string json) {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _bridgeSendLock.WaitAsync();
            try {
                if (ws.State == WebSocketState.Open) {
                    await ws.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text, true,
                        CancellationToken.None);
                }
            } finally {
                _bridgeSendLock.Release();
            }
        }

        private async Task BroadcastQueueStatus() {
            WebSocket? ws;
            lock (_bridgeLock) { ws = _bridgeSocket; }
            if (ws == null || ws.State != WebSocketState.Open) return;

            var msg = JsonConvert.SerializeObject(new BridgeQueueStatusMessage {
                Type = "queueStatus",
                PlayersInQueue = QueueCount,
                ActiveMatches = ActiveMatchCount
            });
            await BridgeSend(ws, msg);
        }

        // ── Queue Snapshot ──

        private QueueSnapshotMessage BuildQueueSnapshot() {
            List<QueuePlayerPreview> previews;
            int total;
            lock (_queueLock) {
                total = _queue.Count;
                IEnumerable<QueueEntry> source = _queue.OrderBy(e => e.JoinedAt);
                if (total > QueueSnapshotSampleSize) {
                    source = source.Take(QueueSnapshotSampleSize);
                }
                previews = source
                    .Select(e => new QueuePlayerPreview {
                        PlayerId = e.PlayerId,
                        Tier = e.Tier,
                        TierDivision = e.TierDivision
                    })
                    .ToList();
            }
            return new QueueSnapshotMessage {
                Type = "queueSnapshot",
                TotalPlayers = total,
                ActiveMatches = ActiveMatchCount,
                Players = previews
            };
        }

        private async Task BroadcastQueueSnapshot() {
            _lastSnapshotBroadcastUnix = Time.UnixNow();
            var snapshot = BuildQueueSnapshot();
            foreach (var conn in _connectedPlayers.Values) {
                if (conn.Socket.State != WebSocketState.Open) {
                    _connectedPlayers.TryRemove(conn.PlayerId, out _);
                    continue;
                }
                try {
                    await MatchSession.SendMessage(conn.Socket, snapshot);
                } catch (Exception ex) {
                    _logger.LogDebug(ex, "Failed to push queue snapshot to {PlayerId}", conn.PlayerId);
                }
            }
        }

        private async Task MaybeBroadcastQueueSnapshot() {
            if (Time.UnixNow() - _lastSnapshotBroadcastUnix < QueueSnapshotBroadcastIntervalSeconds) return;
            await BroadcastQueueSnapshot();
        }
    }
}
