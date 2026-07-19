using BeatLeader_RankedPlay_Sockets.Models;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;

namespace BeatLeader_RankedPlay_Sockets.Core {
    // Round-aware state container for a best-of-3 ranked play match (docs/RankedPlay.md
    // §4.1.C / §8). The MatchmakingService drives the higher-level orchestration (calling
    // StartRound, fetching replacements from the bridge, running the countdown / round
    // loop); this class is purely state + transition validation.
    public class MatchSession {
        public string MatchId { get; } = Guid.NewGuid().ToString();
        public MatchPhase Phase { get; private set; } = MatchPhase.Connecting;

        public QueueEntry PlayerA { get; }
        public QueueEntry PlayerB { get; }

        // PickerId for round N — R1 = lower-rated, R2 = higher-rated, R3 = lower-rated (§7.3).
        // Captured at construction time so it's stable even if MMR drifts mid-series.
        private readonly string _lowerRatedPlayerId;
        private readonly string _higherRatedPlayerId;

        // Series-level state
        public int CurrentRound { get; private set; }                  // 1..3, 0 before any round starts
        public int PlayerAGamesWon { get; private set; }
        public int PlayerBGamesWon { get; private set; }
        public int DrawnGames { get; private set; }
        public List<RoundData> CompletedRounds { get; } = new();

        public int CreatedAt { get; } = Time.UnixNow();
        public int PlayStartedAt { get; private set; }                 // First round's play start

        private bool _playerADisconnected;
        private bool _playerBDisconnected;
        private bool _playerAMatchForfeit;
        private bool _playerBMatchForfeit;

        // ── Per-round mutable state — reset by StartRound ──

        public List<MapCandidateInfo> Hand { get; private set; } = new();
        public int DiscardDeadline { get; private set; }
        public string? PlayerADiscard { get; private set; }
        public string? PlayerBDiscard { get; private set; }
        public bool PlayerADiscardSubmitted { get; private set; }
        public bool PlayerBDiscardSubmitted { get; private set; }

        public List<MapCandidateInfo> Replacements { get; private set; } = new();

        public string PickerId { get; private set; } = "";
        public int PickDeadline { get; private set; }
        public bool PickSubmitted { get; private set; }

        public MapCandidateInfo? DecidedMap { get; private set; }
        public int MapDownloadDeadline { get; private set; }

        // One-shot transition guards (Interlocked-style flags). Reset by StartRound.
        private int _discardRevealTriggered;
        private int _roundResolutionTriggered;
        private int _countdownTriggered;
        private int _seriesFinalizationTriggered;

        public bool PlayerAMapReady { get; private set; }
        public bool PlayerBMapReady { get; private set; }

        public float PlayerAAccuracy { get; private set; }
        public float PlayerBAccuracy { get; private set; }
        public int PlayerAScore { get; private set; }
        public int PlayerBScore { get; private set; }
        public float PlayerATime { get; private set; }
        public float PlayerBTime { get; private set; }
        public int PlayerACombo { get; private set; }
        public int PlayerBCombo { get; private set; }
        public int PlayerAMistakes { get; private set; }
        public int PlayerBMistakes { get; private set; }

        public bool PlayerAFailed { get; private set; }
        public bool PlayerBFailed { get; private set; }
        public bool PlayerAForfeit { get; private set; }
        public bool PlayerBForfeit { get; private set; }
        public bool PlayerAFinished { get; private set; }
        public bool PlayerBFinished { get; private set; }

        // ── Timing constants (§7.3.1 / §8.2) ──

        public const int DiscardPhaseSeconds = 20;
        public const int PickPhaseSeconds = 15;
        public const int MapDownloadGraceSeconds = 60;
        public const int CountdownSeconds = 30;

        private readonly object _lock = new();

        public MatchSession(QueueEntry playerA, QueueEntry playerB) {
            PlayerA = playerA;
            PlayerB = playerB;

            // Stable tie-break by playerId so the same pair always picks the same
            // lower-rated player even when MMRs are equal.
            int cmp = playerA.MMR.CompareTo(playerB.MMR);
            if (cmp == 0) cmp = string.CompareOrdinal(playerA.PlayerId, playerB.PlayerId);
            _lowerRatedPlayerId = cmp <= 0 ? playerA.PlayerId : playerB.PlayerId;
            _higherRatedPlayerId = cmp <= 0 ? playerB.PlayerId : playerA.PlayerId;
        }

