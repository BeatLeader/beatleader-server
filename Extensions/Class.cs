using Azure.Storage.Blobs;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using System.Linq.Expressions;

namespace BeatLeader_Server.Extensions
{
    public static class HttpContextExtensions
    {
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

        public static string? CurrentUserID(this HttpContext context, AppContext dbcontext)
        {
            try
            {
                string? currentID = context.User.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
                if (currentID == null) return null;

                long intId = Int64.Parse(currentID);
                AccountLink? accountLink = dbcontext.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

                return accountLink != null ? accountLink.SteamID : currentID;
            }
            catch (Exception)
            {
                return null;
            }

        }
    }

    public static class LinqExtensions
    {

        public static IOrderedQueryable<TSource> Order<TSource, TKey>(this IQueryable<TSource> source, string by, Expression<Func<TSource, TKey>> keySelector)
        {
            if (by == "desc")
            {
                return source.OrderByDescending(keySelector);
            } else
            {
                return source.OrderBy(keySelector);
            }
        }
        public static void SetPublicContainerPermissions(this BlobContainerClient container)
        {
            try {
                container.SetAccessPolicy(accessType: Azure.Storage.Blobs.Models.PublicAccessType.BlobContainer);
            } catch { }
        }
    }
}
