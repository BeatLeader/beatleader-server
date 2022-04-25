using System.Security.Claims;
using BeatLeader_Server.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    
    public class AuthenticationController : Controller
    {
        PlayerController _playerController;
        CurrentUserController _currentUserController;

        public AuthenticationController(PlayerController playerController, CurrentUserController currentUserController)
        {
            _playerController = playerController;
            _currentUserController = currentUserController;
        }

        [HttpGet("~/signin")]
        public async Task<IActionResult> SignIn() => View("SignIn", await HttpContext.GetExternalProvidersAsync());

        [HttpPost("~/signin")]
        public async Task<IActionResult> SignIn([FromForm] string provider, [FromForm] string returnUrl)
        {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider))
            {
                return BadRequest();
            }

            var redirectUrl = Url.Action("SteamLoginCallback", new { ReturnUrl = returnUrl });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        [Authorize]
        [HttpPost("~/signinmigrate")]
        public async Task<IActionResult> SignInMigrate([FromForm] string provider, [FromForm] string returnUrl)
        {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider))
            {
                return BadRequest();
            }

            var redirectUrl = Url.Action("SteamLoginCallback", new { ReturnUrl = returnUrl, MigrateProfile = Int64.Parse(HttpContext.CurrentUserID()) });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        private async Task<IActionResult> SteamLoginCallback(string returnUrl, int? migrateProfile = null)
        {
            string userId = HttpContext.CurrentUserID();
            if (userId != null)
            {
                await _playerController.GetLazy(userId);

                if (migrateProfile != null)
                {
                    await _currentUserController.MigratePrivate((int)migrateProfile);
                }
            }

            return Redirect(returnUrl);
        }

        [HttpPost("~/signinoculus")]
        public IActionResult SignInOculus()
        {

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            var result = Challenge(new AuthenticationProperties { RedirectUri = "/" }, "oculus");

            return result;
        }

        [HttpGet("~/signout"), HttpPost("~/signout")]
        public IActionResult SignOutCurrentUser()
        {
            // Instruct the cookies middleware to delete the local cookie created
            // when the user agent is redirected from the external identity provider
            // after a successful authentication flow (e.g Google or Facebook).
            return SignOut(new AuthenticationProperties { RedirectUri = "/" },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}


