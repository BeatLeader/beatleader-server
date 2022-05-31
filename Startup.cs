using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging.AzureAppServices;
using Azure.Storage.Queues;
using Azure.Storage.Blobs;
using Azure.Core.Extensions;
using System;
using System.Text.Json.Serialization;

namespace BeatLeader_Server {
    public class AzureStorageConfig {
        public string AccountName { get; set; }
        public string ReplaysContainerName { get; set; }
        public string AssetsContainerName { get; set; }
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

            string patreonId = Configuration.GetValue<string>("PatreonId");
            string patreonSecret = Configuration.GetValue<string>("PatreonSecret");

            string? cookieDomain = Configuration.GetValue<string>("CookieDomain");

            string oculusToken = Configuration.GetValue<string>("OculusToken");
            string oculusKey = Configuration.GetValue<string>("OculusKey");

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
            .AddSteamTicket(options =>
            {
                options.Key = steamKey;
                options.ApplicationID = "620980";
                options.ApiUrl = steamApi;
            })
            .AddPassword(options => {})
            .AddSteam (options => {
                options.ApplicationKey = steamKey;
                options.Events.OnAuthenticated = ctx => {
                    /* ... */
                    return Task.CompletedTask;
                };
            });
            

            if (!Environment.IsDevelopment()) {
                authBuilder.AddPatreon(options => {
                    options.SignInScheme = "BLPatreon";
                    options.SaveTokens = true;
                    options.ClientId = patreonId;
                    options.ClientSecret = patreonSecret;
                });

                authBuilder.AddOculus(options => {
                     options.Key = oculusKey;
                     options.Token = oculusToken;
                });
            }

            string connection;
            if (Environment.IsDevelopment()) {
                connection = "Data Source = tcp:localhost,1433; Initial Catalog = BeatLeader; User Id = sa; Password = SuperStrong!";
            } else {
                connection = Configuration.GetConnectionString("DefaultConnection");
            }
            
            services.AddServerTiming();

            services.AddDbContext<AppContext> (options => options.UseSqlServer (connection));

            services.Configure<AzureStorageConfig> (Configuration.GetSection ("AzureStorageConfig"));

            services.AddMvc ().AddControllersAsServices ().AddJsonOptions (options => {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
            services.AddAzureClients (builder => {
                builder.AddBlobServiceClient (Configuration ["CDN:blob"], preferMsi: true);
                builder.AddQueueServiceClient (Configuration ["CDN:queue"], preferMsi: true);
            });
            services.AddCors (options => {
                options.AddPolicy (name: MyAllowSpecificOrigins,
                    builder => {
                        builder.WithOrigins("http://localhost:8888",
                                            "https://www.beatleader.xyz",
                                            "https://agitated-ptolemy-7d772c.netlify.app");
                        builder.AllowCredentials();
                    });
            });

            services.Configure<AzureFileLoggerOptions>(options =>
            {
                options.FileName = "azure-diagnostics-";
                options.FileSizeLimit = 50 * 1024;
                options.RetainedFileCountLimit = 5;
            });

            services.AddSwaggerGen();

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
            });

            services.AddRequestDecompression();
            
        }

        public void Configure (IApplicationBuilder app)
        {
            app.UseStaticFiles ();
            app.UseServerTiming();

            app.UseRouting ();

            app.UseAuthentication ();
            app.UseAuthorization ();

            app.UseCors (MyAllowSpecificOrigins);

            app.UseResponseCompression();
            app.UseRequestDecompression();

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
