using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class MiniRankingController : Controller
    {
        private readonly AppContext _context;
        private readonly IConfiguration _configuration;

        public MiniRankingController(AppContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public class MiniRankingPlayer {
            public string Name { get; set; }
            public string Id { get; set; }
            public string Alias { get; set; }
            public string Country { get; set; }

            public int Rank { get; set; }
            public int CountryRank { get; set; }
            public float Pp { get; set; }
        }

        public class MiniRankingResponse {
            public List<MiniRankingPlayer> Global { get; set; }
            public List<MiniRankingPlayer> Country { get; set; }
            public List<MiniRankingPlayer>? Friends { get; set; }
        }

        [NonAction]
        public async Task<ActionResult<MiniRankingResponse>> GetMiniRankingsContext(
            [FromQuery] int rank, 
            [FromQuery] string country, 
            [FromQuery] int countryRank,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] bool friends = false)
        {

            var players = _context
                .PlayerContextExtensions
                .Include(ce => ce.PlayerInstance)
                .Where(p => 
                    !p.Banned && 
                    p.Context == leaderboardContext &&
                    (
                        (p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3) || 
                        (p.Rank <= rank + 1 && p.Rank >= rank - 3)
                    ))
                .Select(p => new MiniRankingPlayer { Id = p.PlayerId, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Alias = p.PlayerInstance.Alias, Pp = p.Pp });

            var result = new MiniRankingResponse()
            {
                Global = await players.Where(p => p.Rank <= rank + 1 && p.Rank >= rank - 3).OrderBy(s => s.Rank).ToListAsync(),
                Country = await players.Where(p => p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3).OrderBy(s => s.CountryRank).ToListAsync()
            };

            if (friends) {
                string? currentID = HttpContext.CurrentUserID(_context);
                if (currentID != null) {
                    var friendsList = (await _context
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => new { friends = f.Friends.Select(p => p.Id) })
                        .FirstOrDefaultAsync())
                        ?.friends.ToList();
                    if (friendsList != null) {
                        friendsList.Add(currentID);
                    } else {
                        friendsList = new List<string> { currentID };
                    }

                    var friendsPlayers = await _context
                        .PlayerContextExtensions
                        .Include(ce => ce.PlayerInstance)
                        .Where(p => 
                            !p.Banned && 
                            p.Context == leaderboardContext &&
                            friendsList.Contains(p.PlayerId))
                        .Select(p => new MiniRankingPlayer { Id = p.PlayerId, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Alias = p.PlayerInstance.Alias, Pp = p.Pp })
                        .ToListAsync();

                    var currentPlayer = friendsPlayers.FirstOrDefault(p => p.Id == currentID);
                    int topCount = friendsPlayers.Count(p => p.Rank < (currentPlayer?.Rank ?? 0));
                    result.Friends = friendsPlayers.OrderBy(p => p.Rank).Skip(topCount > 3 ? topCount - 3 : 0).Take(5).ToList();
                }
            }

            return result;
        }

        [HttpGet("~/minirankings")]
        public async Task<ActionResult<MiniRankingResponse>> GetMiniRankings(
            [FromQuery] int rank, 
            [FromQuery] string country, 
            [FromQuery] int countryRank,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] bool friends = false)
        {
            if (rank < 4) {
                rank = 4;
            }
            if (countryRank < 4) {
                countryRank = 4;
            }

            if (leaderboardContext != LeaderboardContexts.General && leaderboardContext != LeaderboardContexts.None) {
                return await GetMiniRankingsContext(rank, country, countryRank, leaderboardContext, friends);
            }

            var players = _context
                .Players
                .Where(p => 
                    !p.Banned && 
                    (
                        (p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3) || 
                        (p.Rank <= rank + 1 && p.Rank >= rank - 3)
                    ))
                .Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Alias = p.Alias, Pp = p.Pp });

            var result = new MiniRankingResponse()
            {
                Global = await players.Where(p => p.Rank <= rank + 1 && p.Rank >= rank - 3).OrderBy(s => s.Rank).ToListAsync(),
                Country = await players.Where(p => p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3).OrderBy(s => s.CountryRank).ToListAsync()
            };

            if (friends) {
                string? currentID = HttpContext.CurrentUserID(_context);
                if (currentID != null) {
                    var friendsList = (await _context
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => new { friends = f.Friends.Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Alias = p.Alias, Pp = p.Pp }) })
                        .FirstOrDefaultAsync())
                        ?.friends.ToList();
                    var currentPlayer = await players.Where(p => p.Id == currentID).FirstOrDefaultAsync();
                    if (currentPlayer == null) {
                        currentPlayer = await _context
                        .Players
                        .Where(p => p.Id == currentID)
                        .Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Alias = p.Alias, Pp = p.Pp })
                        .FirstOrDefaultAsync();
                    }

                    if (currentPlayer != null) {
                        if (friendsList != null) {
                            friendsList.Add(currentPlayer);
                        } else {
                            friendsList = new List<MiniRankingPlayer> { currentPlayer };
                        }

                        int topCount = friendsList.Count(p => p.Rank < currentPlayer.Rank);
                        result.Friends = friendsList.OrderBy(p => p.Rank).Skip(topCount > 3 ? topCount - 3 : 0).Take(5).ToList();
                    }
                }
            }

            return result;
        }
    }
}
