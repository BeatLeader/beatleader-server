using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Dasync.Collections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class BanAdminController : Controller
    {
        private readonly AppContext _context;

        public BanAdminController(
            AppContext context)
        {
            _context = context;
        }

        [HttpGet("~/admin/bans")]
        public async Task<ActionResult<ResponseWithMetadata<List<PlayerWithBanHistory>>>> GetBans(
            [FromQuery, SwaggerParameter("Include self banned records")] bool selfBan = false,
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of players per page, default is 50")] int count = 50)
        {

            string currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null)
            {
                return Unauthorized();
            }

            var currentPlayer = await _context.Players.FindAsync(currentID);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            if (page < 1) page = 1;
            if (count < 0 || count > 100)
            {
                return BadRequest("Please use count between 0 and 100");
            }

            var result = new ResponseWithMetadata<PlayerWithBanHistory>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count
                }
            };

            var groupedBans = (await _context.Bans
                .AsNoTracking()
                .Where(b => (selfBan || (b.BannedBy != b.PlayerId)) && b.BanReason != "Bot")
                .ToListAsync())
                .GroupBy(b => b.PlayerId);

            var playerIds = groupedBans
                .OrderByDescending(g => g.Max(b => b.Timeset))
                .Select(g => g.Key)
                .ToList();

            var data = (await _context
                .Players
                .AsNoTracking()
                .TagWithCallerS()
                .Where(p => !p.Banned && !p.Bot && playerIds.Contains(p.Id))
                .Select(p => new PlayerWithBanHistory()
                {
                    Id = p.Id,
                    Name = p.Name,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,
                    Alias = p.Alias,

                    Bot = p.Bot,
                    Banned = p.Banned,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    Role = p.Role
                })
                .ToListAsync())
                .OrderBy(p => playerIds.IndexOf(p.Id))
                .Skip((page - 1) * count)
                .Take(count);

            result.Metadata.Total = groupedBans.Count();
            foreach (var item in data) {
                item.Bans = groupedBans.First(g => g.Key == item.Id).OrderByDescending(b => b.Timeset).ToList();
            }

            result.Data = data.OrderByDescending(p => p.Bans.Max(b => b.Timeset));

            return Ok(result);
        }
    }
}
