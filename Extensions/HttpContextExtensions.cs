﻿using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BeatLeader_Server.Enums;

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

        public static string? CurrentUserID(this HttpContext? context, AppContext dbcontext, bool ignoreIssued = false)
        {
            try
            {
                string? currentID = context?.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value.Split("/").LastOrDefault();
                if (currentID == null) return null;

                if (!ignoreIssued) {
                    string? issuedTime = context?.User.Claims.FirstOrDefault(c => c.Type == CustomAuthClaims.Issued)?.Value.Split("/").LastOrDefault();
                    if (issuedTime == null) return null;
                }

                long intId = Int64.Parse(currentID);
                if (intId < 70000000000000000) {
                    AccountLink? accountLink = dbcontext.AccountLinks.AsNoTracking().FirstOrDefault(el => el.OculusID == intId);

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

                   AccountLink? accountLink = await dbcontext.AccountLinks.AsNoTracking().FirstOrDefaultAsync(el => el.OculusID == intId);

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

        public static async Task<bool> ItsAdmin(this HttpContext? context, AppContext dbcontext) {
            if (context != null) {
                string? currentID = context.CurrentUserID(dbcontext);
                if (currentID == null) return false;
                var currentPlayer = await dbcontext.Players.FindAsync(currentID);

                return currentPlayer != null && currentPlayer.Role.Contains("admin");
            }
            return true;
        }

        public static bool ShouldShowAllRatings(this HttpContext? context, AppContext dbcontext) {
            string? currentID = context?.CurrentUserID(dbcontext);
            return currentID != null 
                ? dbcontext.Players
                    .AsNoTracking()
                    .Include(p => p.ProfileSettings)
                    .Where(p => p.Id == currentID)
                    .Select(p => p.ProfileSettings)
                    .FirstOrDefault()?
                    .ShowAllRatings ?? false 
                : false;
        }

        public static async Task<string> PlayerIdToMain(this AppContext _context, string id) {
            Int64 oculusId = 0;
            id = id.ToLower();
            if (!Int64.TryParse(id, out oculusId)) {
                var alias = _context.Players.Where(p => p.Alias == id || p.OldAlias == id).Select(p => p.Id).FirstOrDefault();
                if (alias != null) {
                    return alias;
                } else {
                    return id;
                }
            }
            AccountLink? link = null;
            if (oculusId < 1000000000000000) {
                link = await _context.AccountLinks.AsNoTracking().FirstOrDefaultAsync(el => el.OculusID == oculusId);
            }
            if (link == null && oculusId < 70000000000000000) {
                link = await _context.AccountLinks.AsNoTracking().FirstOrDefaultAsync(el => el.PCOculusID == id);
            }
            return (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : id);
        }
    }
}
