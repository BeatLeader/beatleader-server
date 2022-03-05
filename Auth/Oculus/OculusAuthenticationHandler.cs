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
        if (Request.Query["token"].Count == 0) {
            return AuthenticateResult.Fail("No token found");
        } else {
            string? token = Request.Query["token"].First();
            if (token == null) return AuthenticateResult.Fail("No token found");
            string? tokenJSON = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            if (tokenJSON == null) return AuthenticateResult.Fail("Token is not valid");
            dynamic? decodedToken = JsonConvert.DeserializeObject<ExpandoObject>(tokenJSON, new ExpandoObjectConverter());
            if (decodedToken == null) return AuthenticateResult.Fail("Token is not valid");

            OculusAuthInfo authInfo = new OculusAuthInfo();
            authInfo.ScoredID = decodedToken.org_scoped_id.ToString();
            authInfo.Code = decodedToken.code.ToString();

            dynamic? auth_token_info = await MakeAsyncRequest("https://graph.oculus.com/sso_authorize_code?code=" + authInfo.Code + "&access_token=&org_scoped_id=" + authInfo.ScoredID, "POST");
            dynamic? request = await MakeAsyncRequest("https://graph.oculus.com/me?access_token=" + auth_token_info.oauth_token + "&fields=id,alias", "GET");

            authInfo.UserID = request.id;
            authInfo.AuthToken = auth_token_info.oauth_token;

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<BeatLeader_Server.AppContext>();

                OculusAuthInfo? oculusAuth = dbContext.OculusAuths.Count() > 0 ? dbContext.OculusAuths.First(el => el.UserID == authInfo.UserID) : null;
                if (oculusAuth == null)
                {
                    dbContext.OculusAuths.Add(authInfo);
                } else
                {
                    oculusAuth.ScoredID = decodedToken.org_scoped_id.ToString();
                    oculusAuth.Code = decodedToken.code.ToString();
                    oculusAuth.AuthToken = auth_token_info.oauth_token;
                    dbContext.OculusAuths.Update(oculusAuth);
                }
                await dbContext.SaveChangesAsync();
            }

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, request.id) };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Cookies");

            await AuthenticationHttpContextExtensions.SignInAsync(Context, CookieAuthenticationDefaults.AuthenticationScheme, principal);

            var result = AuthenticateResult.Success(ticket);

            return result;
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
        var task2 = ((Func<Task<AuthenticateResult>>)(async () => {
            AuthenticateResult result = await HandleAuthenticateOnceAsync();
            //Response.Redirect("https://beatleader.azurewebsites.net/user/id");
            return result;
        }))();

        return task2;
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
