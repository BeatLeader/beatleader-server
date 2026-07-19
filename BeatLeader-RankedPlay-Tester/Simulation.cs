using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace BeatLeader_RankedPlay_Tester;

public enum BotBehavior {
    Normal,
    DisconnectDuringNegotiation,
    DisconnectEarlyInRound,
    DisconnectLateInRound,
    DiscardTimeout,
    PickTimeout,
    DownloadFailed,
    ForfeitGame,
    ForfeitMatch
}

public class BotConfig {
    public string ApiBase { get; set; } = "https://api.beatleader.com";
    public string ReplayCdn { get; set; } = "https://cdn.replays.beatleader.com";
    public string RankedPlayWsUrl { get; set; } = "wss://sockets.api.beatleader.com/rankedplay/match";
    public string ReplayWsUrl { get; set; } = "wss://sockets.api.beatleader.com/stream/player/post";

    // How often the runner logs queue status to the console.
    public int StatusPollIntervalMs { get; set; } = 5000;

    // Probability per match cycle that the bot uses an abnormal behaviour
    // (disconnect, timeout, forfeit, download failure). Keeps the queue from
    // feeling robotic so corner-case server paths get exercised too.
    public float CornerCaseChance { get; set; } = 0.10f;

    // Connection / negotiation pacing.
    public int ConnectionStaggerMinMs { get; set; } = 100;
    public int ConnectionStaggerMaxMs { get; set; } = 3000;
    public int DiscardDelayMinMs { get; set; } = 800;
    public int DiscardDelayMaxMs { get; set; } = 8000;
    public int PickDelayMinMs { get; set; } = 800;
    public int PickDelayMaxMs { get; set; } = 6000;
    public int MapReadyDelayMinMs { get; set; } = 200;
    public int MapReadyDelayMaxMs { get; set; } = 5000;

    // Post-match idle window before re-queueing.
    public int RequeueIdleMinMs { get; set; } = 5000;
    public int RequeueIdleMaxMs { get; set; } = 30000;
}

/// <summary>
/// Long-running bot runner — logs in N test accounts, parks them in the ranked
/// play queue, and behaves like real players when matched. The matchmaker on
/// the server enforces "real players preferred, no bot-vs-bot pairings"
/// (§4.2), so a pool of these bots fills the queue without stealing matches
/// from each other; a single real human in the queue at any time gets paired
/// with the closest-MMR bot every cycle.
///
/// This runs forever — Ctrl+C / cancellation token stops it. There's no
/// success/failure report; logs are the diagnostic output.
/// </summary>
public class BotRunner {
    private readonly BotConfig _config;
    private readonly ReplayFetcher _replayFetcher;
    private readonly List<PlayerProfile> _players;
    private readonly HttpClient _queueStatusHttp;

