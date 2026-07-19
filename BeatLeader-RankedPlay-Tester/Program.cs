using BeatLeader_RankedPlay_Tester;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("==============================================================");
Console.WriteLine("  BeatLeader Ranked Play - Bot Runner");
Console.WriteLine("  Fills the queue with bots so real players always have an");
Console.WriteLine("  opponent. The matchmaking server preferentially pairs real");
Console.WriteLine("  players with each other and never pairs two bots together.");
Console.WriteLine("==============================================================");
Console.WriteLine();

var config = new BotConfig {
    StatusPollIntervalMs = 5000,
    CornerCaseChance = 0.10f,
    DiscardDelayMinMs = 800,
    DiscardDelayMaxMs = 8000,
    PickDelayMinMs = 800,
    PickDelayMaxMs = 6000,
    MapReadyDelayMinMs = 200,
    MapReadyDelayMaxMs = 5000,
    ConnectionStaggerMinMs = 100,
    ConnectionStaggerMaxMs = 3000,
    RequeueIdleMinMs = 5000,
    RequeueIdleMaxMs = 30000
};

int playersPerBracket = PlayerGenerator.PlayersPerBracket;

foreach (var arg in args) {
    if (arg.StartsWith("--players-per-bracket=", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(arg["--players-per-bracket=".Length..], out int parsedPlayersPerBracket)) {
        playersPerBracket = Math.Max(1, parsedPlayersPerBracket);
        continue;
    }

    if (arg.StartsWith("--corner-case=", StringComparison.OrdinalIgnoreCase) &&
        float.TryParse(arg["--corner-case=".Length..], out float parsedCorner)) {
        config.CornerCaseChance = Math.Clamp(parsedCorner, 0f, 1f);
        continue;
    }

    if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)) {
        PrintHelp();
        return;
    }
}

var players = PlayerGenerator.GenerateAll(playersPerBracket);

Console.WriteLine("Configuration:");
Console.WriteLine($"  Total bot accounts: {players.Count} ({PlayerGenerator.Brackets.Length} brackets x {playersPerBracket} each)");
Console.WriteLine($"  Corner-case chance: {config.CornerCaseChance:P0}");
Console.WriteLine();

Console.WriteLine("Rank Brackets:");
foreach (var bracket in PlayerGenerator.Brackets) {
    Console.WriteLine($"  {bracket.Name,-12} ranks {bracket.RankMin,5}-{bracket.RankMax,-5}  approx seed MMR: {bracket.ApproxSeedMMR:F0}");
}
Console.WriteLine();
Console.WriteLine("  Bots use the rank encoded in their login (TestPlayer_rank<N>_<idx>).");
Console.WriteLine("  The server seeds them via the log-curve formula (§5.1) on the bot's");
Console.WriteLine("  designated rank — the 'approx seed MMR' column above is just a hint.");
Console.WriteLine();

Console.WriteLine("Sample credentials (login = password):");
for (int i = 0; i < Math.Min(5, players.Count); i++) {
    Console.WriteLine($"  {players[i].Login,-32} rank ~{players[i].EstimatedRank,-5}");
}
if (players.Count > 5) Console.WriteLine($"  ... and {players.Count - 5} more");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop.");

var runner = new BotRunner(config, players);

try {
    await runner.RunAsync(cts.Token);
} catch (OperationCanceledException) {
    Console.WriteLine("\nBot runner stopped by user.");
}

Console.WriteLine("\nDone. Press any key to exit.");
try { Console.ReadKey(true); } catch { }


static void PrintHelp() {
    Console.WriteLine("Usage: BeatLeader-RankedPlay-Tester [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --players-per-bracket=N   How many bots to spin up per rank bracket (default 1).");
    Console.WriteLine("  --corner-case=0.10        Probability per match cycle that the bot picks an");
    Console.WriteLine("                            abnormal behaviour (disconnect / timeout / forfeit /");
    Console.WriteLine("                            download fail). Default 0.10.");
    Console.WriteLine("  --help, -h                Show this help text.");
}
