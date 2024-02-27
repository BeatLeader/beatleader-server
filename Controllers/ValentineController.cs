using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BeatLeader_Server.Controllers.RankController;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class TopPlayerResponse {
        public string Id { get; set; }
        public int Count { get; set; }

        public bool Sent { get; set; }
        public bool Viewed { get; set; }
    }

    public class ValentineController : Controller
    {
        private readonly AppContext _context;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public ValentineController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;

            _serverTiming = serverTiming;
            _configuration = configuration;
        }

        [HttpGet("~/mytopviewed/")]
        public async Task<ActionResult<List<TopPlayerResponse>>> mytopviewed()
        {
            var ip = HttpContext.GetIpAddress();

            if (ip == null) return BadRequest();

            string ipString = ip;
            string? currentID = HttpContext.CurrentUserID(_context);

            if (currentID == null) return Unauthorized();

            var watchedScores = await _context
                .WatchingSessions
                .Where(ws => ws.Id < 1730859 && (ws.IP == ip || ws.Player == currentID))
                .Select(ws => ws.ScoreId)
                .ToListAsync();

            var playerIds = await _context
                .Scores
                .Where(s => 
                    s.PlayerId != currentID && 
                    !s.Player.Banned &&
                    watchedScores.Contains(s.Id))
                .Select(s => s.PlayerId)
                .ToListAsync();

            var topPlayers = playerIds
                .GroupBy(p => p)
                .Select(p => new TopPlayerResponse { Id = p.Key, Count = p.Count() })
                .OrderByDescending(p => p.Count)
                .Take(3)
                .ToList();

            foreach (var topPlayer in topPlayers)
            {
                var valentine = await _context.ValentineMessages.Where(vm => vm.SenderId == currentID && vm.ReceiverId == topPlayer.Id).FirstOrDefaultAsync();
                topPlayer.Sent = valentine != null;
                topPlayer.Viewed = valentine?.Viewed ?? false;
            }

            return topPlayers;
        }

        [HttpPost("~/sendvalentine/")]
        public async Task<ActionResult> sendvalentine(
            [FromQuery] int index, 
            [FromQuery] string message)
        {
            if (index > 2) {
                return BadRequest("There could be only 3 receipents");
            }

            if (message.Length > 100) {
                return BadRequest("Message should be below 100 characters");
            }

            var topViewed = (await mytopviewed())?.Value;
            if (topViewed == null) {
                return NotFound();
            }

            string? currentID = HttpContext.CurrentUserID(_context);

            var topPlayer = topViewed[index];
            if (!topPlayer.Sent) {

                _context.ValentineMessages.Add(new ValentineMessage {
                    SenderId = currentID,
                    ReceiverId = topPlayer.Id,
                    Message = message,
                    ViewCount = topPlayer.Count,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                });
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpGet("~/valentines/")]
        public async Task<ActionResult<List<ValentineMessage>>> getvalentines()
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            return await _context.ValentineMessages.Where(vm => vm.SenderId == currentID || vm.ReceiverId == currentID).ToListAsync();
        }

        [HttpGet("~/valentine/viewed")]
        public async Task<ActionResult> viewedValentine([FromQuery] int id)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            var message = await _context.ValentineMessages.Where(vm => vm.ReceiverId == currentID && vm.Id == id).FirstOrDefaultAsync();
            if (message == null) {
                return NotFound();
            }

            message.Viewed = true;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}

