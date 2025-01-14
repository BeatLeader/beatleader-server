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

            var groupedBans = _context.Bans
                .AsNoTracking()
                .OrderByDescending(b => b.Timeset)
                .GroupBy(b => b.PlayerId).ToList();

            var filtered = groupedBans.Select(group => new
                {
                    Player = _context.Players.Where(p => p.Id == group.Key).FirstOrDefault()!,
                    Bans = group.OrderByDescending(b => b.Timeset).ToList(),
                    LatestBan = group.OrderByDescending(b => b.Timeset).FirstOrDefault()!
                })
                // idk, but
                .Where(item => item.Player != null)
                // bots
                .Where(pb => !pb.Player.Banned || pb.LatestBan.BanReason != "Bot")
                // filter self banned
                .Where(pb => selfBan || (!pb.Player.Banned || pb.LatestBan.BannedBy != pb.Player.Id));

            result.Metadata.Total = filtered.Count();

            var data = filtered
                .Skip((page - 1) * count)
                .Take(count)
                .Select(item => MapToResponse(item.Player, item.Bans))
                .ToList();

            result.Data = data;

            return Ok(result);
        }

        private PlayerWithBanHistory MapToResponse(Player p, List<Ban> bans)
        {
            return new PlayerWithBanHistory()
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
                Role = p.Role,

                Bans = bans
            };
        }
    }
}
