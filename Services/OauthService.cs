using BeatLeader_Server.Models;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BeatLeader_Server.Services {

    public class OauthService : IHostedService {
        public static string TestClientId = "TestOauthApp";
        public static string TestClientSecret = "388D45FA-B36B-4988-BA59-B187D329C207";

        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _environment;

        public OauthService(IServiceProvider serviceProvider, IWebHostEnvironment environment) {
            _serviceProvider = serviceProvider;
            _environment = environment;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            using var scope = _serviceProvider.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<AppContext>();
            await context.Database.EnsureCreatedAsync();

            if (_environment.IsDevelopment()) {
                var clanScope = context.OpenIddictScopes.FirstOrDefault(os => os.Id == CustomScopes.Clan);
                if (clanScope == null) {
                    context.OpenIddictScopes.Add(new OpenIddictEntityFrameworkCoreScope {
                        Id = CustomScopes.Clan,
                        Name = CustomScopes.Clan,
                        DisplayName = "Clan management"
                    });
                    await context.SaveChangesAsync();
                }

                var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                if (await manager.FindByClientIdAsync(TestClientId) is null) {
                    await manager.CreateAsync(new OpenIddictApplicationDescriptor {
                        ClientId = TestClientId,
                        ClientSecret = TestClientSecret,
                        DisplayName = "Test app",
                        RedirectUris =
                        {
                            new Uri("https://localhost:44313/signin-beatleader")
                        },
                        Permissions =
                        {
                            Permissions.Endpoints.Authorization,
                            Permissions.Endpoints.Logout,
                            Permissions.Endpoints.Token,
                            Permissions.GrantTypes.AuthorizationCode,
                            Permissions.GrantTypes.RefreshToken,
                            Permissions.ResponseTypes.Code,
                            Permissions.Scopes.Profile,
                            CustomScopePermissions.Clan
                        },
                        Properties =
                        {
                            ["PictureUrl"] = JsonDocument.Parse($"\"https://www.beatleader.com/assets/favicon-96x96.png\"").RootElement
                        }
                    });
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
