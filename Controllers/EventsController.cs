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
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public EventsController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration, 
            IServerTiming serverTiming,
            IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
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
            IQueryable<Player> request = _readContext
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.EventsParticipating)
                .Include(p => p.ProfileSettings)
                .Where(p => !p.Banned);
            
            if (countries.Length != 0)
            {
                var player = Expression.Parameter(typeof(Player), "p");

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in countries.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Equal(Expression.Property(player, "Country"), Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<Player, bool>>)Expression.Lambda(exp, player));
            }

            if (search.Length != 0)
            {
                var player = Expression.Parameter(typeof(Player), "p");

                var contains = "".GetType().GetMethod("Contains", new[] { typeof(string) });

                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));
                foreach (var term in search.ToLower().Split(","))
                {
                    exp = Expression.OrElse(exp, Expression.Call(Expression.Property(player, "Name"), contains, Expression.Constant(term)));
                }
                request = request.Where((Expression<Func<Player, bool>>)Expression.Lambda(exp, player));
            }

            var players = request.Where(p => p.EventsParticipating.FirstOrDefault(e => e.EventId == id) != null)
                .AsEnumerable()
                .Select(ResponseWithStatsFromPlayer)
                .OrderByDescending(p => p.EventsParticipating.First(e => e.EventId == id).Pp);

            var allPlayers = players.Skip((page - 1) * count).Take(count).ToList();

            foreach (var resultPlayer in allPlayers)
            {
                var eventPlayer = resultPlayer.EventsParticipating.First(e => e.EventId == id);

                resultPlayer.Rank = eventPlayer.Rank;
                resultPlayer.Pp = eventPlayer.Pp;
                resultPlayer.CountryRank = eventPlayer.CountryRank;
            }

            return new ResponseWithMetadata<PlayerResponseWithStats>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = players.Count()
                },
                Data = allPlayers
            };
        }
    }
}
