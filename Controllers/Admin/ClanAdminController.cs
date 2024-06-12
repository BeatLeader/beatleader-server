using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SixLabors.ImageSharp;
using static BeatLeader_Server.Controllers.ClanController;

namespace BeatLeader_Server.Controllers {
    [ApiExplorerSettings(IgnoreApi = true)]
    public class ClanAdminController : Controller {

        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        IHttpClientFactory _httpClientFactory;
        IWebHostEnvironment _environment;

        public ClanAdminController(
            AppContext context,
            IWebHostEnvironment env,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/clans/refreshglobalmap")]
        public async Task<ActionResult<byte[]?>> RefreshGlobalMap() {
            if (HttpContext != null) {
                string currentID = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(currentID);

                if (!currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var points = await _context
                    .Leaderboards
                    .AsNoTracking()
                    .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                    .Select(lb => new ClanGlobalMapPoint {
                        LeaderboardId = lb.Id,
                        CoverImage = lb.Song.CoverImage,
                        Stars = lb.Difficulty.Stars,
                        Tie = lb.ClanRankingContested,
                        Clans = lb.ClanRanking
                                   .OrderByDescending(cr => cr.Pp)
                                   .Take(3)
                                   .Select(cr => new ClanMapConnection {
                                       Id = cr.ClanId,
                                       Pp = cr.Pp,
                                   })
                                   .ToList()
                    })
                    .ToListAsync();

            var clanIds = new List<int>();
            foreach (var item in points)
            {
                foreach (var clan in item.Clans)
                {
                    if (clan.Id != null && !clanIds.Contains((int)clan.Id)) {
                        clanIds.Add((int)clan.Id);
                    }
                }
            }

            var map = new ClanGlobalMap {
                Points = points,
                Clans = await _context
                    .Clans
                    .AsNoTracking()
                    .Where(c => clanIds.Contains(c.Id))
                    .Select(c => new ClanPoint {
                        Id = c.Id,
                        Tag = c.Tag,
                        Color = c.Color,
                        X = c.GlobalMapX,
                        Y = c.GlobalMapY
                    })
                    .ToListAsync()
            };

            DefaultContractResolver contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };

            await _s3Client.UploadStream("global-map-file.json", S3Container.assets, new BinaryData(JsonConvert.SerializeObject(map, new JsonSerializerSettings
            {
                ContractResolver = contractResolver
            })).ToStream());

            try {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(5);
                string url = "https://render.beatleader.xyz/download/general/clansmap/save";

                HttpResponseMessage response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return NotFound();
                } else {
                    var file = await response.Content.ReadAsByteArrayAsync();
                    await _s3Client.UploadAsset("clansmap-globalcache.json", file);

                    return file;
                }
            } catch (Exception e) {
                return NotFound();
            }
        }

        public class SimulationCache {
            public Dictionary<int, PointF> Clans { get; set; }
        }

        [HttpPost("~/clans/importLocations")]
        public async Task<ActionResult> ImportLocations([FromBody] SimulationCache cache) {
            if (HttpContext != null) {
                string currentID = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(currentID);

                if (!currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }
            var clans = await _context.Clans.ToListAsync();
            foreach (var clan in clans)
            {
                if (cache.Clans.ContainsKey(clan.Id)) {
                    clan.GlobalMapX = cache.Clans[clan.Id].X;
                    clan.GlobalMapY = cache.Clans[clan.Id].Y;
                }
            }

            await _context.BulkSaveChangesAsync();

            return Ok();
        }
    }
}
