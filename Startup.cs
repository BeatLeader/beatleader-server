using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Azure;
using Azure.Storage.Queues;
using Azure.Storage.Blobs;
using Azure.Core.Extensions;
using System;
using System.Text.Json.Serialization;
using BeatLeader_Server.Services;

namespace BeatLeader_Server {
    public class AzureStorageConfig {
        public string AccountName { get; set; }
        public string ReplaysContainerName { get; set; }
        public string OtherReplaysContainerName { get; set; }
        public string AssetsContainerName { get; set; }
        public string PlaylistContainerName { get; set; }
        public string ScoreStatsContainerName { get; set; }
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

            string? cookieDomain = Configuration.GetValue<string>("CookieDomain");

            var authBuilder = services.AddAuthentication (options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })

            .AddCookie (options => {
                options.Events.OnRedirectToAccessDenied =
                options.Events.OnRedirectToLogin = c => {
                    c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.FromResult<object> (null);
                };
                options.Cookie.SameSite = SameSiteMode.None;
                if (cookieDomain != null) {
                    options.Cookie.Domain = cookieDomain;
                }
                options.Cookie.HttpOnly = false;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.Cookie.MaxAge = options.ExpireTimeSpan;
                options.SlidingExpiration = true;
            })
            .AddCookie("BLPatreon")
            .AddCookie("BLTwitch")
            .AddCookie("BLTwitter")
            .AddCookie("BLGoogle")
            //.AddCookie("BLDiscord")
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
            }

            services.AddServerTiming();

            string readWriteConnection;
            if (Environment.IsDevelopment())
            {
                readWriteConnection = "Data Source = tcp:localhost,1433; Initial Catalog = BeatLeader; User Id = sa; Password = SuperStrong!";
            }
            else
            {
                readWriteConnection = Configuration.GetConnectionString("DefaultConnection");
            }
            string readConnection;
            if (Environment.IsDevelopment())
            {
                readConnection = "Data Source = tcp:localhost,1433; Initial Catalog = BeatLeader; ApplicationIntent=ReadOnly; User Id = sa; Password = SuperStrong!";
            }
            else
            {
                readConnection = Configuration.GetConnectionString("ReadOnlyConnection");
            }


            services.AddDbContext<AppContext>(options => options.UseSqlServer(readWriteConnection));
            services.AddDbContext<ReadAppContext>(options => options.UseSqlServer(readConnection));

            services.Configure<AzureStorageConfig> (Configuration.GetSection ("AzureStorageConfig"));

            if (Configuration.GetValue<string>("ServicesHost") == "YES")
            {
                services.AddHostedService<HourlyRefresh>();
                services.AddHostedService<DailyRefresh>();
                services.AddHostedService<HistoryService>();
                services.AddHostedService<RankingService>();
            }
            services.AddMvc ().AddControllersAsServices ().AddJsonOptions (options => {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
            services.AddAzureClients (builder => {
                builder.AddBlobServiceClient (Configuration ["CDN:blob"], preferMsi: true);
                builder.AddQueueServiceClient (Configuration ["CDN:queue"], preferMsi: true);
            });
            if (!Environment.IsDevelopment())
            {
                services.AddCors (options => {
                options.AddPolicy (name: MyAllowSpecificOrigins,
                    builder => {
                        builder.WithOrigins("http://localhost:8888",
                                            "https://www.beatleader.xyz",
                                            "https://agitated-ptolemy-7d772c.netlify.app");
                        builder.AllowCredentials();
                    });
            });
            }

            services.AddSwaggerGen();

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            RequestDecompressionServiceCollectionExtensions.AddRequestDecompression(services);
        }

        public void Configure (IApplicationBuilder app)
        {
            app.UseStaticFiles ();
            app.UseServerTiming();
            app.UseWebSockets(new WebSocketOptions {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            app.UseRouting ();

            app.UseAuthentication ();
            app.UseAuthorization ();

            app.UseCors (MyAllowSpecificOrigins);

            app.UseResponseCompression();
            RequestDecompressionApplicationBuilderExtensions.UseRequestDecompression(app);

            app.UseEndpoints (endpoints => {
                endpoints.MapDefaultControllerRoute ();
            });

            app.UseSwagger();
            app.UseSwaggerUI();
        }
    }
    internal static class StartupExtensions {
        public static IAzureClientBuilder<BlobServiceClient, BlobClientOptions> AddBlobServiceClient (this AzureClientFactoryBuilder builder, string serviceUriOrConnectionString, bool preferMsi)
        {
            if (preferMsi && Uri.TryCreate (serviceUriOrConnectionString, UriKind.Absolute, out Uri serviceUri)) {
                return builder.AddBlobServiceClient (serviceUri);
            } else {
                return builder.AddBlobServiceClient (serviceUriOrConnectionString);
            }
        }
        public static IAzureClientBuilder<QueueServiceClient, QueueClientOptions> AddQueueServiceClient (this AzureClientFactoryBuilder builder, string serviceUriOrConnectionString, bool preferMsi)
        {
            if (preferMsi && Uri.TryCreate (serviceUriOrConnectionString, UriKind.Absolute, out Uri serviceUri)) {
                return builder.AddQueueServiceClient (serviceUri);
            } else {
                return builder.AddQueueServiceClient (serviceUriOrConnectionString);
            }
        }
    }
}
