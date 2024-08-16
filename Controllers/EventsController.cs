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
            IQueryable<EventRanking> query = _context.EventRankings.Include(e => e.Players);

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

            int[] undownloadable = [48, 46, 32];
            result.Data = await query.Select(e => new EventResponse {
                Id = e.Id,
                Name = e.Name,
                EndDate = e.EndDate,
                PlaylistId = e.PlaylistId,
                Image = e.Image,
                Downloadable = !undownloadable.Contains(e.Id), 

                PlayerCount = e.Players.Count(),
                Leader = new PlayerResponse {
                    Id = e.Players.OrderByDescending(p => p.Pp).FirstOrDefault().PlayerId
                }
            }).Skip((page - 1) * count).Take(count).ToListAsync();

            foreach (var item in result.Data)
            {
                item.Leader = ResponseFromPlayer(await _context.Players.Include(p => p.Clans).FirstOrDefaultAsync(p => p.Id == item.Leader.Id));
            }

            return result;
        }

        [HttpGet("~/event/{id}")]
        public async Task<ActionResult<EventRanking?>> GetEvent(int id) {
            return await _context.EventRankings.FirstOrDefaultAsync(e => e.Id == id);
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

                (string extension, MemoryStream stream2) = ImageUtils.GetFormatAndResize(ms);
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
                        .FirstOrDefaultAsync();

                    if (lb != null && lb.Difficulty.Status != DifficultyStatus.outdated) {
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
    }
}
