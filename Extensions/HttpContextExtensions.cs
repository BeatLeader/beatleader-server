using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Extensions
{
    public static class HttpContextExtensions
    {
        public static string? GetIpAddress(this HttpContext context)
        {
            if (!string.IsNullOrEmpty(context.Request.Headers["cf-connecting-ip"]))
                return context.Request.Headers["cf-connecting-ip"];

            var ipAddress = context.GetServerVariable("HTTP_X_FORWARDED_FOR");

            if (!string.IsNullOrEmpty(ipAddress))
            {
                var addresses = ipAddress.Split(',');
                if (addresses.Length != 0)
                    return addresses.Last();
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }

        public static async Task<AuthenticationScheme[]> GetExternalProvidersAsync(this HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();

            return (from scheme in await schemes.GetAllSchemesAsync()
                    where !string.IsNullOrEmpty(scheme.DisplayName)
                    select scheme).ToArray();
        }

        public static async Task<bool> IsProviderSupportedAsync(this HttpContext context, string provider)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return (from scheme in await context.GetExternalProvidersAsync()
                    where string.Equals(scheme.Name, provider, StringComparison.OrdinalIgnoreCase)
                    select scheme).Any();
        }

        public static string? CurrentUserID(this HttpContext context)
        {
            try
            {
                return context.User.Claims.First().Value.Split("/").Last();
            } catch (Exception)
            {
                return null;
            }
            
        }

        public static string? CurrentUserID(this HttpContext? context, AppContext dbcontext)
        {
            try
            {
                string? currentID = context?.User.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
                if (currentID == null) return null;

                long intId = Int64.Parse(currentID);
                if (intId < 70000000000000000) {
                    AccountLink? accountLink = dbcontext.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

                    return accountLink != null ? (accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID) : currentID;
                } else {
                    return currentID;
                }
            }
            catch (Exception)
            {
                return null;
            }

        }

        public static async Task<string?> CurrentOauthUserID(this HttpContext context, AppContext dbcontext, string scope)
        {
            try
            {
                var claimsPrincipal = (await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)).Principal;
                string? currentID = claimsPrincipal.GetClaim(OpenIddictConstants.Claims.Subject);
                if (currentID == null) return null;

                string? claimedScope = claimsPrincipal.GetClaims("oi_scp").FirstOrDefault(c => c == scope);
                if (claimedScope == null) return null;
                long intId = Int64.Parse(currentID);
                if (intId < 70000000000000000) {

                   AccountLink? accountLink = dbcontext.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

                    return accountLink != null ? (accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID) : currentID;
                } else {
                    return currentID;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool ItsAdmin(this HttpContext? context, AppContext dbcontext) {
            if (context != null) {
                string? currentID = context.CurrentUserID(dbcontext);
                if (currentID == null) return false;
                var currentPlayer = dbcontext.Players.Find(currentID);

                return currentPlayer != null && currentPlayer.Role.Contains("admin");
            }
            return true;
        }

        public static bool ShouldShowAllRatings(this HttpContext? context, AppContext dbcontext) {
            string? currentID = context?.CurrentUserID(dbcontext);
            return currentID != null 
                ? dbcontext.Players
                    .Include(p => p.ProfileSettings)
                    .Where(p => p.Id == currentID)
                    .Select(p => p.ProfileSettings)
                    .FirstOrDefault()?
                    .ShowAllRatings ?? false 
                : false;
        }

        public static string PlayerIdToMain(this AppContext _context, string id) {
            Int64 oculusId = 0;
            try {
                oculusId = Int64.Parse(id);
            } catch {
                return id;
            }
            AccountLink? link = null;
            if (oculusId < 1000000000000000) {
                link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
            }
            if (link == null && oculusId < 70000000000000000) {
                link = _context.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
            }
            return (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : id);
        }
    }
}
