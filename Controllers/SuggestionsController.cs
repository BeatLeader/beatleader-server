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

        private readonly LeaderboardController _leaderboardController;

        public SuggestionsController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            LeaderboardController leaderboardController)
        {
            _context = context;

            _serverTiming = serverTiming;
            _configuration = configuration;
            _leaderboardController = leaderboardController;
        }

        [HttpGet("~/trending/")]
        public async Task<ActionResult<ResponseWithMetadata<LeaderboardInfoResponse>>> TrandingMaps()
        {
            int timeset = Time.UnixNow();
            var currentId = HttpContext.CurrentUserID(_context);
            var topToday = _leaderboardController.GetAll(1, 1, SortBy.PlayCount, date_from: timeset - 60 * 60 * 24, overrideCurrentId: currentId);
            var topWeek = _leaderboardController.GetAll(1, 1, SortBy.PlayCount, date_from: timeset - 60 * 60 * 24 * 7, overrideCurrentId: currentId);
            var topVoted = _leaderboardController.GetAll(1, 1, SortBy.VoteCount, date_from: timeset - 60 * 60 * 24 * 30, overrideCurrentId: currentId);

            Task.WaitAll([topToday, topWeek, topVoted]);

            return new ResponseWithMetadata<LeaderboardInfoResponse> {
                Metadata = new Metadata {
                    Page = 1,
                    ItemsPerPage = 3,
                    Total = topToday.Result.Value.Metadata.Total + topWeek.Result.Value.Metadata.Total + topVoted.Result.Value.Metadata.Total
                },
                Data = topToday.Result.Value.Data.Concat(topWeek.Result.Value.Data).Concat(topVoted.Result.Value.Data)
            };
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


