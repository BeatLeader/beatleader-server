using BeatLeader_Server.Bot;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Discord.Rest;
using Discord.Webhook;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;

namespace BeatLeader_Server.Controllers
{
    public class ReweightController : Controller
    {
        private readonly AppContext _context;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly ScoreRefreshController _scoreRefreshController;
        private readonly PlayerRefreshController _playerRefreshController;
        private readonly PlaylistController _playlistController;
        private readonly RTNominationsForum _rtNominationsForum;

        public ReweightController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            ScoreRefreshController scoreRefreshController,
            PlayerRefreshController playerRefreshController,
            PlaylistController playlistController,
            RTNominationsForum rtNominationsForum)
        {
            _context = context;

            _serverTiming = serverTiming;
            _configuration = configuration;
            _scoreRefreshController = scoreRefreshController;
            _playerRefreshController = playerRefreshController;
            _playlistController = playlistController;
            _rtNominationsForum = rtNominationsForum;
        }

        [Authorize]
        [HttpPost("~/reweight/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> UpdateReweight(
            string hash,
            string diff,
            string mode,
            [FromQuery] bool? keep,
            [FromQuery] int? type,
            [FromQuery] int? criteriaCheck,
            [FromQuery] string? criteriaCommentary,
            [FromQuery] string? modifiers)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            Leaderboard? leaderboard = await _context.Leaderboards
                .Include(l => l.Difficulty)
                .Include(l => l.Song)
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Reweight)
                .ThenInclude(q => q.Changes)
                .Include(l => l.Reweight)
                .ThenInclude(q => q.Modifiers)
                .FirstOrDefaultAsync(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            bool isRT = true;
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var reweight = leaderboard?.Reweight;

            ModifiersMap? modifierValues = modifiers == null ? null : JsonConvert.DeserializeObject<ModifiersMap>(modifiers);


            if (reweight == null || reweight.Finished)
            {
                reweight = new RankUpdate {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    RTMember = currentID,
                    Keep = keep ?? true,
                    CriteriaMet = criteriaCheck ?? 1,
                    Modifiers = modifierValues ?? leaderboard.Difficulty.ModifierValues,
                    CriteriaCommentary = criteriaCommentary,
                    Type = type ?? 0,
                };
                leaderboard.Reweight = reweight;

                var dsClient = reweightDSClient();

                if (dsClient != null) {
                    string message = currentPlayer.Name + " initiated reweight for **" + leaderboard.Song.Name + "**\n";
                    if (keep == false) {
                        message += "*UNRANK!*\n Reason: " + criteriaCommentary + "\n";
                    } else {
                        message += FormatUtils.DescribeTypeChanges(leaderboard.Difficulty.Type, reweight.Type);
                        message += FormatUtils.DescribeModifiersChanges(leaderboard.Difficulty.ModifierValues, reweight.Modifiers);
                    }
                    message += "https://beatleader.com/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }
            }
            else
            {
                RankUpdateChange rankUpdateChange = new RankUpdateChange
                {
                    PlayerId = currentID,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    OldKeep = reweight.Keep,
                    OldType = reweight.Type,
                    OldCriteriaMet = reweight.CriteriaMet,
                    OldCriteriaCommentary = reweight.CriteriaCommentary,
                    OldModifiers = reweight.Modifiers,
                };

                if (Request.Query.ContainsKey("type"))
                {
                    reweight.Type = type ?? 0;
                }

                if (criteriaCheck != null && criteriaCheck != reweight.CriteriaMet)
                {
                    reweight.CriteriaMet = (int)criteriaCheck;
                }

                if (Request.Query.ContainsKey("criteriaCommentary"))
                {
                    reweight.CriteriaCommentary = criteriaCommentary;
                }

                if (Request.Query.ContainsKey("keep"))
                {
                    reweight.Keep = keep ?? false;
                }
                if (Request.Query.ContainsKey("modifiers"))
                {
                    reweight.Modifiers = modifierValues;
                }

                rankUpdateChange.NewKeep = reweight.Keep;
                rankUpdateChange.NewType = reweight.Type;
                rankUpdateChange.NewCriteriaMet = reweight.CriteriaMet;
                rankUpdateChange.NewCriteriaCommentary = reweight.CriteriaCommentary;
                rankUpdateChange.NewModifiers = reweight.Modifiers;

                if (rankUpdateChange.NewKeep != rankUpdateChange.OldKeep
                    || rankUpdateChange.NewType != rankUpdateChange.OldType
                    || rankUpdateChange.NewCriteriaMet != rankUpdateChange.OldCriteriaMet
                    || rankUpdateChange.NewCriteriaCommentary != rankUpdateChange.OldCriteriaCommentary 
                    || rankUpdateChange.NewModifiers?.EqualTo(rankUpdateChange.OldModifiers) == false)
                {

                    if (reweight.Changes == null)
                    {
                        reweight.Changes = new List<RankUpdateChange>();
                    }
                    reweight.Changes.Add(rankUpdateChange);
                }
            }

            await _context.SaveChangesAsync();

            return Ok();
        }   

        [Authorize]
        [HttpPost("~/reweight/approve/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> ApproveReweight(
            string hash,
            string diff,
            string mode)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentPlayer.Role.Contains("juniorrankedteam") ||(!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var transaction = await _context.Database.BeginTransactionAsync();
            Leaderboard? leaderboard = await _context.Leaderboards
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Song)
                .Include(l => l.Changes)
                .Include(l => l.Reweight)
                .ThenInclude(r => r.Modifiers)
                .FirstOrDefaultAsync(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null && leaderboard.Reweight != null)
            {
                var reweight = leaderboard.Reweight;

                if (reweight.RTMember == currentID)
                {
                    return Unauthorized("Can't approve own reweight");
                }

                DifficultyDescription? difficulty = leaderboard.Difficulty;
                if (leaderboard.Changes == null) {
                    leaderboard.Changes = new List<LeaderboardChange>();
                }
                LeaderboardChange rankChange = new LeaderboardChange
                {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    PlayerId = currentID,
                    OldRankability = difficulty.Status == DifficultyStatus.ranked ? 1 : 0,
                    OldType = difficulty.Type,
                    OldModifiers = difficulty.ModifierValues,
                    OldCriteriaMet = difficulty.Status == DifficultyStatus.ranked ? 1 : 0,
                    NewRankability = reweight.Keep ? 1 : 0,
                    NewType = reweight.Type,
                    NewModifiers = reweight.Modifiers,
                    NewCriteriaMet = reweight.CriteriaMet
                };
                leaderboard.Changes.Add(rankChange);
                reweight.Finished = true;

                var dsClient = reweightDSClient();

                if (dsClient != null)
                {
                    string message = currentPlayer.Name + " approved reweight for **" + leaderboard.Song.Name + "**!\n";
                    if (!reweight.Keep)
                    {
                        message += "**UNRANKED!**\n Reason: " + reweight.CriteriaCommentary + "\n";
                    }
                    else
                    {
                        message += FormatUtils.DescribeTypeChanges(leaderboard.Difficulty.Type, reweight.Type);
                        message += FormatUtils.DescribeModifiersChanges(leaderboard.Difficulty.ModifierValues, reweight.Modifiers);
                    }
                    message += "https://beatleader.com/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }

                bool updatePlaylists = (difficulty.Status == DifficultyStatus.ranked) != reweight.Keep;

                if (difficulty.Status != DifficultyStatus.ranked && reweight.Keep)
                {
                    difficulty.RankedTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                }

                if (difficulty.Status == DifficultyStatus.ranked && !reweight.Keep)
                {
                    difficulty.ModifierValues = new ModifiersMap();
                }

                difficulty.Status = reweight.Keep ? DifficultyStatus.ranked : DifficultyStatus.unranked;
                if (!reweight.Keep) {
                    difficulty.ModifierValues = null;
                } else {
                     difficulty.ModifierValues = reweight.Modifiers;
                }
                difficulty.Type = reweight.Type;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (updatePlaylists)
                {
                    await _playlistController.RefreshNominatedPlaylist();
                    await _playlistController.RefreshQualifiedPlaylist();
                    await _playlistController.RefreshRankedPlaylist();
                }

                HttpContext.Response.OnCompleted(async () => {
                    await _scoreRefreshController.RefreshScores(leaderboard.Id);
                    await _playerRefreshController.RefreshLeaderboardPlayers(leaderboard.Id);
                    await _playerRefreshController.RefreshRanks();
                });
            }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/reweight/cancel/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> CancelReweight(
            string hash,
            string diff,
            string mode)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            Leaderboard? leaderboard = await _context.Leaderboards
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Song)
                .Include(l => l.Reweight)
                .FirstOrDefaultAsync(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null && leaderboard.Reweight != null)
            {

                if (leaderboard.Reweight.RTMember != currentID && currentPlayer.Role.Contains("juniorrankedteam"))
                {
                    return Unauthorized("Can't cancel this reweight");
                }
                leaderboard.Reweight = null;
                await _context.SaveChangesAsync();

                var dsClient = reweightDSClient();

                if (dsClient != null)
                {
                    string message = currentPlayer.Name + " canceled reweight for **" + leaderboard.Song.Name + "**\n";
                    message += "https://beatleader.com/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }
            }

            return Ok();
        }

        [NonAction]
        public DiscordWebhookClient? reweightDSClient()
        {
            var link = _configuration.GetValue<string?>("ReweightDSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }
    }
}
