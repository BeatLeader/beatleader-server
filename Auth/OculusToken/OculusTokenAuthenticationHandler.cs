/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AspNet.Security.OpenId.Events;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AspNet.Security.OculusToken;

public partial class OculusTokenAuthenticationHandler<TOptions> : AuthenticationHandler<TOptions>
    where TOptions : OculusTokenAuthenticationOptions, new()
{

    private readonly IServiceProvider _serviceProvider;
    public OculusTokenAuthenticationHandler(
        IOptionsMonitor<TOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IServiceProvider serviceProvider)
        : base(options, logger, encoder, clock)
    {
        _serviceProvider = serviceProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var task2 = ((Func<Task<AuthenticateResult>>)(async () => {
            string? ticket = Request.Query["ticket"].FirstOrDefault();
            if (ticket == null) {
                ticket = Request.Form["ticket"].FirstOrDefault();
            }

            if (ticket == null) {
                Response.StatusCode = 401;
                await Task.Delay(0);
                return AuthenticateResult.NoResult();
            } else {
                (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(ticket, null);
                if (id == null)
                {
                    Response.StatusCode = 401;
                    await Task.Delay(0);
                    return AuthenticateResult.NoResult();
                }

                var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<BeatLeader_Server.AppContext>();

                var link = dbContext.AccountLinks.Where(l => l.PCOculusID == id).FirstOrDefault();

                    if (link != null && link.SteamID.Length > 0) {
                        id = link.SteamID;
                    }

                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id) };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var authTicket = new AuthenticationTicket(principal, "Cookies");

                await AuthenticationHttpContextExtensions.SignInAsync(Context, CookieAuthenticationDefaults.AuthenticationScheme, principal);

                var result = AuthenticateResult.Success(authTicket);
                return result;
            }
        }))();
        return task2;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        return HandleAuthenticateOnceAsync();
    }
}