        public bool IsPlayerA(string playerId) => PlayerA.PlayerId == playerId;
        public bool IsPlayerB(string playerId) => PlayerB.PlayerId == playerId;
        public bool IsParticipant(string playerId) => IsPlayerA(playerId) || IsPlayerB(playerId);
        public QueueEntry GetPlayer(string playerId)   => IsPlayerA(playerId) ? PlayerA : PlayerB;
        public QueueEntry GetOpponent(string playerId) => IsPlayerA(playerId) ? PlayerB : PlayerA;
        public WebSocket GetPlayerSocket(string playerId)   => IsPlayerA(playerId) ? PlayerA.Socket : PlayerB.Socket;
        public WebSocket GetOpponentSocket(string playerId) => IsPlayerA(playerId) ? PlayerB.Socket : PlayerA.Socket;
        public bool IsLowerRated(string playerId) => playerId == _lowerRatedPlayerId;

        /// <summary>
        /// Returns the picker for a round per §7.3 (R1: lower, R2: higher, R3: lower).
        /// </summary>
        public string GetPickerForRound(int roundNumber) {
            return roundNumber == 2 ? _higherRatedPlayerId : _lowerRatedPlayerId;
        }

        /// <summary>
        /// Set of leaderboard IDs the players have already played this match — fed into
        /// the bridge's replacement-draw exclusion list so prior rounds' maps don't reappear.
        /// </summary>
        public IEnumerable<string> PlayedLeaderboardIds =>
            CompletedRounds.Where(r => !string.IsNullOrEmpty(r.LeaderboardId)).Select(r => r.LeaderboardId!);

        // ── Round lifecycle ──

        /// <summary>Begin round <paramref name="roundNumber"/> with the supplied 5-card hand.</summary>
        public void StartRound(int roundNumber, List<MapCandidateInfo> hand) {
            lock (_lock) {
                if (Phase != MatchPhase.Connecting && Phase != MatchPhase.RoundResolved) {
                    return;
                }

                CurrentRound = roundNumber;
                Hand = new List<MapCandidateInfo>(hand);
                PlayerADiscard = null;
                PlayerBDiscard = null;
                PlayerADiscardSubmitted = false;
                PlayerBDiscardSubmitted = false;
                Replacements = new List<MapCandidateInfo>();
                PickerId = GetPickerForRound(roundNumber);
                PickSubmitted = false;
                DecidedMap = null;
                MapDownloadDeadline = 0;
                _discardRevealTriggered = 0;
                _roundResolutionTriggered = 0;
                _countdownTriggered = 0;
                PlayerAMapReady = false;
                PlayerBMapReady = false;
                PlayerAAccuracy = 0;
                PlayerBAccuracy = 0;
                PlayerAScore = 0;
                PlayerBScore = 0;
                PlayerATime = 0;
                PlayerBTime = 0;
                PlayerACombo = 0;
                PlayerBCombo = 0;
                PlayerAMistakes = 0;
                PlayerBMistakes = 0;
                PlayerAFailed = false;
                PlayerBFailed = false;
                PlayerAForfeit = false;
                PlayerBForfeit = false;
                PlayerAFinished = false;
                PlayerBFinished = false;

                Phase = MatchPhase.HandDealt;
            }
        }

        /// <summary>Open the simultaneous-discard window.</summary>
        public void OpenDiscardPhase() {
            lock (_lock) {
                if (Phase != MatchPhase.HandDealt) return;
                DiscardDeadline = Time.UnixNow() + DiscardPhaseSeconds;
                Phase = MatchPhase.DiscardPhase;
            }
        }

        /// <summary>
        /// Record a discard (or pass, when <paramref name="leaderboardId"/> is null/empty).
        /// Returns false if the action isn't valid in the current phase or the player
        /// already submitted.
        /// </summary>
        public bool ProcessDiscard(string playerId, string? leaderboardId) {
            lock (_lock) {
                if (Phase != MatchPhase.DiscardPhase) return false;
                if (!IsParticipant(playerId)) return false;

                // Pass is encoded as null/empty.
                string? choice = string.IsNullOrEmpty(leaderboardId) ? null : leaderboardId;

                // If choosing a specific map, it has to be in the current hand.
                if (choice != null && !Hand.Any(m => m.LeaderboardId == choice)) return false;

                if (IsPlayerA(playerId)) {
                    if (PlayerADiscardSubmitted) return false;
                    PlayerADiscard = choice;
                    PlayerADiscardSubmitted = true;
                } else {
                    if (PlayerBDiscardSubmitted) return false;
                    PlayerBDiscard = choice;
                    PlayerBDiscardSubmitted = true;
                }
                return true;
            }
        }

