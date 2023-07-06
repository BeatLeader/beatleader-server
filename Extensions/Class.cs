using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Dynamic;
using System.Linq.Expressions;
using System.Net;
using System.ComponentModel;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Utils;
using Newtonsoft.Json.Serialization;

namespace BeatLeader_Server.Extensions
{
    public static class ModelExtensions {

        public static bool WithRating(this DifficultyStatus context) {
            return context == DifficultyStatus.ranked || context == DifficultyStatus.qualified || context == DifficultyStatus.nominated;
        }
    }

    public static class JsonExtensions
    {
        static DefaultContractResolver contractResolver = new DefaultContractResolver {
            NamingStrategy = new CamelCaseNamingStrategy()
        };

        public static string SerializeObject(object? value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings
            {
                ContractResolver = contractResolver,
                Formatting = Formatting.Indented
            });
        }
    }

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

        public static IOrderedQueryable<TSource> Order<TSource, TKey>(this IQueryable<TSource> source, Order by, Expression<Func<TSource, TKey>> keySelector)
        {
            if (by == Enums.Order.Desc)
            {
                return source.OrderByDescending(keySelector);
            } else
            {
                return source.OrderBy(keySelector);
            }
        }
        public static IOrderedQueryable<TSource> ThenOrder<TSource, TKey>(this IOrderedQueryable<TSource> source, Order by, Expression<Func<TSource, TKey>> keySelector)
        {
            if (by == Enums.Order.Desc)
            {
                return source.ThenByDescending(keySelector);
            }
            else
            {
                return source.ThenBy(keySelector);
            }
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

        public static Task<T?> DynamicResponse<T>(this HttpWebRequest request)
        {
            WebResponse? response = null;
            T? song = default;
            var stream =
            Task<(WebResponse?, T?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = default;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadObjectFromResponse(t.Result));
        }

        private static T? ReadObjectFromResponse<T>((WebResponse?, T?) response)
        {
            if (response.Item1 != null)
            {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return default(T);
                    }

                    return JsonConvert.DeserializeObject<T>(results);
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
        public static ExpandoObject? ObjectFromStream(this Stream ms)
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

        public static T? ObjectFromStream<T>(this Stream ms)
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
