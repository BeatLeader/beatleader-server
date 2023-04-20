using BeatLeader_Server.Extensions;
using BeatLeader_Server.Migrations;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static System.Net.WebRequestMethods;

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
            Player? currentPlayer = await _context.Players.FindAsync(playerId);
            if (currentPlayer != null && !currentPlayer.Role.Contains("mapper"))
            {
                currentPlayer.Role = string.Join(",", currentPlayer.Role.Split(",").Append("mapper"));

                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [NonAction]
        private async Task<ActionResult> ApproveWithMapper(string leaderboardId, Player? player) {
            if (player == null)
            {
                return Unauthorized("Could find mapper");
            }

            var leaderboards = _context.Leaderboards
                .Where(lb => lb.Qualification != null && lb.Song.MapperId == player.MapperId)
                .Include(lb => lb.Qualification)
                .Include(lb => lb.Song)
                .ToList();
            var leaderboard = leaderboards.FirstOrDefault(lb => lb.Id == leaderboardId);
            var leaderboardsToApprove = leaderboard != null ? leaderboards.Where(lb => lb.Song.Id == leaderboard.Song.Id).ToList() : null;

            if (leaderboardsToApprove == null || leaderboardsToApprove.Count() == 0 || leaderboardsToApprove[0].Qualification == null)
            {
                return NotFound("Mapper id is different from yours");
            }

            foreach (var lb in leaderboardsToApprove)
            {
                lb.Qualification.MapperAllowed = true;
                lb.Qualification.MapperId = player.Id;
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/user/linkBeatSaverAndApprove")]
        public async Task<ActionResult> LinkBeatSaverAndApprove([FromQuery] string leaderboardId, [FromQuery] string? returnUrl = null) {
            (int? id, string? error) = await LinkBeatSaverPrivate();
            if (id == null)
            {
                return Unauthorized(error);
            }

            var actionResult = await ApproveWithMapper(leaderboardId, _context.Players.Where(p => p.MapperId == id).FirstOrDefault());
            

            return returnUrl != null ? Redirect(returnUrl) : actionResult;
        }

        [HttpGet("~/user/approveQualification")]
        public async Task<ActionResult> ApproveQualification([FromQuery] string leaderboardId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            return await ApproveWithMapper(leaderboardId, await _context.Players.FindAsync(currentID));
        }

        [HttpGet("~/user/linkBeatSaver")]
        public async Task<ActionResult> LinkBeatSaver([FromQuery] string? returnUrl = null, [FromQuery] string? oauthState = null)
        {
            (int? id, string? error) = await LinkBeatSaverPrivate();
            if (id == null) {
                return Unauthorized(error);
            }
            if (oauthState != null) {
                return Redirect($"/oauth2/authorize{oauthState}");
            }
            return returnUrl != null ? Redirect(returnUrl) : Ok();
        }

        [NonAction]
        public async Task<(int?, string?)> LinkBeatSaverPrivate() {
            var auth = await HttpContext.AuthenticateAsync("BeatSaver");
            string? beatSaverId = auth?.Principal?.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
            if (beatSaverId == null)
            {
                return (null, "Need to login with BeatSaver first");  
            }

            var bslink = _context.BeatSaverLinks.FirstOrDefault(link => link.BeatSaverId == beatSaverId);
            string? playerId = HttpContext.CurrentUserID(_context);

            if (playerId != null && bslink != null && bslink.Id != playerId) {
                if (long.Parse(bslink.Id) > 30000000 && long.Parse(bslink.Id) < 1000000000000000) {
                    var lbs = _context.Leaderboards.Where(lb => lb.Qualification != null && lb.Qualification.MapperId == bslink.Id).Include(lb => lb.Qualification).ToList();

                    foreach (var lb in lbs)
                    {
                        if (lb.Qualification.MapperAllowed && lb.Qualification.MapperId != null)
                        {
                            lb.Qualification.MapperId = playerId;
                            if (lb.Qualification.MapperQualification)
                            {
                                lb.Qualification.RTMember = playerId;
                            }
                        }
                    }

                    var oldplayer = _context.Players
                        .Where(p => p.Id == bslink.Id)
                        .Include(p => p.Socials)
                        .Include(p => p.ProfileSettings)
                        .Include(p => p.History)
                        .Include(p => p.Changes)
                        .Include(p => p.Achievements)
                        .FirstOrDefault();
                    if (oldplayer != null)
                    {
                        var plink = _context.PatreonLinks.Where(l => l.Id == oldplayer.Id).FirstOrDefault();
                        if (plink != null) {
                            _context.PatreonLinks.Remove(plink);
                        }
                        oldplayer.Socials = null;
                        oldplayer.ProfileSettings = null;
                        oldplayer.History = null;
                        oldplayer.Changes = null;
                        oldplayer.Achievements = null;
                        _context.Players.Remove(oldplayer);
                    }
                    _context.BeatSaverLinks.Remove(bslink);
                    bslink = null;
                } else {
                    return (null, "This account is already linked to another BL account");
                }
            }

            Player? player = null;

            (Player? bsplayer, bool _) = await PlayerUtils.GetPlayerFromBeatSaver(beatSaverId);

            if (playerId == null) {
                if (bslink == null) {
                    player = bsplayer;
                    if (player == null) {
                        return (null, "Could not receive this user from BeatSaver");
                    }

                    _context.Players.Add(player);
                    playerId = player.Id;
                    PlayerSearchService.AddNewPlayer(player);

                } else {
                    playerId = bslink.Id;
                }

                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, playerId) };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Cookies");

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

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

                player ??= _context.Players.Include(p => p.Socials).FirstOrDefault(p => p.Id == playerId);
                if (player != null) {
                    player.MapperId = Int32.Parse(beatSaverId);
                    player.Socials ??= new List<PlayerSocial>();
                    player.Socials.Add(new PlayerSocial {
                        Service = "BeatSaver",
                        UserId = beatSaverId,
                        Link = "https://beatsaver.com/profile/" + beatSaverId,
                        User = bsplayer?.Name ?? ""
                    });
                }
            }
            await _context.SaveChangesAsync();

            return (int.Parse(beatSaverId), null);
        }

        [HttpGet("~/beatsaver/refresh")]
        public async Task<ActionResult> BeatSaverRefresh()
        {

            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var mappers = _context.Players.Where(p => p.MapperId != 0).Include(p => p.Socials).ToList();
            foreach (var mapper in mappers)
            {
                if (mapper.Socials == null) {
                    mapper.Socials = new List<PlayerSocial>();
                }

                (Player? bsplayer, bool _) = await PlayerUtils.GetPlayerFromBeatSaver("" + mapper.MapperId);

                mapper.Socials.Add(new PlayerSocial {
                    Service = "BeatSaver",
                    UserId = mapper.MapperId + "",
                    User = bsplayer?.Name ?? "",
                    Link = "https://beatsaver.com/profile/" + mapper.MapperId,
                });
            }
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}

