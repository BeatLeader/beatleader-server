using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using BeatLeader_Server.Services;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus.Client.DependencyInjection;
using Prometheus.Client.HttpRequestDurations;
using Prometheus.Client.AspNetCore;
using System.Net;
using BeatLeader_Server.Bot;
using static OpenIddict.Abstractions.OpenIddictConstants;
using BeatLeader_Server.Models;
using Microsoft.OpenApi.Models;
using System.Reflection;
using ReplayDecoder;
using System.Security.Cryptography.X509Certificates;
using Prometheus.Client;
using System.Diagnostics;
using BeatLeader_Server.Utils;
using Swashbuckle.AspNetCore.Annotations;
using Prometheus.Client.Collectors.ProcessStats;

namespace BeatLeader_Server {

    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorLoggingMiddleware> _logger;
        private readonly Process process;

        public ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            process = Process.GetCurrentProcess();
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                var guid = Guid.NewGuid();
                float before = (float)(Process.GetCurrentProcess().WorkingSet64 / 1000l) / 1000000.0f;
                if (context.Request.Path != "/servername") {
                    _logger.LogWarning(null, $"STARTED {guid} {before} GB {context.Request.Path}{context.Request.QueryString}");
                }
                await _next(context);
                if (context.Request.Path != "/servername") {
                    _logger.LogWarning(null, $"FINISHED {guid} {before} {(float)(Process.GetCurrentProcess().WorkingSet64 / 1000l) / 1000000.0f} GB {context.Request.Path}{context.Request.QueryString}");
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(null, "LOL500 " + context.Request.Path + context.Request.QueryString);
                throw;
            }
        }
    }

    public class LocalstatsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LocalstatsMiddleware> _logger;
 
        public LocalstatsMiddleware(RequestDelegate next, ILogger<LocalstatsMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }
 
        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path == "/metrics")
            {
                var remoteIp = context.Connection.RemoteIpAddress;
 
                if (context.Request.Headers.ContainsKey("X-Forwarded-For") || !IPAddress.IsLoopback(remoteIp))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }
            }
 
            await _next(context);
        }
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

    public class ReplayRecalculatorStub : IReplayRecalculator
    {
        public async Task<(int?, Replay)> RecalculateReplay(Replay replay)
        {
            return (0, replay);
        }
    }

    public class Startup {
        static string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
        public Startup (IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Environment = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Environment { get; }

        public void ConfigureServices (IServiceCollection services)
        {
            string steamKey = Configuration.GetValue<string>("SteamKey");
            string steamApi = Configuration.GetValue<string>("SteamApi");

            string beatSaverId = Configuration.GetValue<string>("BeatSaverId");
            string beatSaverSecret = Configuration.GetValue<string>("BeatSaverSecret");

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(@"../keys/"))
                .SetApplicationName("/home/site/wwwroot/");

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });

            var replayRecalculatorType = Assembly.GetExecutingAssembly()
                .GetTypes()
                .FirstOrDefault(t => typeof(IReplayRecalculator).IsAssignableFrom(t) && !t.FullName.Contains("Stub") && !t.IsInterface && !t.IsAbstract);

            if (replayRecalculatorType != null)
            {
                services.AddScoped(typeof(IReplayRecalculator), replayRecalculatorType);
            } else {
                services.AddScoped(typeof(IReplayRecalculator), typeof(ReplayRecalculatorStub));
            }

            var authBuilder = services.AddAuthentication (options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })

            .AddCookie (options => {
                if (!Environment.IsDevelopment()) {
                    options.CookieManager = new CustomCookieManager();
                }
                options.Events.OnRedirectToAccessDenied =
                options.Events.OnRedirectToLogin = c => {
                    c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.FromResult<object> (null);
                };
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.HttpOnly = false;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.Cookie.MaxAge = options.ExpireTimeSpan;
                options.SlidingExpiration = true;
            })
            .AddCookie("BLPatreon")
            .AddCookie("BLTwitch")
            .AddCookie("BLTwitter")
            .AddCookie("BLGoogle")
            .AddCookie("BLDiscord")
            .AddCookie("BLBeatSaver")
            .AddSteamTicket(options =>
            {
                options.Key = steamKey;
                options.ApplicationID = "620980";
                options.ApiUrl = steamApi;
            })
            .AddOculusToken (options =>
            {
            })
            .AddOculus(options => {})
            .AddSteam (options => {
                options.ApplicationKey = steamKey;
                options.UserInformationEndpoint = $"{steamApi}/ISteamUser/GetPlayerSummaries/v0002/";
                options.Events.OnAuthenticated = ctx => {
                    /* ... */
                    return Task.CompletedTask;
                };
            })
            .AddBeatSaver(options => {
                options.SignInScheme = "BLBeatSaver";
                options.SaveTokens = true;
                options.ClientId = beatSaverId;
                options.ClientSecret = beatSaverSecret;
            });

            services.AddMemoryCache();
            services.Configure<IpRateLimitOptions>(options =>
            {
                options.EnableEndpointRateLimiting = true;
                options.StackBlockedRequests = false;
                options.HttpStatusCode = 429;
                options.RealIpHeader = "cf-connecting-ip";
                options.ClientIdHeader = "X-ClientId";
                options.GeneralRules = new List<RateLimitRule>
                    {
                        new RateLimitRule
                        {
                            Endpoint = "GET:/*",
                            Period = "10s",
                            Limit = 50,
                        },
                        new RateLimitRule
                        {
                            Endpoint = "GET:/players",
                            Period = "10s",
                            Limit = 10,
                        },
                        new RateLimitRule
                        {
                            Endpoint = "GET:/user/friendScores",
                            Period = "1s",
                            Limit = 1,
                        }
                    };
            });
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
            services.AddInMemoryRateLimiting();

            if (!Environment.IsDevelopment()) {

                string patreonId = Configuration.GetValue<string>("PatreonId");
                string patreonSecret = Configuration.GetValue<string>("PatreonSecret");
                authBuilder.AddPatreon(options => {
                    options.SignInScheme = "BLPatreon";
                    options.SaveTokens = true;
                    options.ClientId = patreonId;
                    options.ClientSecret = patreonSecret;
                });

                string twitchId = Configuration.GetValue<string>("TwitchId");
                string twitchSecret = Configuration.GetValue<string>("TwitchSecret");
                authBuilder.AddTwitch(options =>
                {
                    options.SaveTokens = true;
                    options.ClientId = twitchId;
                    options.ClientSecret = twitchSecret;
                    options.SignInScheme = "BLTwitch";
                });

                string twitterId = Configuration.GetValue<string>("TwitterId");
                string twitterSecret = Configuration.GetValue<string>("TwitterSecret");
                authBuilder.AddTwitter(options =>
                {
                    options.SaveTokens = true;
                    options.ClientId = twitterId;
                    options.ClientSecret = twitterSecret;
                    options.SignInScheme = "BLTwitter";
                });

                string discordId = Configuration.GetValue<string>("DiscordId");
                string discordSecret = Configuration.GetValue<string>("DiscordSecret");
                authBuilder.AddDiscord(options => 
                {
                    options.SaveTokens = true;
                    options.ClientId = discordId;
                    options.ClientSecret = discordSecret;
                    options.SignInScheme = "BLDiscord";
                });

                string googleId = Configuration.GetValue<string>("GoogleId");
                string googleSecret = Configuration.GetValue<string>("GoogleSecret");
                authBuilder.AddGoogle(options =>
                {
                    options.SaveTokens = true;
                    options.ClientId = googleId;
                    options.ClientSecret = googleSecret;
                    options.SignInScheme = "BLGoogle";
                    options.Scope.Add("https://www.googleapis.com/auth/youtube.readonly");
                });
            } else {
                authBuilder.AddCookie("BLBeatLeader")
                .AddBeatLeader(options => {
                    options.SignInScheme = "BLBeatLeader";
                    options.SaveTokens = true;
                    options.ClientId = OauthService.TestClientId;
                    options.ClientSecret = OauthService.TestClientSecret;
                });
            }

            services.AddServerTiming();
            services.AddMetricFactory();
            services.AddAntiforgery(options =>
            {
                options.Cookie.Name = "X-CSRF-TOKEN";
                options.HeaderName = "X-CSRF-TOKEN";
                options.SuppressXFrameOptionsHeader = false;
            });

            services.AddDbContextFactory<AppContext>(options => { 
                options.UseSqlServer(Configuration.GetValue<string>("DefaultConnection"));

                options.UseOpenIddict();
            });

            services.AddOpenIddict()
            // Register the OpenIddict core components.
            .AddCore(options =>
            {
                // Configure OpenIddict to use the Entity Framework Core stores and models.
                // Note: call ReplaceDefaultEntities() to replace the default entities.
                options.UseEntityFrameworkCore()
                       .UseDbContext<AppContext>();
            })

            // Register the OpenIddict server components.
            .AddServer(options =>
            {
                // Enable the token endpoint.
                options.SetAuthorizationEndpointUris("oauth2/authorize")
                       .SetLogoutEndpointUris("signout")
                       .SetTokenEndpointUris("oauth2/token")
                       .SetUserinfoEndpointUris("oauth2/identity");

                // Enable the client credentials flow.
                options.RegisterScopes(
                    Scopes.Profile,
                    CustomScopes.Clan);

                // Note: the sample uses the code and refresh token flows but you can enable
                // the other flows if you need to support implicit, password or client credentials.
                options.AllowAuthorizationCodeFlow()
                       .AllowRefreshTokenFlow();

                options.SetRefreshTokenReuseLeeway(TimeSpan.FromMinutes(10));

                if (Environment.IsDevelopment()) {
                    // Register the signing and encryption credentials.
                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();
                } else {
                    var signingCertificate = new X509Certificate2("../keys/openiddict_certificate.pfx", Configuration.GetValue<string>("OpeniddictPassword"));
                    options.AddSigningCertificate(signingCertificate);
                    options.AddEncryptionCertificate(signingCertificate);
                }

                // Register the ASP.NET Core host and configure the ASP.NET Core options.
                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableLogoutEndpointPassthrough()
                       .EnableStatusCodePagesIntegration()
                       .EnableUserinfoEndpointPassthrough();
            })

            // Register the OpenIddict validation components.
            .AddValidation(options =>
            {
                // Import the configuration from the local OpenIddict server instance.
                options.UseLocalServer();

                // Register the ASP.NET Core host.
                options.UseAspNetCore();
            });

            if (!Environment.IsDevelopment()) {
                if (Configuration.GetValue<string>("ServicesHost") == "YES")
                {
                    services.AddHostedService<HourlyRefresh>();
                    services.AddHostedService<DailyRefresh>();
                    services.AddHostedService<HistoryService>();
                    services.AddHostedService<BotService>();
                    services.AddHostedService<RankingService>();
                }
            }
            
            services.AddHostedService<ConstantsService>();
            services.AddHostedService<RefreshTaskService>();
            services.AddHostedService<MinuteRefresh>();
            services.AddHostedService<ClanTaskService>();
            services.AddHostedService<SearchService>();
            services.AddHostedService<OauthService>();

            services.AddSingleton<RTNominationsForum>();

            services.AddMvc ().AddControllersAsServices ().AddJsonOptions (options => {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
            services.AddCors (options => {
                options.AddPolicy (name: MyAllowSpecificOrigins,
                    builder => {
                        builder.WithOrigins(Configuration.GetSection("CORS").Get<string[]>())
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                    });
            });
            services.AddHttpClient();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("blapi", new OpenApiInfo { Title = "API.BL", Version = "v1" });
                c.SwaggerDoc("blapifull", new OpenApiInfo { Title = "API.BL.FULL", Version = "v1" });

                c.CustomOperationIds(apiDesc =>
                {
                    var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];
                    var actionName = apiDesc.ActionDescriptor.RouteValues["action"];
                    return $"{controllerName}_{actionName}";
                });
                c.EnableAnnotations();
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(System.AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
                c.IncludeXmlComments(Path.Combine(System.AppContext.BaseDirectory, "Models.xml"));
                c.IncludeXmlComments(Path.Combine(System.AppContext.BaseDirectory, "Parser.xml"));

                c.DocInclusionPredicate((documentName, apiDescription) =>
                {
                    if (documentName == "blapifull")
                    {
                        return true;
                    }
                    else
                    {
                        var swaggerOperationAttribute = apiDescription.ActionDescriptor.EndpointMetadata
                            .OfType<SwaggerOperationAttribute>()
                            .FirstOrDefault();

                        return swaggerOperationAttribute?.Summary != null;
                    }
                });

                c.SchemaFilter<EnumSchemaFilter>();
            });

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            RequestDecompressionServiceCollectionExtensions.AddRequestDecompression(services);
        }

        public class GrafanaTimingMiddleware
        {
            private readonly RequestDelegate _next;
            private readonly ILogger<LocalstatsMiddleware> _logger;
            private readonly IMetricFamily<ISummary> _durationHistogram;
 
            public GrafanaTimingMiddleware(
                RequestDelegate next, 
                ILogger<LocalstatsMiddleware> logger,
                IMetricFactory metricFactory)
            {
                _next = next;
                _logger = logger;
                _durationHistogram = metricFactory.CreateSummary(
                    "bl_requests_duration_seconds", 
                    "Histogram of request durations",
                    new[] {"method", "endpoint"}
                );
            }
 
            public async Task Invoke(HttpContext context)
            {
                var start = DateTime.Now;
                await _next(context);
                var duration = DateTime.Now - start;

                var routeData = context.GetRouteData();
                string path = routeData?.Values["controller"]?.ToString() + "/" + routeData?.Values["action"]?.ToString();

                _durationHistogram.WithLabels(context.Request.Method, path).Observe(duration.TotalSeconds);
            }
        }

        public void Configure (IApplicationBuilder app)
        {
            app.UseMiddleware<ErrorLoggingMiddleware>();
            app.UseMiddleware<LocalstatsMiddleware>();
            
            app.UsePrometheusServer();
            app.UsePrometheusRequestDurations();
            var sqlServerProcess = Process.GetProcessesByName("sqlservr").FirstOrDefault();
            if (sqlServerProcess != null)
            {
                Console.WriteLine("SQLSERVER!");
                Metrics.DefaultCollectorRegistry.Add(new ProcessCollector(sqlServerProcess, "sql_"));
            }

            app.UseMiddleware<GrafanaTimingMiddleware>();
            app.UseStaticFiles();
            app.UseForwardedHeaders();
            app.UseServerTiming();
            app.UseWebSockets(new WebSocketOptions {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });
            app.UseIpRateLimiting();

            app.UseRouting ();
            app.UseCookiePolicy(new CookiePolicyOptions {
                MinimumSameSitePolicy = SameSiteMode.None,
                Secure = CookieSecurePolicy.Always
            });

            app.UseAuthentication ();
            app.UseAuthorization ();

            app.UseCors (MyAllowSpecificOrigins);

            app.UseResponseCompression();
            RequestDecompressionApplicationBuilderExtensions.UseRequestDecompression(app);

            app.UseEndpoints (endpoints => {
                endpoints.MapDefaultControllerRoute ();
            });

            app.UseSwagger(c => {
                c.PreSerializeFilters.Add((swagger, httpReq) =>
                {
                    swagger.Info.Title = "BeatLeader API. Get various Beat Saber information.";
                    swagger.Info.Description = "Retrieves players, scores, rankings, maps, leaderboards and much more for Beat Saber.";
                    swagger.Servers = new List<OpenApiServer> { new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" } };
                });
            });

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/blapi/swagger.json", "BeatLeader API v1");
                c.SwaggerEndpoint("/swagger/blapifull/swagger.json", "Full(Undocumented) BL API v1");
                c.RoutePrefix = "swagger";
            });
        }
    }
}
