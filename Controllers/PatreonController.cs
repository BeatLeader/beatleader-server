using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Swashbuckle.AspNetCore.Annotations;
using System.Dynamic;
using System.Security.Cryptography;
using System.Text;

namespace BeatLeader_Server.Controllers {
    public class PatreonController : Controller {
        private readonly AppContext _context;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;

        public PatreonController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration) {
            _context = context;
            _environment = env;
            _configuration = configuration;
        }

        [HttpGet("~/refreshmypatreon")]
        [Authorize]
        [SwaggerOperation(Summary = "Refresh Patreon Link", Description = "Refreshes the Patreon link for the current user, updating their Patreon tier and roles based on the latest information.")]
        [SwaggerResponse(200, "Patreon link refreshed successfully")]
        [SwaggerResponse(400, "Bad request, no existing Patreon link")]
        [SwaggerResponse(401, "Unauthorized, user not found or not logged in")]
        public async Task<ActionResult> RefreshMyPatreon() {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = currentID == null ? null : await _context.Players.FindAsync(currentID);

            if (currentPlayer == null) {
                return Unauthorized();
            }

            var link = await _context.PatreonLinks.Where(p => p.Id == currentPlayer.Id).FirstOrDefaultAsync();

            if (link == null) {
                return BadRequest("No existing Patreon link");
            }

            await PatreonControllerHelper.UpdateRolesFromLink(link, _configuration, _context);

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/user/linkPatreon")]
        public async Task<ActionResult> LinkPatreon([FromQuery] string returnUrl) {
            string? playerId = HttpContext.CurrentUserID(_context);
            if (playerId == null) {
                return Redirect(returnUrl);
            }
            var auth = await HttpContext.AuthenticateAsync("Patreon");
            string? token = auth?.Properties?.Items[".Token.access_token"];
            string? refreshToken = auth?.Properties?.Items[".Token.refresh_token"];
            string? timestamp = auth?.Properties?.Items[".Token.expires_at"];

            if (token != null && refreshToken != null && timestamp != null) {
                var user = await PatreonControllerHelper.GetPatreonUser(token);

                if (user != null && ExpandantoObject.HasProperty(user, "included")) {
                    string id = user.data.id;

                    var existingPatreonLink = await _context.PatreonLinks.FirstOrDefaultAsync(pl => pl.PatreonId == id);

                    if (existingPatreonLink != null) {
                        var player = _context.Players.FirstOrDefault(p => p.Id == existingPatreonLink.Id);
                        if (player != null) {
                            return Redirect(returnUrl);
                        } else {
                            _context.PatreonLinks.Remove(existingPatreonLink);
                            _context.SaveChanges();
                        }
                    }

                    string? tier = PatreonControllerHelper.GetUserTier(user);

                    if (tier != null) {
                        var patreonLink = new PatreonLink {
                            PatreonId = id,
                            Id = playerId,
                            Token = token,
                            Tier = tier,
                            RefreshToken = refreshToken,
                            Timestamp = timestamp
                        };
                        _context.PatreonLinks.Add(patreonLink);
                        if (tier.Contains("tipper")) {
                            await PatreonControllerHelper.AddPatreonRole(_context, playerId, "tipper", 1);
                        } else if (tier.Contains("supporter")) {
                            await PatreonControllerHelper.AddPatreonRole(_context, playerId, "supporter", 2);
                        } else if (tier.Contains("sponsor")) {
                            await PatreonControllerHelper.AddPatreonRole(_context, playerId, "sponsor", 3);
                        }
                    }
                }
            }

            return Redirect(returnUrl);
        }

        private bool IsValidSignature(string payload, string receivedSignature, string webhookSecret) {
            using (var hmac = new HMACMD5(Encoding.UTF8.GetBytes(webhookSecret))) {
                var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var computedSignature = BitConverter.ToString(computedHash).Replace("-", "").ToLower();
                return computedSignature.Equals(receivedSignature);
            }
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/patreon/webhook")]
        public async Task<IActionResult> HandleWebhook() {
            using (var reader = new StreamReader(Request.Body)) {
                var payload = await reader.ReadToEndAsync();
                var receivedSignature = Request.Headers["X-Patreon-Signature"].FirstOrDefault();
                var webhookSecret = _configuration.GetValue<string>("PatreonHookSecret");

                if (receivedSignature == null || webhookSecret == null || !IsValidSignature(payload, receivedSignature, webhookSecret)) {
                    return Unauthorized("Invalid signature.");
                }

                dynamic? json = JsonConvert.DeserializeObject<ExpandoObject>(payload, new ExpandoObjectConverter());
                string? eventType = Request.Headers["X-Patreon-Event"];
                if (json != null) {
                    string userId = json.data.relationships.patron.data.id;

                    var link = await _context.PatreonLinks.FirstOrDefaultAsync(pl => pl.PatreonId == userId);
                    if (link != null) {
                        await PatreonControllerHelper.UpdateRolesFromLink(link, _configuration, _context);
                    }
                }
            }

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpDelete("~/patreon/link")]
        public async Task<ActionResult> DeletePatreonLink([FromQuery] string id) {
            if (!(await HttpContext.ItsAdmin(_context))) {
                return Unauthorized();
            }

            var link = await _context.PatreonLinks.Where(pl => pl.Id == id).FirstOrDefaultAsync();
            if (link != null) {
                _context.PatreonLinks.Remove(link);
                _context.SaveChanges();
            }

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/refreshpatreon")]
        public async Task<ActionResult> RefreshPatreon([FromQuery] string? id = null) {
            if (!(await HttpContext.ItsAdmin(_context))) {
                return Unauthorized();
            }

            var links = await _context.PatreonLinks.Where(pl => id == null || pl.Id == id).ToListAsync();

            foreach (var link in links) {
                var user = await PatreonControllerHelper.GetPatreonUser(link.Token);

                if (user != null) {
                    string? tier = PatreonControllerHelper.GetUserTier(user);

                    if (tier != link.Tier) {
                        var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == link.Id);
                        if (player == null) {
                            long intId = Int64.Parse(link.Id);
                            if (intId < 70000000000000000) {
                                AccountLink? accountLink = await _context.AccountLinks.FirstOrDefaultAsync(el => el.OculusID == intId);

                                if (accountLink != null) {
                                    string playerId = accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID;

                                    player = await _context.Players.FirstOrDefaultAsync(p => p.Id == playerId);
                                }
                            }
                        }
                        if (player != null) {
                            if (tier != null) {
                                if (tier.Contains("tipper")) {
                                    PatreonControllerHelper.UpdatePatreonRole(player, "tipper");
                                } else if (tier.Contains("supporter")) {
                                    PatreonControllerHelper.UpdatePatreonRole(player, "supporter");
                                } else if (tier.Contains("sponsor")) {
                                    PatreonControllerHelper.UpdatePatreonRole(player, "sponsor");
                                } else {
                                    PatreonControllerHelper.UpdatePatreonRole(player, null);
                                }
                                link.Tier = tier;
                            } else {
                                PatreonControllerHelper.UpdatePatreonRole(player, null);
                                link.Tier = "";
                            }
                        }
                    }
                } else {
                    var newToken = await PatreonControllerHelper.RefreshToken(link.RefreshToken, _configuration);
                    if (newToken != null) {
                        link.Token = newToken.access_token;
                        link.RefreshToken = newToken.refresh_token;
                    } else {
                        var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == link.Id);
                        if (player == null) {
                            long intId = Int64.Parse(link.Id);
                            if (intId < 70000000000000000) {
                                AccountLink? accountLink = await _context.AccountLinks.FirstOrDefaultAsync(el => el.OculusID == intId);

                                if (accountLink != null) {
                                    string playerId = accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID;

                                    player = await _context.Players.FirstOrDefaultAsync(p => p.Id == playerId);
                                }
                            }
                        }
                        _context.PatreonLinks.Remove(link);

                        if (player != null) {
                            PatreonControllerHelper.UpdatePatreonRole(player, null);
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }
    }
}
