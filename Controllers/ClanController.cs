using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BeatLeader_Server.Controllers
{
    public class ClanController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;

        public ClanController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/clans/")]
        public async Task<ActionResult<ResponseWithMetadata<Clan>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sort = "captures",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? type = null,
            [FromQuery] string? sortBy = null)
        {
            var sequence = _context
                .Clans
                .Include(cl => cl.CapturedLeaderboards)
                .AsQueryable();
            if (sortBy != null)
            {
                sort = sortBy;
            }
            switch (sort)
            {
                case "name":
                    sequence = sequence.Order(order, t => t.Name);
                    break;
                case "pp":
                    sequence = sequence.Order(order, t => t.Pp);
                    break;
                case "acc":
                    sequence = sequence.Where(c => c.PlayersCount > 2).Order(order, t => t.AverageAccuracy);
                    break;
                case "rank":
                    sequence = sequence.Where(c => c.PlayersCount > 2 && c.AverageRank > 0).Order(order.Reverse(), t => t.AverageRank);
                    break;
                case "count":
                    sequence = sequence.Order(order, t => t.PlayersCount);
                    break;
                case "captures":
                    sequence = sequence.Order(order, c => c.CapturedLeaderboards.Count);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Where(p => p.Name.ToLower().Contains(lowSearch) ||
                                p.Tag.ToLower().Contains(lowSearch));
            }

            return new ResponseWithMetadata<Clan>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await sequence.CountAsync()
                },
                Data = await sequence
                    .AsSplitQuery()
                    .TagWithCallSite()
                    .Skip((page - 1) * count)
                    .Take(count)
                    .ToListAsync()
            };
        }

        [HttpGet("~/clan/{tag}")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, Clan>>> GetClan(
            string tag,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sort = "pp",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
        {
            Clan? clan = null;
            if (tag == "my")
            {
                string? currentID = HttpContext.CurrentUserID(_context);
                var player = currentID != null ? await _context.Players.FindAsync(currentID) : null;

                if (player == null)
                {
                    return NotFound();
                }
                clan = await _context
                    .Clans
                    .Where(c => c.LeaderID == currentID)
                    .FirstOrDefaultAsync();
            }
            else
            {
                clan = await _context
                    .Clans
                    .Where(c => c.Tag == tag)
                    .FirstOrDefaultAsync();
            }
            if (clan == null)
            {
                return NotFound();
            }

            var players = _context
                .Players
                .Include(p => p.ProfileSettings)
                .Where(p => !p.Banned && p.Clans.Contains(clan));
            switch (sort)
            {
                case "pp":
                    players = players.Order(order, t => t.Pp);
                    break;
                case "acc":
                    players = players.Order(order, t => t.ScoreStats.AverageRankedAccuracy);
                    break;
                case "rank":
                    players = players.Order(order, t => t.Rank);
                    break;
                default:
                    break;
            }
            return new ResponseWithMetadataAndContainer<PlayerResponse, Clan>
            {
                Container = clan,
                Data = (await players
                    .AsSplitQuery()
                    .TagWithCallSite()
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(p => new PlayerResponse
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Platform = p.Platform,
                        Avatar = p.Avatar,
                        Country = p.Country,

                        Bot = p.Bot,
                        Pp = p.Pp,
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        Role = p.Role,
                        ProfileSettings = p.ProfileSettings,
                    })
                    .ToListAsync())
                    .Select(PostProcessSettings),
                Metadata = new Metadata
                {
                    Page = 1,
                    ItemsPerPage = 10,
                    Total = await players.CountAsync()
                }
            };
        }

        public class ClanPoint {
            public int Id { get; set; }
            public string Tag { get; set; }
            public string Color { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
        }

        public class ClanMapConnection {
            public int? Id { get; set; }
            
            public float Pp { get; set; }
        }

        public class ClanGlobalMapPoint {
            public string LeaderboardId { get; set; }
            public string CoverImage { get; set; }

            public float? Stars { get; set; }

            public bool Tie { get; set; }

            public List<ClanMapConnection> Clans { get; set; }
        }

        public class ClanGlobalMap {
            public List<ClanGlobalMapPoint> Points { get; set; }
            public List<ClanPoint> Clans { get; set; }
        }

        [HttpGet("~/clans/globalmap")]
        public async Task<ActionResult> GlobalMap() {
            var mapUrl = await _s3Client.GetPresignedUrl("global-map-file.json", S3Container.assets);
            if (mapUrl == null) {
                return NotFound();
            }
            return Redirect(mapUrl);
        }

        [HttpGet("~/clans/refreshglobalmap")]
        public async Task<ActionResult> RefreshGlobalMap() {
            if (HttpContext != null) {
                string currentID = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(currentID);

                if (!currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var points = _context
                    .Leaderboards
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
                    .ToList();

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
                Clans = _context
                    .Clans
                    .Where(c => clanIds.Contains(c.Id))
                    .Select(c => new ClanPoint {
                        Id = c.Id,
                        Tag = c.Tag,
                        Color = c.Color,
                        X = c.GlobalMapX,
                        Y = c.GlobalMapY
                    })
                    .ToList()
            };

            DefaultContractResolver contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };

            await _s3Client.UploadStream("global-map-file.json", S3Container.assets, new BinaryData(JsonConvert.SerializeObject(map, new JsonSerializerSettings
            {
                ContractResolver = contractResolver
            })).ToStream());
            return Ok();
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
            var clans = _context.Clans.ToList();
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