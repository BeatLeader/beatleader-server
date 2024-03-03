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
    public class TopSimilarPlayer {
        public string Id { get; set; }
        public float Value { get; set; }
    }

    public class SuggestionsController : Controller
    {
        private readonly AppContext _context;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public SuggestionsController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;

            _serverTiming = serverTiming;
            _configuration = configuration;
        }

        [HttpGet("~/mytopsimilar/")]
        public async Task<ActionResult<List<TopSimilarPlayer>>> mytopsimilar()
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            if (currentID == null) return Unauthorized();

            var playerIds = await _context
                .Scores
                .Where(s => 
                    s.PlayerId == currentID && 
                    s.ValidContexts.HasFlag(LeaderboardContexts.General))
                .Select(s => 
                    s.Leaderboard
                    .Scores
                    .Where(s => 
                        s.ValidContexts.HasFlag(LeaderboardContexts.General))
                    .Select(s => new { s.PlayerId, s.Player.ScoreStats.TotalPlayCount }))
                .ToListAsync();
            
            var playersDictionary = new Dictionary<string, float>();
            foreach (var l in playerIds)
            {
                var lb = l.ToList();
                foreach (var p in lb)
                {
                    if (p.PlayerId == currentID || p.TotalPlayCount == 0 || lb.Count == 0) continue;

                    float value = (1f / lb.Count) * (p.TotalPlayCount > 1000 ? (1000.0f / p.TotalPlayCount) : 1.0f);

                    if (playersDictionary.ContainsKey(p.PlayerId)) {
                        playersDictionary[p.PlayerId] += value;
                    } else {
                        playersDictionary[p.PlayerId] = value;
                    }
                }
            }

            return playersDictionary.Select(x => new TopSimilarPlayer {
                Id = x.Key,
                Value = x.Value
            }).OrderByDescending(p => p.Value).Take(5).ToList();
        }
    }
}