        public bool BothDiscardsSubmitted {
            get { lock (_lock) return PlayerADiscardSubmitted && PlayerBDiscardSubmitted; }
        }

        public bool DiscardDeadlinePassed {
            get { lock (_lock) return Phase == MatchPhase.DiscardPhase && Time.UnixNow() > DiscardDeadline; }
        }

        /// <summary>One-shot guard so two concurrent paths (both-submitted vs timeout) don't both start the reveal flow.</summary>
        public bool TryBeginDiscardReveal() =>
            Interlocked.Exchange(ref _discardRevealTriggered, 1) == 0;

        /// <summary>
        /// Returns the unique leaderboard IDs the discard phase decided to remove from
        /// the hand. (Duplicate discards count once.) Caller uses this list to compute
        /// how many replacement draws to request from the bridge.
        /// </summary>
        public List<string> GetDiscardsToApply() {
            lock (_lock) {
                var discards = new List<string>(2);
                if (!string.IsNullOrEmpty(PlayerADiscard)) discards.Add(PlayerADiscard);
                if (!string.IsNullOrEmpty(PlayerBDiscard) && PlayerBDiscard != PlayerADiscard) {
                    discards.Add(PlayerBDiscard);
                }
                return discards;
            }
        }

        /// <summary>
        /// Finalize the discard phase: remove discarded maps, splice replacements in.
        /// Marks any player who didn't submit by the deadline as having passed.
        /// </summary>
        public void ApplyDiscardReveal(List<MapCandidateInfo> replacements) {
            lock (_lock) {
                if (Phase != MatchPhase.DiscardPhase) return;

                if (!PlayerADiscardSubmitted) { PlayerADiscardSubmitted = true; PlayerADiscard = null; }
                if (!PlayerBDiscardSubmitted) { PlayerBDiscardSubmitted = true; PlayerBDiscard = null; }

                var toRemove = GetDiscardsToApply();
                Hand.RemoveAll(m => toRemove.Contains(m.LeaderboardId));

                Replacements = new List<MapCandidateInfo>(replacements);
                Hand.AddRange(replacements);

                Phase = MatchPhase.DiscardRevealed;
            }
        }

        // ── Pick phase ──

        public void OpenPickPhase() {
            lock (_lock) {
                // Normally HandDealt → DiscardPhase → DiscardRevealed → PickPhase,
                // but rounds that skip the discard step (round 3 — see §7.3.3
                // simplification) jump straight from HandDealt to PickPhase.
                if (Phase != MatchPhase.DiscardRevealed && Phase != MatchPhase.HandDealt) return;
                PickDeadline = Time.UnixNow() + PickPhaseSeconds;
                Phase = MatchPhase.PickPhase;
            }
        }

        /// <summary>
        /// Record a pick. Returns false if not in PickPhase, not the picker, the map
        /// isn't in the hand, or a pick is already in.
        /// </summary>
        public bool ProcessPick(string playerId, string leaderboardId) {
            lock (_lock) {
                if (Phase != MatchPhase.PickPhase) return false;
                if (PickSubmitted) return false;
                if (playerId != PickerId) return false;

                var map = Hand.FirstOrDefault(m => m.LeaderboardId == leaderboardId);
                if (map == null) return false;

                DecidedMap = map;
                PickSubmitted = true;
                EnterMapDownload();
                return true;
            }
        }

        public bool PickDeadlinePassed {
            get { lock (_lock) return Phase == MatchPhase.PickPhase && Time.UnixNow() > PickDeadline; }
        }

        /// <summary>
        /// Auto-pick: server selects a random map from the current hand. Used on timeout.
        /// </summary>
        public void AutoPick() {
            lock (_lock) {
                if (Phase != MatchPhase.PickPhase) return;
                if (Hand.Count == 0) return;
                DecidedMap = Hand[Random.Shared.Next(Hand.Count)];
                PickSubmitted = true;
                EnterMapDownload();
            }
        }

        private void EnterMapDownload() {
            // 60s grace + small buffer so we don't fight the client's own retry attempts.
            MapDownloadDeadline = Time.UnixNow() + MapDownloadGraceSeconds + 30;
            Phase = MatchPhase.MapDownload;
        }

