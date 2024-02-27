using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Net;
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

            var player = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player != null && (player.Socials == null || player.Socials.FirstOrDefault(s => s.Service == "Twitch") == null)) {
                player.Socials ??= new List<PlayerSocial>();

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

                    await _context.SaveChangesAsync();
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

            var player = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player != null && (player.Socials?.FirstOrDefault(s => s.Service == "Twitter") == null))
            {
                player.Socials ??= new List<PlayerSocial>();

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
                        User = "@" + username,
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

                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                return Unauthorized("This Twitch profile is already linked");
            }

            return Redirect(returnUrl);
        }

        [HttpGet("~/user/linkDiscord")]
        public async Task<ActionResult> LinkDiscord([FromQuery] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("Discord");

            var player = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player != null && (player.Socials?.FirstOrDefault(s => s.Service == "Discord") == null))
            {
                player.Socials ??= new List<PlayerSocial>();

                var claims = auth.Principal.Claims;

                var id = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                var discriminator = claims.FirstOrDefault(c => c.Type == "urn:discord:user:discriminator")?.Value;

                ulong ulongId = 0;
                if (!ulong.TryParse(id, out ulongId)) {
                    return Unauthorized("Failed to parse Discord ID, please ping NSGolova");
                }

                string? token = auth?.Properties?.Items[".Token.access_token"];
                string? refreshToken = auth?.Properties?.Items[".Token.refresh_token"];
                string? timestamp = auth?.Properties?.Items[".Token.expires_at"];

                if (discriminator != null && id != null && username != null && token != null)
                {
                    var usertag = discriminator == "0" ? "@" + username : username + "#" + discriminator;
                    player.Socials.Add(new PlayerSocial
                    {
                        Service = "Discord",
                        User = usertag,
                        UserId = id,
                        Link = "https://discordapp.com/users/" + id
                    });
                    _context.DiscordLinks.Add(new DiscordLink
                    {
                        Token = token,
                        RefreshToken = refreshToken ?? "",
                        DiscordId = id,
                        Id = playerId,
                        Timestamp = timestamp ?? ""
                    });

                    await _context.SaveChangesAsync();
                    await PlayerUtils.UpdateBoosterRole(_context, ulongId);
                }
            }
            else
            {
                return Unauthorized("This Discord profile is already linked");
            }

            return Redirect(returnUrl);
        }

        [HttpGet("~/user/linkGoogle")]
        public async Task<ActionResult> LinkGoogle([FromQuery] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("Google");

            var player = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player != null && (player.Socials?.FirstOrDefault(s => s.Service == "YouTube") == null))
            {
                player.Socials ??= new List<PlayerSocial>();

                var claims = auth.Principal.Claims;

                var id = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

                string? token = auth?.Properties?.Items[".Token.access_token"];
                string? timestamp = auth?.Properties?.Items[".Token.expires_at"];

                if (id != null && username != null && token != null)
                {
                    var channels = await ListChanneld(token);
                    string? channelLink = null;
                    if (channels != null && ExpandantoObject.HasProperty(channels, "items") && channels.items.Count > 0) {

                        var channel = channels.items[0];
                        if (channel.kind == "youtube#channel") {
                            channelLink = "https://www.youtube.com/channel/" + channel.id;
                            username = channel.snippet.title;
                        }
                    }

                    if (channelLink == null) {
                        return Unauthorized("Please login with the YouTube account");
                    }

                    player.Socials.Add(new PlayerSocial
                    {
                        Service = "YouTube",
                        User = username,
                        UserId = id,
                        Link = channelLink
                    });
                    _context.YouTubeLinks.Add(new YouTubeLink
                    {
                        Token = token,
                        GoogleId = id,
                        Id = playerId,
                        Timestamp = timestamp ?? ""
                    });

                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                return Unauthorized("This YouTube profile is already linked");
            }

            return Redirect(returnUrl);
        }

        // Demo endpoint sample for the BL oauth2 flow
        [HttpGet("~/user/linkBeatLeader")]
        public async Task<ActionResult> LinkBeatLeader([FromQuery] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("BeatLeader");

            var claims = auth.Principal.Claims;

            var id = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

            string? token = auth?.Properties?.Items[".Token.access_token"];
            string? timestamp = auth?.Properties?.Items[".Token.expires_at"];

            return Redirect(returnUrl);
        }

        [NonAction]
        public Task<dynamic?> ListChanneld(string token)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://youtube.googleapis.com/youtube/v3/channels?part=snippet&mine=true&key=" + _configuration.GetValue<string>("GoogleSecret"));
            request.Method = "GET";
            request.Headers.Add("Authorization", "Bearer " + token);
            request.Proxy = null;

            return request.DynamicResponse();
        }

        [HttpPost("~/user/unlink")]
        public async Task<ActionResult> Unlink([FromForm] string provider, [FromForm] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            var loggedInPlayer = playerId != null ? long.Parse(playerId) : 0; 
            if (playerId == null || (!(loggedInPlayer < 30000000 || loggedInPlayer > 1000000000000000) && provider == "BeatSaver"))
            {
                return Redirect(returnUrl);
            }

            var player = await _context.Players.Where(p => p.Id == playerId).Include(p => p.Socials).FirstOrDefaultAsync();

            if (player == null)
            {
                return Redirect(returnUrl);
            }

            string serviceName = provider == "Google" ? "YouTube" : provider;

            var social = player.Socials?.Where(s => s.Service == serviceName).FirstOrDefault();
            

            if (social != null) {
                player.Socials.Remove(social);
            }
            switch (provider)
            {
                case "BeatSaver":
                    player.MapperId = 0;
                    player.Role = string.Join(",", player.Role.Split(",").Where(r => r != "mapper"));
                    var link = await _context.BeatSaverLinks.FirstOrDefaultAsync(l => l.Id == player.Id);
                    if (link != null) {
                        _context.BeatSaverLinks.Remove(link);
                    }
                    break;
                case "Twitter":
                    var link1 = await _context.TwitterLinks.FirstOrDefaultAsync(l => l.Id == player.Id);
                    if (link1 != null)
                    {
                        _context.TwitterLinks.Remove(link1);
                    }
                    break;
                case "Twitch":
                    var link2 = await _context.TwitchLinks.FirstOrDefaultAsync(l => l.Id == player.Id);
                    if (link2 != null)
                    {
                        _context.TwitchLinks.Remove(link2);
                    }
                    break;
                case "Google":
                    var link3 = await _context.YouTubeLinks.FirstOrDefaultAsync(l => l.Id == player.Id);
                    if (link3 != null)
                    {
                        _context.YouTubeLinks.Remove(link3);
                    }
                    break;
                case "Discord":
                    var link4 = await _context.DiscordLinks.FirstOrDefaultAsync(l => l.Id == player.Id);
                    if (link4 != null)
                    {
                        _context.DiscordLinks.Remove(link4);
                        PlayerUtils.UpdateBoosterRole(player, null);
                    }
                    break;
                default:
                    break;
            }

            await HttpContext.SignOutAsync("BL" + provider);
            await _context.SaveChangesAsync();

            return Redirect(returnUrl);
        }
    }
}
