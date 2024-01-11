using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Services.SearchService;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class BadgeController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public BadgeController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpPut("~/badge")]
        [Authorize]
        public async Task<ActionResult<Badge>> CreateBadge(
                [FromQuery] string description, 
                [FromQuery] string? link = null,
                [FromQuery] string? image = null,
                [FromQuery] int? timeset = null,
                [FromQuery] string? playerId = null) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Badge badge = new Badge {
                Description = description,
                Image = image ?? "",
                Link = link,
                Timeset = timeset ?? (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            };

            _context.Badges.Add(badge);
            await _context.SaveChangesAsync();

            if (image == null) {
                string? fileName = null;
                try
                {
                    var ms = new MemoryStream(5);
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    (string extension, MemoryStream stream) = ImageUtils.GetFormat(ms);
                    Random rnd = new Random();
                    fileName = "badge-" + badge.Id + "R" + rnd.Next(1, 50) + extension;

                    badge.Image = await _assetsS3Client.UploadAsset(fileName, stream);
                }
                catch {}
            }

            await _context.SaveChangesAsync();

            if (playerId != null) {
                await AddBadge(playerId, badge.Id);
            }

            return badge;
        }

        [HttpPut("~/badge/{id}")]
        [Authorize]
        public async Task<ActionResult<Badge>> UpdateBadge(int id, [FromQuery] string? description, [FromQuery] string? image, [FromQuery] string? link = null)
        {
            string? currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentId != null ? _context.Players.Find(currentId) : null;
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var badge = _context.Badges.Find(id);

            if (badge == null) {
                return NotFound();
            }

            if (description != null) {
                badge.Description = description;
            }

            if (image != null) {
                badge.Image = image;
            }

            if (Request.Query.ContainsKey("link"))
            {
                badge.Link = link;
            }

            await _context.SaveChangesAsync();

            return badge;
        }

        [HttpPut("~/player/badge/{playerId}/{badgeId}")]
        [Authorize]
        public async Task<ActionResult<Player>> AddBadge(string playerId, int badgeId)
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            Player? player = _context.Players.Include(p => p.Badges).FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound("Player not found");
            }

            Badge? badge = await _context.Badges.FindAsync(badgeId);
            if (badge == null)
            {
                return NotFound("Badge not found");
            }
            if (player.Badges == null) {
                player.Badges = new List<Badge>();
            }

            player.Badges.Add(badge);
            await _context.SaveChangesAsync();

            return player;
        }
    }
}