        public bool MapDownloadDeadlinePassed {
            get { lock (_lock) return Phase == MatchPhase.MapDownload && Time.UnixNow() > MapDownloadDeadline; }
        }

        // ── Map download / countdown ──

        public void MarkMapReady(string playerId) {
            lock (_lock) {
                if (Phase != MatchPhase.MapDownload) return;
                if (IsPlayerA(playerId)) PlayerAMapReady = true;
                else PlayerBMapReady = true;

                if (PlayerAMapReady && PlayerBMapReady) {
                    Phase = MatchPhase.Countdown;
                }
            }
        }

        public bool BothMapsReady {
            get { lock (_lock) return PlayerAMapReady && PlayerBMapReady; }
        }

        public bool TryBeginCountdown() =>
            Interlocked.Exchange(ref _countdownTriggered, 1) == 0;

        public void StartPlaying() {
            lock (_lock) {
                if (Phase != MatchPhase.Countdown) return;
                if (PlayStartedAt == 0) PlayStartedAt = Time.UnixNow();
                Phase = MatchPhase.Playing;
            }
        }

        // ── Live updates during play ──

        public void ApplyScore(string playerId, float accuracy, int score, float time, int combo, int mistakes, bool failed) {
            lock (_lock) {
                if (Phase != MatchPhase.Playing) return;

                if (IsPlayerA(playerId)) {
                    PlayerAAccuracy = accuracy;
                    PlayerAScore = score;
                    PlayerATime = time;
                    PlayerACombo = combo;
                    PlayerAMistakes = mistakes;
                    if (failed) PlayerAFailed = true;
                } else {
                    PlayerBAccuracy = accuracy;
                    PlayerBScore = score;
                    PlayerBTime = time;
                    PlayerBCombo = combo;
                    PlayerBMistakes = mistakes;
                    if (failed) PlayerBFailed = true;
                }
            }
        }

        public void MarkPlayerFinished(string playerId, float accuracy, int score) {
            lock (_lock) {
                if (Phase != MatchPhase.Playing) return;

                if (IsPlayerA(playerId)) {
                    PlayerAAccuracy = accuracy;
                    PlayerAScore = score;
                    PlayerAFinished = true;
                } else {
                    PlayerBAccuracy = accuracy;
                    PlayerBScore = score;
                    PlayerBFinished = true;
                }
            }
        }

        public void MarkPlayerFailed(string playerId) {
            lock (_lock) {
                if (Phase != MatchPhase.Playing) return;
                if (IsPlayerA(playerId)) PlayerAFailed = true;
                else PlayerBFailed = true;
                // NF means the level continues — wait for the actual Clear/Quit to mark finished.
            }
        }

        // ── Forfeit / disconnect ──

        /// <summary>
        /// Forfeit the current round (§8.4). Marks the player as finished but does not
        /// transition phase — caller still needs to wait for / cancel the opponent's
        /// round-finish signal to advance.
        /// </summary>
        public void MarkPlayerForfeitGame(string playerId) {
            lock (_lock) {
                if (IsPlayerA(playerId)) {
                    PlayerAForfeit = true;
                    PlayerAFinished = true;
                } else {
                    PlayerBForfeit = true;
                    PlayerBFinished = true;
                }
            }
        }

        /// <summary>
        /// Forfeit the entire series. Marks the player as round-forfeit AND match-forfeit;
        /// also marks the opponent as finished so the round can resolve immediately, and
        /// IsSeriesOver will be true.
        /// </summary>
        public void MarkPlayerForfeitMatch(string playerId) {
            lock (_lock) {
                if (IsPlayerA(playerId)) {
                    _playerAMatchForfeit = true;
                    PlayerAForfeit = true;
                    PlayerAFinished = true;
                    PlayerBFinished = true;
                } else {
                    _playerBMatchForfeit = true;
                    PlayerBForfeit = true;
                    PlayerBFinished = true;
                    PlayerAFinished = true;
                }
            }
        }

        public void MarkPlayerDisconnected(string playerId) {
            lock (_lock) {
                if (IsPlayerA(playerId)) {
                    _playerADisconnected = true;
                    PlayerAFinished = true;
                    PlayerBFinished = true;
                } else {
                    _playerBDisconnected = true;
                    PlayerBFinished = true;
                    PlayerAFinished = true;
                }
            }
        }

