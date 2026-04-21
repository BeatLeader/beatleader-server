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

        public class ScoresContext {
            public LeaderboardContexts Id { get; set; }
            public string Icon { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string Key { get; set; }
        }

        [HttpGet("~/mod/leaderboardContexts")]
        public async Task <ActionResult<List<ScoresContext>>> GetContexts()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            return new List<ScoresContext> {
                new ScoresContext {
                    Id = LeaderboardContexts.General,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_ContextGeneral.png",
                    Name = "General",
                    Description = "Everything allowed",
                    Key = "modifiers"
                },
                new ScoresContext {
                    Id = LeaderboardContexts.LeftLeader,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_ContextFunnyV2.png",
                    Name = "LeftLeader",
                    Description = "Scores from the right notes are negative",
                    Key = "funnyv2"
                },
                new ScoresContext {
                    Id = LeaderboardContexts.NoMods,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_ContextNoModifiers.png",
                    Name = "No Mods",
                    Description = "Modifiers are not allowed",
                    Key = "standard"
                },
                new ScoresContext {
                    Id = LeaderboardContexts.NoPause,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_ContextNoPause.png",
                    Name = "No Pause",
                    Description = "Pauses are not allowed",
                    Key = "nopause"
                },
                new ScoresContext {
                    Id = LeaderboardContexts.Golf,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_ContextGolf.png",
                    Name = "Golf",
                    Description = "The worse you play the better",
                    Key = "golf"
                },
                new ScoresContext {
                    Id = LeaderboardContexts.SCPM,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_ContextSCPM.png",
                    Name = "SCPM",
                    Description = "Smaller Notes+Pro Mod only",
                    Key = "scpm"
                }
            };
        }

        public class MapsTypeDescription {
            public MapTypes Id { get; set; }
            public string Icon { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string Color { get; set; }
        }

        [HttpGet("~/mod/mapTypes")]
        public ActionResult<List<MapsTypeDescription>> GetMapTypes()
        {
            return new List<MapsTypeDescription> {
                new MapsTypeDescription {
                    Id = MapTypes.Acc,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesAcc.png",
                    Name = "Acc",
                    Description = "Slower map suitable for high accuracy plays.",
                    Color = "#800080"
                },
                new MapsTypeDescription {
                    Id = MapTypes.Tech,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesTechSnek.png",
                    Name = "Tech",
                    Description = "Tech map with quirky patterns",
                    Color = "#ff0000"
                },
                new MapsTypeDescription {
                    Id = MapTypes.Midspeed,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesMidspeed.png",
                    Name = "Midspeed",
                    Description = "Medium speed map with regular patterns",
                    Color = "#ffa500"
                },
                new MapsTypeDescription {
                    Id = MapTypes.Speed,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesSpeed.png",
                    Name = "Speed",
                    Description = "High speed map. Please warm up before playing!",
                    Color = "#ffa500"
                },
                new MapsTypeDescription {
                    Id = MapTypes.Fitbeat,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesFitbeat.png",
                    Name = "Fitbeat",
                    Description = "Maps that focus on moving the player with walls (mostly crouch walls, but also dodge walls)",
                    Color = "#800080"
                },
                new MapsTypeDescription {
                    Id = MapTypes.Linear,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesLinear.png",
                    Name = "Linear",
                    Description = "Maps with low amount of repositioning and rotation required.",
                    Color = "#0000ff"
                },
                new MapsTypeDescription {
                    Id = MapTypes.BombReset,
                    Icon = "https://cdn.assets.beatleader.com/Ingame_BL_MapTypesBombReset.png",
                    Name = "Bomb Avoidance",
                    Description = "Maps that contains bomb avoidances.",
                    Color = "#ff0000"
                }
            };
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

            string fileName = Time.UnixNow() + "-newspost";
            string? imageUrl = null;
            try
            {

                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream2) = ImageUtils.GetFormatPng(ms);
                fileName += extension;

                imageUrl = await _s3Client.UploadAsset(fileName, stream2);
            }
            catch (Exception)
            {
                return BadRequest("Error saving avatar");
            }
            var news = new ModNews {
                Owner = owner,
                OwnerIcon = ownerIcon,
                Body = body,
                Timepost = Time.UnixNow(),
                Image = imageUrl,
            };

            _context.ModNews.Add(news);
            await _context.SaveChangesAsync();

            return Ok(news);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpDelete("~/mod/news")]
        public async Task<ActionResult> DeleteNews(
               [FromQuery] int id)
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

            var newsPost = _context.ModNews.Where(n => n.Id == id).First();
            _context.ModNews.Remove(newsPost);
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/servername")]
        public string? ServerName()
        {
            return _configuration.GetValue<string>("ServerName");
        }
    }
}
