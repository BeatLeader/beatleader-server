using System.Security.Claims;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BeatLeader_Server.Controllers {

    public class AuthenticationController : Controller {
        CurrentUserController _currentUserController;
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictAuthorizationManager _authorizationManager;
        private readonly IOpenIddictScopeManager _scopeManager;
        private readonly IConfiguration _configuration;
        AppContext _context;

        string _websiteUrl;

        public AuthenticationController(
            AppContext context,
            CurrentUserController currentUserController,
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictScopeManager scopeManager,
            IConfiguration configuration,
            IWebHostEnvironment environment) {
            _context = context;
            _currentUserController = currentUserController;
            _applicationManager = applicationManager;
            _authorizationManager = authorizationManager;
            _configuration = configuration;
            _scopeManager = scopeManager;

            _websiteUrl = (environment.IsDevelopment() ? "http://localhost:8888" : "https://beatleader.xyz") + "/signin/oauth2";
        }

        [HttpGet("~/signin")]
        public async Task<IActionResult> SignIn() => View("SignIn", await HttpContext.GetExternalProvidersAsync());

        [HttpPost("~/signin")]
        public async Task<IActionResult> SignIn(
            [FromForm] string provider,
            [FromForm] string returnUrl,
            [FromForm] string? oauthState = null) {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider)) {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider)) {
                return BadRequest();
            }

            string redirectUrl = returnUrl;

            if (provider == "Steam") {
                redirectUrl = Url.Action("SteamLoginCallback",
                    new {
                        ReturnUrl = returnUrl ?? "/",
                        oauthState
                    });
            } else if (provider == "Patreon") {
                redirectUrl = Url.Action("LinkPatreon", "Patreon", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "BeatSaver") {
                redirectUrl = Url.Action("LinkBeatSaver", "BeatSaver",
                    new {
                        returnUrl = returnUrl ?? "/",
                        oauthState
                    });
            } else if (provider == "Twitch") {
                redirectUrl = Url.Action("LinkTwitch", "Socials", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "Twitter") {
                redirectUrl = Url.Action("LinkTwitter", "Socials", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "Discord") {
                redirectUrl = Url.Action("LinkDiscord", "Socials", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "Google") {
                redirectUrl = Url.Action("LinkGoogle", "Socials", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "BeatLeader") {
                redirectUrl = Url.Action("LinkBeatLeader", "Socials", new { returnUrl = returnUrl ?? "/" });
            }

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        [HttpPost("~/signin/approve")]
        public async Task<IActionResult> SignInApprove([FromForm] string returnUrl, [FromQuery] string leaderboardId) {
            string redirectUrl = returnUrl;

            redirectUrl = Url.Action("LinkBeatSaverAndApprove", "BeatSaver", new { returnUrl = returnUrl, leaderboardId = leaderboardId });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, "BeatSaver");
        }

        [Authorize]
        [HttpPost("~/signinmigrate")]
        public async Task<IActionResult> SignInMigrate([FromForm] string provider, [FromForm] string returnUrl) {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider)) {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider)) {
                return BadRequest();
            }
            string? iPAddress = Request.HttpContext.GetIpAddress();
            if (iPAddress == null) {
                return BadRequest("IP address should not be null");
            }
            int random = new Random().Next(200);
            var linkRequest = new AccountLinkRequest {
                IP = iPAddress,
                OculusID = HttpContext.CurrentUserID(),
                Random = random,
            };
            _context.AccountLinkRequests.Add(linkRequest);
            await _context.SaveChangesAsync();

            var redirectUrl = Url.Action("SteamLoginCallback", new { ReturnUrl = returnUrl, Random = random, migrateTo = HttpContext.CurrentUserID() });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        [HttpGet("~/steamcallback")]
        public async Task<IActionResult> SteamLoginCallback(
            [FromQuery] string ReturnUrl,
            [FromQuery] int Random = 0,
            [FromQuery] string? migrateTo = null,
            [FromQuery] string? oauthState = null) {
            string? userId = HttpContext.CurrentUserID();
            if (userId != null) {
                await PlayerControllerHelper.GetLazy(_context, _configuration, userId);

                string? iPAddress = Request.HttpContext.GetIpAddress();
                if (iPAddress != null && migrateTo != null) {
                    string ip = iPAddress;
                    AccountLinkRequest? request = await _context.AccountLinkRequests.FirstOrDefaultAsync(a => a.IP == ip && a.Random == Random && a.OculusID == migrateTo);

                    if (request != null) {
                        _context.AccountLinkRequests.Remove(request);
                        await _context.SaveChangesAsync();
                        await _currentUserController.MigratePrivate(userId, request.OculusID.ToString());
                    }
                }
            }
            if (oauthState != null) {
                return Redirect($"/oauth2/authorize{oauthState}");
            }

            return Redirect(ReturnUrl);
        }

        [HttpPost("~/signinmigrate/oculuspc")]
        public async Task<IActionResult> SignInMigrateOculus(
            [FromForm] string provider,
            [FromForm] string returnUrl,
            [FromForm] string token) {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider)) {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider)) {
                return BadRequest();
            }

            var redirectUrl = Url.Action("MigrateOculusPC", "CurrentUser", new { ReturnUrl = returnUrl, Token = token });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        [HttpPost("~/signinoculus")]
        public IActionResult SignInOculus([FromForm] string? oauthState) {

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            var result = Challenge(new AuthenticationProperties { RedirectUri = oauthState != null ? $"/oauth2/authorize{oauthState}" : "/" }, "oculus");

            return result;
        }

        [HttpPost("~/signinoculus/oculuspc")]
        public IActionResult SignInOculusMigrateOculus(
            [FromForm] string token,
            [FromForm] string returnUrl) {

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            var result = Challenge(new AuthenticationProperties { RedirectUri = Url.Action("MigrateOculusPC", "CurrentUser", new { ReturnUrl = returnUrl, Token = token }) }, "oculus");

            return result;
        }

        public class OauthClientInfo {
            public string Name { get; set; }
            public string? Icon { get; set; }

        }

        [HttpGet("~/oauthclient/info")]
        public async Task<ActionResult<OauthClientInfo>> GetOauthClientInfo([FromQuery] string? clientId) {
            if (clientId == null || clientId.Length == 0) {
                return BadRequest("Please provide clientId");
            }

            var application = await _applicationManager.FindByClientIdAsync(clientId);

            if (application == null) {
                return NotFound();
            }

            return new OauthClientInfo {
                Name = await _applicationManager.GetDisplayNameAsync(application),
                Icon = (await _applicationManager.GetPropertiesAsync(application))["PictureUrl"].GetString()
            };
        }

        [HttpGet("~/oauthclient/antiforgery")]
        public async Task<ActionResult<string>> GetAntiforgeryToken() {
            var antiforgery = HttpContext.RequestServices.GetService<IAntiforgery>();
            var tokenSet = antiforgery.GetAndStoreTokens(HttpContext);
            return tokenSet.RequestToken;
        }

        [HttpGet("~/oauth2/authorize"), IgnoreAntiforgeryToken]
        public async Task<IActionResult> Authorize() {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // Try to retrieve the user principal stored in the authentication cookie and redirect
            // the user agent to the login page (or to an external provider) in the following cases:
            //
            //  - If the user principal can't be extracted or the cookie is too old.
            //  - If prompt=login was specified by the client application.
            //  - If a max_age parameter was provided and the authentication cookie is not considered "fresh" enough.
            var result = await HttpContext.AuthenticateAsync("Cookies");
            if (result == null || !result.Succeeded || request.HasPrompt(Prompts.Login) ||
               (request.MaxAge != null && result.Properties?.IssuedUtc != null &&
                DateTimeOffset.UtcNow - result.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value))) {
                // If the client application requested promptless authentication,
                // return an error indicating that the user is not logged in.
                if (request.HasPrompt(Prompts.None)) {
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string> {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
                        }));
                }

                return Redirect(_websiteUrl + Request.QueryString);
            }

            string? userId = HttpContext.CurrentUserID(_context);
            if (userId == null) {
                return Unauthorized();
            }

            // Retrieve the application details from the database.
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
                throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

            // Retrieve the permanent authorizations associated with the user and the calling client application.
            var authorizations = await _authorizationManager.FindAsync(
                subject: userId,
                client: await _applicationManager.GetIdAsync(application),
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes()).ToListAsync();

            var consentType = await _applicationManager.GetConsentTypeAsync(application);

            switch (consentType) {
                // If the consent is external (e.g when authorizations are granted by a sysadmin),
                // immediately return an error if no authorization can be found in the database.
                case ConsentTypes.External when !authorizations.Any():
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string> {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                                "The logged in user is not allowed to access this client application."
                        }));

                // If the consent is implicit or if an authorization was found,
                // return an authorization response without displaying the consent form.
                case ConsentTypes.Implicit:
                case ConsentTypes.External when authorizations.Any():
                case ConsentTypes.Explicit when authorizations.Any() && !request.HasPrompt(Prompts.Consent):
                    // Create the claims-based identity that will be used by OpenIddict to generate tokens.
                    var identity = new ClaimsIdentity(
                        authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                        nameType: Claims.Name,
                        roleType: Claims.Role);

                    userId = await _context.PlayerIdToMain(userId);
                    var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == userId);

                    // Add the claims that will be persisted in the tokens.
                    identity.SetClaim(Claims.Subject, userId)
                            .SetClaim(Claims.Name, player.Name);

                    // Note: in this sample, the granted scopes match the requested scope
                    // but you may want to allow the user to uncheck specific scopes.
                    // For that, simply restrict the list of scopes before calling SetScopes.
                    identity.SetScopes(request.GetScopes());
                    identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

                    // Automatically create a permanent authorization to avoid requiring explicit consent
                    // for future authorization or token requests containing the same scopes.
                    var authorization = authorizations.LastOrDefault();
                    authorization ??= await _authorizationManager.CreateAsync(
                        identity: identity,
                        subject: userId,
                        client: await _applicationManager.GetIdAsync(application),
                        type: AuthorizationTypes.Permanent,
                        scopes: identity.GetScopes());

                    identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
                    identity.SetDestinations(GetDestinations);

                    return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                // At this point, no authorization was found in the database and an error must be returned
                // if the client application specified prompt=none in the authorization request.
                case ConsentTypes.Explicit when request.HasPrompt(Prompts.None):
                case ConsentTypes.Systematic when request.HasPrompt(Prompts.None):
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string> {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                                "Interactive user consent is required."
                        }));

                // In every other case, render the consent form.
                default: return Redirect(_websiteUrl + Request.QueryString);
            }
        }

        [Authorize]
        [HttpPost("~/oauth2/authorize"), IgnoreAntiforgeryToken]
        public async Task<IActionResult> Accept() {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            string? userId = HttpContext.CurrentUserID(_context);
            if (userId == null) {
                return Unauthorized();
            }

            // Retrieve the application details from the database.
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId) ??
                throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

            // Retrieve the permanent authorizations associated with the user and the calling client application.
            var authorizations = await _authorizationManager.FindAsync(
                subject: userId,
                client: await _applicationManager.GetIdAsync(application),
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes()).ToListAsync();

            // Note: the same check is already made in the other action but is repeated
            // here to ensure a malicious user can't abuse this POST-only endpoint and
            // force it to return a valid response without the external authorization.
            if (!authorizations.Any() && await _applicationManager.HasConsentTypeAsync(application, ConsentTypes.External)) {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The logged in user is not allowed to access this client application."
                    }));
            }

            // Create the claims-based identity that will be used by OpenIddict to generate tokens.
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            userId = await _context.PlayerIdToMain(userId);
            var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == userId);

            // Add the claims that will be persisted in the tokens.
            identity.SetClaim(Claims.Subject, userId)
                    .SetClaim(Claims.Name, player.Name);

            // Note: in this sample, the granted scopes match the requested scope
            // but you may want to allow the user to uncheck specific scopes.
            // For that, simply restrict the list of scopes before calling SetScopes.
            identity.SetScopes(request.GetScopes());
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

            // Automatically create a permanent authorization to avoid requiring explicit consent
            // for future authorization or token requests containing the same scopes.
            var authorization = authorizations.LastOrDefault();
            authorization ??= await _authorizationManager.CreateAsync(
                identity: identity,
                subject: userId,
                client: await _applicationManager.GetIdAsync(application),
                type: AuthorizationTypes.Permanent,
                scopes: identity.GetScopes());

            identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
            identity.SetDestinations(GetDestinations);

            // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
        [HttpGet("~/oauth2/identity")]
        public async Task<IActionResult> Userinfo() {
            var claimsPrincipal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

            return Ok(new {
                Id = claimsPrincipal.GetClaim(OpenIddictConstants.Claims.Subject),
                Name = claimsPrincipal.GetClaim(OpenIddictConstants.Claims.Name),
            });
        }

        [HttpGet("~/signout"), HttpPost("~/signout")]
        public async Task<IActionResult> SignOutCurrentUser() {
            string? userId = HttpContext.CurrentUserID(_context);
            if (userId != null) {
                var clientIds = new List<string>();

                var apps = _applicationManager.ListAsync();

                await foreach (var item in apps) {
                    var clientId = await _applicationManager.GetIdAsync(item);
                    if (clientId != null) {
                        clientIds.Add(clientId);
                    }
                }

                foreach (var clientId in clientIds) {
                    // Retrieve the authorizations associated with the user.
                    var authorizations = await _authorizationManager.FindAsync(
                        subject: userId,
                        client: clientId,
                        status: Statuses.Valid)
                        .ToListAsync();

                    // Revoke each valid authorization associated with the user.
                    foreach (var authorization in authorizations) {
                        await _authorizationManager.TryRevokeAsync(authorization);
                    }
                }
            }

            // Instruct the cookies middleware to delete the local cookie created
            // when the user agent is redirected from the external identity provider
            // after a successful authentication flow (e.g Google or Facebook).
            return SignOut(new AuthenticationProperties { RedirectUri = "/" },
                CookieAuthenticationDefaults.AuthenticationScheme, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        private static IEnumerable<string> GetDestinations(Claim claim) {
            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.

            switch (claim.Type) {
                case Claims.Name:
                    yield return Destinations.AccessToken;

                    if (claim.Subject.HasScope(Scopes.Profile))
                        yield return Destinations.IdentityToken;

                    yield break;

                case Claims.Email:
                    yield return Destinations.AccessToken;

                    if (claim.Subject.HasScope(Scopes.Email))
                        yield return Destinations.IdentityToken;

                    yield break;

                case Claims.Role:
                    yield return Destinations.AccessToken;

                    if (claim.Subject.HasScope(Scopes.Roles))
                        yield return Destinations.IdentityToken;

                    yield break;

                // Never include the security stamp in the access and identity tokens, as it's a secret value.
                case "AspNet.Identity.SecurityStamp": yield break;

                default:
                    yield return Destinations.AccessToken;
                    yield break;
            }
        }
    }
}


