using BeatLeader_Server.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class ModController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly AppContext _context;

        public ModController(
            IConfiguration configuration,
            AppContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        [HttpGet("~/mod/lastVersions")]
        public ActionResult GetLastVersions()
        {
            if (HttpContext.Request.Headers["User-Agent"].Contains("0.8.0")) {
                return Content(_configuration.GetValue<string>("ModVersionsLatest"), "application/json");
            } else {
                return Content(_configuration.GetValue<string>("ModVersions"), "application/json");
            }
        }

        [HttpPost("~/mod/version")]
        public async Task<ActionResult> AddModVersion([FromQuery] string platform, [FromQuery] string gameVersion, [FromQuery] string version)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            _context.ModVersions.Add(new Models.ModVersion {
                Platform = platform,
                GameVersion = gameVersion,
                Version = version,
                Timeset = Time.UnixNow()
            });
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("~/mod/uptodate")]
        public async Task<ActionResult<bool>> IsModUptodate([FromQuery] string platform, [FromQuery] string gameVersion, [FromQuery] string version)
        {
            var latestVersion = await _context.ModVersions.Where(m => m.Platform == platform && m.GameVersion == gameVersion).OrderByDescending(m => m.Timeset).FirstOrDefaultAsync();
            return latestVersion != null && latestVersion.Version == version;
        }

        [HttpGet("~/servername")]
        public string? ServerName()
        {
            return _configuration.GetValue<string>("ServerName");
        }
    }
}
