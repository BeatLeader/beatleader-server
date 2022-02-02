using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })

            .AddCookie(options =>
            {
                options.Events.OnRedirectToAccessDenied =
               options.Events.OnRedirectToLogin = c =>
               {
                   c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                   return Task.FromResult<object>(null);
               };
            })

            .AddSteam(options =>
            {
                options.ApplicationKey = "B0A7AF33E804D0ABBDE43BA9DD5DAB48";
                options.Events.OnAuthenticated = ctx =>
                {
                    /* ... */
                    return Task.CompletedTask;
                };
            });
            

            var connection = // Insert connection string to your database
            services.AddDbContext<AppContext>(options => options.UseSqlServer(connection));

            services.AddControllersWithViews();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
            });
        }
    }
}
