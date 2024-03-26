using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;
using OpenIddict.EntityFrameworkCore.Models;
using Newtonsoft.Json;

namespace BeatLeader_Server.Controllers
{
    public class OAuthApp
    {
        public string ClientId { get; set; }
        public string Name { get; set; }
        public string CoverImageUrl { get; set; }
        public string? ClientSecret { get; set; }
        public List<string> Scopes { get; set; }
        public List<string> RedirectUrls { get; set; }

        public static async Task<OAuthApp> CreateFromOpenIddict(OpenIddictEntityFrameworkCoreApplication oauthApp, IOpenIddictApplicationManager manager)
        {
            var permissions = await manager.GetPermissionsAsync(oauthApp);
            var redirectUris = await manager.GetRedirectUrisAsync(oauthApp);

            var properties = JsonDocument.Parse(oauthApp.Properties ?? "{}").RootElement;
            var coverImageUrl = properties.TryGetProperty("PictureUrl", out var pictureUrlElement)
                                ? pictureUrlElement.GetString()
                                : string.Empty;

            return new OAuthApp {
                ClientId = oauthApp.ClientId,
                Name = oauthApp.DisplayName,
                CoverImageUrl = coverImageUrl,
                Scopes = permissions.Where(p => p.StartsWith("scp")).ToList(),
                RedirectUrls = redirectUris.ToList()
            };
        }
    }

    public class DeveloperController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _assetsS3Client;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;

