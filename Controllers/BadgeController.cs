using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using BeatLeader_Server.Enums;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Services.SearchService;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class BadgeController : Controller
    {
        private readonly AppContext _context;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public BadgeController(
            AppContext context,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpPut("~/badge")]
        [Authorize]
        public async Task<ActionResult<Badge>> CreateBadge(
                [FromQuery] string description, 
                [FromQuery] string? link = null,
                [FromQuery] string? image = null,
                [FromQuery] int? timeset = null,
                [FromQuery] string? playerId = null) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Badge badge = new Badge {
                Description = description,
                Image = image ?? "",
                Link = link,
                Timeset = timeset ?? (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            };

            _context.Badges.Add(badge);
            await _context.SaveChangesAsync();

            if (image == null) {
                string? fileName = null;
                try
                {
                    var ms = new MemoryStream(5);
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    (string extension, MemoryStream stream) = ImageUtils.GetFormat(ms);
                    Random rnd = new Random();
                    fileName = "badge-" + badge.Id + "R" + rnd.Next(1, 50) + extension;

                    badge.Image = await _assetsS3Client.UploadAsset(fileName, stream);
                }
                catch {}
            }

            await _context.SaveChangesAsync();

            if (playerId != null) {
                await AddBadge(playerId, badge.Id);
            }

            return badge;
        }

        [HttpPut("~/badge/{id}")]
        [Authorize]
        public async Task<ActionResult<Badge>> UpdateBadge(
            int id, 
            [FromQuery] string? description = null, 
            [FromQuery] string? image = null, 
            [FromQuery] string? link = null,
            [FromQuery] int? priority = null)
        {
            string? currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentId != null ? await _context.Players.FindAsync(currentId) : null;
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var badge = await _context.Badges.FindAsync(id);

            if (badge == null) {
                return NotFound();
            }

            if (description != null) {
                badge.Description = description;
            }

            if (image != null) {
                badge.Image = image;
            }

            if (priority != null) {
                badge.Priority = priority ?? 0;
            }

            if (Request.Query.ContainsKey("link"))
            {
                badge.Link = link;
            }

            await _context.SaveChangesAsync();

            return badge;
        }

        [HttpDelete("~/badge/{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteBadge(int id)
        {
            string? currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentId != null ? await _context.Players.FindAsync(currentId) : null;
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var badge = await _context.Badges.FindAsync(id);

            if (badge == null) {
                return NotFound();
            }

            _context.Badges.Remove(badge);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("~/player/badge/{playerId}/{badgeId}")]
        [Authorize]
        public async Task<ActionResult<Player>> AddBadge(string playerId, int badgeId)
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            playerId = await _context.PlayerIdToMain(playerId);

            Player? player = await _context.Players.Include(p => p.Badges).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound("Player not found");
            }

            Badge? badge = await _context.Badges.FindAsync(badgeId);
            if (badge == null)
            {
                return NotFound("Badge not found");
            }
            if (player.Badges == null) {
                player.Badges = new List<Badge>();
            }

            player.Badges.Add(badge);
            await _context.SaveChangesAsync();

            return player;
        }

        public class BadgeListingResponse {
            public int Id { get; set; }
            public string Description { get; set; }
            public string Image { get; set; }
            public string? Link { get; set; }
            public int Timeset { get; set; }
            public int Priority { get; set; }

            public ICollection<PlayerResponse> Players { get; set; }
        }

        [HttpGet("~/badges/all")]
        public async Task<ActionResult<ICollection<BadgeListingResponse>>> AllBadges()
        {
            var badges = await _context.Badges.Where(b => b.PlayerId != null).AsNoTracking().Select(b => new {
                Description = b.Description,
                    b.Image,
                    b.Link,
                    b.Timeset,
                    b.Priority,
                    b.Id,
                    Player = new PlayerResponse {
                        Id = b.Player.Id,
                        Name = b.Player.Name,
                        Alias = b.Player.Alias,
                        Platform = b.Player.Platform,
                        Country = b.Player.Country,
                        Avatar = b.Player.Avatar,
                        ProfileSettings = b.Player.ProfileSettings,

                        Pp = b.Player.Pp,
                        Rank = b.Player.Rank,
                        CountryRank = b.Player.CountryRank,
                    }
            }).ToListAsync();

            var result = badges.GroupBy(b => b.Image).Select(g => { 
                var badge = g.First();
                return new BadgeListingResponse {
                    Description = badge.Description,
                    Image = badge.Image,
                    Link = badge.Link,
                    Timeset = badge.Timeset,
                    Priority = badge.Priority,
                    Id = badge.Id,
                    Players = g.Select(s => s.Player).ToList()
                };
            }).OrderByDescending(g => g.Timeset).ThenBy(g => g.Priority).ToList();

            foreach (var item in result) {
                if (item.Description == "Helped fight cancer by donating a total of $2115! Tier 4, Beat Cancer, December 2024") {
                    item.Description = "Helped fight cancer by donating more than $300! Tier 4, Beat Cancer, December 2024";
                }
                if (item.Description == "Helped fight cancer by donating a total of $185! Tier 3, Beat Cancer, December 2024") {
                    item.Description = "Helped fight cancer by donating $150-300! Tier 3, Beat Cancer, December 2024";
                }
                if (item.Description == "Helped fight cancer by donating a total of $69.99! Tier 2, Beat Cancer, December 2024") {
                    item.Description = "Helped fight cancer by donating $50-150! Tier 2, Beat Cancer, December 2024";
                }
                if (item.Description == "Helped fight cancer by donating a total of $10! Tier 1, Beat Cancer, December 2024") {
                    item.Description = "Helped fight cancer by donating $5-50! Tier 1, Beat Cancer, December 2024";
                }
            }

            return result;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/badges/beatkhanahook")]
        public async Task<IActionResult> BeatKhanaHook()
        {
            byte[] bodyBytes;
            using (var ms = new MemoryStream()) {
                await Request.Body.CopyToAsync(ms);
                bodyBytes = ms.ToArray();
            }

            string? signature = Request.Headers["x-beatkhana-signature-256"].FirstOrDefault();
            if (signature == null || !IsValidBeatKhanaSignature(bodyBytes, signature)) {
                return Unauthorized("Invalid signature.");
            }

            BeatKhanaWebhookPayload? payload;
            try {
                payload = JsonConvert.DeserializeObject<BeatKhanaWebhookPayload>(Encoding.UTF8.GetString(bodyBytes));
            } catch {
                return BadRequest("Invalid payload.");
            }
            if (payload == null) {
                return BadRequest("Invalid payload.");
            }

            string? eventType = Request.Headers["x-beatkhana-event"].FirstOrDefault();
            if (eventType != "request_accepted") {
                return Ok();
            }

            if (payload.Provider?.ContextId != "beatleader") {
                return BadRequest("Not supported badge provider");
            }

            var badges = payload.Request?.Version?.Badges;
            if (badges == null) {
                return Ok();
            }

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var grantedKeys = new HashSet<string>();
            int granted = 0;

            foreach (var badge in badges) {
                if (badge?.Recipients == null || string.IsNullOrEmpty(badge.ImageUrl) || string.IsNullOrEmpty(badge.Name)) {
                    continue;
                }

                string? image = null;
                bool imageResolved = false;

                foreach (var recipient in badge.Recipients) {
                    string? blId = FirstNonEmpty(
                        recipient.User?.BeatleaderId,
                        recipient.User?.BlSteamId,
                        recipient.User?.BlOculusPCId,
                        recipient.User?.BlQuestId,
                        recipient.IdentifierValue);
                    if (blId == null) {
                        continue;
                    }

                    string mainId = await _context.PlayerIdToMain(blId);
                    Player? player = await _context.Players.Include(p => p.Badges).FirstOrDefaultAsync(p => p.Id == mainId);
                    if (player == null) {
                        continue;
                    }

                    string key = player.Id + "|" + badge.Name + "|" + badge.Href;
                    if (!grantedKeys.Add(key)) {
                        continue;
                    }
                    if (player.Badges != null && player.Badges.Any(b => b.Description == badge.Name && b.Link == badge.Href)) {
                        continue;
                    }

                    if (!imageResolved) {
                        image = await ReuploadBadgeImage(badge.ImageUrl) ?? badge.ImageUrl;
                        imageResolved = true;
                    }

                    _context.Badges.Add(new Badge {
                        Description = (badge.Name ?? ""),
                        Details = badge.Description,
                        Image = image ?? "",
                        Link = badge.Href,
                        Timeset = timeset,
                        PlayerId = player.Id,
                    });
                    granted++;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { granted });
        }

        private static string? FirstNonEmpty(params string?[] values) {
            foreach (var value in values) {
                if (!string.IsNullOrEmpty(value)) {
                    return value;
                }
            }
            return null;
        }

        private async Task<string?> ReuploadBadgeImage(string? imageUrl) {
            if (string.IsNullOrEmpty(imageUrl)) {
                return null;
            }
            try {
                using var httpClient = new HttpClient();
                var ms = new MemoryStream(await httpClient.GetByteArrayAsync(imageUrl));
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormatPng(ms);
                string fileName = "badge-beatkhana-" + Guid.NewGuid().ToString("N") + extension;

                return await _assetsS3Client.UploadAsset(fileName, stream);
            } catch {
                return null;
            }
        }

        private bool IsValidBeatKhanaSignature(byte[] body, string signatureHeader)
        {
            string signature = (signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
                ? signatureHeader.Substring("sha256=".Length)
                : signatureHeader).Trim().ToLowerInvariant();

            string? beatKhanaWebhookSecret = _configuration.GetValue<string?>("BeatKhanaWebhookSecret");
            if (beatKhanaWebhookSecret == null) return false;

            // The secret is a base64-encoded key, but accept its raw string form as a fallback.
            var keys = new List<byte[]> { Encoding.UTF8.GetBytes(beatKhanaWebhookSecret) };
            try {
                keys.Insert(0, Convert.FromBase64String(beatKhanaWebhookSecret));
            } catch {}

            foreach (var key in keys) {
                using var hmac = new HMACSHA256(key);
                string computed = Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
                if (string.Equals(computed, signature, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        public class BeatKhanaWebhookPayload {
            [JsonProperty("event")]
            public string? Event { get; set; }
            [JsonProperty("request")]
            public BeatKhanaRequest? Request { get; set; }
            [JsonProperty("provider")]
            public BeatKhanaProvider? Provider { get; set; }
        }

        public class BeatKhanaRequest {
            [JsonProperty("version")]
            public BeatKhanaVersion? Version { get; set; }
        }

        public class BeatKhanaProvider {
            [JsonProperty("contextId")]
            public string ContextId { get; set; }
        }

        public class BeatKhanaVersion {
            [JsonProperty("badges")]
            public List<BeatKhanaBadge>? Badges { get; set; }
        }

        public class BeatKhanaBadge {
            [JsonProperty("name")]
            public string? Name { get; set; }
            [JsonProperty("description")]
            public string? Description { get; set; }
            [JsonProperty("imageUrl")]
            public string? ImageUrl { get; set; }
            [JsonProperty("href")]
            public string? Href { get; set; }
            [JsonProperty("sortOrder")]
            public int SortOrder { get; set; }
            [JsonProperty("recipients")]
            public List<BeatKhanaRecipient>? Recipients { get; set; }
        }

        public class BeatKhanaRecipient {
            [JsonProperty("identifierType")]
            public string? IdentifierType { get; set; }
            [JsonProperty("identifierValue")]
            public string? IdentifierValue { get; set; }
            [JsonProperty("user")]
            public BeatKhanaUser? User { get; set; }
        }

        public class BeatKhanaUser {
            [JsonProperty("beatleaderId")]
            public string? BeatleaderId { get; set; }
            [JsonProperty("blSteamId")]
            public string? BlSteamId { get; set; }
            [JsonProperty("blOculusPCId")]
            public string? BlOculusPCId { get; set; }
            [JsonProperty("blQuestId")]
            public string? BlQuestId { get; set; }
        }
    }
}
