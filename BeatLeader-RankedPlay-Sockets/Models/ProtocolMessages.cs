namespace BeatLeader_RankedPlay_Sockets.Models {

    // Protocol contract between the socket server and the mod / main-server bridge.
    // Mirrors docs/RankedPlay.md §4.1.C — keep both in sync.

    // ── Base ──

    public class SocketMessage {
        public string Type { get; set; } = "";
    }

    // ── Client → Server ──

    public class JoinQueueMessage : SocketMessage {
        // Type = "joinQueue"
    }

    public class LeaveQueueMessage : SocketMessage {
        // Type = "leaveQueue"
    }

    // Simultaneous discard during the discard phase. LeaderboardId == null = pass.
    public class DiscardMapMessage : SocketMessage {
        // Type = "discardMap"
        public string? LeaderboardId { get; set; }
    }

    public class PickMapMessage : SocketMessage {
        // Type = "pickMap"
        public string LeaderboardId { get; set; } = "";
    }

    public class MapReadyMessage : SocketMessage {
        // Type = "mapReady"
    }

    public class MapDownloadFailedMessage : SocketMessage {
        // Type = "mapDownloadFailed"
    }

    // Forfeit the current round (§8.4). Server awards the round to the opponent
    // immediately; the forfeiter's local level keeps running (no socket abort).
    public class ForfeitGameMessage : SocketMessage {
        // Type = "forfeitGame"
    }

    // Forfeit the entire best-of-3 series. Remaining rounds are awarded to the opponent.
    public class ForfeitMatchMessage : SocketMessage {
        // Type = "forfeitMatch"
    }

    // ── Server → Client ──

    public class QueueStatusMessage : SocketMessage {
        // Type = "queueStatus"
        public string Status { get; set; } = "";     // "joined", "searching", "left"
        public int QueuePosition { get; set; }
        public int EstimatedWaitSeconds { get; set; }
    }

    public class QueueSnapshotMessage : SocketMessage {
        // Type = "queueSnapshot"
        public int TotalPlayers { get; set; }
        public int ActiveMatches { get; set; }
        public List<QueuePlayerPreview> Players { get; set; } = new();
    }

    public class QueuePlayerPreview {
        public string PlayerId { get; set; } = "";
        public string Tier { get; set; } = "";
        public int TierDivision { get; set; }
    }

    // No longer carries a map list — those arrive separately via HandDealt at the
    // start of each round.
    public class MatchFoundMessage : SocketMessage {
        // Type = "matchFound"
        public string MatchId { get; set; } = "";
        public MatchPlayerInfo Opponent { get; set; } = new();
        public bool IsLowerRated { get; set; }
    }

    public class MatchPlayerInfo {
        public string PlayerId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public float MMR { get; set; }
        public string Tier { get; set; } = "";
        public int TierDivision { get; set; }
        // Pre-match calibration state. The mod uses this to hide MMR / tier
        // for opponents who are still in placement (§5.6).
        public bool IsCalibrated { get; set; }
        public int CalibrationMatchesPlayed { get; set; }
    }

    public class MapCandidateInfo {
        public string LeaderboardId { get; set; } = "";
        public string SongName { get; set; } = "";
        public string SongAuthor { get; set; } = "";
        public string Mapper { get; set; } = "";
        public int MapperId { get; set; }                 // used for diversity during replacement draws
        public string CoverImage { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Hash { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public string Mode { get; set; } = "";
        public float Stars { get; set; }
        public double Duration { get; set; }
    }

    // 5-card hand for a round (§7.3). Sent at the start of each round.
    public class HandDealtMessage : SocketMessage {
        // Type = "handDealt"
        public int RoundNumber { get; set; }
        public List<MapCandidateInfo> Hand { get; set; } = new();
        public int YourGamesWon { get; set; }
        public int OpponentGamesWon { get; set; }
    }

    // Opens the simultaneous-discard window. Pairs with the client's DiscardMapMessage.
    public class DiscardPhaseMessage : SocketMessage {
        // Type = "discardPhase"
        public int RoundNumber { get; set; }
        public int DeadlineSeconds { get; set; }          // 20s per §7.3.1
    }

    // Reveals both players' discards (or pass = null) + the replacement maps drawn.
    public class DiscardRevealedMessage : SocketMessage {
        // Type = "discardRevealed"
        public int RoundNumber { get; set; }
        public string? YourDiscardLeaderboardId { get; set; }
        public string? OpponentDiscardLeaderboardId { get; set; }
        public List<MapCandidateInfo> Replacements { get; set; } = new();
        public List<MapCandidateInfo> NewHand { get; set; } = new();
    }

    // Opens the pick window. Picker rotates per §7.3 (R1: lower, R2: higher, R3: lower).
    public class PickPhaseMessage : SocketMessage {
        // Type = "pickPhase"
        public int RoundNumber { get; set; }
        public string PickerId { get; set; } = "";
        public bool IsYou { get; set; }
        public int DeadlineSeconds { get; set; }          // 15s per §7.3.1
    }

    public class MapDecidedMessage : SocketMessage {
        // Type = "mapDecided"
        public int RoundNumber { get; set; }
        public MapCandidateInfo Map { get; set; } = new();
    }

    public class CountdownMessage : SocketMessage {
        // Type = "countdown"
        public int SecondsRemaining { get; set; }
    }

    // Live opponent state during a round. Failed flips true when their energy hits 0
    // under NF — score keeps accumulating in the raw field but will be halved server-side
    // for the winner check (§5.3).
    public class OpponentScoreMessage : SocketMessage {
        // Type = "opponentScore"
        public float Accuracy { get; set; }
        public int Score { get; set; }
        public float Time { get; set; }
        public int Combo { get; set; }
        public int Mistakes { get; set; }
        public bool Failed { get; set; }
    }

    // Per-round result. Scores already have NF halving applied to FinalScore (§5.3).
    public class RoundResultMessage : SocketMessage {
        // Type = "roundResult"
        public int RoundNumber { get; set; }
        public string? LeaderboardId { get; set; }
        public int YourScore { get; set; }
        public int OpponentScore { get; set; }
        public int YourFinalScore { get; set; }
        public int OpponentFinalScore { get; set; }
        public float YourAccuracy { get; set; }
        public float OpponentAccuracy { get; set; }
        public bool YourFailed { get; set; }
        public bool OpponentFailed { get; set; }
        public bool YourForfeit { get; set; }
        public bool OpponentForfeit { get; set; }
        public string? WinnerId { get; set; }              // null = draw
        public bool IsYouWinner { get; set; }
    }

    // Running series score broadcast after each round.
    public class SeriesUpdateMessage : SocketMessage {
        // Type = "seriesUpdate"
        public int YourGamesWon { get; set; }
        public int OpponentGamesWon { get; set; }
        public int DrawnGames { get; set; }
        public bool IsSeriesOver { get; set; }             // signals MatchResult is imminent
    }

    // Acks the forfeiter that their request was accepted.
    public class ForfeitRequestedMessage : SocketMessage {
        // Type = "forfeitRequested"
        public string ForfeitType { get; set; } = "game"; // "game" or "match"
        public int RoundNumber { get; set; }
    }

    // Notifies the opponent that the other player forfeited (round or whole match).
    public class ForfeitConfirmedMessage : SocketMessage {
        // Type = "forfeitConfirmed"
        public string OpponentForfeitType { get; set; } = "game"; // "game" or "match"
        public int RoundNumber { get; set; }
    }

    public class MatchResultMessage : SocketMessage {
        // Type = "matchResult"
        public string WinnerId { get; set; } = "";
        public string Result { get; set; } = "";          // "Completed", "Drawn", "PlayerAForfeitedMatch", ...
        public int YourGamesWon { get; set; }
        public int OpponentGamesWon { get; set; }
        public int DrawnGames { get; set; }
        public bool IsYouWinner { get; set; }
        public float MMRChange { get; set; }
        public float NewMMR { get; set; }
        public float OpponentMMRChange { get; set; }
        public float OpponentNewMMR { get; set; }
        // Post-match calibration state (§5.6). When IsCalibrated is false the
        // mod hides MMR and renders a "Placement N / 5" indicator instead.
        // OpponentIsCalibrated is the opponent's flag — when false, the
        // results screen just shows "Unranked" on their card.
        public bool IsCalibrated { get; set; }
        public int CalibrationMatchesPlayed { get; set; }
        public bool OpponentIsCalibrated { get; set; }
        public int OpponentCalibrationMatchesPlayed { get; set; }
        public List<RoundSummary> Rounds { get; set; } = new();
    }

    public class RoundSummary {
        public int RoundNumber { get; set; }
        public string? LeaderboardId { get; set; }
        public int YourScore { get; set; }
        public int OpponentScore { get; set; }
        public int YourFinalScore { get; set; }
        public int OpponentFinalScore { get; set; }
        public float YourAccuracy { get; set; }
        public float OpponentAccuracy { get; set; }
        public bool YourFailed { get; set; }
        public bool OpponentFailed { get; set; }
        public bool YourForfeit { get; set; }
        public bool OpponentForfeit { get; set; }
        public string? WinnerId { get; set; }
    }

    public class MatchCancelledMessage : SocketMessage {
        // Type = "matchCancelled"
        public string Reason { get; set; } = "";
    }

    public class ErrorMessage : SocketMessage {
        // Type = "error"
        public string Error { get; set; } = "";
    }

    // ── Bridge → Main Server ──

    public class BridgeMatchResultMessage : SocketMessage {
        // Type = "matchResult"
        public string MatchId { get; set; } = "";
        public string PlayerAId { get; set; } = "";
        public string PlayerBId { get; set; } = "";
        public string Result { get; set; } = "";
        public float Duration { get; set; }
        public List<BridgeGameDto> Games { get; set; } = new();
    }

    public class BridgeGameDto {
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

    public class BridgeQueueStatusMessage : SocketMessage {
        // Type = "queueStatus"
        public int PlayersInQueue { get; set; }
        public int ActiveMatches { get; set; }
    }

    public class BridgeDodgePenaltyMessage : SocketMessage {
        // Type = "dodgePenalty"
        public string PlayerId { get; set; } = "";
        public string? OpponentPlayerId { get; set; }
        public string? MatchId { get; set; }
        public float Penalty { get; set; }
    }

    // ── Main Server → Bridge (responses) ──

    public class BridgePlayerProfileMessage : SocketMessage {
        // Type = "playerProfileResponse"
        public string RequestId { get; set; } = "";
        public string PlayerId { get; set; } = "";
        public float MMR { get; set; }
        public string Tier { get; set; } = "";
        public int TierDivision { get; set; }
        public string PlayerName { get; set; } = "";
        public bool IsCalibrated { get; set; }
        public int CalibrationMatchesPlayed { get; set; }
        public int GlobalRank { get; set; }
        // Test-bot flag from RankedPlayBotUtils on the main server. Controls
        // matchmaking pairing rules (no bot-vs-bot; real-vs-real prioritised).
        public bool IsBot { get; set; }
    }

    // Replaces the old BridgeMapCandidatesMessage.
    public class BridgeHandDealtMessage : SocketMessage {
        // Type = "handDealtResponse"
        public string RequestId { get; set; } = "";
        public List<MapCandidateInfo> Maps { get; set; } = new();
    }

    public class BridgeMapReplacementMessage : SocketMessage {
        // Type = "mapReplacementResponse"
        public string RequestId { get; set; } = "";
        public List<MapCandidateInfo> Maps { get; set; } = new();
    }

    public class BridgeMatchResultAck : SocketMessage {
        // Type = "matchResultAck"
        public string MatchId { get; set; } = "";
        public float PlayerAMMRChange { get; set; }
        public float PlayerBMMRChange { get; set; }
        public float PlayerANewMMR { get; set; }
        public float PlayerBNewMMR { get; set; }
        public int PlayerAGamesWon { get; set; }
        public int PlayerBGamesWon { get; set; }
        // Post-match calibration state for both players — drives the mod's
        // "placement N / 5" display so the results screen can hide MMR for
        // players who are still in placement (§5.6).
        public bool PlayerAIsCalibrated { get; set; }
        public int PlayerACalibrationMatchesPlayed { get; set; }
        public bool PlayerBIsCalibrated { get; set; }
        public int PlayerBCalibrationMatchesPlayed { get; set; }
    }

    public class BridgeRankedPlayVisibilityMessage : SocketMessage {
        // Type = "rankedPlayVisibility"
        public string RequestId { get; set; } = "";
        public string MatchId { get; set; } = "";
        public List<string> PlayerIds { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class BridgeRankedPlayVisibilityAckMessage : SocketMessage {
        // Type = "rankedPlayVisibilityAck"
        public string RequestId { get; set; } = "";
    }
}
