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
using SixLabors.ImageSharp;

namespace BeatLeader_Server.Controllers
{
    public class ClanController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        CurrentUserController _userController;
        ScreenshotController _screenshotController;
        IWebHostEnvironment _environment;

        public ClanController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            ScreenshotController screenshotController,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _screenshotController = screenshotController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/clans/")]
        public async Task<ActionResult<ResponseWithMetadata<ClanResponseFull>>> GetAll(
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
                .AsNoTracking()
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
            string sortBy = "pp",
            Order order = Order.Desc,
            bool primary = false,
            string? search = null,
            string? capturedLeaderboards = null)
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
                    RichBio = clan.RichBio,
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
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>>> GetClan(
            string tag,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "pp",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] bool primary = false,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            Clan? clan = await CurrentClan(tag, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await PopulateClan(clan, currentID, page, count, sortBy, order, primary, search, capturedLeaderboards);
        }

        [HttpGet("~/clan/id/{id}")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>>> GetClanById(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "pp",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] bool primary = false,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
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

            return await PopulateClan(clan, currentID, page, count, sortBy, order, primary, search, capturedLeaderboards);
        }

        [NonAction]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> PopulateClanWithMaps(
            Clan clan,
            string? currentID,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "pp",
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
        {
            var rankings = _context
                .ClanRanking
                .AsNoTracking()
                .Include(p => p.Leaderboard)
                .ThenInclude(l => l.Song)
                .ThenInclude(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .Where(p => p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked && p.ClanId == clan.Id);

            switch (sortBy)
            {
                case "pp":
                    rankings = rankings.Order(order, t => t.Pp);
                    break;
                case "acc":
                    rankings = rankings.Order(order, t => t.AverageAccuracy);
                    break;
                case "rank":
                    rankings = rankings.Order(order, t => t.Rank);
                    break;
                case "date":
                    rankings = rankings.Order(order, t => t.LastUpdateTime);
                    break;
                case "tohold":
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
                case "toconquer":
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

            if (sortBy == "tohold" || sortBy == "toconquer") {
                var pps = await rankings
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(t => new { t.LeaderboardId, Pp = t.Pp, SecondPp = t
                        .Leaderboard
                        .ClanRanking
                        .Where(cr => cr.ClanId != clan.Id && (cr.Rank == (sortBy == "tohold" ? 2 : 1)))
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
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> GetClanWithMaps(
            string tag,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "pp",
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            Clan? clan = await CurrentClan(tag, currentID);
            if (clan == null)
            {
                return NotFound();
            }

            return await PopulateClanWithMaps(clan, currentID, page, count, sortBy, leaderboardContext, order, search, capturedLeaderboards);
        }

        [HttpGet("~/clan/id/{id}/maps")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> GetClanWithMapsById(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "pp",
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
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

            return await PopulateClanWithMaps(clan, currentID, page, count, sortBy, leaderboardContext, order, search, capturedLeaderboards);
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
        public async Task<ActionResult<byte[]>> RefreshGlobalMap() {
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

            var file = await _screenshotController.DownloadFileContent("general", "clansmap/save", new Dictionary<string, string> { });
            await _s3Client.UploadAsset("clansmap-globalcache.json", file);

            return file;
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
