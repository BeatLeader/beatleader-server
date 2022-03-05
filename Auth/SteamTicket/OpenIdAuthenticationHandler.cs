/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AspNet.Security.OpenId.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AspNet.Security.SteamTicket;

public partial class SteamTicketAuthenticationHandler<TOptions> : AuthenticationHandler<TOptions>
    where TOptions : SteamTicketAuthenticationOptions, new()
{
    public SteamTicketAuthenticationHandler(
        IOptionsMonitor<TOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Query["ticket"].Count == 0) {
            Response.StatusCode = 401;
            var task2 = ((Func<Task<AuthenticateResult>>)(async () =>{
                await Task.Delay(0);
                return AuthenticateResult.NoResult();
            }))();
            return task2;
        } else {
            return MakeAsyncRequest("https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v0001?appid=" + Options.ApplicationID + "&key=" + Options.Key + "&ticket=" + Request.Query["ticket"].First(), "");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        return HandleAuthenticateOnceAsync();
    }

    public Task<AuthenticateResult> MakeAsyncRequest(string url, string contentType)
    {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

        WebResponse response = null;
        AuthenticateResult authenticateResult = null;
        var stream = 
        Task<(WebResponse, AuthenticateResult)>.Factory.FromAsync(request.BeginGetResponse, result =>
        {
            try
            {
                response = request.EndGetResponse(result);
            }
            catch (Exception e)
            {
                authenticateResult = AuthenticateResult.Fail(e);
            }
            
            return (response, authenticateResult);
        }, request);

            return stream.ContinueWith(t => ReadStreamFromResponse(t.Result));
        }

        private AuthenticateResult ReadStreamFromResponse((WebResponse, AuthenticateResult) response)
        {
            if (response.Item1 != null) {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (!string.IsNullOrEmpty(results))
                    {
                    
                    }
                    var info = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(results);

                    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, info["response"]["params"]["steamid"]) };
                    var identity = new ClaimsIdentity(claims, "Test");
                    var principal = new ClaimsPrincipal(identity);
                    var ticket = new AuthenticationTicket(principal, "Cookies");

                    AuthenticationHttpContextExtensions.SignInAsync(Context, CookieAuthenticationDefaults.AuthenticationScheme, principal);

                    var result = AuthenticateResult.Success(ticket);
                    return result;
                }
            } else {
                Response.StatusCode = 401;
                return response.Item2;
            }
            
        }
}