        public bool BothPlayersDisconnected {
            get { lock (_lock) return _playerADisconnected && _playerBDisconnected; }
        }

        // ── Round resolution ──

        public bool RoundReadyToResolve {
            get { lock (_lock) return Phase == MatchPhase.Playing && PlayerAFinished && PlayerBFinished; }
        }

        public bool TryBeginRoundResolution() =>
            Interlocked.Exchange(ref _roundResolutionTriggered, 1) == 0;

        public bool TryBeginSeriesFinalization() =>
            Interlocked.Exchange(ref _seriesFinalizationTriggered, 1) == 0;

        /// <summary>
        /// Compute the round outcome (winner / draw / final scores with NF halving), append
        /// it to <see cref="CompletedRounds"/>, update the series counters, and transition
        /// to <see cref="MatchPhase.RoundResolved"/>. Returns the round data so the caller
        /// can broadcast <c>roundResult</c> + <c>seriesUpdate</c>.
        /// </summary>
        public RoundData ResolveRound() {
            lock (_lock) {
                // Halve scores for failed players (§5.3). Integer division is what we want —
                // the winner check is on integer ModifiedScore, so two halved values are
                // still comparable as integers.
                int playerAFinalScore = PlayerAFailed ? PlayerAScore / 2 : PlayerAScore;
                int playerBFinalScore = PlayerBFailed ? PlayerBScore / 2 : PlayerBScore;

                string? winnerId;
                if (PlayerAForfeit && PlayerBForfeit) {
                    winnerId = null;
                } else if (PlayerAForfeit) {
                    winnerId = PlayerB.PlayerId;
                } else if (PlayerBForfeit) {
                    winnerId = PlayerA.PlayerId;
                } else if (playerAFinalScore > playerBFinalScore) {
                    winnerId = PlayerA.PlayerId;
                } else if (playerBFinalScore > playerAFinalScore) {
                    winnerId = PlayerB.PlayerId;
                } else {
                    winnerId = null;
                }

                var hand = new List<string>(Hand.Select(m => m.LeaderboardId));
                // Note: the hand state we capture here is *after* the discard reveal — it's
                // what the picker actually chose from. For replay/auditing we record this
                // post-reveal hand; the original 5-card hand can be reconstructed by joining
                // it with the discards and removing the replacements.
                if (DecidedMap != null && !hand.Contains(DecidedMap.LeaderboardId)) {
                    hand.Insert(0, DecidedMap.LeaderboardId);
                }

                var data = new RoundData {
                    RoundNumber = CurrentRound,
                    HandJson = JsonConvert.SerializeObject(hand),
                    PlayerADiscardLeaderboardId = PlayerADiscard,
                    PlayerBDiscardLeaderboardId = PlayerBDiscard,
                    ReplacementsJson = JsonConvert.SerializeObject(Replacements.Select(m => m.LeaderboardId)),
                    PickerId = PickerId,
                    LeaderboardId = DecidedMap?.LeaderboardId,
                    PlayerAScore = PlayerAScore,
                    PlayerBScore = PlayerBScore,
                    PlayerAFinalScore = playerAFinalScore,
                    PlayerBFinalScore = playerBFinalScore,
                    PlayerAAccuracy = PlayerAAccuracy,
                    PlayerBAccuracy = PlayerBAccuracy,
                    PlayerAFailed = PlayerAFailed,
                    PlayerBFailed = PlayerBFailed,
                    PlayerAForfeit = PlayerAForfeit,
                    PlayerBForfeit = PlayerBForfeit,
                    WinnerId = winnerId
                };

                CompletedRounds.Add(data);
                if (winnerId == PlayerA.PlayerId) PlayerAGamesWon++;
                else if (winnerId == PlayerB.PlayerId) PlayerBGamesWon++;
                else DrawnGames++;

                Phase = MatchPhase.RoundResolved;
                return data;
            }
        }

        /// <summary>
        /// True when the series has ended: 2-0 / 2-1 / 1-1 + drawn R3 / a match-forfeit
        /// landed / both disconnected / all 3 rounds played.
        /// </summary>
        public bool IsSeriesOver {
            get {
                lock (_lock) {
                    if (Phase == MatchPhase.Cancelled) return true;
                    if (_playerAMatchForfeit || _playerBMatchForfeit) return true;
                    if (_playerADisconnected && _playerBDisconnected) return true;
                    if (PlayerAGamesWon >= 2 || PlayerBGamesWon >= 2) return true;
                    if (CompletedRounds.Count >= 3) return true;
                    return false;
                }
            }
        }

