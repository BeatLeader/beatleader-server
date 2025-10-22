using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Services.SearchService;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class EventsController : Controller
    {
        private readonly AppContext _context;
        private readonly ScoreRefreshController _scoreRefreshController;

        private readonly IConfiguration _configuration;
        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public EventsController(
            AppContext context,
            ScoreRefreshController scoreRefreshController,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;

            _scoreRefreshController = scoreRefreshController;
            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/events")]
        public async Task<ActionResult<ResponseWithMetadata<EventResponse>>> GetEvents(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string? sortBy = "date",
            [FromQuery] string? search = null,
            [FromQuery] Order order = Order.Desc)
        {
            IQueryable<EventRanking> query = _context.EventRankings.Where(e => e.Id != 75).Include(e => e.Players);

            switch (sortBy)
            {
                case "date":
                    query = query.Order(order, p => p.EndDate);
                    break;
                case "name":
                    query = query.Order(order, t => t.Name);
                    break;
                default:
                    break;
            }

            if (search != null)
            {
                string lowSearch = search.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(lowSearch));
            }

            var result = new ResponseWithMetadata<EventResponse>
            {
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await query.CountAsync()
                }
            };

            int[] undownloadable = [48, 46, 32, 63];
            result.Data = await query
            .TagWithCaller()
            .AsNoTracking()
            .Select(e => new EventResponse {
                Id = e.Id,
                Name = e.Name,
                EndDate = e.EndDate,
                PlaylistId = e.PlaylistId,
                Image = e.Image,
                Downloadable = !undownloadable.Contains(e.Id), 
                Description = e.Description,
                EventType = e.EventType,
                MainColor = e.MainColor,
                SecondaryColor = e.SecondaryColor,
                PageAlias = e.PageAlias,
                AnimatedImage = e.AnimatedImage,

                PlayerCount = e.Players.Count(),
                Leader = new PlayerResponse {
                    Id = e.Players.OrderByDescending(p => p.Pp).FirstOrDefault().PlayerId
                }
            })
            .Skip((page - 1) * count)
            .Take(count)
            .ToListAsync();

            foreach (var item in result.Data)
            {
                item.Leader = ResponseFromPlayer(await _context.Players.Include(p => p.Clans).FirstOrDefaultAsync(p => p.Id == item.Leader.Id));
            }

            return result;
        }

        [HttpGet("~/mod/events")]
        public async Task<ActionResult<ResponseWithMetadata<EventResponse>>> GetModEvents(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string? sortBy = "date",
            [FromQuery] string? search = null,
            [FromQuery] Order order = Order.Desc)
        {
            IQueryable<EventRanking> query = _context.EventRankings.Where(e => e.Id != 75).Include(e => e.Players);

            switch (sortBy)
            {
                case "date":
                    query = query.Order(order, p => p.EndDate);
                    break;
                case "name":
                    query = query.Order(order, t => t.Name);
                    break;
                default:
                    break;
            }

            if (search != null)
            {
                string lowSearch = search.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(lowSearch));
            }

            var result = new ResponseWithMetadata<EventResponse>
            {
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await query.CountAsync()
                }
            };

            int[] undownloadable = [48, 46, 32, 63];
            result.Data = await query
            .TagWithCaller()
            .AsNoTracking()
            .Select(e => new EventResponse {
                Id = e.Id,
                Name = e.Name,
                EndDate = e.EndDate,
                PlaylistId = e.PlaylistId,
                Image = e.Image,
                Downloadable = !undownloadable.Contains(e.Id), 
                Description = e.Description,
                EventType = e.EventType,
                MainColor = e.MainColor,
                SecondaryColor = e.SecondaryColor
            })
            .Skip((page - 1) * count)
            .Take(count)
            .ToListAsync();

            return result;
        }

        [HttpGet("~/event/{id}")]
        public async Task<ActionResult<EventRanking?>> GetEvent(string id) {
            if (int.TryParse(id, out var intId)) {
                return await _context.EventRankings.FirstOrDefaultAsync(e => e.Id == intId);
            } else {
                return await _context.EventRankings.Where(e => e.PageAlias == id).FirstOrDefaultAsync();
            }
        }

        [HttpGet("~/event/{id}/players")]
        public async Task<ActionResult<ResponseWithMetadata<PlayerResponseWithStats>>> GetEventPlayers(
            int id,
            [FromQuery] string sortBy = "pp", 
            [FromQuery] int page = 1, 
            [FromQuery] int count = 50, 
            [FromQuery] string search = "",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string countries = ""
            )
        {
            IQueryable<EventPlayer> request = _context
                .EventPlayer
                .Where(p => p.EventRankingId == id);
            
            if (countries.Length != 0)
            {
                var player = Expression.Parameter(typeof(EventPlayer), "p");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in countries.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(player, "Country"), Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<EventPlayer, bool>>)Expression.Lambda(exp, player));
            }

            if (search.Length != 0)
            {
                var player = Expression.Parameter(typeof(EventPlayer), "p");

                var contains = "".GetType().GetMethod("Contains", new[] { typeof(string) });

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in search.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Call(Expression.Property(player, "PlayerName"), contains, Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<EventPlayer, bool>>)Expression.Lambda(exp, player));
            }

            var eventPlayers = request.OrderByDescending(ep => ep.Pp).Skip((page - 1) * count).Take(count).ToList();
            var ids = eventPlayers.Select(ep => ep.PlayerId).ToList();

            var players = await _context
                .Players
                .Where(p => ids.Contains(p.Id))
                .Select(p => new PlayerResponseWithStats {
                    Id = p.Id,
                    Name = p.Name,
                    Alias = p.Alias,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,
                    ScoreStats = p.ScoreStats,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    LastWeekPp = p.LastWeekPp,
                    LastWeekRank = p.LastWeekRank,
                    LastWeekCountryRank = p.LastWeekCountryRank,
                    Role = p.Role,
                    PatreonFeatures = p.PatreonFeatures,
                    ProfileSettings = p.ProfileSettings,
                    ClanOrder = p.ClanOrder,
                    Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                }).ToListAsync();

            foreach (var resultPlayer in players)
            {
                var eventPlayer = eventPlayers.First(p => p.PlayerId == resultPlayer.Id);

                resultPlayer.Rank = eventPlayer.Rank;
                resultPlayer.Pp = eventPlayer.Pp;
                resultPlayer.CountryRank = eventPlayer.CountryRank;
                PostProcessSettings(resultPlayer, false);
            }

            return new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await request.CountAsync()
                },
                Data = players.OrderByDescending(ep => ep.Pp)
            };
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/event/{id}/refresh")]
        public async Task<ActionResult> RefreshEvent(int id)
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

            var eventRanking = await _context.EventRankings
                .Where(e => e.Id == id)
                .Include(e => e.Leaderboards)
                .ThenInclude(lb => lb.Scores)
                .ThenInclude(s => s.Player)
                .ThenInclude(pl => pl.EventsParticipating).FirstOrDefaultAsync();

            List<Player> players = new List<Player>();

            foreach (var lb in eventRanking.Leaderboards)
            {
                foreach (var score in lb.Scores)
                {
                    if (!players.Contains(score.Player)) {
                        players.Add(score.Player);
                    }
                }
            }

            foreach (var player in players)
            {
                await _context.RecalculateEventsPP(player, eventRanking.Leaderboards.First());
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/start/{id}")]
        public async Task<ActionResult> StartEvent(
               int id, 
               [FromQuery] string name,
               [FromQuery] string? description,
               [FromQuery] int endDate)
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

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist(id + ".bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                }
            }

            if (playlist == null)
            {
                return BadRequest("Can't find such plist");
            }

            string fileName = id + "-event";
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

            var leaderboards = new List<Leaderboard>();
            var players = new List<EventPlayer>();
            var basicPlayers = new List<Player>();
            var playerScores = new Dictionary<string, List<Score>>();

            foreach (var song in playlist.songs) {
                foreach (var diff in song.difficulties)
                {
                    string hash = song.hash.ToLower();
                    string diffName = diff.name.ToLower();
                    string characteristic = diff.characteristic.ToLower();

                    var lb = await _context.Leaderboards.Where(lb => 
                        lb.Song.Hash.ToLower() == hash && 
                        lb.Difficulty.DifficultyName.ToLower() == diffName &&
                        lb.Difficulty.ModeName.ToLower() == characteristic)
                        .Include(lb => lb.Difficulty)
                        .Include(lb => lb.Scores)
                        .ThenInclude(s => s.Player)
                        .Include(lb => lb.Song)
                        .FirstOrDefaultAsync();

                    if (lb != null && lb.Difficulty.Status != DifficultyStatus.outdated) {
                        if (lb.Difficulty.Stars == null) {
                            await RatingUtils.UpdateFromExMachina(lb, null);
                        }
                        if (lb.Difficulty.Stars != null) {

                            if (lb.Difficulty.Status == DifficultyStatus.unranked) {
                                lb.Difficulty.Status = DifficultyStatus.inevent;
                                await _context.SaveChangesAsync();

                                await _scoreRefreshController.RefreshScores(lb.Id);
                            }
                            leaderboards.Add(lb);
                        } else { continue; }

                        leaderboards.Add(lb);
                        
                        foreach (var score in lb.Scores)
                        {
                            if (score.Player.Banned) continue;
                            if (players.FirstOrDefault(p => p.PlayerId == score.PlayerId) == null) {
                                players.Add(new EventPlayer {
                                    PlayerId = score.PlayerId,
                                    Country = score.Player.Country,
                                    EventName = name,
                                    PlayerName = score.Player.Name
                                });
                                basicPlayers.Add(score.Player);
                                playerScores[score.PlayerId] = new List<Score> { score };
                            } else {
                                playerScores[score.PlayerId].Add(score);
                            }
                        }
                    }
                }
            }

            foreach (var player in players) {
                float resultPP = 0f;
                foreach ((int i, Score s) in playerScores[player.PlayerId].OrderByDescending(s => s.Pp).Select((value, i) => (i, value)))
                {

                    resultPP += s.Pp * MathF.Pow(0.925f, i);
                }

                player.Pp = resultPP;
            }

            Dictionary<string, int> countries = new Dictionary<string, int>();
            
            int ii = 0;
            foreach (EventPlayer p in players.OrderByDescending(s => s.Pp))
            {
                if (p.Rank != 0) {
                    p.Rank = p.Rank;
                }
                p.Rank = ii + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;
                ii++;
            }

            var eventRanking = new EventRanking {
                Name = name,
                Leaderboards = leaderboards,
                Players = players,
                EndDate = endDate,
                PlaylistId = id,
                Description = description,
                Image = imageUrl ?? ""
            };

            _context.EventRankings.Add(eventRanking);

            await _context.SaveChangesAsync();

            foreach (var player in players) {
                var basicPlayer = basicPlayers.FirstOrDefault(p => p.Id == player.PlayerId);
                if (basicPlayer != null) {
                    if (basicPlayer.EventsParticipating == null) {
                        basicPlayer.EventsParticipating = new List<EventPlayer>();
                    }
                    basicPlayer.EventsParticipating.Add(player);
                }
                player.EventRankingId = eventRanking.Id;
                player.EventName = eventRanking.Name;
                player.PlayerName = basicPlayer.Name;
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/start/noplaylist")]
        public async Task<ActionResult> StartEventWithoutPlaylist(
               [FromQuery] string name,
               [FromQuery] string? description,
               [FromQuery] int? playlistId,
               [FromQuery] int endDate,
               [FromQuery] EventRankingType eventType)
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

            var lastEvent = _context.EventRankings.OrderByDescending(e => e.Id).FirstOrDefault();

            string fileName = ((lastEvent?.Id ?? 0) + 1) + "-event-cover";
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

            var eventRanking = new EventRanking {
                Name = name,
                EndDate = endDate,
                Description = description,
                Image = imageUrl ?? "",
                EventType = eventType,
                PlaylistId = playlistId ?? 0
            };

            _context.EventRankings.Add(eventRanking);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/add/map")]
        public async Task<ActionResult> AddMapOTD(
               [FromQuery] int id,
               [FromQuery] string songId,
               [FromQuery] string leaderboardId,
               [FromQuery] int startDate,
               [FromQuery] int endDate)
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

            var eventDescription = await _context.EventRankings.Where(e => e.Id == id).Include(e => e.MapOfTheDays).ThenInclude(m => m.Leaderboards).FirstOrDefaultAsync();
            if (eventDescription == null || eventDescription.EventType != EventRankingType.MapOfTheDay) return NotFound();

            var mapOfTheDay = eventDescription.MapOfTheDays.Where(s => s.SongId == songId).FirstOrDefault();
            var leaderboard = await _context.Leaderboards.Where(l => l.Id == leaderboardId).FirstOrDefaultAsync();
            if (leaderboard == null) return NotFound();

            if (mapOfTheDay != null) {
                mapOfTheDay.Timestart = startDate;
                mapOfTheDay.Timeend = endDate;

                if (!mapOfTheDay.Leaderboards.Any(l => l.Id == leaderboardId)) {
                    mapOfTheDay.Leaderboards.Add(leaderboard);
                }
            } else {
                mapOfTheDay = new MapOfTheDay {
                    Timestart = startDate,
                    Timeend = endDate,
                    SongId = songId,
                    Leaderboards = new List<Leaderboard> {
                        leaderboard
                    }
                };
                eventDescription.MapOfTheDays.Add(mapOfTheDay);
            }

            await _context.BulkSaveChangesAsync();

            return Ok(mapOfTheDay);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/add/leaderboard")]
        public async Task<ActionResult> AddLeaderboard(
               [FromQuery] int id,
               [FromQuery] string leaderboardId)
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

            var eventDescription = await _context.EventRankings.Where(e => e.Id == id).Include(e => e.Leaderboards).FirstOrDefaultAsync();
            if (eventDescription == null || eventDescription.EventType == EventRankingType.MapOfTheDay) return NotFound();

            var leaderboard = await _context.Leaderboards.Where(l => l.Id == leaderboardId).Include(lb => lb.Difficulty).FirstOrDefaultAsync();
            if (leaderboard == null) return NotFound();

            if (eventDescription.Leaderboards.Any(l => l.Id == leaderboardId)) return BadRequest("Already included");

            if (leaderboard.Difficulty.Stars == null) {
                await RatingUtils.UpdateFromExMachina(leaderboard, null);
            }
            if (leaderboard.Difficulty.Stars != null) {

                if (leaderboard.Difficulty.Status == DifficultyStatus.unranked) {
                    leaderboard.Difficulty.Status = DifficultyStatus.inevent;
                    await _context.SaveChangesAsync();

                    await _scoreRefreshController.RefreshScores(leaderboard.Id);
                }
                eventDescription.Leaderboards.Add(leaderboard);
            }

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpDelete("~/event/remove/leaderboard")]
        public async Task<ActionResult> RemoveLeaderboard(
               [FromQuery] int id,
               [FromQuery] string leaderboardId)
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

            var eventDescription = await _context.EventRankings.Where(e => e.Id == id).Include(e => e.Leaderboards).FirstOrDefaultAsync();
            if (eventDescription == null || eventDescription.EventType == EventRankingType.MapOfTheDay) return NotFound();

            var leaderboard = await _context.Leaderboards.Where(l => l.Id == leaderboardId).Include(lb => lb.Difficulty).FirstOrDefaultAsync();
            if (leaderboard == null) return NotFound();

            if (!eventDescription.Leaderboards.Any(l => l.Id == leaderboardId)) return NotFound("Not in event");

            if (leaderboard.Difficulty.Status == DifficultyStatus.inevent) {
                leaderboard.Difficulty.Status = DifficultyStatus.unranked;
                await _context.SaveChangesAsync();

                await _scoreRefreshController.RefreshScores(leaderboard.Id);
            }
            eventDescription.Leaderboards.Remove(leaderboard);

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpDelete("~/event/remove/map")]
        public async Task<ActionResult> RemoveMapOTD(
               [FromQuery] int id,
               [FromQuery] string songId)
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

            var eventDescription = await _context.EventRankings.Where(e => e.Id == id).Include(e => e.MapOfTheDays).ThenInclude(m => m.Leaderboards).FirstOrDefaultAsync();
            if (eventDescription == null || eventDescription.EventType != EventRankingType.MapOfTheDay) return NotFound();

            var mapOfTheDay = eventDescription.MapOfTheDays.Where(s => s.SongId == songId).FirstOrDefault();
            eventDescription.MapOfTheDays.Remove(mapOfTheDay);
            mapOfTheDay.Leaderboards = new List<Leaderboard>();
            await _context.BulkSaveChangesAsync();
            _context.MapOfTheDay.Remove(mapOfTheDay);
            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/schedule/map")]
        public async Task<ActionResult> ScheduleMapOTD(
               [FromQuery] int id,
               [FromQuery] string songId,
               [FromQuery] int startDate,
               [FromQuery] int endDate,
               [FromQuery] string? videoUrl)
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

            var existingSchedule = await _context.ScheduledEventMaps.Where(em => em.SongId == songId).FirstOrDefaultAsync();
            if (existingSchedule != null) {
                _context.ScheduledEventMaps.Remove(existingSchedule);
            }

            _context.ScheduledEventMaps.Add(new ScheduledEventMap {
                EventId = id,
                SongId = songId,
                StartDate = startDate,
                EndDate = endDate,
                VideoUrl = videoUrl
            });

            await _context.BulkSaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpDelete("~/event/{id}")]
        public async Task<ActionResult> DeleteEvent(int id)
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

            var eventRanking = await _context.EventRankings
                .Where(e => e.Id == id).Include(e => e.Players).FirstOrDefaultAsync();

            foreach (var p in eventRanking.Players)
            {
                eventRanking.Players.Remove(p);
            }

            _context.EventRankings.Remove(eventRanking);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/{id}/description")]
        public async Task<ActionResult<EventRanking?>> UpdateDescription(int id, [FromQuery] string value) {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var eventRanking = await _context.EventRankings.FirstOrDefaultAsync(e => e.Id == id);
            eventRanking.Description = value;
            _context.SaveChanges();
            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/{id}/colors")]
        public async Task<ActionResult<EventRanking?>> UpdateColors(int id, [FromQuery] string mainColor, [FromQuery] string secondaryColor) {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var eventRanking = await _context.EventRankings.FirstOrDefaultAsync(e => e.Id == id);
            eventRanking.MainColor = mainColor;
            eventRanking.SecondaryColor = secondaryColor;
            _context.SaveChanges();
            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/{id}/alias")]
        public async Task<ActionResult<EventRanking?>> UpdateAlias(int id, [FromQuery] string newValue) {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var eventRanking = await _context.EventRankings.FirstOrDefaultAsync(e => e.Id == id);
            eventRanking.PageAlias = newValue;
            _context.SaveChanges();
            return Ok();
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("~/event/project-tree/champion")]
        public async Task<ActionResult> ProjectTreeChampion([FromQuery] string playerId, [FromQuery] int diff, [FromQuery] int day) {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var player = _context.Players.Where(p => p.Id == playerId).Include(p => p.EventsParticipating).FirstOrDefault();
            if (player == null) {
                return NotFound();
            }

            if (player.EventsParticipating == null) {
                player.EventsParticipating = new List<EventPlayer>();
            }

            var currentEvent = player.EventsParticipating.FirstOrDefault(ep => ep.EventRankingId == 62);
            if (currentEvent == null) {
                currentEvent = new EventPlayer {
                    PlayerId = player.Id,
                    Country = player.Country,
                    EventName = "Project Tree",
                    PlayerName = player.Name,
                    EventRankingId = 62
                };
                player.EventsParticipating.Add(currentEvent);
            }

            var champion = _context.TreeChampions.FirstOrDefault(c => c.Day == day && c.PlayerId == player.Id);

            if (champion == null) {
                currentEvent.Pp += 1f;
            }

            _context.TreeChampions.Add(new TreeChampion {
                Day = day,
                PlayerId = player.Id,
                Diffs = diff
            });

            _context.SaveChanges();

            var champions = _context.TreeChampions.ToList();

            var eps = _context.EventPlayer.Where(e => e.EventRankingId == 62).ToList();
            Dictionary<string, int> countries = new Dictionary<string, int>();
            
            int ii = 0;
            foreach (EventPlayer p in eps.OrderByDescending(s => (int)s.Pp).ThenByDescending(s => champions.Where(c => c.PlayerId == s.PlayerId).Sum(c => c.Diffs)))
            {
                if (p.Rank != 0) {
                    p.Rank = p.Rank;
                }
                p.Rank = ii + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;
                ii++;
            }

            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/event/motd/{id}/status")]
        public async Task<ActionResult> GetMotdStatus(int id) {

            var currentId = HttpContext.CurrentUserID(_context);

            var now = Time.UnixNow();
            var previousDays = new List<MapOTDDayStatus>();
            var result = new MapOTDEventStatus {
            };

            int[] undownloadable = [48, 46, 32, 63];
            var eventDescription = await _context.EventRankings.Where(e => e.Id == id)
                .Select(e => new {
                    EventDescription = new EventResponse {
                        Id = e.Id,
                        Name = e.Name,
                        EndDate = e.EndDate,
                        PlaylistId = e.PlaylistId,
                        Image = e.Image,
                        Downloadable = !undownloadable.Contains(e.Id), 
                        Description = e.Description,
                        EventType = e.EventType,
                        MainColor = e.MainColor,
                        SecondaryColor = e.SecondaryColor,
                    },
                    MapOfTheDays = e.MapOfTheDays.Where(m => m.Timestart < now).OrderBy(m => m.Timestart).Select(s => new { 
                        Song = new SongResponse {
                            Id = s.Song.Id,
                            Hash = s.Song.Hash,
                            Name = s.Song.Name,
                            SubName = s.Song.SubName,
                            Author = s.Song.Author,
                            Mapper = s.Song.Mapper,
                            MapperId  = s.Song.MapperId,
                            CoverImage   = s.Song.CoverImage,
                            FullCoverImage = s.Song.FullCoverImage,
                            DownloadUrl = s.Song.DownloadUrl,
                            Bpm = s.Song.Bpm,
                            Duration = s.Song.Duration,
                            UploadTime = s.Song.UploadTime,
                            Difficulties = s.Song.Difficulties,
                            VideoPreviewUrl = s.Song.VideoPreviewUrl
                        },
                        LeaderboardIds = s.Leaderboards.Select(l => l.Id).ToList(),
                        s.Timestart,
                        s.Timeend
                    }).ToList()
                }).FirstOrDefaultAsync();

            if (eventDescription == null) return NotFound();

            var eventPlayer = await _context
                .EventPlayer
                .Where(ep => ep.EventRankingId == id && ep.PlayerId == currentId)
                .Include(ep => ep.MapOfTheDayPoints)
                .ThenInclude(mp => mp.MapOfTheDay)
                .FirstOrDefaultAsync();

            for (int i = 0; i < eventDescription.MapOfTheDays.Count; i++) {
                var map = eventDescription.MapOfTheDays[i];

                var song = map.Song;

                var score = currentId == null ? null : (await _context.Scores.Where(s => s.ValidForGeneral && s.PlayerId == currentId && !s.Modifiers.Contains("NF") && map.LeaderboardIds.Contains(s.LeaderboardId)).Select(s => new ScoreResponseWithAcc {
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
                    ReplaysWatched = s.ReplayWatchedTotal,
                    LeaderboardId = s.LeaderboardId,
                    Platform = s.Platform,
                    Weight = s.Weight,
                    AccLeft = s.AccLeft,
                    AccRight = s.AccRight,
                    MaxStreak = s.MaxStreak,
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Platform = s.Player.Platform,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        Socials = s.Player.Socials,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player.Clans != null ? s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color }) : null
                    },
                }).FirstOrDefaultAsync());

                if (now > map.Timestart && now < map.Timeend) {
                    EarnedPoints? points = null;
                    
                    if (score != null) {
                        points = new EarnedPoints {
                            Rank = score.Rank
                        };

                        if (score.Rank == 1) {
                            points.Points = 10;
                        } else if (score.Rank == 2) {
                            points.Points = 5;
                        } else if (score.Rank == 3) {
                            points.Points = 3;
                        } else if (score.Rank <= 10) {
                            points.Points = 1;
                        }
                    }

                    result.today = new MapOTDDayStatus {
                        song = song,
                        score = score,
                        startTime = map.Timestart,
                        endTime = map.Timeend,
                        Points = points,
                        day = i + 1
                    };
                } else {
                    previousDays.Add(new MapOTDDayStatus {
                        song = song,
                        score = score,
                        startTime = map.Timestart,
                        endTime = map.Timeend,
                        day = i + 1,
                        Points = eventPlayer?
                            .MapOfTheDayPoints
                            .Where(m => m.MapOfTheDay.Timestart == map.Timestart)
                            .Select(m => new EarnedPoints {
                                Points = m.Points,
                                Rank = m.Rank
                            })
                            .FirstOrDefault()
                    });
                }
            }

            result.previousDays = previousDays.ToArray();
            result.EventDescription = eventDescription.EventDescription;

            return Ok(result);
        }

        [HttpGet("~/event/motd/{id}/status/today")]
        public async Task<ActionResult> GetMotdStatusToday(int id) {

            var currentId = HttpContext.CurrentUserID(_context);

            var now = Time.UnixNow();
            var previousDays = new List<MapOTDDayStatus>();
            var result = new MapOTDEventStatus {
            };

            int[] undownloadable = [48, 46, 32, 63];
            var eventDescription = await _context.EventRankings.Where(e => e.Id == id)
                .Select(e => new {
                    EventDescription = new EventResponse {
                        Id = e.Id,
                        Name = e.Name,
                        EndDate = e.EndDate,
                        PlaylistId = e.PlaylistId,
                        Image = e.Image,
                        Downloadable = !undownloadable.Contains(e.Id), 
                        Description = e.Description,
                        EventType = e.EventType,
                        MainColor = e.MainColor,
                        SecondaryColor = e.SecondaryColor,
                    },
                    MapOfTheDays = e.MapOfTheDays.Where(m => m.Timestart < now && m.Timeend > now).OrderBy(m => m.Timestart).Select(s => new { 
                        Song = new SongResponse {
                            Id = s.Song.Id,
                            Hash = s.Song.Hash,
                            Name = s.Song.Name,
                            SubName = s.Song.SubName,
                            Author = s.Song.Author,
                            Mapper = s.Song.Mapper,
                            MapperId  = s.Song.MapperId,
                            CoverImage   = s.Song.CoverImage,
                            FullCoverImage = s.Song.FullCoverImage,
                            DownloadUrl = s.Song.DownloadUrl,
                            Bpm = s.Song.Bpm,
                            Duration = s.Song.Duration,
                            UploadTime = s.Song.UploadTime,
                            Difficulties = s.Song.Difficulties,
                            VideoPreviewUrl = s.Song.VideoPreviewUrl
                        },
                        LeaderboardIds = s.Leaderboards.Select(l => l.Id).ToList(),
                        s.Timestart,
                        s.Timeend,
                        Index = e.MapOfTheDays.Count(m => m.Timestart < s.Timestart)
                    }).ToList()
                }).FirstOrDefaultAsync();

            if (eventDescription == null) return NotFound();

            var eventPlayer = await _context
                .EventPlayer
                .Where(ep => ep.EventRankingId == id && ep.PlayerId == currentId)
                .Include(ep => ep.MapOfTheDayPoints)
                .ThenInclude(mp => mp.MapOfTheDay)
                .FirstOrDefaultAsync();

            for (int i = 0; i < eventDescription.MapOfTheDays.Count; i++) {
                var map = eventDescription.MapOfTheDays[i];

                var song = map.Song;

                var score = currentId == null ? null : (await _context.Scores.Where(s => s.ValidForGeneral && s.PlayerId == currentId && !s.Modifiers.Contains("NF") && map.LeaderboardIds.Contains(s.LeaderboardId)).Select(s => new ScoreResponseWithAcc {
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
                    ReplaysWatched = s.ReplayWatchedTotal,
                    LeaderboardId = s.LeaderboardId,
                    Platform = s.Platform,
                    Weight = s.Weight,
                    AccLeft = s.AccLeft,
                    AccRight = s.AccRight,
                    MaxStreak = s.MaxStreak,
                    Player = new PlayerResponse
                    {
                        Id = s.Player.Id,
                        Name = s.Player.Name,
                        Alias = s.Player.Alias,
                        Platform = s.Player.Platform,
                        Avatar = s.Player.Avatar,
                        Country = s.Player.Country,

                        Pp = s.Player.Pp,
                        Rank = s.Player.Rank,
                        CountryRank = s.Player.CountryRank,
                        Role = s.Player.Role,
                        Socials = s.Player.Socials,
                        ProfileSettings = s.Player.ProfileSettings,
                        Clans = s.Player.Clans != null ? s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color }) : null
                    },
                }).FirstOrDefaultAsync());

                if (now > map.Timestart && now < map.Timeend) {
                    EarnedPoints? points = null;
                    
                    if (score != null) {
                        points = new EarnedPoints {
                            Rank = score.Rank
                        };

                        if (score.Rank == 1) {
                            points.Points = 10;
                        } else if (score.Rank == 2) {
                            points.Points = 5;
                        } else if (score.Rank == 3) {
                            points.Points = 3;
                        } else if (score.Rank <= 10) {
                            points.Points = 1;
                        }
                    }

                    result.today = new MapOTDDayStatus {
                        song = song,
                        score = score,
                        startTime = map.Timestart,
                        endTime = map.Timeend,
                        Points = points,
                        day = map.Index + 1
                    };
                }
            }
            result.EventDescription = eventDescription.EventDescription;

            return Ok(result);
        }

        [HttpGet("~/event/motd/{id}/players")]
        public async Task<ActionResult> GetMotdPlayers(int id, [FromQuery] int page = 1, [FromQuery] int count = 50) {
            var result = await _context
                .EventPlayer
                .Where(ep => ep.EventRankingId == id)
            .Select(p => new TreePlayer {
                Id = p.PlayerId,
                Rank = p.Rank,
                Name = p.PlayerName,
                Avatar = p.Player.Avatar,
                AvatarBorder = p.Player.ProfileSettings != null ? p.Player.ProfileSettings.EffectName : null,
                Hue = p.Player.ProfileSettings != null ? p.Player.ProfileSettings.Hue : null,
                Score = (int)p.Pp
            }).ToArrayAsync();

            var champions = (await _context.EventRankings.Where(e => e.Id == id).Include(er => er.MapOfTheDays).ThenInclude(m => m.Champions).ThenInclude(c => c.MapOfTheDayPoints).ThenInclude(m => m.MapOfTheDay).Select(e => e.MapOfTheDays.Select(m => new {
                MapOfTheDayId = m.Id,
                m.Timestart,
                m.Timeend,
                e.EndDate,
                Diffs = m.Leaderboards.Select(l => l.Difficulty.Value).ToList(),
                Champions = m.Champions.Select(c => new { c.PlayerId, c.MapOfTheDayPoints }).ToList()
            })).FirstOrDefaultAsync()).OrderBy(m => m.Timestart).Select((value, i) => (i, value));
            foreach (var item in result) {
                item.Days.AddRange(champions.Where(c => c.value.Champions.Any(cmp => cmp.PlayerId == item.Id)).Select(c => new TreePlayerDay {
                    Day = c.i + 1,
                    Points = c.value.Champions.Where(cmp => cmp.PlayerId == item.Id).FirstOrDefault()?.MapOfTheDayPoints.Where(mp => mp.MapOfTheDay.Id == c.value.MapOfTheDayId).FirstOrDefault(),
                    Diffs = c.value.Diffs.ToArray()
                }));
            }

            return Ok(result.OrderBy(c => c.Rank).Skip((page - 1) * count)
                .Take(count));
        }
    }
}
