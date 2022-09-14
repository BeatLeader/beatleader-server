using Azure.Storage.Blobs;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Dynamic;
using System.Linq.Expressions;
using System.Net;
using System.ComponentModel;

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

        public static string? CurrentUserID(this HttpContext context, ReadAppContext dbcontext)
        {
            try
            {
                string? currentID = context.User.Claims.FirstOrDefault()?.Value.Split("/").LastOrDefault();
                if (currentID == null) return null;

                long intId = Int64.Parse(currentID);
                if (intId < 70000000000000000)
                {
                    AccountLink? accountLink = dbcontext.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

                    return accountLink != null ? (accountLink.SteamID.Length > 0 ? accountLink.SteamID : accountLink.PCOculusID) : currentID;
                }
                else
                {
                    return currentID;
                }
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
        public static IOrderedQueryable<TSource> ThenOrder<TSource, TKey>(this IOrderedQueryable<TSource> source, string by, Expression<Func<TSource, TKey>> keySelector)
        {
            if (by == "desc")
            {
                return source.ThenByDescending(keySelector);
            }
            else
            {
                return source.ThenBy(keySelector);
            }
        }
        public static void SetPublicContainerPermissions(this BlobContainerClient container)
        {
            try {
                container.SetAccessPolicy(accessType: Azure.Storage.Blobs.Models.PublicAccessType.BlobContainer);
            } catch { }
        }
    }

    public static class StringExtensions
    {
        public static string FirstCharToLower(this string input) =>
            input switch
            {
                null => null,
                "" => "",
                _ => string.Concat(input[0].ToString().ToLower(), input.AsSpan(1))
            };
    }

    public static class HttpWebRequestExtensions
    {
        public static Task<dynamic?> DynamicResponse(this HttpWebRequest request)
        {
            WebResponse? response = null;
            dynamic? song = null;
            var stream =
            Task<(WebResponse?, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadObjectFromResponse(t.Result));
        }

        private static dynamic? ReadObjectFromResponse((WebResponse?, dynamic?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return null;
                    }

                    return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                }
            }
            else
            {
                return response.Item2;
            }
        }
    }

    public static class StreamExtentions
        {
        public static ExpandoObject? ObjectFromStream(this MemoryStream ms)
        {
            using (StreamReader reader = new StreamReader(ms))
            {
                string results = reader.ReadToEnd();
                if (string.IsNullOrEmpty(results))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
            }
        }

        public static T? ObjectFromStream<T>(this MemoryStream ms)
        {
            using (StreamReader reader = new StreamReader(ms))
            {
                string results = reader.ReadToEnd();
                if (string.IsNullOrEmpty(results))
                {
                    return default(T);
                }

                return JsonConvert.DeserializeObject<T>(results);
            }
        }
    }

    public static class ObjectToDictionaryHelper
    {
        public static IDictionary<string, object> ToDictionary(this object source)
        {
            return source.ToDictionary<object>();
        }

        public static IDictionary<string, T> ToDictionary<T>(this object source)
        {
            if (source == null)
                ThrowExceptionWhenSourceArgumentIsNull();

            var dictionary = new Dictionary<string, T>();
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(source))
                AddPropertyToDictionary<T>(property, source, dictionary);
            return dictionary;
        }

        private static void AddPropertyToDictionary<T>(PropertyDescriptor property, object source, Dictionary<string, T> dictionary)
        {
            object value = property.GetValue(source);
            if (IsOfType<T>(value))
                dictionary.Add(property.Name, (T)value);
        }

        private static bool IsOfType<T>(object value)
        {
            return value is T;
        }

        private static void ThrowExceptionWhenSourceArgumentIsNull()
        {
            throw new ArgumentNullException("source", "Unable to convert object to a dictionary. The source object is null.");
        }
    }
}
