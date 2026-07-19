using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class RankedPlayController : Controller {
        private readonly AppContext _context;

        public RankedPlayController(AppContext context) {
            _context = context;
        }

        [HttpGet("~/rankedplay/seasons")]
        public async Task<ActionResult<IEnumerable<RankedPlaySeasonResponse>>> GetSeasons() {
            var seasons = await _context.RankedPlaySeasons
                .AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => new RankedPlaySeasonResponse {
                    Id = s.Id,
                    Name = s.Name,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    IsActive = s.IsActive
                })
                .ToListAsync();

            return Ok(seasons);
        }

        [HttpGet("~/rankedplay/profile/{playerId}")]
        public async Task<ActionResult<RankedPlayProfileResponse>> GetProfile(
            string playerId,
            [FromQuery] int? seasonId = null) {

            var season = seasonId != null
                ? await _context.RankedPlaySeasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId)
                : await RankedPlayControllerHelper.GetActiveSeason(_context);

            if (season == null) return NotFound("No active season found");

            var profile = await RankedPlayControllerHelper.ProfileResponseQuery(
                _context.RankedPlayProfiles
                    .AsNoTracking()
                    .Where(p => p.PlayerId == playerId && p.SeasonId == season.Id)
            ).FirstOrDefaultAsync();

            if (profile == null) return NotFound("Player has no ranked play profile for this season");

            profile.Season = new RankedPlaySeasonResponse {
                Id = season.Id,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                IsActive = season.IsActive
            };

            // 1-based rank within the season. Uncalibrated players (§5.6) are
            // excluded from ranking entirely — Rank stays 0 so the mod can hide
            // the rank chip and show "Placement N / 5" instead.
            if (profile.IsCalibrated) {
                float selfMmr = profile.MMR ?? 0f;
                int higher = await _context.RankedPlayProfiles
                    .AsNoTracking()
                    .CountAsync(p => p.SeasonId == season.Id && p.IsCalibrated && p.MMR > selfMmr);
                profile.Rank = higher + 1;
            }

            return Ok(profile);
        }

        [HttpGet("~/rankedplay/profile/{playerId}/mmr-history")]
        public async Task<ActionResult<RankedPlayMmrHistoryResponse>> GetMmrHistory(
            string playerId,
            [FromQuery] int? seasonId = null,
            [FromQuery] int count = 50) {

            count = Math.Clamp(count, 1, 200);

            var season = seasonId != null
                ? await _context.RankedPlaySeasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId)
                : await RankedPlayControllerHelper.GetActiveSeason(_context);

            if (season == null) return NotFound("No active season found");

            // Walk the most-recent N matches descending, then reverse so the
            // mod can plot left→right as time→now.
            var recent = await _context.RankedPlayMatches
                .AsNoTracking()
                .Where(m => m.SeasonId == season.Id && (m.PlayerAId == playerId || m.PlayerBId == playerId))
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .Select(m => new {
                    m.Id,
                    m.Timestamp,
                    PreMMR = m.PlayerAId == playerId ? m.PlayerAPreMatchMMR : m.PlayerBPreMatchMMR,
                    Change = m.PlayerAId == playerId ? m.PlayerAMMRChange : m.PlayerBMMRChange,
                    m.WinnerId,
                    m.Result
                })
                .ToListAsync();

            recent.Reverse();

            var entries = recent.Select(m => new RankedPlayMmrHistoryEntry {
                MatchId = m.Id,
                Timestamp = m.Timestamp,
                MMR = m.PreMMR + m.Change,
                Change = m.Change,
                Win = m.WinnerId == playerId,
                Draw = m.Result == RankedPlayMatchResult.Drawn
            }).ToList();

            var currentMmr = await _context.RankedPlayProfiles
                .AsNoTracking()
                .Where(p => p.PlayerId == playerId && p.SeasonId == season.Id)
                .Select(p => (float?)p.MMR)
                .FirstOrDefaultAsync();

            return Ok(new RankedPlayMmrHistoryResponse {
                SeasonId = season.Id,
                CurrentMMR = currentMmr ?? (entries.Count > 0 ? entries[^1].MMR : 0f),
                Entries = entries
            });
        }

        [HttpGet("~/rankedplay/profile/{playerId}/history")]
        public async Task<ActionResult<ResponseWithMetadata<RankedPlayMatchResponse>>> GetMatchHistory(
            string playerId,
            [FromQuery] int? seasonId = null,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] Order order = Order.Desc) {

            var season = seasonId != null
                ? await _context.RankedPlaySeasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId)
                : await RankedPlayControllerHelper.GetActiveSeason(_context);

            if (season == null) return NotFound("No active season found");

            var query = _context.RankedPlayMatches
                .AsNoTracking()
                .Where(m => m.SeasonId == season.Id && (m.PlayerAId == playerId || m.PlayerBId == playerId));

            query = order == Order.Desc
                ? query.OrderByDescending(m => m.Timestamp)
                : query.OrderBy(m => m.Timestamp);

            int total = await query.CountAsync();

            var matches = await RankedPlayControllerHelper.MatchResponseQuery(
                query.Skip((page - 1) * count).Take(count)
            ).ToListAsync();

            return Ok(new ResponseWithMetadata<RankedPlayMatchResponse> {
                Metadata = new Metadata {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                },
                Data = matches
            });
        }

        [HttpGet("~/rankedplay/seasons/{seasonId}/leaderboard")]
        public async Task<ActionResult<RankedPlayLeaderboardResponse>> GetLeaderboard(
            int seasonId,
            [FromQuery] int page = 1,
            [FromQuery] int count = 50,
            [FromQuery] string? country = null,
            [FromQuery] string? scope = null,
            [FromQuery] string? player = null) {

            // Uncalibrated players are excluded from rankings (§5.6) — they
            // don't appear in the leaderboard regardless of internal MMR.
            var query = _context.RankedPlayProfiles
                .AsNoTracking()
                .Where(p => p.IsCalibrated);

            if (country != null) {
                query = query.Where(p => p.Player.Country == country);
            }

            query = query.OrderByDescending(p => p.MMR);

            int total = await query.CountAsync();

            // "around" centers the requested page on the requesting player so
            // the mod's leaderboard view can highlight them mid-list.
            if (scope?.ToLowerInvariant() == "around" && !string.IsNullOrEmpty(player)) {
                var targetMmr = await query
                    .Where(p => p.PlayerId == player)
                    .Select(p => (float?)p.MMR)
                    .FirstOrDefaultAsync();
                if (targetMmr.HasValue) {
                    int higherCount = await query.CountAsync(p => p.MMR > targetMmr.Value);
                    page = (higherCount / count) + 1;
                }
            }

            var entries = await query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(p => new RankedPlayLeaderboardEntry {
                    MMR = p.MMR,
                    Tier = p.Tier,
                    TierDivision = p.TierDivision,
                    GrandmasterRank = p.GrandmasterRank,
                    Wins = p.Wins,
                    Losses = p.Losses,
                    Draws = p.Draws,
                    IsCalibrated = p.IsCalibrated,
                    Player = new PlayerResponse {
                        Id = p.Player.Id,
                        Name = p.Player.Name,
                        Alias = p.Player.Alias,
                        Platform = p.Player.Platform,
                        Avatar = p.Player.Avatar,
                        Country = p.Player.Country,
                        Pp = p.Player.Pp,
                        Rank = p.Player.Rank,
                        CountryRank = p.Player.CountryRank,
                        Role = p.Player.Role,
                        ProfileSettings = p.Player.ProfileSettings,
                        ClanOrder = p.Player.ClanOrder,
                        Clans = p.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    }
                })
                .ToListAsync();

            int rankOffset = (page - 1) * count;
            for (int i = 0; i < entries.Count; i++) {
                entries[i].Rank = rankOffset + i + 1;
            }

            // Resolve the requesting player's own row so the mod can pin it
            // when they're outside the visible page. Computed against the
            // SAME query (so country filter + calibration filter carry over);
            // null if not signed in or no profile under the active filter.
            RankedPlayLeaderboardEntry? self = null;
            string? selfId = HttpContext.CurrentUserID(_context);
            if (!string.IsNullOrEmpty(selfId)) {
                self = await query
                    .Where(p => p.PlayerId == selfId)
                    .Select(p => new RankedPlayLeaderboardEntry {
                        MMR = p.MMR,
                        Tier = p.Tier,
                        TierDivision = p.TierDivision,
                        GrandmasterRank = p.GrandmasterRank,
                        Wins = p.Wins,
                        Losses = p.Losses,
                        Draws = p.Draws,
                        IsCalibrated = p.IsCalibrated,
                        Player = new PlayerResponse {
                            Id = p.Player.Id,
                            Name = p.Player.Name,
                            Alias = p.Player.Alias,
                            Platform = p.Player.Platform,
                            Avatar = p.Player.Avatar,
                            Country = p.Player.Country,
                            Pp = p.Player.Pp,
                            Rank = p.Player.Rank,
                            CountryRank = p.Player.CountryRank,
                            Role = p.Player.Role,
                            ProfileSettings = p.Player.ProfileSettings,
                            ClanOrder = p.Player.ClanOrder,
                            Clans = p.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                        }
                    })
                    .FirstOrDefaultAsync();

                if (self != null) {
                    int higher = await query.CountAsync(p => p.MMR > self.MMR);
                    self.Rank = higher + 1;
                }
            }

            return Ok(new RankedPlayLeaderboardResponse {
                Metadata = new Metadata {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                },
                Data = entries,
                Self = self
            });
        }

        [HttpGet("~/rankedplay/match/{matchId}")]
        public async Task<ActionResult<RankedPlayMatchResponse>> GetMatch(int matchId) {
            var match = await RankedPlayControllerHelper.MatchResponseQuery(
                _context.RankedPlayMatches
                    .AsNoTracking()
                    .Where(m => m.Id == matchId)
            ).FirstOrDefaultAsync();

            if (match == null) return NotFound();

            return Ok(match);
        }

        [HttpGet("~/rankedplay/queue/status")]
        public ActionResult<RankedPlayQueueStatusResponse> GetQueueStatus() {
            int playersInQueue = RankedPlayBridgeService.PlayersInQueue;
            int activeMatches = RankedPlayBridgeService.ActiveMatches;
            int estimatedWait = playersInQueue > 0
                ? Math.Max(5, 30 - (playersInQueue * 2))
                : 0;

            return Ok(new RankedPlayQueueStatusResponse {
                PlayersInQueue = playersInQueue,
                ActiveMatches = activeMatches,
                OnlinePlayers = playersInQueue + (activeMatches * 2),
                EstimatedWaitSeconds = estimatedWait
            });
        }
    }
}
