using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using SixLabors.ImageSharp;
using Swashbuckle.AspNetCore.Annotations;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Controllers {
    public class SpeedrunController : Controller {
        private readonly AppContext _context;
        private readonly SpeedrunService _speedrunService;

        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;

        public SpeedrunController(
            AppContext context,
            SpeedrunService speedrunService,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _environment = env;
            _speedrunService = speedrunService;
            _s3Client = configuration.GetS3Client();
        }

        [HttpPost("~/speedrun/start")]
        public async Task<ActionResult> StartSpeedrun()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;
            if (currentPlayer == null) Unauthorized();


            var timeset = Time.UnixNow();
            var scores = _context
                .Scores
                .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.Speedrun))
                .Include(s => s.ContextExtensions)
                .ToList();
            var playerExtension = _context
                .PlayerContextExtensions
                .Where(ce => ce.PlayerId == currentID && ce.Context == LeaderboardContexts.Speedrun)
                .Include(p => p.ScoreStats)
                .FirstOrDefault();

            if (timeset - currentPlayer.SpeedrunStart < 60 * 60) {
                foreach (var score in scores) {
                    var ce = score.ContextExtensions.Where(c => c.Context == LeaderboardContexts.Speedrun).FirstOrDefault();
                    if (ce != null) {
                        _context.ScoreContextExtensions.Remove(ce);
                    }

                    score.ValidContexts &= ~LeaderboardContexts.Speedrun;
                    if (score.ValidContexts == LeaderboardContexts.None) {
                        _context.Scores.Remove(score);
                    }
                }
                if (playerExtension != null) {
                    _context.PlayerContextExtensions.Remove(playerExtension);
                }
            } else {
                foreach (var score in scores) {
                    var ce = score.ContextExtensions.Where(c => c.Context == LeaderboardContexts.Speedrun).FirstOrDefault();
                    if (ce != null) {
                        ce.Context = LeaderboardContexts.SpeedrunBackup;
                    }

                    score.ValidContexts &= ~LeaderboardContexts.Speedrun;
                    score.ValidContexts |= LeaderboardContexts.SpeedrunBackup;
                }
                if (playerExtension != null) {
                    playerExtension.Context = LeaderboardContexts.SpeedrunBackup;
                }
            }

            currentPlayer.SpeedrunStart = timeset;
            await _context.BulkSaveChangesAsync();
            _speedrunService.SpeedrunStarted(currentID);

            return Ok();
        }
    }
}
