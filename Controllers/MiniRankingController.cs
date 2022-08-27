using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    public class MiniRankingController : Controller
    {
        private readonly ReadAppContext _readContext;
        private readonly IConfiguration _configuration;

        public MiniRankingController(ReadAppContext readContext, IConfiguration configuration)
        {
            _readContext = readContext;
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
        }

        [HttpGet("~/minirankings")]
        public ActionResult<MiniRankingResponse> GetMiniRankings([FromQuery] int rank, [FromQuery] string country, [FromQuery] int countryRank)
        {
            if (rank < 4) {
                rank = 4;
            }
            if (countryRank < 4) {
                countryRank = 4;
            }

            var players = _readContext.Players.Where(p => 
                !p.Banned 
             && ((p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3)
             || (p.Rank <= rank + 1 && p.Rank >= rank - 3)))
                .Select(p => new MiniRankingPlayer { Id = p.Id, Rank = p.Rank, CountryRank = p.CountryRank, Country = p.Country, Name = p.Name, Pp = p.Pp });
            
            return new MiniRankingResponse()
            {
                Global = players.Where(p => p.Rank <= rank + 1 && p.Rank >= rank - 3).OrderBy(s => s.Rank).ToList(),
                Country = players.Where(p => p.Country == country && p.CountryRank <= countryRank + 1 && p.CountryRank >= countryRank - 3).OrderBy(s => s.CountryRank).ToList()
            };
        }
    }
}
