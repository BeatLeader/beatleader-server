using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Ganss.Xss;
using System.Net;

namespace BeatLeader_Server.Controllers {
    public class WatermarkManagementController : Controller {

        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public WatermarkManagementController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [HttpPost("~/globalwatermark/request")]
        public async Task<ActionResult<WatermarkRequest>> MakeAliasRequest([FromQuery] string reason)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            var existingRequest = await
                _context.WatermarkRequests
                .OrderByDescending(ar => ar.Timeset)
                .FirstOrDefaultAsync(ar => ar.PlayerId == currentID);
            var timeset = Time.UnixNow();
            if (existingRequest != null) {
                if (existingRequest.Status == WatermarkRequestStatus.open) {
                    return BadRequest("You already have an open request");                
                }
                if ((timeset - existingRequest.Timeset) < 60 * 60 * 24 * 7) {
                    return BadRequest($"Please wait {(timeset - existingRequest.Timeset) / 60 * 60} hours before making another request");
                }
            }
            var player = await _context.Players.Where(p => p.Id == currentID).Include(p => p.DeveloperProfile).FirstOrDefaultAsync();
            if (player == null || player.DeveloperProfile == null) {
                return BadRequest("Please make a developer profile first");
            }

            if (player.DeveloperProfile.GlobalWatermarkPermissions) {
                return BadRequest("You already have permissions to remove replay watermark");
            }

            var wr = new WatermarkRequest {
                PlayerId = currentID,
                Reason = reason,
                Timeset = timeset,
                Status = WatermarkRequestStatus.open
            };
            _context.WatermarkRequests.Add(wr);
            _context.SaveChanges();
                
            return wr;
        }

        [HttpGet("~/globalwatermark/requests")]
        public async Task<ActionResult<ResponseWithMetadata<WatermarkRequest>>> GetAliasRequest(
            [FromQuery] int count = 10,
            [FromQuery] int page = 1)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var query = _context.WatermarkRequests
                .Where(ar => ar.Status == WatermarkRequestStatus.open);

            return new ResponseWithMetadata<WatermarkRequest> {
                Metadata = new Metadata {
                    ItemsPerPage = count,
                    Page = page,
                    Total = await query.CountAsync()
                },
                Data = await query
                    .OrderByDescending(ar => ar.Timeset)
                    .Skip((page - 1) * count)
                    .Take(count)
                    .ToListAsync()
            };
        }

        [HttpPost("~/globalwatermark/request/{id}/resolve")]
        public async Task<ActionResult<WatermarkRequest>> ResolveAliasRequest(int id, [FromQuery] WatermarkRequestStatus status)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var wr = await _context.WatermarkRequests.FirstOrDefaultAsync(wr => wr.Id == id);
            if (wr == null) {
                return NotFound();
            }

            if (wr.Status == WatermarkRequestStatus.approved) {
                return BadRequest("Request is already approved");
            }

            if (status == WatermarkRequestStatus.approved) {
                var playerId = await _context.PlayerIdToMain(wr.PlayerId);
                var player = await _context.Players.Where(p => p.Id == playerId).Include(p => p.DeveloperProfile).FirstOrDefaultAsync();
                if (player == null) {
                    return NotFound("Such player not found");
                }

                if (player.DeveloperProfile == null) {
                    return NotFound("Such player doesn't have a developer profile");
                }

                player.DeveloperProfile.GlobalWatermarkPermissions = true;
            }
            wr.Status = status;
            _context.SaveChanges();

            return wr;
        }
    }
}
