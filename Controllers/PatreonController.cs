using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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

        public PatreonController(
            AppContext context,
            IWebHostEnvironment env)
        {
            _context = context;
            _environment = env;
        }

        [NonAction]
        public async Task<ActionResult> AddPatreonRole(string role)
        {
            await RemovePatreonRoles();
            string playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null)
            {
                return NotFound();
            }
            Player? currentPlayer = _context.Players.Find(playerId);
            if (currentPlayer != null)
            {
                currentPlayer.Role += "," + role;
                _context.SaveChanges();
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
            Player? currentPlayer = _context.Players.Find(playerId);
            if (currentPlayer != null)
            {
                currentPlayer.Role = string.Join(",", currentPlayer.Role.Split(",").Where(r => r != "tipper" && r != "supporter" && r != "sponsor"));
                _context.SaveChanges();
            }
            return Ok();
        }


        [HttpGet("~/user/linkPatreon")]
        public async Task<ActionResult> LinkPatreon([FromQuery] string returnUrl)
        {
            string playerId = HttpContext.CurrentUserID(_context);
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

                    var existingPatreonLink = await _context.PatreonLinks.FirstOrDefaultAsync(pl => pl.PatreonId == id);
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
                            await AddPatreonRole("tipper");
                        }
                        else if (tier.Contains("supporter"))
                        {
                            await AddPatreonRole("supporter");
                        }
                        else if (tier.Contains("sponsor"))
                        {
                            await AddPatreonRole("sponsor");
                        }
                    }
                }
            }

            return Redirect(returnUrl);
        }

        [HttpPatch("~/user/patreon")]
        public async Task<ActionResult> PatchPatreonFeatures([FromQuery] string? message = null, [FromQuery] string? leftSaberColor = null, [FromQuery] string? rightSaberColor = null, [FromQuery] string? id = null)
        {
            string playerId = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.Where(p => p.Id == playerId).Include(p => p.PatreonFeatures).FirstOrDefaultAsync();

            if (id != null && player != null && player.Role.Contains("admin"))
            {
                player = await _context.Players.FindAsync(id);
            }

            if (player == null)
            {
                return NotFound();
            }

            if (player.Role.Split(",").Where(r => r == "tipper" || r == "supporter" || r == "sponsor").Count() == 0) {
                return Unauthorized("Not a supporter");
            }

            PatreonFeatures? features = player.PatreonFeatures;
            if (features == null)
            {
                features = new PatreonFeatures();
                player.PatreonFeatures = features;
            }

            if (message != null && player.Role.Contains("sponsor"))
            {
                if (message.Length < 3 || message.Length > 150)
                {
                    return BadRequest("Use message between the 3 and 150 symbols");
                }

                features.Message = message;
            }

            if (leftSaberColor != null)
            {
                int colorLength = leftSaberColor.Length;
                try
                {
                    if (!((colorLength == 7 || colorLength == 9) && Int64.Parse(leftSaberColor.Substring(1), System.Globalization.NumberStyles.HexNumber) != 0))
                    {
                        return BadRequest("leftSaberColor is not valid");
                    }
                }
                catch
                {
                    return BadRequest("leftSaberColor is not valid");
                }

                features.LeftSaberColor = leftSaberColor;
            }

            if (rightSaberColor != null)
            {
                int colorLength = rightSaberColor.Length;
                try
                {
                    if (!((colorLength == 7 || colorLength == 9) && Int64.Parse(rightSaberColor.Substring(1), System.Globalization.NumberStyles.HexNumber) != 0))
                    {
                        return BadRequest("rightSaberColor is not valid");
                    }
                }
                catch
                {
                    return BadRequest("rightSaberColor is not valid");
                }

                features.RightSaberColor = rightSaberColor;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [NonAction]
        public Task<dynamic?> GetPatreonUser(string token)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.patreon.com/api/oauth2/v2/identity?include=memberships.currently_entitled_tiers&fields%5Btier%5D=title");
            request.Method = "GET";
            request.Headers.Add("Authorization", "Bearer " + token);
            request.Proxy = null;

            WebResponse? response = null;
            dynamic? song = null;
            var stream =
            Task<(WebResponse?, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadPlayerFromResponse(t.Result));
        }

        [NonAction]
        private dynamic? ReadPlayerFromResponse((WebResponse?, dynamic?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return null;
                    }

                    return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                }
            }
            else
            {
                return response.Item2;
            }

        }
    }
}
