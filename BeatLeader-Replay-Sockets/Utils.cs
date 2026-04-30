using Microsoft.AspNetCore.Authentication.Cookies;

namespace BeatLeader_Replay_Sockets {
    public static class Time
    {
        public static int UnixNow() {
            return (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }

    public class CustomAuthClaims {
        public static string Issued = "Issued";
    }

    public class CustomCookieManager : ICookieManager
    {
        public string GetRequestCookie(HttpContext context, string key)
        {
            return context.Request.Cookies[key];
        }

        public void AppendResponseCookie(HttpContext context, string key, string value, CookieOptions options)
        {
            if (options.Domain == null)
            {
                options.Domain = context.Request.Host.Value.Replace("api", "");
            }
            context.Response.Cookies.Append(key, value, options);
        }

        public void DeleteCookie(HttpContext context, string key, CookieOptions options)
        {
            if (options.Domain == null)
            {
                options.Domain = context.Request.Host.Value.Replace("api", "");
            }
            context.Response.Cookies.Delete(key, options);
        }
    }
}
