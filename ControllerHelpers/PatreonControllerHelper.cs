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
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BeatLeader_Server.ControllerHelpers {
    public class PatreonControllerHelper {
        public static async Task UpdateRolesFromLink(
            PatreonLink link, 
            IConfiguration configuration,
            AppContext dbContext) {
            var user = await GetPatreonUser(link.Token);

            if (user != null) {
                string? tier = GetUserTier(user);
                string userId = await dbContext.PlayerIdToMain(link.Id);

                var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == userId);
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
            } else {
                var newToken = await RefreshToken(link.RefreshToken, configuration);
                if (newToken != null) {
                    link.Token = newToken.access_token;
                    link.RefreshToken = newToken.refresh_token;
                } else {
                    var player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == link.Id);
                    if (player == null) {
                        long intId = Int64.Parse(link.Id);
                        if (intId < 70000000000000000) {
                            AccountLink? accountLink = await dbContext.AccountLinks.FirstOrDefaultAsync(el => el.OculusID == intId);

                            if (accountLink != null) {
                                string playerId = accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID;

                                player = await dbContext.Players.FirstOrDefaultAsync(p => p.Id == playerId);
                            }
                        }
                    }
                    dbContext.PatreonLinks.Remove(link);

                    if (player != null) {
                        UpdatePatreonRole(player, null);
                    }
                }
            }

            await dbContext.SaveChangesAsync();
        }

        public static async Task AddPatreonRole(AppContext dbContext, string playerId, string role, int tier)
        {
            Player? currentPlayer = await dbContext.Players.Include(p => p.ProfileSettings).FirstOrDefaultAsync(p => p.Id == playerId);
            if (currentPlayer == null) return;

            RemovePatreonRoles(currentPlayer);
            
            if (currentPlayer != null)
            {
                currentPlayer.Role += "," + role;
                if (currentPlayer.ProfileSettings == null) {
                    currentPlayer.ProfileSettings = new ProfileSettings();
                }

                currentPlayer.ProfileSettings.EffectName = "TheSun_Tier" + tier;
                await dbContext.SaveChangesAsync();
            }
        }

        public static void RemovePatreonRoles(Player player)
        {
            player.Role = string.Join(",", player.Role.Split(",").Where(r => r != "tipper" && r != "supporter" && r != "sponsor"));
        }

        public static void UpdatePatreonRole(Player player, string? role)
        {
            player.Role = string.Join(",", player.Role.Split(",").Where(r => r != "tipper" && r != "supporter" && r != "sponsor"));
            if (role != null) {
                player.Role += "," + role;
            }
        }

        public static Task<dynamic?> GetPatreonUser(string token)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.patreon.com/api/oauth2/v2/identity?include=memberships.currently_entitled_tiers&fields%5Btier%5D=title");
            request.Method = "GET";
            request.UserAgent = "\r\n\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"";
            request.Headers.Add("Authorization", "Bearer " + token);
            request.Proxy = null;

            return request.DynamicResponse();
        }

        public static string? GetUserTier(dynamic user) {
            string? tier = null;

            try {
            foreach (var item in user.included)
            {
                if (ExpandantoObject.HasProperty(item.attributes, "title"))
                {
                    tier = item.attributes.title.ToLower();
                    if (tier != "free") {
                        break;
                    }
                }
            }
            } catch (Exception e) {
                Console.WriteLine($"EXCEPTION {e}");
            }

            return tier;
        }

        public static Task<dynamic?> RefreshToken(string token, IConfiguration configuration)
        {
            string id = configuration.GetValue<string>("PatreonId");
            string secret = configuration.GetValue<string>("PatreonSecret");

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