        public void MarkSeriesResolved() {
            lock (_lock) {
                if (Phase != MatchPhase.RoundResolved && Phase != MatchPhase.Cancelled) return;
                if (Phase == MatchPhase.RoundResolved) Phase = MatchPhase.SeriesResolved;
            }
        }

        public void Cancel(string reason = "cancelled") {
            lock (_lock) {
                Phase = MatchPhase.Cancelled;
            }
        }

        // ── Result building ──

        public string? GetSeriesWinnerId() {
            lock (_lock) {
                if (_playerAMatchForfeit) return PlayerB.PlayerId;
                if (_playerBMatchForfeit) return PlayerA.PlayerId;
                if (_playerADisconnected && !_playerBDisconnected) return PlayerB.PlayerId;
                if (_playerBDisconnected && !_playerADisconnected) return PlayerA.PlayerId;
                if (PlayerAGamesWon > PlayerBGamesWon) return PlayerA.PlayerId;
                if (PlayerBGamesWon > PlayerAGamesWon) return PlayerB.PlayerId;
                return null;
            }
        }

        /// <summary>
        /// Mapping to the main-server enum (kept as strings on the wire). Has to match
        /// <c>RankedPlayMatchResult</c> in <c>beatleader-server-models</c>.
        /// </summary>
        public string GetResultString() {
            lock (_lock) {
                if (Phase == MatchPhase.Cancelled) return "Cancelled";

                if (_playerAMatchForfeit && _playerBMatchForfeit) return "Drawn";
                if (_playerAMatchForfeit) return "PlayerAForfeitedMatch";
                if (_playerBMatchForfeit) return "PlayerBForfeitedMatch";

                if (_playerADisconnected && _playerBDisconnected) return "BothDisconnected";
                if (_playerADisconnected) return "PlayerADisconnected";
                if (_playerBDisconnected) return "PlayerBDisconnected";

                // Series score-based resolution.
                if (PlayerAGamesWon == PlayerBGamesWon) return "Drawn";
                return "Completed";
            }
        }

        public float GetDuration() {
            if (PlayStartedAt == 0) return 0;
            return Time.UnixNow() - PlayStartedAt;
        }

        public BridgeMatchResultMessage ToBridgeResult() {
            lock (_lock) {
                return new BridgeMatchResultMessage {
                    Type = "matchResult",
                    MatchId = MatchId,
                    PlayerAId = PlayerA.PlayerId,
                    PlayerBId = PlayerB.PlayerId,
                    Result = GetResultString(),
                    Duration = GetDuration(),
                    Games = CompletedRounds.Select(r => new BridgeGameDto {
                        RoundNumber = r.RoundNumber,
                        HandJson = r.HandJson,
                        PlayerADiscardLeaderboardId = r.PlayerADiscardLeaderboardId,
                        PlayerBDiscardLeaderboardId = r.PlayerBDiscardLeaderboardId,
                        ReplacementsJson = r.ReplacementsJson,
                        PickerId = r.PickerId,
                        LeaderboardId = r.LeaderboardId,
                        PlayerAScore = r.PlayerAScore,
                        PlayerBScore = r.PlayerBScore,
                        PlayerAFinalScore = r.PlayerAFinalScore,
                        PlayerBFinalScore = r.PlayerBFinalScore,
                        PlayerAAccuracy = r.PlayerAAccuracy,
                        PlayerBAccuracy = r.PlayerBAccuracy,
                        PlayerAFailed = r.PlayerAFailed,
                        PlayerBFailed = r.PlayerBFailed,
                        PlayerAForfeit = r.PlayerAForfeit,
                        PlayerBForfeit = r.PlayerBForfeit,
                        WinnerId = r.WinnerId
                    }).ToList()
                };
            }
        }

        public static async Task SendMessage(WebSocket socket, object message) {
            if (socket.State != WebSocketState.Open) return;

            var json = JsonConvert.SerializeObject(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            try {
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true,
                    CancellationToken.None);
            } catch (Exception ex) {
                Console.WriteLine($"Send error: {ex.Message}");
            }
        }
    }

    /// <summary>One round's worth of resolved data, held in <see cref="MatchSession.CompletedRounds"/>.</summary>
    public class RoundData {
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
        public string? WinnerId { get; set; }
    }
}
