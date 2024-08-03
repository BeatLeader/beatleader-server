using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class ModController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IAmazonS3 _s3Client;
        private readonly AppContext _context;

        public ModController(
            IConfiguration configuration,
            AppContext context)
        {
            _configuration = configuration;
            _context = context;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/mod/lastVersions")]
        public ActionResult GetLastVersions()
        {
            if (HttpContext.Request.Headers["User-Agent"].Contains("0.8.0")) {
                return Content(_configuration.GetValue<string>("ModVersionsLatest"), "application/json");
            } else {
                return Content(_configuration.GetValue<string>("ModVersions"), "application/json");
            }
        }

        [HttpPost("~/mod/version")]
        public async Task<ActionResult> AddModVersion([FromQuery] string platform, [FromQuery] string gameVersion, [FromQuery] string version)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            _context.ModVersions.Add(new Models.ModVersion {
                Platform = platform,
                GameVersion = gameVersion,
                Version = version,
                Timeset = Time.UnixNow()
            });
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("~/mod/uptodate")]
        public async Task<ActionResult<bool>> IsModUptodate([FromQuery] string platform, [FromQuery] string gameVersion, [FromQuery] string version)
        {
            var latestVersion = await _context.ModVersions.Where(m => m.Platform == platform && m.GameVersion == gameVersion).OrderByDescending(m => m.Timeset).FirstOrDefaultAsync();
            return latestVersion != null && latestVersion.Version == version;
        }

        [HttpGet("~/mod/news")]
        public async Task<ActionResult<ResponseWithMetadata<ModNews>>> GetModNews([FromQuery] int page = 1, int count = 10)
        {
            return new ResponseWithMetadata<ModNews> {
                Metadata = new Metadata {
                    ItemsPerPage = count,
                    Page = page,
                    Total = await _context.ModNews.CountAsync()
                },
                Data = await _context.ModNews.OrderByDescending(n => n.Timepost).Skip((page - 1) * count).Take(count).ToListAsync()
            };
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/mod/news")]
        public async Task<ActionResult> PostNews(
               [FromQuery] string owner,
               [FromQuery] string ownerIcon,
               [FromQuery] string body)
        {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            string fileName = Time.UnixNow() + "-event";
            string? imageUrl = null;
            try
            {

                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream2) = ImageUtils.GetFormatAndResize(ms);
                fileName += extension;

                imageUrl = await _s3Client.UploadAsset(fileName, stream2);
            }
            catch (Exception)
            {
                return BadRequest("Error saving avatar");
            }

            _context.ModNews.Add(new ModNews {
                Owner = owner,
                OwnerIcon = ownerIcon,
                Body = body,
                Timepost = Time.UnixNow(),
                Image = imageUrl,
            });
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/servername")]
        public string? ServerName()
        {
            return _configuration.GetValue<string>("ServerName");
        }
    }
}
