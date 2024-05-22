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

        private async Task<Clan?> CurrentClan(string tag, string? currentID) {
            if (tag == "my")
            {
                var player = currentID != null ? await _context.Players.FindAsync(currentID) : null;

                if (player == null)
                {
                    return null;
                }
                return await _context
                    .Clans
                    .Where(c => c.LeaderID == currentID)
                    .Include(c => c.FeaturedPlaylists)
                    .FirstOrDefaultAsync();
            }
            else
            {
                return await _context
                    .Clans
                    .Where(c => c.Tag == tag)
                    .Include(c => c.FeaturedPlaylists)
                    .FirstOrDefaultAsync();
            }
        }

        [NonAction]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>>> PopulateClan(
            Clan clan,
            string? currentID,
            int page = 1,
            int count = 10,
            PlayerSortBy sortBy = PlayerSortBy.Pp,
            Order order = Order.Desc,
            bool primary = false)
        {
            IQueryable<Player> players = _context
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings)
                .Include(p => p.Socials);
            if (primary) {
                players = players.Where(p => !p.Banned && p.Clans.OrderBy(c => p.ClanOrder.IndexOf(c.Tag))
                        .ThenBy(c => c.Id)
                        .Take(1).Contains(clan));
            } else {
                players = players.Where(p => !p.Banned && p.Clans.Contains(clan));
            }
            switch (sortBy)
            {
                case PlayerSortBy.Pp:
                    players = players.Order(order, t => t.Pp);
                    break;
                case PlayerSortBy.Acc:
                    players = players.Order(order, t => t.ScoreStats.AverageRankedAccuracy);
                    break;
                case PlayerSortBy.Rank:
                    players = players.Order(order, t => t.Rank);
                    break;
                
            }
            return new ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>
            {
                Container = new ClanResponseFull {
                    Id = clan.Id,
                    Name = clan.Name,
                    Color = clan.Color,
                    Icon = clan.Icon,
                    Tag = clan.Tag,
                    LeaderID = clan.LeaderID,
                    Description = clan.Description,
                    Bio = clan.Bio,
                    RichBioTimeset = clan.RichBioTimeset,
                    DiscordInvite = clan.DiscordInvite,
                    PlayersCount = clan.PlayersCount,
                    Pp = clan.Pp,
                    Rank = clan.Rank,
                    AverageRank = clan.AverageRank,
                    AverageAccuracy = clan.AverageAccuracy,
                    RankedPoolPercentCaptured = clan.RankedPoolPercentCaptured,
                    CaptureLeaderboardsCount = clan.CaptureLeaderboardsCount,
                    FeaturedPlaylists = clan.FeaturedPlaylists.Select(fp => new FeaturedPlaylistResponse {
                        Id = fp.Id,
                        PlaylistLink = fp.PlaylistLink,
                        Cover = fp.Cover,
                        Title = fp.Title,
                        Description = fp.Description,

                        Owner = fp.Owner,
                        OwnerCover = fp.OwnerCover,
                        OwnerLink = fp.OwnerLink,
                    }).ToList(),
                    ClanRankingDiscordHook = currentID == clan.LeaderID ? clan.ClanRankingDiscordHook : null,
                    PlayerChangesCallback = currentID == clan.LeaderID ? clan.PlayerChangesCallback : null
                },
                Data = (await players
                    .TagWithCallSite()
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(p => new PlayerResponse
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Alias = p.Alias,
                        Platform = p.Platform,
                        Avatar = p.Avatar,
                        Country = p.Country,

                        Bot = p.Bot,
                        Pp = p.Pp,
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        Role = p.Role,
                        ProfileSettings = p.ProfileSettings,
                        Socials = p.Socials,
                        ClanOrder = p.ClanOrder.Length > 0 ? p.ClanOrder : string.Join(",", p.Clans.OrderBy(c => c.Id).Select(c => c.Tag)) 
                    })
                    .ToListAsync())
                    .Select(p => PostProcessSettings(p, false)),
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await players.CountAsync()
                }
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
            Clan? clan = await CurrentClan(tag, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await PopulateClan(clan, currentID, page, count, sortBy, order, primary);
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
            Clan? clan = await _context
                    .Clans
                    .Include(c => c.FeaturedPlaylists)
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .FirstOrDefaultAsync();
            if (clan == null)
            {
                return NotFound();
            }

            return await PopulateClan(clan, currentID, page, count, sortBy, order, primary);
        }

        [NonAction]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> PopulateClanWithMaps(
            Clan clan,
            string? currentID,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] ClanMapsSortBy sortBy = ClanMapsSortBy.Pp,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] Order order = Order.Desc)
        {
            var rankings = _context
                .ClanRanking
                .AsNoTracking()
                .Include(p => p.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(p => p.Leaderboard)
                .ThenInclude(l => l.Song)
                .ThenInclude(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .Where(p => p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked && p.ClanId == clan.Id);

            switch (sortBy)
            {
                case ClanMapsSortBy.Pp:
                    rankings = rankings.Order(order, t => t.Pp);
                    break;
                case ClanMapsSortBy.Acc:
                    rankings = rankings.Order(order, t => t.AverageAccuracy);
                    break;
                case ClanMapsSortBy.Rank:
                    rankings = rankings.Order(order, t => t.Rank);
                    break;
                case ClanMapsSortBy.Date:
                    rankings = rankings.Order(order, t => t.LastUpdateTime);
                    break;
                case ClanMapsSortBy.Tohold:
                    rankings = rankings
                        .Where(cr => cr.Rank == 1 && cr.Leaderboard.ClanRanking.Count > 1)
                        .Order(
                            order.Reverse(), 
                            t => t.Pp - t
                                    .Leaderboard
                                    .ClanRanking
                                    .Where(cr => cr.ClanId != clan.Id && cr.Rank == 2)
                                    .Select(cr => cr.Pp)
                                    .First());
                    break;
                case ClanMapsSortBy.Toconquer:
                    rankings = rankings
                        .Where(cr => cr.Rank != 1 || cr.Leaderboard.ClanRankingContested)
                        .Order(
                            order, 
                            t => t.Pp - t
                                    .Leaderboard
                                    .ClanRanking
                                    .Where(cr => cr.ClanId != clan.Id && cr.Rank == 1)
                                    .Select(cr => cr.Pp)
                                    .First());
                    break;
                default:
                    break;
            }

            var rankingList = await rankings
            
            .Skip((page - 1) * count)
            .Take(count)
            .Select(cr => new ClanRankingResponse {
                Id = cr.Id,
                Clan = cr.Clan,
                LastUpdateTime = cr.LastUpdateTime,
                AverageRank = cr.AverageRank,
                Pp = cr.Pp,
                AverageAccuracy = cr.AverageAccuracy,
                TotalScore = cr.TotalScore,
                LeaderboardId = cr.LeaderboardId,
                Leaderboard = cr.Leaderboard,
                Rank = cr.Rank,
                MyScore = currentID == null ? null : cr.Leaderboard.Scores.Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(leaderboardContext) && !s.Banned).Select(s => new ScoreResponseWithAcc {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
                    BonusPp = s.BonusPp,
                    Rank = s.Rank,
                    Replay = s.Replay,
                    Offsets = s.ReplayOffsets,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    Pauses = s.Pauses,
                    FullCombo = s.FullCombo,
                    Hmd = s.Hmd,
                    Timeset = s.Timeset,
                    Timepost = s.Timepost,
                    ReplaysWatched = s.AuthorizedReplayWatched + s.AnonimusReplayWatched,
                    LeaderboardId = s.LeaderboardId,
                    Platform = s.Platform,
                    Weight = s.Weight,
                    AccLeft = s.AccLeft,
                    AccRight = s.AccRight,
                    MaxStreak = s.MaxStreak,
                }).FirstOrDefault(),
            })
            .TagWithCallSite()
            .AsSplitQuery()
            .ToListAsync();

            if (sortBy == ClanMapsSortBy.Tohold || sortBy == ClanMapsSortBy.Toconquer) {
                var pps = await rankings
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(t => new { t.LeaderboardId, Pp = t.Pp, SecondPp = t
                        .Leaderboard
                        .ClanRanking
                        .Where(cr => cr.ClanId != clan.Id && (cr.Rank == (sortBy == ClanMapsSortBy.Tohold ? 2 : 1)))
                        .Select(cr => cr.Pp)
                        .FirstOrDefault()
                    })
                    .TagWithCallSite()
                    .AsSplitQuery()
                    .ToListAsync();

                foreach (var item in pps)
                {
                    rankingList.First(cr => cr.LeaderboardId == item.LeaderboardId).Pp = item.Pp - item.SecondPp;
                }
            }

            return new ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>
            {
                Container = new ClanResponseFull {
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
                    CaptureLeaderboardsCount = clan.CaptureLeaderboardsCount,
                    ClanRankingDiscordHook = currentID == clan.LeaderID ? clan.ClanRankingDiscordHook : null,
                    PlayerChangesCallback = currentID == clan.LeaderID ? clan.PlayerChangesCallback : null
                },
                Data = rankingList,
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await rankings.CountAsync()
                }
            };
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
            Clan? clan = await CurrentClan(tag, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await PopulateClanWithMaps(clan, currentID, page, count, sortBy, leaderboardContext, order);
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
            Clan? clan = await _context
                    .Clans
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .FirstOrDefaultAsync();
            if (clan == null)
            {
                return NotFound();
            }

            return await PopulateClanWithMaps(clan, currentID, page, count, sortBy, leaderboardContext, order);
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
