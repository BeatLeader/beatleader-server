using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers {
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class RankedPlayAdminController : Controller {
        private readonly AppContext _context;

        public RankedPlayAdminController(AppContext context) {
            _context = context;
        }

        [HttpPost("~/admin/rankedplay/season")]
        public async Task<ActionResult<RankedPlaySeason>> CreateSeason(
            [FromQuery] string name,
            [FromQuery] int startDate,
            [FromQuery] int endDate) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var activeSeason = await _context.RankedPlaySeasons
                .FirstOrDefaultAsync(s => s.IsActive);

            if (activeSeason != null) {
                activeSeason.IsActive = false;
            }

            var season = new RankedPlaySeason {
                Name = name,
                StartDate = startDate,
                EndDate = endDate,
                IsActive = true
            };

            _context.RankedPlaySeasons.Add(season);
            await _context.SaveChangesAsync();

            return Ok(season);
        }

        [HttpPut("~/admin/rankedplay/season/{seasonId}")]
        public async Task<ActionResult<RankedPlaySeason>> UpdateSeason(
            int seasonId,
            [FromQuery] string? name = null,
            [FromQuery] int? startDate = null,
            [FromQuery] int? endDate = null,
            [FromQuery] bool? isActive = null) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var season = await _context.RankedPlaySeasons.FindAsync(seasonId);
            if (season == null) return NotFound();

            if (name != null) season.Name = name;
            if (startDate != null) season.StartDate = startDate.Value;
            if (endDate != null) season.EndDate = endDate.Value;
            if (isActive != null) {
                if (isActive.Value) {
                    var currentActive = await _context.RankedPlaySeasons
                        .Where(s => s.IsActive && s.Id != seasonId)
                        .FirstOrDefaultAsync();
                    if (currentActive != null) {
                        currentActive.IsActive = false;
                    }
                }
                season.IsActive = isActive.Value;
            }

            await _context.SaveChangesAsync();

            return Ok(season);
        }

        [HttpPost("~/admin/rankedplay/map/ban/{seasonId}")]
        public async Task<ActionResult<RankedPlayMapBan>> BanMap(
            int seasonId,
            [FromQuery] string leaderboardId,
            [FromQuery] string? reason = null) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var season = await _context.RankedPlaySeasons.FindAsync(seasonId);
            if (season == null) return NotFound("Season not found");

            var leaderboard = await _context.Leaderboards.FindAsync(leaderboardId);
            if (leaderboard == null) return NotFound("Leaderboard not found");

            var existing = await _context.RankedPlayMapBans
                .FirstOrDefaultAsync(b => b.SeasonId == seasonId && b.LeaderboardId == leaderboardId);
            if (existing != null) return BadRequest("Map is already banned for this season");

            var ban = new RankedPlayMapBan {
                SeasonId = seasonId,
                LeaderboardId = leaderboardId,
                Reason = reason,
                BannedAt = Time.UnixNow()
            };

            _context.RankedPlayMapBans.Add(ban);
            await _context.SaveChangesAsync();

            return Ok(ban);
        }

        [HttpDelete("~/admin/rankedplay/map/ban/{seasonId}")]
        public async Task<ActionResult> UnbanMap(
            int seasonId,
            [FromQuery] string leaderboardId) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var ban = await _context.RankedPlayMapBans
                .FirstOrDefaultAsync(b => b.SeasonId == seasonId && b.LeaderboardId == leaderboardId);
            if (ban == null) return NotFound("Map ban not found");

            _context.RankedPlayMapBans.Remove(ban);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/admin/rankedplay/map/bans/{seasonId}")]
        public async Task<ActionResult<IEnumerable<RankedPlayMapBan>>> GetBannedMaps(int seasonId) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var bans = await _context.RankedPlayMapBans
                .AsNoTracking()
                .Where(b => b.SeasonId == seasonId)
                .ToListAsync();

            return Ok(bans);
        }

        // DESTRUCTIVE: wipes every season, profile, match, and map-ban row
        // in the ranked play feature. Intended for the pre-rewrite reset
        // described in docs/RankedPlay.md §12 item 16 (schema is changing
        // too much to migrate). Requires ?confirm=true so it can't be hit
        // by accident from a browser bar or Swagger click.
        [HttpDelete("~/admin/rankedplay/wipe")]
        public async Task<ActionResult> WipeAllData([FromQuery] bool confirm = false) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            if (!confirm) {
                return BadRequest("Pass ?confirm=true to acknowledge that this wipes ALL ranked play data.");
            }

            // Delete children before parents to satisfy FK constraints
            // (matches/profiles/map-bans all reference seasons; matches and
            // profiles also reference players). ExecuteDeleteAsync issues a
            // single bulk DELETE per table without loading rows into the
            // change tracker — appropriate for a wipe of unknown size.
            
            int games  = await _context.RankedPlayGames.ExecuteDeleteAsync();
            int matches  = await _context.RankedPlayMatches.ExecuteDeleteAsync();
            int profiles = await _context.RankedPlayProfiles.ExecuteDeleteAsync();
            int mapBans  = await _context.RankedPlayMapBans.ExecuteDeleteAsync();
            int seasons  = await _context.RankedPlaySeasons.ExecuteDeleteAsync();

            return Ok(new {
                DeletedMatches  = matches,
                DeletedGames = games,
                DeletedProfiles = profiles,
                DeletedMapBans  = mapBans,
                DeletedSeasons = seasons
            });
        }

        [HttpGet("~/admin/rankedplay/zeroplayers")]
        public async Task<ActionResult<IEnumerable<RankedPlayMapBan>>> zeroplayers([FromQuery] int seasonId) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var players = await _context.RankedPlayProfiles
                .Where(b => b.SeasonId == seasonId)
                .ToListAsync();
            foreach (var player in players) {
                player.MMR = 1500;
            }

            _context.SaveChanges();

            return Ok();
        }
    }
}
