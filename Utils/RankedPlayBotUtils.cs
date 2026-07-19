using System.Text.RegularExpressions;

namespace BeatLeader_Server.Utils {
    // Recognises Ranked-Play test-bot accounts and extracts their designated
    // rank from the login (docs/RankedPlay.md §4.2 follow-up: test bots).
    //
    // Convention: bot accounts log in via /signinoculus with a name of the form
    //     TestPlayer_<bracket>_<idx>
    // where <bracket> is one of:
    //     top<N>    — top-of-ladder brackets (top10 / top50 / top100), <N> is
    //                 the upper rank bound used as the bot's effective rank
    //     rank<N>   — mid/lower brackets (rank200, rank500, rank5000, …)
    // The captured number is the rank the bot's first-season seed MMR is
    // computed from via the standard §5.1 log-curve. The matchmaker also
    // uses the bot flag to keep bots from matching each other.
    public static class RankedPlayBotUtils {
        // Login pattern. Capture group 1 is the designated global rank.
        // Accepts both "top<N>" and "rank<N>" bracket prefixes — see comment above.
        private static readonly Regex BotLoginPattern =
            new(@"^TestPlayer_(?:top|rank)(\d+)_\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsBotName(string? name) {
            if (string.IsNullOrEmpty(name)) return false;
            return BotLoginPattern.IsMatch(name);
        }

        /// <summary>
        /// Extracts the designated global rank from a bot login. Returns null
        /// for non-bot names or unparseable formats.
        /// </summary>
        public static int? GetDesignatedRank(string? name) {
            if (string.IsNullOrEmpty(name)) return null;
            var match = BotLoginPattern.Match(name);
            if (!match.Success) return null;
            return int.TryParse(match.Groups[1].Value, out var rank) ? rank : null;
        }
    }
}
