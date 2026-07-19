using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services {
    // MMR + tier + decay logic for Ranked Play.
    // All formulas / constants come from docs/RankedPlay.md §5 — keep both in sync.
    public class RankedPlayMMRService {

        // === Shared seeding bounds (§5.1) ===
        public const float MinSeed   = 1500f;     // Silver III floor — nobody seeds below Silver
        public const float MaxSeed   = 2500f;     // Gold II  ceiling — placement headroom for top performers
        public const float BaseSeed  = 1800f;     // middle of Silver — soft-reset anchor

        // First-season log curve: seedMMR = MaxSeed - LogSlope * log10(rank)
        // Each 10× drop in rank lowers the seed by LogSlope MMR.
        public const float LogSlope  = 200f;

        // Subsequent-season regression-to-the-mean: pull half the distance back to BaseSeed.
        public const float RegressionK = 0.5f;

        // === Per-match MMR change (§5.1) ===
        public const float BaseK         = 100f;  // |delta| ≈ 50 at even MMR
        public const float MaxMMRChange  = 80f;
        public const float MinMMRChange  = -80f;

        // Placement amplifier — sliding K, one step per placement match (§5.1, §5.6).
        // GetKFactor(0) = 280, GetKFactor(1) = 250, ..., GetKFactor(5+) = BaseK.
        public const int PlacementMatchCount = 5;
        private static readonly float[] PlacementKFactors = { 280f, 250f, 220f, 190f, 160f };

        // === Decay (§5.4) — top-100 only ===
        public const int   DecayTopRankCap     = 100;
        public const int   DecayGracePeriodDays = 7;
        public const float DecayPerDay         = 10f;
        public const float MaxTotalDecay       = 200f;
        public const float DecayFloor          = 3900f;  // Master tier floor — can't decay below this

        // === Tier thresholds (§5.2) ===
        // Sorted floor-descending so the first match wins.
        // Format: (tier, tierFloor, divisionII floor, divisionI floor).
        // Grandmaster has no divisions — handled separately.
        private static readonly (RankedPlayTier Tier, float Floor, float DivIIFloor, float DivIFloor)[] TierThresholds = {
            (RankedPlayTier.Grandmaster, 5000f, 5000f, 5000f),
            (RankedPlayTier.Master,      3900f, 4200f, 4600f),
            (RankedPlayTier.Platinum,    3000f, 3300f, 3600f),
            (RankedPlayTier.Gold,        2100f, 2400f, 2700f),
            (RankedPlayTier.Silver,      1500f, 1700f, 1900f),
            (RankedPlayTier.Bronze,      900f,  1100f, 1300f),
            (RankedPlayTier.Cube,        0f,    500f,  700f),
        };

        // === Seeding ===

        /// <summary>
        /// First-season seed from a player's global PP rank — logarithmic curve.
        /// The log handles the long tail gracefully; rank 1 → MaxSeed, rank 10× lower → 200 MMR lower.
        /// </summary>
        public static float SeedMMRFromGlobalRank(int globalRank) {
            int rank = Math.Max(globalRank, 1);
            float raw = MaxSeed - LogSlope * (float)Math.Log10(rank);
            return Math.Clamp(raw, MinSeed, MaxSeed);
        }

        /// <summary>
        /// Subsequent-season seed from previous-season final MMR — regression-to-the-mean
        /// soft reset (§5.1). Most returning players land in Silver, top performers in Gold.
        /// </summary>
        public static float SeedMMRFromPreviousSeason(float previousFinalMMR) {
            float raw = BaseSeed + RegressionK * (previousFinalMMR - BaseSeed);
            return Math.Clamp(raw, MinSeed, MaxSeed);
        }

        /// <summary>
        /// K-factor for a single match given how many placement matches the player has played.
        /// Slides 280 → 250 → 220 → 190 → 160 across the 5 placement matches, then locks at BaseK.
        /// </summary>
        public static float GetKFactor(int calibrationMatchesPlayed) {
            if (calibrationMatchesPlayed < 0) return BaseK;
            if (calibrationMatchesPlayed >= PlacementKFactors.Length) return BaseK;
            return PlacementKFactors[calibrationMatchesPlayed];
        }

        // === Per-match MMR change ===

        /// <summary>
        /// Pure MMR-delta calculation for a best-of-3 series.
        /// Returns (playerAChange, playerBChange).
        /// </summary>
        public static (float PlayerAChange, float PlayerBChange) CalculateMMRChange(
            float playerAMMR,
            float playerBMMR,
            SeriesOutcome outcome,
            int playerACalibrationMatchesPlayed,
            int playerBCalibrationMatchesPlayed
        ) {
            float expectedA = ExpectedScore(playerAMMR, playerBMMR);
            float expectedB = 1f - expectedA;

            float actualA = outcome switch {
                SeriesOutcome.PlayerAWins => 1f,
                SeriesOutcome.PlayerBWins => 0f,
                SeriesOutcome.Draw        => 0.5f,
                _                         => 0.5f,
            };
            float actualB = 1f - actualA;

            float kA = GetKFactor(playerACalibrationMatchesPlayed);
            float kB = GetKFactor(playerBCalibrationMatchesPlayed);

            float rawDeltaA = kA * (actualA - expectedA);
            float rawDeltaB = kB * (actualB - expectedB);

            float deltaA = Math.Clamp(rawDeltaA, MinMMRChange, MaxMMRChange);
            float deltaB = Math.Clamp(rawDeltaB, MinMMRChange, MaxMMRChange);

            return (deltaA, deltaB);
        }

        // === Series-result application ===

        /// <summary>
        /// Applies a completed best-of-3 series to both profiles. Mutates in-place.
        /// Score-cancelling results (BothDisconnected / Cancelled) skip MMR but still
        /// touch LastMatchTime so the decay grace clock restarts.
        /// </summary>
        public static MatchMMRResult ApplyMatchResult(
            RankedPlayProfile profileA,
            RankedPlayProfile profileB,
            int playerAGamesWon,
            int playerBGamesWon,
            RankedPlayMatchResult result
        ) {
            float preA = profileA.MMR;
            float preB = profileB.MMR;

            var outcome = InferOutcome(playerAGamesWon, playerBGamesWon, result);
            bool skipMMR = result == RankedPlayMatchResult.BothDisconnected
                        || result == RankedPlayMatchResult.Cancelled;

            float deltaA = 0f, deltaB = 0f;
            if (!skipMMR) {
                (deltaA, deltaB) = CalculateMMRChange(
                    profileA.MMR, profileB.MMR, outcome,
                    profileA.CalibrationMatchesPlayed, profileB.CalibrationMatchesPlayed);

                profileA.MMR = Math.Max(0f, profileA.MMR + deltaA);
                profileB.MMR = Math.Max(0f, profileB.MMR + deltaB);

                profileA.PeakMMR = Math.Max(profileA.PeakMMR, profileA.MMR);
                profileB.PeakMMR = Math.Max(profileB.PeakMMR, profileB.MMR);
            }

            // Series-counter bookkeeping. Disconnect / forfeit results still count
            // as a played series for the winner's W and loser's L (consistent with §8.3 / §8.4).
            string? winnerId = null;
            switch (outcome) {
                case SeriesOutcome.PlayerAWins:
                    profileA.Wins++;
                    profileB.Losses++;
                    winnerId = profileA.PlayerId;
                    break;
                case SeriesOutcome.PlayerBWins:
                    profileB.Wins++;
                    profileA.Losses++;
                    winnerId = profileB.PlayerId;
                    break;
                case SeriesOutcome.Draw:
                    profileA.Draws++;
                    profileB.Draws++;
                    break;
            }

            // Touch decay clocks regardless of MMR outcome — the player showed up to play.
            profileA.DecayedMMR = 0;
            profileB.DecayedMMR = 0;
            profileA.LastMatchTime = Time.UnixNow();
            profileB.LastMatchTime = Time.UnixNow();

            UpdateCalibration(profileA);
            UpdateCalibration(profileB);

            UpdateTier(profileA);
            UpdateTier(profileB);

            return new MatchMMRResult {
                Outcome = outcome,
                WinnerId = winnerId,
                PlayerAMMRChange = deltaA,
                PlayerBMMRChange = deltaB,
                PlayerAPreMatchMMR = preA,
                PlayerBPreMatchMMR = preB,
                MatchResult = result
            };
        }

        /// <summary>
        /// Maps a series result + per-side games-won counts to a clean win/loss/draw outcome.
        /// Score-cancelling results return Draw (caller is responsible for skipping MMR application).
        /// </summary>
        private static SeriesOutcome InferOutcome(int playerAGamesWon, int playerBGamesWon, RankedPlayMatchResult result) {
            switch (result) {
                case RankedPlayMatchResult.Completed:
                    if (playerAGamesWon > playerBGamesWon) return SeriesOutcome.PlayerAWins;
                    if (playerBGamesWon > playerAGamesWon) return SeriesOutcome.PlayerBWins;
                    return SeriesOutcome.Draw;
                case RankedPlayMatchResult.Drawn:
                    return SeriesOutcome.Draw;
                case RankedPlayMatchResult.PlayerAForfeitedMatch:
                case RankedPlayMatchResult.PlayerADisconnected:
                    return SeriesOutcome.PlayerBWins;
                case RankedPlayMatchResult.PlayerBForfeitedMatch:
                case RankedPlayMatchResult.PlayerBDisconnected:
                    return SeriesOutcome.PlayerAWins;
                case RankedPlayMatchResult.BothDisconnected:
                case RankedPlayMatchResult.Cancelled:
                default:
                    return SeriesOutcome.Draw;
            }
        }

        // === Decay (§5.4) — top-100 only ===

        /// <summary>
        /// Daily decay tick. Returns the MMR lost.
        /// </summary>
        /// <param name="currentRank">Player's current ranking position (1-based) within the season's
        /// calibrated MMR leaderboard. Pass null or any value > <see cref="DecayTopRankCap"/> to
        /// skip — decay applies only to the top 100 (§5.4).</param>
        public static float ApplyDecay(RankedPlayProfile profile, int currentTime, int? currentRank) {
            if (!profile.IsCalibrated) return 0f;
            if (profile.LastMatchTime == 0) return 0f;
            if (currentRank == null || currentRank.Value > DecayTopRankCap) return 0f;

            int secondsSinceLastMatch = currentTime - profile.LastMatchTime;
            int daysSinceLastMatch = secondsSinceLastMatch / 86400;
            if (daysSinceLastMatch <= DecayGracePeriodDays) return 0f;

            int decayDays = daysSinceLastMatch - DecayGracePeriodDays;
            float potentialDecay = decayDays * DecayPerDay;
            float remainingDecayBudget = MaxTotalDecay - profile.DecayedMMR;
            if (remainingDecayBudget <= 0f) return 0f;

            float actualDecay = Math.Min(potentialDecay, remainingDecayBudget);

            // Master tier floor — once decay would push the player below DecayFloor,
            // they also drop out of the top-100 bracket the next tick (§5.4).
            float maxDecayToFloor = profile.MMR - DecayFloor;
            if (maxDecayToFloor <= 0f) return 0f;

            actualDecay = Math.Min(actualDecay, maxDecayToFloor);
            if (actualDecay <= 0f) return 0f;

            profile.MMR -= actualDecay;
            profile.DecayedMMR += actualDecay;
            UpdateTier(profile);

            return actualDecay;
        }

        // === Dodge penalty (kept for §6.3 anti-abuse hook) ===

        public static float ApplyDodgePenalty(RankedPlayProfile profile, float penalty = 10f) {
            profile.MMR = Math.Max(0f, profile.MMR - penalty);
            profile.DecayedMMR = 0;
            profile.LastMatchTime = Time.UnixNow();
            UpdateTier(profile);
            return -penalty;
        }

        // === Profile creation / seeding ===

        /// <summary>
        /// Loads the player's profile for the active season, creating it (with a seeded MMR)
        /// on first access. If a previous-season profile exists and was calibrated, we carry
        /// its final MMR forward via the regression formula; otherwise we fall back to the
        /// rank-based first-season seed.
        /// </summary>
        public static async Task<RankedPlayProfile> GetOrCreateProfile(
            AppContext context,
            string playerId,
            int seasonId,
            int? previousSeasonId = null
        ) {
            var profile = await context.RankedPlayProfiles
                .FirstOrDefaultAsync(p => p.PlayerId == playerId && p.SeasonId == seasonId);

            if (profile != null) return profile;

            float? carriedSeed = null;
            if (previousSeasonId.HasValue) {
                var previousProfile = await context.RankedPlayProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PlayerId == playerId && p.SeasonId == previousSeasonId.Value);

                if (previousProfile != null && previousProfile.IsCalibrated) {
                    carriedSeed = SeedMMRFromPreviousSeason(previousProfile.MMR);
                }
            }

            float seedMMR;
            if (carriedSeed.HasValue) {
                seedMMR = carriedSeed.Value;
            } else {
                var player = await context.Players
                    .AsNoTracking()
                    .Where(p => p.Id == playerId)
                    .Select(p => new { p.Rank, p.Name })
                    .FirstOrDefaultAsync();

                // Test bots (TestPlayer_rank<N>_<idx>) carry their designated rank in
                // their login. They don't have a real PP rank so player.Rank is 0,
                // which would seed everyone at MAX_SEED. Override from the login.
                int? botRank = RankedPlayBotUtils.GetDesignatedRank(player?.Name);
                int rank = botRank ?? player?.Rank ?? 0;
                seedMMR = SeedMMRFromGlobalRank(rank);
            }

            profile = new RankedPlayProfile {
                PlayerId = playerId,
                SeasonId = seasonId,
                MMR = seedMMR,
                PeakMMR = seedMMR,
                Tier = RankedPlayTier.Unranked,   // hidden until placement completes (§5.6)
                TierDivision = 0,
                LastMatchTime = Time.UnixNow()
            };

            context.RankedPlayProfiles.Add(profile);
            await context.SaveChangesAsync();

            return profile;
        }

        // === Tier assignment (§5.2) ===

        public static void UpdateTier(RankedPlayProfile profile) {
            if (!profile.IsCalibrated) {
                profile.Tier = RankedPlayTier.Unranked;
                profile.TierDivision = 0;
                profile.GrandmasterRank = null;
                return;
            }

            foreach (var (tier, floor, divIIFloor, divIFloor) in TierThresholds) {
                if (profile.MMR < floor) continue;

                profile.Tier = tier;

                if (tier == RankedPlayTier.Grandmaster) {
                    // GM has no internal divisions — the literal rank number is the badge (§5.2).
                    // GrandmasterRank is populated by a separate ranking pass (the controller
                    // that lists the leaderboard knows the position), so we just clear the
                    // division here and leave the rank number to be set elsewhere.
                    profile.TierDivision = 0;
                } else if (profile.MMR >= divIFloor) {
                    profile.TierDivision = 1;
                    profile.GrandmasterRank = null;
                } else if (profile.MMR >= divIIFloor) {
                    profile.TierDivision = 2;
                    profile.GrandmasterRank = null;
                } else {
                    profile.TierDivision = 3;
                    profile.GrandmasterRank = null;
                }
                return;
            }

            // MMR < 0 shouldn't happen but guard anyway.
            profile.Tier = RankedPlayTier.Cube;
            profile.TierDivision = 3;
            profile.GrandmasterRank = null;
        }

        // === Private helpers ===

        private static float ExpectedScore(float playerMMR, float opponentMMR) {
            return 1f / (1f + MathF.Pow(10f, (opponentMMR - playerMMR) / 400f));
        }

        private static void UpdateCalibration(RankedPlayProfile profile) {
            if (profile.IsCalibrated) return;

            profile.CalibrationMatchesPlayed++;
            if (profile.CalibrationMatchesPlayed >= PlacementMatchCount) {
                profile.IsCalibrated = true;
            }
        }
    }

    public enum SeriesOutcome {
        PlayerAWins,
        PlayerBWins,
        Draw
    }

    public struct MatchMMRResult {
        public SeriesOutcome Outcome { get; set; }
        public string? WinnerId { get; set; }
        public float PlayerAMMRChange { get; set; }
        public float PlayerBMMRChange { get; set; }
        public float PlayerAPreMatchMMR { get; set; }
        public float PlayerBPreMatchMMR { get; set; }
        public RankedPlayMatchResult MatchResult { get; set; }
    }
}