        public DeveloperController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
        }

        [NonAction]
        private string ToFileName(string clientId) {
            Random rnd = new Random();
            return clientId + "_cover_" + "R" + rnd.Next(1, 50);
        }

        [HttpPost("~/developer/app")]
        public async Task<ActionResult<OAuthApp>> CreateOAuthApp(
            [FromQuery] string clientId,
            [FromQuery] string name,
            [FromQuery] string redirectUrls,
            [FromQuery] string scopes = Permissions.Scopes.Profile)
        {
            // Ensure the current user is authenticated
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            if (clientId.Length < 5) {
                return BadRequest("Client ID should be longer than 4 characters");
            }

            if (name.Length < 2 || name.Length > 25) {
                return BadRequest("App name should be between 2 and 25 characters long");
            }

            // Retrieve the player and check for a developer profile
            var player = await _context.Players.Include(p => p.DeveloperProfile).ThenInclude(dp => dp.OauthApps).FirstOrDefaultAsync(p => p.Id == currentID);
            if (player == null) {
                return NotFound("Player not found.");
            }

            if (player.DeveloperProfile?.OauthApps.Count == 5) {
                return BadRequest("You can have only up to 5 apps");
            }

            if (player.DeveloperProfile == null) {
                if ((await _context.DiscordLinks.FirstOrDefaultAsync(d => d.Id == player.Id)) == null) {
                    return BadRequest("Please link Discord account first");
                }

                player.DeveloperProfile = new DeveloperProfile() {
                    OauthApps = new List<OpenIddictEntityFrameworkCoreApplication>()
                };
            }

            // Check if an OAuth application with the same clientId already exists
            var manager = HttpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
            if (await manager.FindByClientIdAsync(clientId) != null) {
                return BadRequest("An OAuth application with this client ID already exists.");
            }

            var scopeList = scopes.Split(',');
            if (scopeList.Length == 0 || scopeList.Any(s => 
                s != Permissions.Scopes.Profile && 
                s != "scp:offline_access" && 
                s != CustomScopePermissions.Clan)) {
                return BadRequest($"Only {Permissions.Scopes.Profile}, {CustomScopePermissions.Clan} and scp:offline_access scopes are supported");
            }

            // Generate a new client secret
            string clientSecret = Guid.NewGuid().ToString();

            // Handle cover image upload
            string? coverImageUrl;
            try {
                var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                string fileName = ToFileName(clientId) + extension;

                coverImageUrl = await _assetsS3Client.UploadAsset(fileName, stream);
            } catch (Exception e) {
                return BadRequest("Error uploading cover image.");
            }

            // Create OAuth application
            var descriptor = new OpenIddictApplicationDescriptor {
                ClientId = clientId,
                ClientSecret = clientSecret,
                DisplayName = name,
                RedirectUris = { },
                Permissions = {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Logout,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code
                },
                Properties = {
                    ["PictureUrl"] = JsonDocument.Parse($"\"{coverImageUrl}\"").RootElement
                }
            };
            descriptor.Permissions.UnionWith(scopeList);
            descriptor.RedirectUris.UnionWith(redirectUrls.Split(",").Select(url => new Uri(url)));

            await manager.CreateAsync(descriptor);

            // Retrieve and link the OAuth app to the developer profile
            var oauthAppObject = await manager.FindByClientIdAsync(clientId);
            if (oauthAppObject == null) {
                return BadRequest("Failed to create OAuth application.");
            }

            var oauthApp = oauthAppObject as OpenIddictEntityFrameworkCoreApplication;
            if (oauthApp == null) {
                return BadRequest("Failed to cast OAuth application object.");
            }

            player.DeveloperProfile.OauthApps.Add(oauthApp);
            await _context.SaveChangesAsync();

            var result = await OAuthApp.CreateFromOpenIddict(oauthApp, manager);
            result.ClientSecret = clientSecret;

            return result;
        }

        [HttpPut("~/developer/app/{clientId}")]
        public async Task<ActionResult<OAuthApp>> UpdateOAuthApp(
            string clientId,
            [FromQuery] string? name = null,
            [FromQuery] string? redirectUrls = null,
            [FromQuery] string? scopes = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var player = await _context.Players
                .Include(app => app.DeveloperProfile)
                .ThenInclude(dp => dp.OauthApps)
                .FirstOrDefaultAsync(player => player.Id == currentID);

            if (player == null || player.DeveloperProfile == null) {
                return NotFound("Developer profile not found.");
            }

            // Find the OAuth application
            var manager = HttpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
            var oauthApp = player.DeveloperProfile.OauthApps.FirstOrDefault(app => app.ClientId == clientId);
            if (oauthApp == null) {
                return Forbid("You are not authorized to view the secret of this application.");
            }

            if (name.Length < 2 || name.Length > 25) {
                return BadRequest("App name should be between 2 and 25 characters long");
            }

            // Update DisplayName
            if (!string.IsNullOrEmpty(name) && name.Length > 2) {
                oauthApp.DisplayName = name;
            }

            // Update RedirectUri
            if (!string.IsNullOrEmpty(redirectUrls)) {
                if (redirectUrls.Split(",").Length == 0) {
                    return BadRequest("Please provide redirect urls");
                }
                oauthApp.RedirectUris = JsonConvert.SerializeObject(redirectUrls.Split(","));
            }

            // Update RedirectUri
            if (!string.IsNullOrEmpty(scopes)) {
                var scopeList = scopes.Split(',');
                if (scopeList.Length == 0 || scopeList.Any(s => s != Permissions.Scopes.Profile && s != CustomScopePermissions.Clan)) {
                    return BadRequest($"Only {Permissions.Scopes.Profile} and {CustomScopePermissions.Clan} scopes are supported");
                }
                var permissions = new List<string> {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Logout,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code
                };
                oauthApp.Permissions = JsonConvert.SerializeObject(permissions.Union(scopeList));
            }

            // Update Cover Image (similar to clan update)
            try {
                var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms);
                if (ms.Length > 0) {
                    ms.Position = 0;

                    (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                    string fileName =  ToFileName(clientId) + extension;

                    var coverImageUrl = await _assetsS3Client.UploadAsset(fileName, stream);
                    oauthApp.Properties = JsonConvert.SerializeObject( new Dictionary<string, string> {
                        ["PictureUrl"] = coverImageUrl
                    });
                }
            } catch (Exception) {
                return BadRequest("Error updating cover image.");
            }

            // Save changes
            await manager.UpdateAsync(oauthApp);

            return Ok(await OAuthApp.CreateFromOpenIddict(oauthApp, manager));
        }

        [HttpDelete("~/developer/app/{clientId}")]
        public async Task<ActionResult> DeleteApp(string clientId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var player = await _context.Players
                .Include(app => app.DeveloperProfile)
                .ThenInclude(dp => dp.OauthApps)
                .FirstOrDefaultAsync(player => player.Id == currentID);

            if (player == null || player.DeveloperProfile == null) {
                return NotFound("Developer profile not found.");
            }

            if (player.DeveloperProfile.OauthApps.FirstOrDefault(app => app.ClientId == clientId) == null) {
                return Forbid("You are not authorized to view the secret of this application.");
            }

            var manager = HttpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
            var oauthApp = await manager.FindByClientIdAsync(clientId);
            if (oauthApp == null) {
                return NotFound("OAuth application not found.");
            }

            // Delete the application
            await manager.DeleteAsync(oauthApp);

            return Ok("Application deleted successfully.");
        }

        [HttpGet("~/developer/apps")]
        public async Task<ActionResult<List<OAuthApp>>> ApplicationsList()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var player = await _context.Players
                                       .Include(p => p.DeveloperProfile)
                                       .ThenInclude(dp => dp.OauthApps)
                                       .FirstOrDefaultAsync(p => p.Id == currentID);

            if (player == null || player.DeveloperProfile == null) {
                return NotFound("Developer profile not found.");
            }

            var manager = HttpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();

            var appDtos = new List<OAuthApp>();
            foreach (var app in player.DeveloperProfile.OauthApps)
            {
                var dto = await OAuthApp.CreateFromOpenIddict(app, manager);
                appDtos.Add(dto);
            }

            return Ok(appDtos);
        }

        [HttpPost("~/developer/appsecretreset/{clientId}")]
        public async Task<ActionResult<string>> ResetAppSecret(string clientId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var player = await _context.Players
                .Include(app => app.DeveloperProfile)
                .ThenInclude(dp => dp.OauthApps)
                .FirstOrDefaultAsync(player => player.Id == currentID);

            if (player == null || player.DeveloperProfile == null) {
                return NotFound("Developer profile not found.");
            }

            if (player.DeveloperProfile.OauthApps.FirstOrDefault(app => app.ClientId == clientId) == null) {
                return Forbid("You are not authorized to view the secret of this application.");
            }

            var manager = HttpContext.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
            var oauthApp = await manager.FindByClientIdAsync(clientId);
            if (oauthApp == null) {
                return NotFound("OAuth application not found.");
            }

            // Reset the client secret
            var newSecret = Guid.NewGuid().ToString();
            await manager.UpdateAsync(oauthApp, newSecret);

            return Ok(newSecret);
        }
    }
}
