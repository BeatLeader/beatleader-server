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
        public ActionResult<MiniRankingResponse> GetMiniRankingsContext(
            [FromQuery] int rank, 
            [FromQuery] string country, 
            [FromQuery] int countryRank,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] bool friends = false)
        {

            var players = _context
                .PlayerContextExtensions
                .Include(ce => ce.Player)
                .Where(p => 
                    !p.Banned && 
                    p.Context == leaderboardContext &&
                    (
                        (p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3) || 
                        (p.Rank <= rank + 1 && p.Rank >= rank - 3)
                    ))
                .Select(p => new MiniRankingPlayer { Id = p.PlayerId, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp });

            var result = new MiniRankingResponse()
            {
                Global = players.Where(p => p.Rank <= rank + 1 && p.Rank >= rank - 3).OrderBy(s => s.Rank).ToList(),
                Country = players.Where(p => p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3).OrderBy(s => s.CountryRank).ToList()
            };

            if (friends) {
                string? currentID = HttpContext.CurrentUserID(_context);
                if (currentID != null) {
                    var friendsList = _context
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => new { friends = f.Friends.Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp }) })
                        .FirstOrDefault()
                        ?.friends.ToList();
                    var currentPlayer = players.Where(p => p.Id == currentID).FirstOrDefault();
                    if (currentPlayer == null) {
                        currentPlayer = _context
                        .PlayerContextExtensions
                        .Include(ce => ce.Player)
                        .Where(p => p.PlayerId == currentID && p.Context == leaderboardContext)
                        .Select(p => new MiniRankingPlayer { Id = p.PlayerId, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp })
                        .FirstOrDefault();
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

        [HttpGet("~/minirankings")]
        public ActionResult<MiniRankingResponse> GetMiniRankings(
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
                return GetMiniRankingsContext(rank, country, countryRank, leaderboardContext, friends);
            }

            var players = _context
                .Players
                .Where(p => 
                    !p.Banned && 
                    (
                        (p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3) || 
                        (p.Rank <= rank + 1 && p.Rank >= rank - 3)
                    ))
                .Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp });

            var result = new MiniRankingResponse()
            {
                Global = players.Where(p => p.Rank <= rank + 1 && p.Rank >= rank - 3).OrderBy(s => s.Rank).ToList(),
                Country = players.Where(p => p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3).OrderBy(s => s.CountryRank).ToList()
            };

            if (friends) {
                string? currentID = HttpContext.CurrentUserID(_context);
                if (currentID != null) {
                    var friendsList = _context
                        .Friends
                        .Where(f => f.Id == currentID)
                        .Include(f => f.Friends)
                        .Select(f => new { friends = f.Friends.Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp }) })
                        .FirstOrDefault()
                        ?.friends.ToList();
                    var currentPlayer = players.Where(p => p.Id == currentID).FirstOrDefault();
                    if (currentPlayer == null) {
                        currentPlayer = _context
                        .Players
                        .Where(p => p.Id == currentID)
                        .Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp })
                        .FirstOrDefault();
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
