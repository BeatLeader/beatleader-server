using Newtonsoft.Json.Linq;
using ReplayDecoder;

namespace BeatLeader_RankedPlay_Tester;

/// <summary>
/// Fetches replays from BeatLeader API.
/// Prefers the ranked-play-selected map and only falls back to unrelated maps as a last resort.
/// </summary>
public class ReplayFetcher : IDisposable {
    private readonly HttpClient _http;
    private readonly string _apiBase;
    private readonly string _replayCdn;

    public ReplayFetcher(string apiBase, string replayCdn) {
        _apiBase = apiBase.TrimEnd('/');
        _replayCdn = replayCdn.TrimEnd('/');
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "BeatLeader-RankedPlay-Tester/1.0");
    }

    public async Task<ReplayFetchResult?> FetchReplayForMap(
        MapCandidateData map,
        int rankMin,
        int rankMax,
        CancellationToken token) {

        var fromBracket = await FetchReplayForSpecificMap(map, score =>
            score.PlayerGlobalRank is >= 1 &&
            score.PlayerGlobalRank >= rankMin &&
            score.PlayerGlobalRank <= rankMax &&
            !string.IsNullOrEmpty(score.ReplayUrl), token);

        if (fromBracket != null) {
            return fromBracket;
        }

        var anyReplayOnMap = await FetchReplayForSpecificMap(map, score =>
            !string.IsNullOrEmpty(score.ReplayUrl), token);

        if (anyReplayOnMap != null) {
            return anyReplayOnMap;
        }

        var bracketFallback = await FetchReplayForBracket(rankMin, rankMax, token);
        if (bracketFallback != null) {
            return bracketFallback with {
                MatchQuality = ReplayMatchQuality.BracketFallback
            };
        }

        var randomFallback = await FetchRandomReplay(token);
        if (randomFallback != null) {
            return randomFallback with {
                MatchQuality = ReplayMatchQuality.RandomFallback
            };
        }

        return null;
    }

    /// <summary>
    /// Fetches a replay from a player around the given rank bracket.
    /// Falls back to completely random if bracket-specific fetch fails.
    /// </summary>
    public async Task<ReplayFetchResult?> FetchReplayForBracket(int rankMin, int rankMax, CancellationToken token) {
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                var url = $"{_apiBase}/scores?sortBy=rank&order=asc&page=1&count=20" +
                          $"&minRank={rankMin}&maxRank={rankMax}&type=ranked";

                var json = await _http.GetStringAsync(url, token);
                var scores = JObject.Parse(json)["data"] as JArray;

                if (scores == null || scores.Count == 0) {
                    return await FetchRandomReplay(token);
                }

                var parsedScores = scores
                    .Select(ParseScoreCandidate)
                    .Where(candidate => candidate != null && !string.IsNullOrEmpty(candidate.ReplayUrl))
                    .Cast<ReplayScoreCandidate>()
                    .ToList();

                if (parsedScores.Count == 0) {
                    return await FetchRandomReplay(token);
                }

                var score = parsedScores[Random.Shared.Next(parsedScores.Count)];
                return await DownloadReplay(score, ReplayMatchQuality.Bracket, token);
            } catch (Exception) {
                await Task.Delay(1000 * (attempt + 1), token);
            }
        }

        return await FetchRandomReplay(token);
    }

    public async Task<ReplayFetchResult?> FetchRandomReplay(CancellationToken token) {
        try {
            var json = await _http.GetStringAsync($"{_apiBase}/score/random", token);
            var score = ParseScoreCandidate(JObject.Parse(json));
            return score == null ? null : await DownloadReplay(score, ReplayMatchQuality.Random, token);
        } catch {
            return null;
        }
    }

    private async Task<ReplayFetchResult?> FetchReplayForSpecificMap(
        MapCandidateData map,
        Func<ReplayScoreCandidate, bool> predicate,
        CancellationToken token) {

        string hash = Uri.EscapeDataString(map.Hash);
        string difficulty = Uri.EscapeDataString(map.Difficulty);
        string mode = Uri.EscapeDataString(map.Mode);

        for (int page = 5; page > 0; page--) {
            try {
                string url = $"{_apiBase}/v3/scores/{hash}/{difficulty}/{mode}/standard/global/top?page={page}&count=50&player=0";
                var json = await _http.GetStringAsync(url, token);
                var data = JObject.Parse(json)["data"] as JArray;
                if (data == null || data.Count == 0) {
                    break;
                }

                var candidates = data
                    .Select(ParseScoreCandidate)
                    .Where(candidate => candidate != null && predicate(candidate))
                    .Cast<ReplayScoreCandidate>()
                    .ToList();

                if (candidates.Count == 0) {
                    continue;
                }

                var selected = candidates[Random.Shared.Next(candidates.Count)];
                return await DownloadReplay(selected, ReplayMatchQuality.MapExact, token);
            } catch {
                continue;
            }
        }

        return null;
    }

    private async Task<ReplayFetchResult?> DownloadReplay(
        ReplayScoreCandidate score,
        ReplayMatchQuality quality,
        CancellationToken token) {

        if (string.IsNullOrEmpty(score.ReplayUrl)) {
            return null;
        }

        string replayUrl = score.ReplayUrl;
        if (!replayUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
            replayUrl = $"{_replayCdn}/{replayUrl.TrimStart('/')}";
        }

        var data = await _http.GetByteArrayAsync(replayUrl, token);
        var (replay, _) = ReplayDecoder.ReplayDecoder.Decode(data);

        if (replay == null) {
            return null;
        }

        return new ReplayFetchResult(
            replay,
            score.SongName,
            score.Hash,
            replayUrl,
            score.PlayerName,
            score.PlayerGlobalRank,
            score.LeaderboardId,
            quality);
    }

    private static ReplayScoreCandidate? ParseScoreCandidate(JToken? score) {
        if (score == null) {
            return null;
        }

        string replayUrl = score["replay"]?.ToString() ?? "";
        string songName = score["song"]?["name"]?.ToString()
            ?? score["leaderboard"]?["song"]?["name"]?.ToString()
            ?? "Unknown";
        string hash = score["song"]?["hash"]?.ToString()
            ?? score["leaderboard"]?["song"]?["hash"]?.ToString()
            ?? "";
        string leaderboardId = score["leaderboardId"]?.ToString()
            ?? score["leaderboard"]?["id"]?.ToString()
            ?? "";
        string playerName = score["player"]?["name"]?.ToString() ?? "Unknown";
        int? playerGlobalRank = score["player"]?["rank"]?.Value<int?>();

        return new ReplayScoreCandidate(
            replayUrl,
            songName,
            hash,
            leaderboardId,
            playerName,
            playerGlobalRank);
    }

    public void Dispose() {
        _http.Dispose();
    }
}

public enum ReplayMatchQuality {
    MapExact,
    Bracket,
    Random,
    BracketFallback,
    RandomFallback
}

public record ReplayFetchResult(
    Replay Replay,
    string SongName,
    string Hash,
    string ReplayUrl,
    string PlayerName,
    int? PlayerGlobalRank,
    string LeaderboardId,
    ReplayMatchQuality MatchQuality);

public record ReplayScoreCandidate(
    string ReplayUrl,
    string SongName,
    string Hash,
    string LeaderboardId,
    string PlayerName,
    int? PlayerGlobalRank);
