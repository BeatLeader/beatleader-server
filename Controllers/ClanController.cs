using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using SixLabors.ImageSharp;
using Swashbuckle.AspNetCore.Annotations;
using BeatLeader_Server.ControllerHelpers;

namespace BeatLeader_Server.Controllers
{
    public class ClanController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;

        public ClanController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/clans/")]
        [SwaggerOperation(Summary = "Retrieve a list of clans", Description = "Fetches a paginated and optionally filtered list of clans (group of players). Filters include sorting by performance points, search, name, rank, and more.")]
        [SwaggerResponse(200, "List of clans retrieved successfully", typeof(ResponseWithMetadata<ClanResponseFull>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Clans not found")]
        public async Task<ActionResult<ResponseWithMetadata<ClanResponseFull>>> GetAll(
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 10")] int count = 10,
            [FromQuery] ClanSortBy sort = ClanSortBy.Captures,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] ClanSortBy? sortBy = null)
        {
            var sequence = _context
                .Clans
                .AsNoTracking()
                .Include(cl => cl.CapturedLeaderboards)
                .AsQueryable();

            if (sortBy != null)
            {
                sort = (ClanSortBy)sortBy;
            }
            switch (sort)
            {
                case ClanSortBy.Name:
                    sequence = sequence.Order(order, t => t.Name);
                    break;
                case ClanSortBy.Pp:
                    sequence = sequence.Order(order, t => t.Pp);
                    break;
                case ClanSortBy.Acc:
                    sequence = sequence.Where(c => c.PlayersCount > 2).Order(order, t => t.AverageAccuracy);
                    break;
                case ClanSortBy.Rank:
                    sequence = sequence.Where(c => c.PlayersCount > 2 && c.AverageRank > 0).Order(order.Reverse(), t => t.AverageRank);
                    break;
                case ClanSortBy.Count:
                    sequence = sequence.Order(order, t => t.PlayersCount);
                    break;
                case ClanSortBy.Captures:
                    sequence = sequence.Order(order, c => c.CaptureLeaderboardsCount);
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

            return new ResponseWithMetadata<ClanResponseFull>()
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
                    .Select(clan => new ClanResponseFull {
                        Id = clan.Id,
                        Name = clan.Name,
                        Color = clan.Color,
                        Icon = clan.Icon,
                        Tag = clan.Tag,
                        LeaderID = clan.LeaderID,
                        Description = clan.Description,
                        Bio = clan.Bio,
                        PlayersCount = clan.PlayersCount,
                        Pp = clan.Pp,
                        Rank = clan.Rank,
                        AverageRank = clan.AverageRank,
                        AverageAccuracy = clan.AverageAccuracy,
                        RankedPoolPercentCaptured = clan.RankedPoolPercentCaptured,
                        CaptureLeaderboardsCount = clan.CaptureLeaderboardsCount
                    })
                    .ToListAsync()
            };
        }
        
        [HttpGet("~/clan/{tag}")]
        [SwaggerOperation(Summary = "Retrieve details of a specific clan by tag", Description = "Fetches details of a specific clan identified by its tag.")]
        [SwaggerResponse(200, "Clan details retrieved successfully", typeof(ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>))]
        [SwaggerResponse(404, "Clan not found")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>>> GetClan(
            [SwaggerParameter("Tag of the clan to retrieve details for")] string tag,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort players by, default is Pp")] PlayerSortBy sortBy = PlayerSortBy.Pp,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Whether to include only players for whom this clan is primary, default is false")] bool primary = false)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var clan = await ClanControllerHelper.CurrentClan(_context, null, tag, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await ClanControllerHelper.PopulateClan(_context, clan, page, count, sortBy, order, primary);
        }

        [HttpGet("~/clan/id/{id}")]
        [SwaggerOperation(Summary = "Retrieve details of a specific clan by ID", Description = "Fetches details of a specific clan identified by its ID.")]
        [SwaggerResponse(200, "Clan details retrieved successfully", typeof(ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>))]
        [SwaggerResponse(404, "Clan not found")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>>> GetClanById(
            [SwaggerParameter("ID of the clan to retrieve details for")] int id,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort players by, default is Pp")] PlayerSortBy sortBy = PlayerSortBy.Pp,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc,
            [FromQuery, SwaggerParameter("Whether to include only players for whom this clan is primary, default is false")] bool primary = false)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var clan = await ClanControllerHelper.CurrentClan(_context, id, null, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await ClanControllerHelper.PopulateClan(_context, clan, page, count, sortBy, order, primary);
        }

        [HttpGet("~/clan/{tag}/maps")]
        [SwaggerOperation(Summary = "Retrieve clan maps by tag", Description = "Fetches ranked maps(maps that can be captured on the global map) for where players of clan made scores identified by its tag, with optional sorting and filtering.")]
        [SwaggerResponse(200, "Clan maps retrieved successfully", typeof(ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>))]
        [SwaggerResponse(404, "Clan not found")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> GetClanWithMaps(
            [SwaggerParameter("Tag of the clan to retrieve maps for")] string tag,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of maps per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort maps by, default is Pp")] ClanMapsSortBy sortBy = ClanMapsSortBy.Pp,
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is General")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var clan = await ClanControllerHelper.CurrentClan(_context, null, tag, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await ClanControllerHelper.PopulateClanWithMaps(_context, clan, currentID, page, count, sortBy, leaderboardContext, order);
        }

        [HttpGet("~/clan/id/{id}/maps")]
        [SwaggerOperation(Summary = "Retrieve clan maps by ID", Description = "Fetches ranked maps(maps that can be captured on the global map) for where players of clan made scores identified by its ID, with optional sorting and filtering.")]
        [SwaggerResponse(200, "Clan maps retrieved successfully", typeof(ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>))]
        [SwaggerResponse(404, "Clan not found")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> GetClanWithMapsById(
            [SwaggerParameter("ID of the clan to retrieve maps for")] int id,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of maps per page, default is 10")] int count = 10,
            [FromQuery, SwaggerParameter("Field to sort maps by, default is Pp")] ClanMapsSortBy sortBy = ClanMapsSortBy.Pp,
            [FromQuery, SwaggerParameter("Context of the leaderboard, default is General")] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery, SwaggerParameter("Order of sorting, default is Desc")] Order order = Order.Desc)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var clan = await ClanControllerHelper.CurrentClan(_context, id, null, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await ClanControllerHelper.PopulateClanWithMaps(_context, clan, currentID, page, count, sortBy, leaderboardContext, order);
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

        // Returns link to ClanGlobalMap
        [HttpGet("~/clans/globalmap")]
        [SwaggerOperation(Summary = "Retrieve the global clan map", Description = "Fetches a global map showing clan captured maps and rankings.")]
        [SwaggerResponse(200, "Global map retrieved successfully", typeof(ClanGlobalMap))]
        [SwaggerResponse(404, "Global map not found")]
        public async Task<ActionResult> GlobalMap() {
            var mapUrl = await _s3Client.GetPresignedUrl("global-map-file.json", S3Container.assets);
            if (mapUrl == null) {
                return NotFound();
            }
            return Redirect(mapUrl);
        }
    }
}
