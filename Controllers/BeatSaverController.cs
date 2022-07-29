using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BeatSaverController : Controller
    {
        private readonly AppContext _context;
        IWebHostEnvironment _environment;

        public BeatSaverController(
            AppContext context,
            IWebHostEnvironment env)
        {
            _context = context;
            _environment = env;
        }

        [NonAction]
        public async Task<ActionResult> AddMapperRole(string playerId)
        {
            Player? currentPlayer = _context.Players.Find(playerId);
            if (currentPlayer != null && !currentPlayer.Role.Contains("mapper"))
            {
                currentPlayer.Role = string.Join(",", currentPlayer.Role.Split(",").Append("mapper"));

                _context.SaveChanges();
            }
            return Ok();
        }

        [HttpGet("~/user/linkBeatSaverAndApprove")]
        public async Task<ActionResult> LinkBeatSaverAndApprove([FromQuery] string leaderboardId, [FromQuery] string? returnUrl = null) {
            (int? id, string? error) = await LinkBeatSaverPrivate();
            if (id == null)
            {
                return Unauthorized(error);
            }
            var leaderboard = _context.Leaderboards.Where(lb => lb.Id == leaderboardId).Include(lb => lb.Qualification).Include(lb => lb.Song).FirstOrDefault();

            if (leaderboard == null || leaderboard.Qualification == null) {
                return returnUrl != null ? Redirect(returnUrl) : NotFound();
            }

            if (leaderboard.Song.MapperId == id) {
                leaderboard.Qualification.MapperAllowed = true;
                await _context.SaveChangesAsync();
            }

            return returnUrl != null ? Redirect(returnUrl) : Ok();
        }

        [HttpGet("~/user/approveQualification")]
        public async Task<ActionResult> ApproveQualification([FromQuery] string leaderboardId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentPlayer.MapperId == 0)
            {
                return Unauthorized();
            }

            var leaderboard = _context.Leaderboards.Where(lb => lb.Id == leaderboardId).Include(lb => lb.Qualification).Include(lb => lb.Song).FirstOrDefault();

            if (leaderboard == null || leaderboard.Qualification == null)
            {
                return NotFound();
            }

            if (leaderboard.Song.MapperId == currentPlayer.MapperId)
            {
                leaderboard.Qualification.MapperAllowed = true;
                await _context.SaveChangesAsync();
                return Ok();
            } else {
                return Unauthorized("Mapper id is different from yours");
            }
        }

        [HttpGet("~/user/linkBeatSaver")]
        public async Task<ActionResult> LinkBeatSaver([FromQuery] string? returnUrl = null)
        {
            (int? id, string? error) = await LinkBeatSaverPrivate();
            if (id == null) {
                return Unauthorized(error);
            }
            return returnUrl != null ? Redirect(returnUrl) : Ok();
        }

        public async Task<(int?, string?)> LinkBeatSaverPrivate() {
            var auth = await HttpContext.AuthenticateAsync("BeatSaver");
            string? beatSaverId = auth?.Principal?.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
            if (beatSaverId == null)
            {
                return (null, "Need to login with BeatSaver first");  
            }

            var bslink = _context.BeatSaverLinks.Where(link => link.BeatSaverId == beatSaverId).FirstOrDefault();
            string? playerId = HttpContext.CurrentUserID(_context);

            if (playerId != null && bslink != null && bslink.Id != playerId) {
                return (null, "Something went wrong while linking");
            }

            Player? player = null;

            if (playerId == null) {
                if (bslink == null) {
                    player = await PlayerUtils.GetPlayerFromBeatSaver(beatSaverId);
                    if (player == null) {
                        return (null, "Could not receive this user from BeatSaver");
                    }

                    _context.Players.Add(player);
                    playerId = player.Id;

                } else {
                    playerId = bslink.Id;
                }

                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, playerId) };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Cookies");

                await AuthenticationHttpContextExtensions.SignInAsync(HttpContext, CookieAuthenticationDefaults.AuthenticationScheme, principal);

                var result = AuthenticateResult.Success(ticket);
            }

            if (bslink == null) {
                string? token = auth?.Properties?.Items[".Token.access_token"];
                string? refreshToken = auth?.Properties?.Items[".Token.refresh_token"];
                string? timestamp = auth?.Properties?.Items[".Token.expires_at"];

                if (token != null && refreshToken != null && timestamp != null) {
                    _context.BeatSaverLinks.Add(new BeatSaverLink
                    {
                        BeatSaverId = beatSaverId,
                        Id = playerId,
                        Token = token,
                        RefreshToken = refreshToken,
                        Timestamp = timestamp
                    });
                }

                await AddMapperRole(playerId);

                if (player == null) {
                    player = _context.Players.Where(p => p.Id == playerId).FirstOrDefault();
                }
                if (player != null) {
                    player.MapperId = Int32.Parse(beatSaverId);
                }
            }
            _context.SaveChanges();

            return (Int32.Parse(beatSaverId), null);
        }
    }
}

