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
    public class AliasManagementController : Controller {

        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public AliasManagementController(
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

        [HttpPost("~/alias/request")]
        public async Task<ActionResult<AliasRequest>> MakeAliasRequest([FromQuery] string alias)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();
            alias = alias.ToLower();

            if (alias.Length < 2 || alias.Length > 20) {
                return BadRequest("Please provide alias between 2 and 20 symbols long");
            }

            var existingRequest = await
                _context.AliasRequests
                .OrderByDescending(ar => ar.Timeset)
                .FirstOrDefaultAsync(ar => ar.PlayerId == currentID);
            var timeset = Time.UnixNow();
            if (existingRequest != null) {
                if (existingRequest.Status == AliasRequestStatus.open) {
                    return BadRequest("You already have an open request");                
                }
                if ((timeset - existingRequest.Timeset) < 60 * 60 * 24 * 7) {
                    return BadRequest($"Please wait {(timeset - existingRequest.Timeset) / 60 * 60} hours before making another request");
                }
            }
            var otherPlayer = await _context.Players.FirstOrDefaultAsync(p => p.Alias == alias || p.OldAlias == alias);
            if (otherPlayer != null) {
                return BadRequest("This alias already in use, please select another. If you want to dispute usage of this alias - please DM NSGolova.");
            }
            if (int.TryParse(alias, out _) && currentID != (await _context.PlayerIdToMain(alias))) {
                return BadRequest("You can't use number only aliases except for your Quest/Oculus ID");
            }

            var ar = new AliasRequest {
                PlayerId = currentID,
                Value = alias,
                Timeset = timeset,
                Status = AliasRequestStatus.open
            };
            _context.AliasRequests.Add(ar);
            _context.SaveChanges();
                
            return ar;
        }

        [HttpGet("~/alias/requests")]
        public async Task<ActionResult<ResponseWithMetadata<AliasRequest>>> GetAliasRequest(
            [FromQuery] int count = 10,
            [FromQuery] int page = 1)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var query = _context.AliasRequests
                .Where(ar => ar.Status == AliasRequestStatus.open);

            return new ResponseWithMetadata<AliasRequest> {
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

        [HttpPost("~/alias/request/{id}/resolve")]
        public async Task<ActionResult<AliasRequest>> ResolveAliasRequest(int id, [FromQuery] AliasRequestStatus status)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var ar = await _context.AliasRequests.FirstOrDefaultAsync(ar => ar.Id == id);
            if (ar == null) {
                return NotFound();
            }

            if (ar.Status == AliasRequestStatus.approved) {
                return BadRequest("Request is already approved");
            }

            if (status == AliasRequestStatus.approved) {
                var playerId = await _context.PlayerIdToMain(ar.PlayerId);
                var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == playerId);
                if (player == null) {
                    return NotFound("Such player not found");
                }

                player.OldAlias = player.Alias;
                player.Alias = ar.Value;
            }
            ar.Status = status;
            _context.SaveChanges();

            return ar;
        }
    }
}
