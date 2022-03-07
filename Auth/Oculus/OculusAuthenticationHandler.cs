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
using AspNet.Security.OpenId.Events;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
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
        string? action = Request.Query["action"].FirstOrDefault();
        if (action == null || (action != "login" && action != "signup"))
        {
            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Context.Response.WriteAsync("Specify action");
            return AuthenticateResult.Fail("Specify action");
        }
        string? login = Request.Query["login"].FirstOrDefault();
        if (login == null)
        {
            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Context.Response.WriteAsync("Specify login");
            return AuthenticateResult.Fail("Specify login");
        }
        string? password = Request.Query["password"].FirstOrDefault();
        if (password == null)
        {
            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Context.Response.WriteAsync("Specify password");
            return AuthenticateResult.Fail("Specify password");
        }
        var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeatLeader_Server.AppContext>();

        string id = "";

        if (action == "login")
        {
            AuthInfo? authInfo = dbContext.Auths.FirstOrDefault(el => el.Login == login);
            if (authInfo == null || authInfo.Password != password)
            {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("Login or password is not valid");
                return AuthenticateResult.Fail("Login or password is not valid");
            }
            id = authInfo.Id.ToString();
        }
        else
        {
            AuthInfo? authInfo = dbContext.Auths.FirstOrDefault(el => el.Login == login);
            if (authInfo != null)
            {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("User with such login already exists");
                return AuthenticateResult.Fail("User with such login already exists");
            }
            Player? player = dbContext.Players.FirstOrDefault(el => el.Name == login);
            if (player != null)
            {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("User with such login already exists");
                return AuthenticateResult.Fail("User with such login already exists");
            }
            if (login.Trim().Length < 2)
            {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("Use two or more symbols for the login");
                return AuthenticateResult.Fail("Use two or more symbols for the login");
            }
            if (password.Trim().Length < 8)
            {
                Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await Context.Response.WriteAsync("Come on, type at least 8 symbols password");
                return AuthenticateResult.Fail("Come on, type at least 8 symbols password");
            }
            
            authInfo = new AuthInfo
            {
                Password = password,
                Login = login
            };
            dbContext.Auths.Add(authInfo);
            dbContext.SaveChanges();

            id = authInfo.Id.ToString();
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Cookies");

        await AuthenticationHttpContextExtensions.SignInAsync(Context, CookieAuthenticationDefaults.AuthenticationScheme, principal);

        var result = AuthenticateResult.Success(ticket);

        return result;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        return HandleAuthenticateOnceAsync();
    }

    public Task<dynamic?> MakeAsyncRequest(string url, string method)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
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
                    if (!string.IsNullOrEmpty(results))
                    {
                    
                    }
                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    return info;
                }
            } else {
                Response.StatusCode = 401;
                return response.Item2;
            }
            
        }
}
