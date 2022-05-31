/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using System.Dynamic;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AspNet.Security.Oculus;

public partial class OculusAuthenticationHandler<TOptions> : AuthenticationHandler<TOptions>
    where TOptions : OculusAuthenticationOptions, new()
{
    private readonly IServiceProvider _serviceProvider;

    public OculusAuthenticationHandler(
        IOptionsMonitor<TOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IServiceProvider serviceProvider)
        : base(options, logger, encoder, clock)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = Request.Form["token"].FirstOrDefault();
        if (token == null) {
            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Context.Response.WriteAsync("No token found");
            return AuthenticateResult.Fail("No token found");
        } else {
            string? tokenJSON = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            if (tokenJSON == null) {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("Token is not valid");
                return AuthenticateResult.Fail("Token is not valid");
            }
            dynamic? decodedToken = JsonConvert.DeserializeObject<ExpandoObject>(tokenJSON, new ExpandoObjectConverter());
            if (decodedToken == null) {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("Token is not valid");
                return AuthenticateResult.Fail("Token is not valid");
            }

            string scoredID = decodedToken.org_scoped_id.ToString();
            string code = decodedToken.code.ToString();

            dynamic? auth_token_info = await MakeAsyncRequest("https://graph.oculus.com/sso_authorize_code?code=" + code + "&access_token=" + Options.Key + "&org_scoped_id=" + scoredID, "POST");
            if (auth_token_info == null) {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("Something went wrong");
                return AuthenticateResult.Fail("Something went wrong");
            }
            dynamic? request = await MakeAsyncRequest("https://graph.oculus.com/me?fields=id,alias", "GET", auth_token_info.oauth_token);

            string userID = request.id;
            string alias = request.alias;

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<BeatLeader_Server.AppContext>();

                var loginLink = dbContext.OculusLoginLinks.FirstOrDefault(l => l.BLOculusID == userID);
                if (loginLink == null) {
                    var allOculusPlayers = dbContext.Players.Where(p => p.Platform == "oculuspc").ToList();

                    var player = allOculusPlayers.FirstOrDefault(p => p.Name == alias);
                    if (player == null) {
                        foreach (var item in allOculusPlayers) {
                           var updatedPlayer = await PlayerUtils.GetPlayerFromOculus(item.Id, Options.Token);
                            if (updatedPlayer != null) {
                                if (updatedPlayer.Name == alias) {
                                    player = updatedPlayer;
                                    break;
                                }
                            }
                        }
                    }

                    if (player != null) {
                        loginLink = new OculusLoginLink {
                            BLOculusID = userID,
                            BSOculusID = player.Id
                        };
                        dbContext.OculusLoginLinks.Add(loginLink);
                        dbContext.SaveChanges();
                    }
                }

                if (loginLink != null) {
                    userID = loginLink.BSOculusID;
                } else {
                    Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await Context.Response.WriteAsync("Cannot find the player. Please post at least one score from the mod.");
                    return AuthenticateResult.Fail("Cannot find the player. Please post at least one score from the mod.");
                }
            }

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userID) };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Cookies");

            await AuthenticationHttpContextExtensions.SignInAsync(Context, CookieAuthenticationDefaults.AuthenticationScheme, principal);

            var result = AuthenticateResult.Success(ticket);

            return result;
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        return HandleAuthenticateOnceAsync();
    }

    public Task<dynamic?> MakeAsyncRequest(string url, string method, string? token = null)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        if (token != null) {
            request.Headers.Add("Authorization", "Bearer " + token);
        }
        request.Method = method;

        WebResponse response = null;
        dynamic? authenticateResult = null;
        var stream = 
        Task<(WebResponse, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
        {
            try
            {
                response = request.EndGetResponse(result);
            }
            catch (Exception e)
            {
                authenticateResult = null;
            }
            
            return (response, authenticateResult);
        }, request);

            return stream.ContinueWith(t => ReadStreamFromResponse(t.Result));
        }

        private dynamic? ReadStreamFromResponse((WebResponse, dynamic?) response)
        {
            if (response.Item1 != null) {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    return info;
                }
            } else {
                Response.StatusCode = 401;
                return response.Item2;
            }
            
        }
}