    public BotRunner(BotConfig config, List<PlayerProfile> players) {
        _config = config;
        _players = players;
        _replayFetcher = new ReplayFetcher(config.ApiBase, config.ReplayCdn);

        _queueStatusHttp = new HttpClient {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _queueStatusHttp.DefaultRequestHeaders.Add("User-Agent", "BeatLeader-RankedPlay-Tester/1.0");
    }

    public async Task RunAsync(CancellationToken token) {
        try {
            Console.WriteLine($"\n{new string('=', 60)}");
            Console.WriteLine($"  RANKED PLAY BOT RUNNER");
            Console.WriteLine($"  Bots: {_players.Count}");
            Console.WriteLine($"  Corner-case chance: {_config.CornerCaseChance:P0}");
            Console.WriteLine($"  Mode: queue-filler — real players get matched first");
            Console.WriteLine($"{new string('=', 60)}\n");

            Console.WriteLine("=== Phase 1: Logging in bot accounts ===");
            var clients = await LoginAllPlayers(token);
            if (clients.Count == 0) {
                Console.WriteLine("ERROR: No bots logged in — nothing to do.");
                return;
            }

            Console.WriteLine("\n=== Phase 2: Connecting WebSockets ===");
            var connected = await ConnectAllSockets(clients, token);
            if (connected.Count == 0) {
                Console.WriteLine("ERROR: No bots connected to the ranked play sockets.");
                return;
            }

            Console.WriteLine($"\n=== Phase 3: Running queue-filler bots ({connected.Count} active) ===");
            await RunBotPool(connected, token);

            Console.WriteLine("\n=== Phase 4: Cleanup ===");
            foreach (var client in connected) {
                await client.DisconnectAsync();
                client.Dispose();
            }
        } finally {
            _queueStatusHttp.Dispose();
            _replayFetcher.Dispose();
        }
    }

    private async Task<List<RankedPlayClient>> LoginAllPlayers(CancellationToken token) {
        var clients = new List<RankedPlayClient>();
        var semaphore = new SemaphoreSlim(10);

        var tasks = _players.Select(async player => {
            await semaphore.WaitAsync(token);
            try {
                int stagger = Random.Shared.Next(_config.ConnectionStaggerMinMs, _config.ConnectionStaggerMaxMs);
                await Task.Delay(stagger, token);

                var client = new RankedPlayClient(
                    player.Login,
                    _config.ApiBase,
                    _config.RankedPlayWsUrl,
                    _config.ReplayWsUrl);

                bool success = await client.LoginAsync();
                if (success) {
                    lock (clients) {
                        clients.Add(client);
                    }
                } else {
                    client.Dispose();
                }
            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return clients;
    }

    private async Task<List<RankedPlayClient>> ConnectAllSockets(List<RankedPlayClient> clients, CancellationToken token) {
        var connected = new List<RankedPlayClient>();

        foreach (var client in clients) {
            if (token.IsCancellationRequested) break;

            int stagger = Random.Shared.Next(50, 500);
            await Task.Delay(stagger, token);

            bool rankedPlayConnected = await client.ConnectRankedPlayAsync(token);
            bool replayConnected = await client.ConnectReplaySocketAsync(token);

            if (rankedPlayConnected && replayConnected) {
                connected.Add(client);
            }
        }

        return connected;
    }

    private async Task RunBotPool(List<RankedPlayClient> bots, CancellationToken token) {
        var botTasks = bots.Select(bot => RunBotLoop(bot, token)).ToList();

        while (!token.IsCancellationRequested && botTasks.Any(t => !t.IsCompleted)) {
            int queuedBots = bots.Count(b => b.IsQueued);
            int botsInMatch = bots.Count(b => b.IsInMatch);

            var liveQueue = await GetLiveQueueStatusAsync(token);
            int observedQueue = liveQueue?.PlayersInQueue ?? queuedBots;
            int observedActiveMatches = liveQueue?.ActiveMatches ?? botsInMatch;
            int estimatedHumans = Math.Max(0, observedQueue - queuedBots);

            Console.Write(
                $"\r  Queue: {observedQueue,2} total | Humans~{estimatedHumans,2} | " +
                $"Bots queued: {queuedBots,2} | In match: {botsInMatch,2} | " +
                $"Server active matches: {observedActiveMatches,2}    ");

            await Task.Delay(_config.StatusPollIntervalMs, token);
        }

        Console.WriteLine();
        await Task.WhenAll(botTasks);
    }

    private async Task<LiveQueueStatusData?> GetLiveQueueStatusAsync(CancellationToken token) {
        try {
            string url = $"{_config.ApiBase.TrimEnd('/')}/rankedplay/queue/status";
            var json = await _queueStatusHttp.GetStringAsync(url, token);
            return JsonConvert.DeserializeObject<LiveQueueStatusData>(json);
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Per-bot forever loop: queue → match → idle → re-queue. Survives socket
    /// drops by reconnecting; exits only on cancellation.
    /// </summary>
    private async Task RunBotLoop(RankedPlayClient bot, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try {
                if (!bot.IsConnected) {
                    await ReconnectBot(bot, token);
                    if (!bot.IsConnected) {
                        Console.WriteLine($"  [reconnect] {bot.Login}: reconnect failed, retrying in 5s");
                        await Task.Delay(5000, token);
                        continue;
                    }
                }

                var behavior = PickBehavior();
                await RunOneMatchCycle(bot, behavior, token);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                Console.WriteLine($"  [error] {bot.Login}: {ex.Message}");
            }

            if (token.IsCancellationRequested) break;

            int idle = Random.Shared.Next(_config.RequeueIdleMinMs, _config.RequeueIdleMaxMs);
            Console.WriteLine($"  [idle]  {bot.Login}: idling {idle / 1000f:F1}s before re-queueing");
            try {
                await Task.Delay(idle, token);
            } catch (OperationCanceledException) {
                break;
            }
        }
    }

    private async Task RunOneMatchCycle(RankedPlayClient bot, BotBehavior behavior, CancellationToken token) {
        // Outer time budget so a hung match doesn't block the bot forever.
        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cycleCts.CancelAfter(TimeSpan.FromMinutes(15));
        var cycleToken = cycleCts.Token;

        Console.WriteLine($"\n  [cycle]    {bot.Login}: queueing — behavior={behavior}");

        var matchFoundTask = bot.WaitForMatchFound(cycleToken);
        bot.JoinQueue();

        MatchFoundData found;
        try {
            found = await matchFoundTask;
        } catch (OperationCanceledException) {
            // No match this cycle — just leave the queue and idle.
            Console.WriteLine($"  [no-match] {bot.Login}: no match this cycle — leaving queue");
            bot.LeaveQueue();
            return;
        }

        string opponentName = found.Opponent?.PlayerName ?? "?";
        Console.WriteLine(
            $"  [match]    {bot.Login} vs {opponentName} "
            + $"(opp MMR {found.Opponent?.MMR:0}, {(found.IsLowerRated ? "you're lower-rated" : "you're higher-rated")})");

        // Stay in match for up to a full best-of-3 series. Run round-by-round
        // until we see matchResult.
        int roundsPlayed = 0;
        while (!cycleToken.IsCancellationRequested) {
            roundsPlayed++;
            var roundOutcome = await PlayOneRound(bot, found, behavior, cycleToken);
            if (roundOutcome == RoundOutcome.SeriesEnded) {
                Console.WriteLine($"  [cycle]    {bot.Login}: cycle complete after {roundsPlayed} round(s)");
                break;
            }
            if (roundOutcome == RoundOutcome.Aborted) {
                Console.WriteLine($"  [cycle]    {bot.Login}: cycle aborted after {roundsPlayed} round(s)");
                break;
            }
            // Otherwise loop — the next handDealt is incoming.
        }
    }

    private enum RoundOutcome {
        Continue,        // round wrapped up, series goes on
        SeriesEnded,     // matchResult received
        Aborted          // disconnect / cancel / unrecoverable error
    }

    private async Task<RoundOutcome> PlayOneRound(
        RankedPlayClient bot,
        MatchFoundData found,
        BotBehavior behavior,
        CancellationToken token) {
        // ── Hand + discard ──

        HandDealtData hand;
        try {
            hand = await bot.WaitForHandDealt(token);
        } catch (OperationCanceledException) {
            Console.WriteLine($"    [round]    {bot.Login}: handDealt timeout");
            return RoundOutcome.Aborted;
        }

        int handSize = hand.Hand?.Count ?? 0;
        Console.WriteLine(
            $"    [round]    {bot.Login}: round {hand.RoundNumber} hand received ({handSize} maps · "
            + $"series {hand.YourGamesWon}-{hand.OpponentGamesWon})");

        // Round 3 skips the discard phase — only wait for it when the server
        // is actually going to open it.
        DiscardPhaseData? discardPhase = null;
        if (hand.RoundNumber < 3) {
            try {
                discardPhase = await bot.WaitForDiscardPhase(token);
            } catch (OperationCanceledException) {
                return RoundOutcome.Aborted;
            }
            Console.WriteLine($"    [discard]  {bot.Login}: phase open ({discardPhase.DeadlineSeconds}s deadline)");
        } else {
            Console.WriteLine($"    [discard]  {bot.Login}: skipped for round 3 — going straight to pick");
        }

        if (behavior == BotBehavior.DisconnectDuringNegotiation && hand.RoundNumber == 1) {
            int delay = Random.Shared.Next(200, 2000);
            Console.WriteLine($"    [behavior] {bot.Login}: will disconnect during negotiation in {delay}ms");
            await Task.Delay(delay, token);
            await bot.DisconnectAsync();
            Console.WriteLine($"    [discard]  {bot.Login}: disconnected during negotiation");
            await ReconnectBot(bot, token);
            return RoundOutcome.Aborted;
        }

        if (discardPhase != null) {
            if (behavior == BotBehavior.DiscardTimeout && hand.RoundNumber == 1) {
                Console.WriteLine($"    [behavior] {bot.Login}: intentionally timing out the discard phase");
                // Don't send anything — server will treat as pass.
            } else {
                int discardDelay = Random.Shared.Next(_config.DiscardDelayMinMs, _config.DiscardDelayMaxMs);
                Console.WriteLine($"    [discard]  {bot.Login}: deciding (after {discardDelay}ms think-time)…");
                await Task.Delay(discardDelay, token);
                string? discardChoice = PickDiscard(hand.Hand);
                bot.SendDiscard(discardChoice);
                var discardName = discardChoice == null
                    ? "pass (keep all 5)"
                    : (hand.Hand?.FirstOrDefault(m => m.LeaderboardId == discardChoice)?.SongName ?? discardChoice);
                Console.WriteLine($"    [discard]  {bot.Login}: → {discardName}");
            }

            DiscardRevealedData revealed;
            try {
                revealed = await bot.WaitForDiscardRevealed(token);
            } catch (OperationCanceledException) {
                return await CheckForCancellationOrAbort(bot, token, "discard reveal timeout");
            }
            int replacements = revealed.Replacements?.Count ?? 0;
            Console.WriteLine($"    [discard]  {bot.Login}: revealed ({replacements} replacement card(s) drawn)");
        }

        // ── Pick ──

        PickPhaseData pickPhase;
        try {
            pickPhase = await bot.WaitForPickPhase(token);
        } catch (OperationCanceledException) {
            return await CheckForCancellationOrAbort(bot, token, "pick phase timeout");
        }

        Console.WriteLine(
            $"    [pick]     {bot.Login}: phase open — picker={(pickPhase.IsYou ? "you" : "opponent")} "
            + $"({pickPhase.DeadlineSeconds}s deadline)");

        // Hand for the pick — fresh from the discard reveal if it ran,
        // otherwise the original handDealt hand (round 3).
        var pickHand = hand.Hand;

        if (pickPhase.IsYou) {
            if (behavior == BotBehavior.PickTimeout && hand.RoundNumber == 1) {
                Console.WriteLine($"    [behavior] {bot.Login}: intentionally timing out the pick — server will auto-pick");
            } else {
                int pickDelay = Random.Shared.Next(_config.PickDelayMinMs, _config.PickDelayMaxMs);
                Console.WriteLine($"    [pick]     {bot.Login}: deciding (after {pickDelay}ms think-time)…");
                await Task.Delay(pickDelay, token);
                var pickedMap = PickMap(pickHand);
                if (pickedMap != null) {
                    bot.SendPick(pickedMap.LeaderboardId);
                    Console.WriteLine($"    [pick]     {bot.Login}: → '{pickedMap.SongName}' ★{pickedMap.Stars:F1}");
                }
            }
        } else {
            Console.WriteLine($"    [pick]     {bot.Login}: waiting on opponent…");
        }

        MapDecidedData decided;
        try {
            decided = await bot.WaitForMapDecided(token);
        } catch (OperationCanceledException) {
            return await CheckForCancellationOrAbort(bot, token, "map decision timeout");
        }
        Console.WriteLine(
            $"    [pick]     {bot.Login}: decided → '{decided.Map?.SongName}' ★{decided.Map?.Stars:F1} "
            + $"[{decided.Map?.Difficulty}]");

        // ── Map download + ready ──

        if (behavior == BotBehavior.DownloadFailed && hand.RoundNumber == 1) {
            Console.WriteLine($"    [download] {bot.Login}: simulating download failure (sending mapDownloadFailed)");
            bot.SendMapDownloadFailed();
            // Server treats this as a per-round draw — we just wait for the
            // round-or-series event.
        } else {
            int downloadMs = Random.Shared.Next(_config.MapReadyDelayMinMs, _config.MapReadyDelayMaxMs);
            Console.WriteLine($"    [download] {bot.Login}: downloading '{decided.Map?.SongName}' (simulated {downloadMs}ms)…");
            await Task.Delay(downloadMs, token);
            Console.WriteLine($"    [download] {bot.Login}: complete — signaling mapReady");
            bot.SendMapReady();
        }

        // ── Wait for startPlaying or a round/series-end event (in case the
        //    opponent timed out their map download or forfeited) ──

        var startPlayingTask = bot.WaitForStartPlaying(token);
        var earlyRoundTask = bot.WaitForRoundOrSeriesEnd(token);
        var finished = await Task.WhenAny(startPlayingTask, earlyRoundTask);
        if (finished == earlyRoundTask) {
            var ev = await earlyRoundTask;
            if (ev.IsSeriesEnd) {
                Console.WriteLine($"    [play]     {bot.Login}: series ended before startPlaying");
                return RoundOutcome.SeriesEnded;
            }
            Console.WriteLine($"    [play]     {bot.Login}: round resolved before startPlaying — moving to next round");
            return RoundOutcome.Continue;
        }
        try { await startPlayingTask; } catch (OperationCanceledException) {
            return RoundOutcome.Aborted;
        }
        Console.WriteLine($"    [play]     {bot.Login}: countdown complete — startPlaying received");

        // ── Play out the round ──

        if (behavior == BotBehavior.DisconnectEarlyInRound && hand.RoundNumber == 1) {
            int delay = Random.Shared.Next(500, 2000);
            Console.WriteLine($"    [behavior] {bot.Login}: will disconnect early in round in {delay}ms");
            await Task.Delay(delay, token);
            await bot.DisconnectAsync();
            Console.WriteLine($"    [play]     {bot.Login}: disconnected early in round");
            await ReconnectBot(bot, token);
            return RoundOutcome.Aborted;
        }

        if (behavior == BotBehavior.ForfeitGame && hand.RoundNumber == 1) {
            int delay = Random.Shared.Next(2000, 5000);
            Console.WriteLine($"    [behavior] {bot.Login}: will forfeit this round in {delay}ms (after some play)");
            await Task.Delay(delay, token);
            bot.SendForfeitGame();
            Console.WriteLine($"    [play]     {bot.Login}: forfeiting this round (forfeitGame sent)");
            // Fall through to wait for the round result — server still ends
            // the round naturally once the opponent finishes.
        } else if (behavior == BotBehavior.ForfeitMatch && hand.RoundNumber >= 2) {
            int delay = Random.Shared.Next(2000, 5000);
            Console.WriteLine($"    [behavior] {bot.Login}: will forfeit the entire series in {delay}ms");
            await Task.Delay(delay, token);
            bot.SendForfeitMatch();
            Console.WriteLine($"    [play]     {bot.Login}: forfeiting the entire series (forfeitMatch sent)");
            // Server will send matchResult almost immediately.
        }

        bot.ClearOpponentScores();
        await StreamReplayForRound(bot, decided.Map, behavior, token);

        // ── Round result OR series-end ──

        Console.WriteLine($"    [play]     {bot.Login}: waiting for round result / series-end…");
        RoundOrSeriesEvent ev2;
        try {
            ev2 = await bot.WaitForRoundOrSeriesEnd(token);
        } catch (OperationCanceledException) {
            return await CheckForCancellationOrAbort(bot, token, "round-end timeout");
        }

        if (ev2.IsSeriesEnd) {
            var mr = ev2.MatchResult!;
            Console.WriteLine(
                $"    [cycle]    {bot.Login}: series over → {mr.Result} "
                + $"({mr.YourGamesWon}-{mr.OpponentGamesWon}, MMR {mr.MMRChange:+0.0;-0.0;0})");
            return RoundOutcome.SeriesEnded;
        }
        return RoundOutcome.Continue;
    }

    /// <summary>
    /// Map decision arrived late or never — see if the server sent a
    /// cancellation reason instead so we can log something useful.
    /// </summary>
    private async Task<RoundOutcome> CheckForCancellationOrAbort(RankedPlayClient bot, CancellationToken token, string reason) {
        try {
            using var quickCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            quickCts.CancelAfter(TimeSpan.FromSeconds(2));
            var cancelReason = await bot.WaitForMatchCancelled(quickCts.Token);
            Console.WriteLine($"    [abort]    {bot.Login}: match cancelled — {cancelReason}");
        } catch {
            Console.WriteLine($"    [abort]    {bot.Login}: {reason}");
        }
        return RoundOutcome.Aborted;
    }

    private async Task StreamReplayForRound(
        RankedPlayClient bot,
        MapCandidateData? map,
        BotBehavior behavior,
        CancellationToken token) {

        var playerProfile = _players.FirstOrDefault(p => p.Login == bot.Login);
        int rankMin = playerProfile?.EstimatedRank ?? 1;
        int rankMax = rankMin + 500;

        ReplayFetchResult? fetchedReplay = null;
        if (map != null && !string.IsNullOrEmpty(map.Hash)) {
            Console.WriteLine(
                $"    [stream]   {bot.Login}: fetching replay for '{map.SongName}' "
                + $"(rank bracket {rankMin}-{rankMax}) from CDN…");
            fetchedReplay = await _replayFetcher.FetchReplayForMap(map, rankMin, rankMax, token);
        }

        if (fetchedReplay?.Replay == null) {
            Console.WriteLine($"    [stream]   {bot.Login}: no replay available — skipping stream");
            return;
        }

        var replay = fetchedReplay.Replay;
        Console.WriteLine(
            $"    [stream]   {bot.Login}: replay fetched — '{fetchedReplay.SongName}' "
            + $"({fetchedReplay.MatchQuality}"
            + $"{(fetchedReplay.PlayerGlobalRank is int rank ? $", global #{rank}" : "")}, "
            + $"{replay.frames.Count} frames)");

        if (behavior == BotBehavior.DisconnectLateInRound) {
            int totalFrames = replay.frames.Count;
            int cutoff = (int)(totalFrames * (0.4f + Random.Shared.NextSingle() * 0.4f));
            replay.frames = replay.frames.Take(cutoff).ToList();
            Console.WriteLine($"    [behavior] {bot.Login}: will disconnect late (cut to frame {cutoff}/{totalFrames})");
        }

        var streamer = bot.CreateReplayStreamer();
        if (streamer == null) {
            Console.WriteLine($"    [stream]   {bot.Login}: replay socket not ready — skipping stream");
            return;
        }

        Console.WriteLine($"    [stream]   {bot.Login}: streaming {replay.frames.Count} frames…");
        var streamStart = DateTimeOffset.UtcNow;
        await streamer.StreamReplayAsync(replay, token);
        var streamElapsed = DateTimeOffset.UtcNow - streamStart;
        Console.WriteLine($"    [stream]   {bot.Login}: stream complete ({streamElapsed.TotalSeconds:F1}s elapsed)");

        if (behavior == BotBehavior.DisconnectLateInRound) {
            Console.WriteLine($"    [stream]   {bot.Login}: cutting connection (late-disconnect behaviour)");
            await bot.DisconnectAsync();
            await ReconnectBot(bot, token);
        }
    }

    private async Task ReconnectBot(RankedPlayClient bot, CancellationToken token) {
        try {
            int delay = Random.Shared.Next(1000, 3000);
            Console.WriteLine($"  [reconnect] {bot.Login}: attempting reconnect in {delay}ms…");
            await Task.Delay(delay, token);
            await bot.ConnectRankedPlayAsync(token);
            await bot.ConnectReplaySocketAsync(token);
            Console.WriteLine($"  [reconnect] {bot.Login}: reconnected");
        } catch (Exception ex) {
            Console.WriteLine($"  [reconnect] {bot.Login}: reconnect failed — {ex.Message}");
        }
    }

    private BotBehavior PickBehavior() {
        if (Random.Shared.NextSingle() > _config.CornerCaseChance) {
            return BotBehavior.Normal;
        }
        var abnormals = new[] {
            BotBehavior.DisconnectDuringNegotiation,
            BotBehavior.DisconnectEarlyInRound,
            BotBehavior.DisconnectLateInRound,
            BotBehavior.DiscardTimeout,
            BotBehavior.PickTimeout,
            BotBehavior.DownloadFailed,
            BotBehavior.ForfeitGame,
            BotBehavior.ForfeitMatch
        };
        return abnormals[Random.Shared.Next(abnormals.Length)];
    }

    /// <summary>
    /// Decide what map (if any) to discard from the round's hand. Mix of
    /// strategies — sometimes drop the highest-star (avoid the hard one),
    /// sometimes drop the lowest-star (push the difficulty), sometimes pass.
    /// </summary>
    private static string? PickDiscard(List<MapCandidateData>? hand) {
        if (hand == null || hand.Count == 0) return null;
        float roll = Random.Shared.NextSingle();
        if (roll < 0.25f) return null; // pass
        if (roll < 0.55f) return hand.OrderByDescending(m => m.Stars).First().LeaderboardId;
        if (roll < 0.80f) return hand.OrderBy(m => m.Stars).First().LeaderboardId;
        return hand[Random.Shared.Next(hand.Count)].LeaderboardId;
    }

    /// <summary>
    /// Decide what map to pick from the hand when it's our turn. Similar
    /// distribution to discard — slight bias toward middle-star picks so
    /// matches don't always end up at the extremes.
    /// </summary>
    private static MapCandidateData? PickMap(List<MapCandidateData>? hand) {
        if (hand == null || hand.Count == 0) return null;
        float roll = Random.Shared.NextSingle();
        if (roll < 0.20f) return hand.OrderByDescending(m => m.Stars).First();
        if (roll < 0.40f) return hand.OrderBy(m => m.Stars).First();
        // Middle-star bias.
        var ordered = hand.OrderBy(m => m.Stars).ToList();
        return ordered[ordered.Count / 2];
    }
}

public class LiveQueueStatusData {
    public int PlayersInQueue { get; set; }
    public int ActiveMatches { get; set; }
    public int OnlinePlayers { get; set; }
    public int EstimatedWaitSeconds { get; set; }
}
