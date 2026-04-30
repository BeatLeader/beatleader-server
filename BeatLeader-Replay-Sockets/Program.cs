using BeatLeader_Replay_Sockets;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"../keys/"))
    .SetApplicationName("/home/site/wwwroot/");
var authBuilder = builder.Services.AddAuthentication (options => {
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})

.AddCookie (options => {
    options.CookieManager = new CustomCookieManager();
    options.Events.OnRedirectToAccessDenied =
    options.Events.OnRedirectToLogin = c => {
        c.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.FromResult<object> (null);
    };
    options.Events.OnSigningIn = async context =>
    {
        var claims = new List<Claim>
        {
            new Claim(CustomAuthClaims.Issued, Time.UnixNow().ToString())
        };
        var appIdentity = new ClaimsIdentity(claims);
        context.Principal?.AddIdentity(appIdentity);

        await Task.CompletedTask;
    };
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = false;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Cookie.MaxAge = options.ExpireTimeSpan;
    options.SlidingExpiration = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();
app.UseCookiePolicy(new CookiePolicyOptions {
    MinimumSameSitePolicy = SameSiteMode.None,
    Secure = CookieSecurePolicy.Always
});

app.UseForwardedHeaders();

app.UseCors();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapControllers();

app.Run();
