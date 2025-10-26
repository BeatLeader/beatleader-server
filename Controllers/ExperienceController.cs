using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Utils.ResponseUtils;
using BeatLeader_Server.ControllerHelpers;
using Newtonsoft.Json;

namespace BeatLeader_Server.Controllers
{
    public class ExperienceController : Controller
    {
        private readonly AppContext _context;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public ExperienceController(
            AppContext context,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpGet("~/experience/prestige")]
        [SwaggerOperation(Summary = "Reset the current player level and prestige", Description = "Reset the current logged in player level and prestige")]
        [SwaggerResponse(200, "Successful prestige")]
        [SwaggerResponse(400, "Player is already max prestige")]
        [SwaggerResponse(404, "Player not found")]
        public async Task<ActionResult> PrestigePlayer()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = currentID == null ? null : await _context.Players.FindAsync(currentID);

            if (currentPlayer == null)
            {
                return NotFound();
            }

            if (currentPlayer.Prestige == 10)
            {
                return BadRequest();
            }

            if (currentPlayer.Level >= 100)
            {
                currentPlayer.Level = 0;
                currentPlayer.Experience = 0;
                currentPlayer.Prestige++;

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpPost("~/experience/set/level")]
        public async Task<ActionResult> SetLevel([FromQuery] int newValue)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("creator")))
            {
                return Unauthorized();
            }

            var player = await _context.Players.Where(p => p.Id == currentID).FirstOrDefaultAsync();
            if (player == null) {
                return NotFound("Player not found"); 
            }
            player.Level = newValue;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/experience/set/experience")]
        public async Task<ActionResult> SetExperience([FromQuery] int newValue)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("creator")))
            {
                return Unauthorized();
            }

            var player = await _context.Players.Where(p => p.Id == currentID).FirstOrDefaultAsync();
            if (player == null) {
                return NotFound("Player not found"); 
            }
            player.Experience = newValue;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/experience/set/prestige")]
        public async Task<ActionResult> SetPrestige([FromQuery] int newValue)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("creator")))
            {
                return Unauthorized();
            }

            var player = await _context.Players.Where(p => p.Id == currentID).FirstOrDefaultAsync();
            if (player == null) {
                return NotFound("Player not found"); 
            }
            player.Prestige = newValue;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/experience/reset")]
        public async Task<ActionResult> ResetExperienceSystem()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("creator")))
            {
                return Unauthorized();
            }

            var players = await _context.Players.AsNoTracking().Select(p => new Player { Id = p.Id }).ToListAsync();
            await _context.BulkUpdateAsync(players, options => options.ColumnInputExpression = c => new { c.Level, c.Experience, c.Prestige });
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
