namespace BeatLeader_RankedPlay_Sockets.Models {
    // Round-aware state machine for a best-of-3 match (docs/RankedPlay.md §4.1.C / §8).
    // Each round cycles through HandDealt → DiscardPhase → DiscardRevealed → PickPhase →
    // MapDownload → Countdown → Playing → RoundResolved, then loops back to HandDealt
    // for the next round unless the series finished (2-0 early-out or all 3 rounds played).
    public enum MatchPhase {
        Connecting,         // Match just created, both clients being notified
        HandDealt,          // 5-card hand sent, waiting to open the discard window
        DiscardPhase,       // Simultaneous discard timer running (blind)
        DiscardRevealed,    // Discards revealed + replacements drawn, before pick opens
        PickPhase,          // Picker is choosing
        MapDownload,        // Both clients downloading the chosen map (60s grace)
        Countdown,          // 30s pre-play window (lets players adjust settings)
        Playing,            // Round in progress
        RoundResolved,      // Round outcome computed, deciding next-round vs end-of-series
        SeriesResolved,     // Series done — about to bridge results and clean up
        Cancelled
    }
}
