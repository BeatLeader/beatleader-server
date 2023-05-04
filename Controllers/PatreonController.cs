using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class PatreonController : Controller
    {
        private readonly AppContext _context;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;

        public PatreonController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _environment = env;
            _configuration = configuration;
        }

        [NonAction]
        public async Task<ActionResult> AddPatreonRole(string role, int tier)
        {
            await RemovePatreonRoles();
            string? playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return NotFound();
            }
            Player? currentPlayer = _context.Players.Include(p => p.ProfileSettings).FirstOrDefault(p => p.Id == playerId);
            if (currentPlayer != null)
            {
                currentPlayer.Role += "," + role;
                if (currentPlayer.ProfileSettings == null) {
                    currentPlayer.ProfileSettings = new ProfileSettings();
                }

                currentPlayer.ProfileSettings.EffectName = "TheSun_Tier" + tier;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [NonAction]
        public async Task<ActionResult> RemovePatreonRoles()
        {
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return NotFound();
            }
            Player? currentPlayer = await _context.Players.FindAsync(playerId);
            if (currentPlayer != null)
            {
                currentPlayer.Role = string.Join(",", currentPlayer.Role.Split(",").Where(r => r != "tipper" && r != "supporter" && r != "sponsor"));
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [NonAction]
        public async void UpdatePatreonRole(Player player, string? role)
        {
            player.Role = string.Join(",", player.Role.Split(",").Where(r => r != "tipper" && r != "supporter" && r != "sponsor"));
            if (role != null) {
                player.Role += "," + role;
            }
        }


        [HttpGet("~/user/linkPatreon")]
        public async Task<ActionResult> LinkPatreon([FromQuery] string returnUrl)
        {
            string? playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("Patreon");
            string? token = auth?.Properties?.Items[".Token.access_token"];
            string? refreshToken = auth?.Properties?.Items[".Token.refresh_token"];
            string? timestamp = auth?.Properties?.Items[".Token.expires_at"];

            if (token != null && refreshToken != null && timestamp != null)
            {
                var user = await GetPatreonUser(token);

                if (user != null && ExpandantoObject.HasProperty(user, "included"))
                {
                    string id = user.data.id;

                    var existingPatreonLink = _context.PatreonLinks.FirstOrDefault(pl => pl.PatreonId == id);
                    if (existingPatreonLink != null)
                    {
                        return Redirect(returnUrl);
                    }

                    var data = user.included;

                    string? tier = null;

                    foreach (var item in data)
                    {
                        if (ExpandantoObject.HasProperty(item.attributes, "title"))
                        {
                            tier = item.attributes.title.ToLower();
                            break;
                        }
                    }

                    if (tier != null)
                    {
                        var patreonLink = new PatreonLink
                        {
                            PatreonId = id,
                            Id = playerId,
                            Token = token,
                            Tier = tier,
                            RefreshToken = refreshToken,
                            Timestamp = timestamp
                        };
                        _context.PatreonLinks.Add(patreonLink);
                        if (tier.Contains("tipper"))
                        {
                            await AddPatreonRole("tipper", 1);
                        }
                        else if (tier.Contains("supporter"))
                        {
                            await AddPatreonRole("supporter", 2);
                        }
                        else if (tier.Contains("sponsor"))
                        {
                            await AddPatreonRole("sponsor", 3);
                        }
                    }
                }
            }

            return Redirect(returnUrl);
        }

        [HttpGet("~/refreshPatreon")]
        public async Task<ActionResult> RefreshPatreon()
        {
            if (HttpContext != null) {
                string currentID = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(currentID);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var links = _context.PatreonLinks.ToList();

            foreach (var link in links)
            {
                var user = await GetPatreonUser(link.Token);

                if (user != null) {
                    string? tier = null;

                    foreach (var item in user.included)
                    {
                        if (ExpandantoObject.HasProperty(item.attributes, "title"))
                        {
                            tier = item.attributes.title.ToLower();
                            break;
                        }
                    }

                    if (tier != link.Tier) {
                        var player = _context.Players.FirstOrDefault(p => p.Id == link.Id);
                        if (player == null) {
                            long intId = Int64.Parse(link.Id);
                            if (intId < 70000000000000000) {
                                AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

                                if (accountLink != null) {
                                    string playerId = accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID;

                                    player = _context.Players.FirstOrDefault(p => p.Id == playerId);
                                }
                            }
                        }
                        if (player != null) {
                            if (tier != null) {
                                if (tier.Contains("tipper"))
                                {
                                    UpdatePatreonRole(player, "tipper");
                                }
                                else if (tier.Contains("supporter"))
                                {
                                    UpdatePatreonRole(player, "supporter");
                                }
                                else if (tier.Contains("sponsor"))
                                {
                                    UpdatePatreonRole(player, "sponsor");
                                }
                                else {
                                    UpdatePatreonRole(player, null);
                                }
                                link.Tier = tier;
                            } else {
                                UpdatePatreonRole(player, null);
                                link.Tier = "";
                            }
                        }
                    }
                } else {
                    var newToken = await RefreshToken(link.RefreshToken);
                    if (newToken != null) {
                        link.Token = newToken.access_token;
                        link.RefreshToken = newToken.refresh_token;
                    } else {
                        var player = _context.Players.FirstOrDefault(p => p.Id == link.Id);
                        if (player == null) {
                            long intId = Int64.Parse(link.Id);
                            if (intId < 70000000000000000) {
                                AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

                                if (accountLink != null) {
                                    string playerId = accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID;

                                    player = _context.Players.FirstOrDefault(p => p.Id == playerId);
                                }
                            }
                        }
                        _context.PatreonLinks.Remove(link);

                        if (player != null) {
                            UpdatePatreonRole(player, null);
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [NonAction]
        public Task<dynamic?> GetPatreonUser(string token)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.patreon.com/api/oauth2/v2/identity?include=memberships.currently_entitled_tiers&fields%5Btier%5D=title");
            request.Method = "GET";
            request.Headers.Add("Authorization", "Bearer " + token);
            request.Proxy = null;

            return request.DynamicResponse();
        }

        [NonAction]
        public Task<dynamic?> RefreshToken(string token)
        {
            string id = _configuration.GetValue<string>("PatreonId");
            string secret = _configuration.GetValue<string>("PatreonSecret");

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                "https://www.patreon.com/api/oauth2/token?grant_type=refresh_token&refresh_token=" + token + 
                "&client_id=" + id +
                "&client_secret =" + secret);
            request.Method = "POST";
            request.Proxy = null;

            return request.DynamicResponse();
        }
    }
}
