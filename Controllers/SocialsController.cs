using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Numerics;
using System.Security.Claims;

namespace BeatLeader_Server.Controllers
{
    public class SocialsController : Controller
    {
        private readonly AppContext _context;
        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        public SocialsController(
            AppContext context,
            IConfiguration configuration,
            IWebHostEnvironment env)
        {
            _context = context;
            _configuration = configuration;
            _environment = env;
        }

        [HttpGet("~/user/linkTwitch")]
        public async Task<ActionResult> LinkTwitch([FromQuery] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("Twitch");

            var player = _context.Players.Include(p => p.Socials).Where(p => p.Id == playerId).FirstOrDefault();
            if (player != null && (player.Socials == null || player.Socials.FirstOrDefault(s => s.Service == "Twitch") == null)) {
                if (player.Socials == null) {
                    player.Socials = new List<PlayerSocial>();
                }

                var claims = auth.Principal.Claims;

                var name = claims.FirstOrDefault(c => c.Type == "urn:twitch:displayname")?.Value;
                var id = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

                string? token = auth?.Properties?.Items[".Token.access_token"];
                string? refreshToken = auth?.Properties?.Items[".Token.refresh_token"];

                if (name != null && id != null && username != null && token != null && refreshToken != null) {
                    player.Socials.Add(new PlayerSocial {
                        Service = "Twitch",
                        User = name,
                        UserId = id,
                        Link = "https://www.twitch.tv/" + username
                    });
                    _context.TwitchLinks.Add(new TwitchLink {
                        Token = token,
                        RefreshToken = refreshToken,
                        TwitchId = id,
                        Id = playerId
                    });;

                    _context.SaveChanges();
                }
            } else {
                return Unauthorized("This Twitch profile is already linked");
            }

            return Redirect(returnUrl);
        }

        [HttpGet("~/user/linkTwitter")]
        public async Task<ActionResult> LinkTwitter([FromQuery] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("Twitter");

            var player = _context.Players.Include(p => p.Socials).Where(p => p.Id == playerId).FirstOrDefault();
            if (player != null && (player.Socials == null || player.Socials.FirstOrDefault(s => s.Service == "Twitter") == null))
            {
                if (player.Socials == null)
                {
                    player.Socials = new List<PlayerSocial>();
                }

                var claims = auth.Principal.Claims;

                var name = claims.FirstOrDefault(c => c.Type == "urn:twitter:name")?.Value;
                var id = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

                string? token = auth?.Properties?.Items[".Token.access_token"];

                if (name != null && id != null && username != null && token != null)
                {
                    player.Socials.Add(new PlayerSocial
                    {
                        Service = "Twitter",
                        User = name,
                        UserId = id,
                        Link = "https://twitter.com/" + username
                    });
                    _context.TwitterLinks.Add(new TwitterLink
                    {
                        Token = token,
                        RefreshToken = "",
                        TwitterId = id,
                        Id = playerId
                    });

                    _context.SaveChanges();
                }
            }
            else
            {
                return Unauthorized("This Twitch profile is already linked");
            }

            return Redirect(returnUrl);
        }

        [HttpPost("~/user/unlink")]
        public async Task<ActionResult> Unlink([FromForm] string provider, [FromForm] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null || (Int64.Parse(playerId) < 1000000000000000 && provider == "BeatSaver"))
            {
                return Redirect(returnUrl);
            }

            var player = _context.Players.Where(p => p.Id == playerId).Include(p => p.Socials).FirstOrDefault();

            if (player == null)
            {
                return Redirect(returnUrl);
            }

            var social = player.Socials?.Where(s => s.Service == provider).FirstOrDefault();
            

            if (social != null) {
                player.Socials.Remove(social);
            }
            switch (provider)
            {
                case "BeatSaver":
                    player.MapperId = 0;
                    player.Role = string.Join(",", player.Role.Split(",").Where(r => r != "mapper"));
                    var link = _context.BeatSaverLinks.Where(l => l.Id == player.Id).FirstOrDefault();
                    if (link != null) {
                        _context.BeatSaverLinks.Remove(link);
                    }
                    break;
                case "Twitter":
                    var link1 = _context.TwitterLinks.Where(l => l.Id == player.Id).FirstOrDefault();
                    if (link1 != null)
                    {
                        _context.TwitterLinks.Remove(link1);
                    }
                    break;
                case "Twitch":
                    var link2 = _context.TwitchLinks.Where(l => l.Id == player.Id).FirstOrDefault();
                    if (link2 != null)
                    {
                        _context.TwitchLinks.Remove(link2);
                    }
                    break;
                default:
                    break;
            }

            await HttpContext.SignOutAsync("BL" + provider);
            _context.SaveChanges();

            return Redirect(returnUrl);
        }
    }
}
