using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services {
    // Map-pool sampling for Ranked Play (§7). Two entry points:
    //   - DealInitialHand: round 1's 5-card hand.
    //   - DrawReplacements: refill maps after a discard reveal or between rounds.
    public class RankedPlayMapPoolService {

        public const int HandSize = 5;

        // Initial star window around the MMR-derived target (§7.2).
        public const float StarWindowHalfSize = 1.5f;
        // If the pool is too thin, widen by this much per iteration (§7.4).
        public const float StarWindowWidenStep = 0.5f;
        public const int   MaxStarWidenIterations = 6;   // gives up to ±4.5★

        // "Recent" = last 5 best-of-3 series the player took part in.
        public const int RecentSeriesExclusionCount = 5;
        // Per series there are 2–3 games, so we over-fetch a bit to be safe.
        private const int RecentGameExclusionCount = RecentSeriesExclusionCount * 3;

        // Average-MMR → target-star mapping. Brackets aligned with the §5.2 tier table.
        // Interpolated linearly between rows.
        private static readonly (float MinMMR, float TargetStars)[] MMRToStarsMapping = {
            (5000f, 12.0f),   // Grandmaster
            (3900f, 10.5f),   // Master
            (3000f, 9.0f),    // Platinum
            (2100f, 7.5f),    // Gold
            (1500f, 6.0f),    // Silver
            (900f,  4.5f),    // Bronze
            (0f,    3.0f),    // Cube
        };

        // ===== Public entry points =====

        /// <summary>
        /// Round-1 hand for a newly-matched pair: 5 maps from the star window for their
        /// average MMR, with diversity (no same mapper, mix of map types) and excluding
        /// each player's recent ranked play maps + banned + upload-date-cut maps.
        /// </summary>
        public static async Task<List<MapCandidate>> DealInitialHand(
            AppContext context,
            float averageMMR,
            int seasonId,
            string playerAId,
            string playerBId,
            int? uploadDateCutoff = null
        ) {
            var excludedLeaderboardIds = await GetRecentMatchMapIds(context, seasonId, playerAId, playerBId);
            return await SampleDiverseHand(
                context, averageMMR, seasonId,
                wanted: HandSize,
                excludedLeaderboardIds: excludedLeaderboardIds,
                excludedMapperIds: new HashSet<int>(),
                uploadDateCutoff: uploadDateCutoff);
        }

        /// <summary>
        /// Refill draws after a discard reveal or between rounds (§7.3.1 step 3, §7.3.2).
        /// The caller passes the leaderboard IDs and mapper IDs already represented in the
        /// remaining hand + the match's played-set so we don't repeat any of them.
        /// </summary>
        public static async Task<List<MapCandidate>> DrawReplacements(
            AppContext context,
            float averageMMR,
            int seasonId,
            int count,
            IEnumerable<string> excludedLeaderboardIds,
            IEnumerable<int> excludedMapperIds,
            int? uploadDateCutoff = null
        ) {
            if (count <= 0) return new List<MapCandidate>();

            return await SampleDiverseHand(
                context, averageMMR, seasonId,
                wanted: count,
                excludedLeaderboardIds: new HashSet<string>(excludedLeaderboardIds),
                excludedMapperIds: new HashSet<int>(excludedMapperIds),
                uploadDateCutoff: uploadDateCutoff);
        }

        /// <summary>
        /// Linear-interpolated MMR → target stars.
        /// </summary>
        public static float MMRToTargetStars(float mmr) {
            for (int i = 0; i < MMRToStarsMapping.Length - 1; i++) {
                var (highMMR, highStars) = MMRToStarsMapping[i];
                var (lowMMR, lowStars) = MMRToStarsMapping[i + 1];

                if (mmr >= lowMMR) {
                    float t = highMMR == lowMMR ? 0f : (mmr - lowMMR) / (highMMR - lowMMR);
                    return lowStars + t * (highStars - lowStars);
                }
            }
            return MMRToStarsMapping[^1].TargetStars;
        }

        // ===== Internals =====

        /// <summary>
        /// Samples up to <paramref name="wanted"/> maps. Widens the star window in
        /// <see cref="StarWindowWidenStep"/> steps until either we have enough OR
        /// <see cref="MaxStarWidenIterations"/> is exhausted (§7.4).
        /// </summary>
        private static async Task<List<MapCandidate>> SampleDiverseHand(
            AppContext context,
            float averageMMR,
            int seasonId,
            int wanted,
            HashSet<string> excludedLeaderboardIds,
            HashSet<int> excludedMapperIds,
            int? uploadDateCutoff
        ) {
            float targetStars = MMRToTargetStars(averageMMR);
            float halfWindow = StarWindowHalfSize;

            var bannedLeaderboardIds = await context.RankedPlayMapBans
                .Where(b => b.SeasonId == seasonId)
                .Select(b => b.LeaderboardId)
                .ToHashSetAsync();

            for (int attempt = 0; attempt < MaxStarWidenIterations; attempt++) {
                float minStars = targetStars - halfWindow;
                float maxStars = targetStars + halfWindow;

                var query = context.Leaderboards
                    .Include(l => l.Difficulty)
                    .Include(l => l.Song)
                    .Where(l =>
                        l.Difficulty.Status == DifficultyStatus.ranked &&
                        l.Difficulty.Stars != null &&
                        l.Difficulty.Stars >= minStars &&
                        l.Difficulty.Stars <= maxStars
                    )
                    .Where(l => !bannedLeaderboardIds.Contains(l.Id))
                    .Where(l => !excludedLeaderboardIds.Contains(l.Id));

                if (uploadDateCutoff.HasValue) {
                    query = query.Where(l => l.Song.UploadTime <= uploadDateCutoff.Value);
                }

                var pool = await query
                    .Select(l => new MapCandidate {
                        LeaderboardId = l.Id,
                        SongName = l.Song.Name,
                        SongAuthor = l.Song.Author,
                        SongMapper = l.Song.Mapper,
                        MapperId = l.Song.MapperId,
                        CoverImage = l.Song.CoverImage,
                        Stars = l.Difficulty.Stars ?? 0,
                        DifficultyName = l.Difficulty.DifficultyName,
                        ModeName = l.Difficulty.ModeName,
                        MapType = l.Difficulty.Type,
                        Duration = l.Difficulty.Duration,
                        SongHash = l.Difficulty.Hash,
                        DownloadUrl = l.Song.DownloadUrl
                    })
                    .ToListAsync();

                var selected = SelectDiverseSample(pool, wanted, new HashSet<int>(excludedMapperIds));
                if (selected.Count >= wanted) return selected;

                // Not enough variety yet — widen the window and retry. If we've already
                // exhausted the widening budget, return whatever we have rather than spin.
                if (attempt == MaxStarWidenIterations - 1) return selected;
                halfWindow += StarWindowWidenStep;
            }

            return new List<MapCandidate>();
        }

        /// <summary>
        /// Three-pass diversity selection:
        ///   1. No two maps from the same mapper + prefer different map types.
        ///   2. Drop the map-type preference if pass 1 came up short.
        ///   3. Drop the mapper exclusion as a last resort.
        /// </summary>
        private static List<MapCandidate> SelectDiverseSample(List<MapCandidate> pool, int wanted, HashSet<int> seedMapperIds) {
            var rng = Random.Shared;
            var selected = new List<MapCandidate>(wanted);
            var usedMappers = new HashSet<int>(seedMapperIds);
            var usedTypes = new HashSet<MapTypes>();

            var shuffled = pool.OrderBy(_ => rng.Next()).ToList();

            // Pass 1 — strict (no same mapper, prefer type variety).
            foreach (var candidate in shuffled) {
                if (selected.Count >= wanted) break;
                if (usedMappers.Contains(candidate.MapperId)) continue;
                if (usedTypes.Contains(candidate.MapType) && selected.Count < wanted - 1) continue;

                selected.Add(candidate);
                usedMappers.Add(candidate.MapperId);
                if (candidate.MapType != MapTypes.None) usedTypes.Add(candidate.MapType);
            }

            // Pass 2 — drop type-variety preference.
            if (selected.Count < wanted) {
                foreach (var candidate in shuffled) {
                    if (selected.Count >= wanted) break;
                    if (selected.Contains(candidate)) continue;
                    if (usedMappers.Contains(candidate.MapperId)) continue;

                    selected.Add(candidate);
                    usedMappers.Add(candidate.MapperId);
                }
            }

            // Pass 3 — last-resort, allow mapper repeats.
            if (selected.Count < wanted) {
                foreach (var candidate in shuffled) {
                    if (selected.Count >= wanted) break;
                    if (selected.Contains(candidate)) continue;

                    selected.Add(candidate);
                }
            }

            return selected;
        }

        /// <summary>
        /// Last-N-series leaderboard IDs across both players. With best-of-3 series
        /// having 2–3 games each, we over-fetch per player to be safe.
        /// </summary>
        private static async Task<HashSet<string>> GetRecentMatchMapIds(
            AppContext context,
            int seasonId,
            string playerAId,
            string playerBId
        ) {
            var playerAMaps = await context.RankedPlayGames
                .Where(g =>
                    g.Match!.SeasonId == seasonId &&
                    (g.Match.PlayerAId == playerAId || g.Match.PlayerBId == playerAId) &&
                    g.Match.Result == RankedPlayMatchResult.Completed &&
                    g.LeaderboardId != null
                )
                .OrderByDescending(g => g.Match!.Timestamp)
                .ThenByDescending(g => g.RoundNumber)
                .Take(RecentGameExclusionCount)
                .Select(g => g.LeaderboardId!)
                .ToListAsync();

            var playerBMaps = await context.RankedPlayGames
                .Where(g =>
                    g.Match!.SeasonId == seasonId &&
                    (g.Match.PlayerAId == playerBId || g.Match.PlayerBId == playerBId) &&
                    g.Match.Result == RankedPlayMatchResult.Completed &&
                    g.LeaderboardId != null
                )
                .OrderByDescending(g => g.Match!.Timestamp)
                .ThenByDescending(g => g.RoundNumber)
                .Take(RecentGameExclusionCount)
                .Select(g => g.LeaderboardId!)
                .ToListAsync();

            var combined = new HashSet<string>(playerAMaps);
            combined.UnionWith(playerBMaps);
            return combined;
        }
    }

    public class MapCandidate {
        public string LeaderboardId { get; set; }
        public string SongName { get; set; }
        public string SongAuthor { get; set; }
        public string SongMapper { get; set; }
        public int MapperId { get; set; }
        public string CoverImage { get; set; }
        public float Stars { get; set; }
        public string DifficultyName { get; set; }
        public string ModeName { get; set; }
        public MapTypes MapType { get; set; }
        public double Duration { get; set; }
        public string SongHash { get; set; }
        public string DownloadUrl { get; set; }
    }
}
