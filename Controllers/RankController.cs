using BeatLeader_Server.Bot;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Discord.Rest;
using Discord.Webhook;
using Ganss.Xss;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;

namespace BeatLeader_Server.Controllers
{
    public class RankController : Controller
    {
        private readonly AppContext _context;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly ScoreRefreshController _scoreRefreshController;
        private readonly PlayerRefreshController _playerRefreshController;
        private readonly PlaylistController _playlistController;
        private readonly RTNominationsForum _rtNominationsForum;

        public RankController(
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
        [HttpPost("~/nominate/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> NominateMap(
            string hash,
            string diff,
            string mode)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Where(p => p.Id == currentID).Include(p => p.Socials).FirstOrDefaultAsync();

            Leaderboard? leaderboard = await _context.Leaderboards.Include(l => l.Difficulty).Include(l => l.Song).FirstOrDefaultAsync(l => l.Song.LowerHash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            bool isRT = true;
            bool verified = false;
            if (currentPlayer == null || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("qualityteam") || (currentPlayer.Role.Contains("rankedteam") && !currentPlayer.Role.Contains("juniorrankedteam"))))
            {
                return Unauthorized();
            }

            if (leaderboard != null)
            {
                var qualifiedLeaderboards = await _context
                       .Leaderboards
                       .Include(lb => lb.Song)
                       .Include(lb => lb.Qualification)
                       .Include(lb => lb.Difficulty)
                       .ThenInclude(d => d.ModifierValues)
                       .Include(lb => lb.Difficulty)
                       .ThenInclude(d => d.ModifiersRating)
                       .Where(lb => lb.Song.Id == leaderboard.Song.Id && lb.Qualification != null)
                       .ToListAsync();
                string? alreadyApproved = qualifiedLeaderboards.Count() == 0 ? null : qualifiedLeaderboards.FirstOrDefault(lb => lb.Qualification.MapperAllowed)?.Qualification.MapperId;

                if (!isRT) {
                    (Player? _, UserDetail? bsmapper) = await PlayerUtils.GetPlayerFromBeatSaver(currentPlayer.MapperId.ToString());
                }

                if (!isRT && alreadyApproved == null) {
                    int? previous = (await PrevQualificationTime(leaderboard.Song.LowerHash)).Value?.Time;
                    int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    int timeFromPrevious = (int)(timestamp - previous);

                    int timeout = verified ? 7 : 30;
                    if (previous != null && timeFromPrevious < 60 * 60 * 24 * timeout)
                    {
                        return BadRequest("Error. You can qualify new map after " + (int)(timeout - timeFromPrevious / (60 * 60 * 24)) + " day(s)");
                    }
                }
                
                DifficultyDescription? difficulty = leaderboard.Difficulty;

                if (difficulty.Status != DifficultyStatus.unranked && difficulty.Status != DifficultyStatus.unrankable)
                {
                    return BadRequest("Already qualified or ranked");
                }

                ModifiersMap? modifierValues = ModifiersMap.RankedMap();
                difficulty.Status = DifficultyStatus.nominated;
                difficulty.NominatedTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                await RatingUtils.UpdateFromExMachina(leaderboard, null);
                await RatingUtils.UpdateTags(leaderboard, null);

                leaderboard.Qualification = new RankQualification {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    RTMember = currentID,
                    MapperId = !isRT ? currentID : alreadyApproved,
                    MapperAllowed = !isRT || alreadyApproved != null,
                    MapperQualification = !isRT,
                    Modifiers = modifierValues,
                };
                
                difficulty.ModifierValues = modifierValues;
                await _context.SaveChangesAsync();
                await _scoreRefreshController.BulkRefreshScores(leaderboard.Id);
                await _playlistController.RefreshNominatedPlaylist();

                var dsClient = nominationDSClient();

                if (dsClient != null)
                {
                    string message = "**" + currentPlayer.Name + "** nominated **" + diff + "** diff of **" + leaderboard.Song.Name + "**! \n";
                    if (isRT || verified) {
                        message += $"Acc: {difficulty.AccRating:0.00}★\nPass: {difficulty.PassRating:0.00}★\nTech: {difficulty.TechRating:0.00}★\n";
                    }
                    message += "https://beatleader.com/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }

                var mapper = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.MapperId == leaderboard.Song.MapperId);

                leaderboard.Qualification.DiscordRTChannelId = (await _rtNominationsForum.OpenNomination(mapper ?? currentPlayer, leaderboard)).ToString();
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualification/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> UpdateMapQualification(
            string hash,
            string diff,
            string mode,
            [FromQuery] DifficultyStatus? newStatus,
            [FromQuery] float? accRating,
            [FromQuery] float? passRating,
            [FromQuery] float? techRating,
            [FromQuery] MapTypes? type,
            [FromQuery] int? criteriaCheck,
            [FromQuery] string? criteriaCommentary,
            [FromQuery] string? modifiers = null)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            Leaderboard? leaderboard = await _context.Leaderboards
                .Include(l => l.Difficulty)
                .Include(l => l.Song)
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Qualification)
                .ThenInclude(q => q.Changes)
                .ThenInclude(ch => ch.OldModifiers)
                .Include(l => l.Qualification)
                .ThenInclude(q => q.Changes)
                .ThenInclude(ch => ch.NewModifiers)
                .Include(l => l.Qualification)
                .ThenInclude(q => q.Modifiers)
                .FirstOrDefaultAsync(l => l.Song.LowerHash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            bool isRT = true;
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var qualification = leaderboard?.Qualification;
            var newModifiers = modifiers == null ? null : JsonConvert.DeserializeObject<ModifiersMap>(modifiers);

            if (qualification != null)
            {
                if (newStatus == DifficultyStatus.qualified 
                    && leaderboard.Difficulty.AccRating == accRating
                    && leaderboard.Difficulty.PassRating == passRating
                    && leaderboard.Difficulty.TechRating == techRating
                    && leaderboard.Difficulty.Type == type
                    && (criteriaCheck == null || criteriaCheck == 1)
                    && qualification.CriteriaChecker != currentID
                    && qualification.CriteriaMet == 1
                    && newModifiers?.EqualTo(qualification.Modifiers) != false
                    && !currentPlayer.Role.Contains("juniorrankedteam"))
                {
                    if (qualification.ApprovalTimeset == 0)
                    {
                        qualification.ApprovalTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                        leaderboard.Difficulty.Status = DifficultyStatus.qualified;
                        leaderboard.Difficulty.QualifiedTime = qualification.ApprovalTimeset;

                        var dsClient = qualificationDSClient();

                        if (qualification.DiscordRTChannelId.Length > 0) {
                            await _rtNominationsForum.NominationQualified(qualification.DiscordRTChannelId);
                        }

                        if (dsClient != null)
                        {
                            string message = currentPlayer.Name + " qualified **" + diff + "** diff of **" + leaderboard.Song.Name + "**! \n";
                            Player? mapper = null;
                            if (qualification.MapperId != null) {
                                var mainId = await _context.PlayerIdToMain(qualification.MapperId);
                                mapper = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == mainId);
                            }
                            if (mapper != null) {
                                var discord = mapper.Socials?.FirstOrDefault(s => s.Service == "Discord");
                                if (discord != null)
                                {
                                    message += $"Mapper: <@{discord.UserId}> <a:wavege:1069819816581546057> \n";
                                }
                            }
                            var difficulty = leaderboard.Difficulty;
                            message += $"Acc: {difficulty.AccRating:0.00}★\nPass: {difficulty.PassRating:0.00}★\nTech: {difficulty.TechRating:0.00}★\n";
                            message += " **T**  ";
                            message += FormatUtils.DescribeType((int)difficulty.Type);
                            message += "\n";
                            message += "https://beatleader.com/leaderboard/global/" + leaderboard.Id;

                            await dsClient.SendMessageAsync(message);
                        }
                    }

                    if (qualification.Approvers == null)
                    {
                        qualification.Approvers = currentID;
                    }
                    else if (!qualification.Approvers.Contains(currentID))
                    {
                        qualification.Approvers += "," + currentID;
                    }

                    qualification.Approved = true;
                } else {

                    QualificationChange qualificationChange = new QualificationChange {
                        PlayerId = currentID,
                        Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                        OldRankability = leaderboard.Difficulty.Status == DifficultyStatus.nominated || leaderboard.Difficulty.Status == DifficultyStatus.qualified ? 1.0f : 0,
                        OldStars = leaderboard.Difficulty.Stars ?? 0,
                        OldAccRating = leaderboard.Difficulty.AccRating ?? 0,
                        OldPassRating = leaderboard.Difficulty.PassRating ?? 0,
                        OldTechRating = leaderboard.Difficulty.TechRating ?? 0,
                        OldType = (int)leaderboard.Difficulty.Type,
                        OldCriteriaMet = qualification.CriteriaMet,
                        OldCriteriaCommentary = qualification.CriteriaCommentary,
                        OldModifiers = qualification.Modifiers,
                    };

                    if (newStatus == DifficultyStatus.unrankable || newStatus == DifficultyStatus.unranked) {
                        leaderboard.Difficulty.Status = (DifficultyStatus)newStatus;
                        leaderboard.Difficulty.NominatedTime = 0;
                        leaderboard.Difficulty.QualifiedTime = 0;
                        leaderboard.Difficulty.Stars = 0;
                        leaderboard.Difficulty.AccRating = 0;
                        leaderboard.Difficulty.PassRating = 0;
                        leaderboard.Difficulty.TechRating = 0;
                        leaderboard.Difficulty.ModifierValues = null;
                    } else {
                        if (accRating != null)
                        {
                            leaderboard.Difficulty.AccRating = accRating;
                        }
                        if (passRating != null)
                        {
                            leaderboard.Difficulty.PassRating = passRating;
                        }
                        if (techRating != null)
                        {
                            leaderboard.Difficulty.TechRating = techRating;
                        }
                        leaderboard.Difficulty.Stars = ReplayUtils.ToStars(leaderboard.Difficulty.AccRating ?? 0, leaderboard.Difficulty.PassRating ?? 0, leaderboard.Difficulty.TechRating ?? 0);
                        if (type != null)
                        {
                            leaderboard.Difficulty.Type = type ?? MapTypes.None;
                        }
                        if (newStatus == DifficultyStatus.nominated) {
                            leaderboard.Difficulty.Status = DifficultyStatus.nominated;
                            qualification.ApprovalTimeset = 0;
                            qualification.Approved = false;
                            qualification.Approvers = null;
                        }
                    }

                    if (criteriaCheck != null && criteriaCheck != qualification.CriteriaMet) {
                        qualification.CriteriaMet = (int)criteriaCheck;
                        qualification.CriteriaTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                        qualification.CriteriaChecker = currentID;
                    }

                    if (Request.Query.ContainsKey("criteriaCommentary")) {
                        qualification.CriteriaCommentary = criteriaCommentary;
                    }

                    if (newModifiers != null) {
                        qualification.Modifiers = newModifiers;
                        leaderboard.Difficulty.ModifierValues = newModifiers;
                    }

                    qualificationChange.NewRankability = leaderboard.Difficulty.Status == DifficultyStatus.nominated || leaderboard.Difficulty.Status == DifficultyStatus.qualified ? 1.0f : 0;

                    qualificationChange.NewAccRating = leaderboard.Difficulty.AccRating ?? 0;
                    qualificationChange.NewPassRating = leaderboard.Difficulty.PassRating ?? 0;
                    qualificationChange.NewTechRating = leaderboard.Difficulty.TechRating ?? 0;
                    qualificationChange.NewStars = leaderboard.Difficulty.Stars ?? 0;
                    qualificationChange.NewType = (int)leaderboard.Difficulty.Type;
                    qualificationChange.NewCriteriaMet = qualification.CriteriaMet;
                    qualificationChange.NewCriteriaCommentary = qualification.CriteriaCommentary;
                    if (qualificationChange.NewRankability <= 0) {
                        qualificationChange.NewModifiers = null;
                    } else {
                        qualificationChange.NewModifiers = qualification.Modifiers;
                    }

                    if (qualificationChange.NewRankability != qualificationChange.OldRankability
                        || qualificationChange.NewPassRating != qualificationChange.OldPassRating
                        || qualificationChange.NewAccRating != qualificationChange.OldAccRating
                        || qualificationChange.NewTechRating != qualificationChange.OldTechRating
                        || qualificationChange.NewType != qualificationChange.OldType
                        || qualificationChange.NewCriteriaMet != qualificationChange.OldCriteriaMet
                        || qualificationChange.NewCriteriaCommentary != qualificationChange.OldCriteriaCommentary
                        || qualificationChange.NewModifiers?.EqualTo(qualificationChange.OldModifiers) == false) {

                        if (qualification.Changes == null) {
                            qualification.Changes = new List<QualificationChange>();
                        }

                        qualification.Changes.Add(qualificationChange);

                        if (qualificationChange.NewRankability <= 0 && qualification.DiscordRTChannelId.Length > 0)
                        {
                            await _rtNominationsForum.CloseNomination(qualification.DiscordRTChannelId);
                        }

                        var dsClient = nominationDSClient();
                        if (dsClient != null)
                        {
                            string message = currentPlayer.Name + " updated nomination for **" + leaderboard.Song.Name + "**!\n";
                            if (qualificationChange.NewRankability <= 0)
                            {
                                message += "**Declined!**\n";

                                if (qualification.CriteriaCommentary?.Length > 0) {    
                                    message += "Reason: " + qualification.CriteriaCommentary + "\n";
                                }
                            }
                            else
                            {
                                if (qualificationChange.NewAccRating != qualificationChange.OldAccRating)
                                {
                                    message += $"Acc {qualificationChange.OldAccRating:0.00}★ → {qualificationChange.NewAccRating:0.00}★";
                                }
                                if (qualificationChange.NewPassRating != qualificationChange.OldPassRating)
                                {
                                    message += $"Pass {qualificationChange.OldPassRating:0.00}★ → {qualificationChange.NewPassRating:0.00}★";
                                }
                                if (qualificationChange.NewTechRating != qualificationChange.OldTechRating)
                                {
                                    message += $"Tech {qualificationChange.OldTechRating:0.00}★ → {qualificationChange.NewTechRating:0.00}★";
                                }
                                message += FormatUtils.DescribeTypeChanges(qualificationChange.OldType, qualificationChange.NewType);
                                if (qualificationChange.OldCriteriaMet != qualificationChange.NewCriteriaMet)
                                {
                                    message += "\n Criteria checked! Verdict: " + FormatUtils.DescribeCriteria(qualificationChange.NewCriteriaMet) + "\n";
                                    if (qualificationChange.NewCriteriaCommentary != null)
                                    {
                                        message += "With commentary: " + qualificationChange.NewCriteriaCommentary + "\n";
                                    }
                                }
                                else
                                {
                                    if (qualificationChange.OldCriteriaCommentary != qualificationChange.NewCriteriaCommentary)
                                    {
                                        message += "Commentary update: " + qualificationChange.NewCriteriaCommentary + "\n";
                                    }
                                }

                                message += FormatUtils.DescribeModifiersChanges(qualificationChange.OldModifiers, qualificationChange.NewModifiers);
                            }
                            message += "\nhttps://beatleader.com/leaderboard/global/" + leaderboard.Id;

                            await dsClient.SendMessageAsync(message);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await _scoreRefreshController.BulkRefreshScoresAllContexts(leaderboard.Id);
                await _scoreRefreshController.BulkRefreshScores(leaderboard.Id);
                await _playlistController.RefreshNominatedPlaylist();
                await _playlistController.RefreshQualifiedPlaylist();
            }

            return Ok();
        }

        public class PrevQualification {
            public int Time { get; set; }
        }

        [Authorize]
        [HttpGet("~/prevQualTime/{hash}")]
        public async Task<ActionResult<PrevQualification>> PrevQualificationTime(string hash)
        {
            string? userId = HttpContext.CurrentUserID(_context);

            if ((await _context.Leaderboards.FirstOrDefaultAsync(lb => lb.Difficulty.Status == DifficultyStatus.nominated && lb.Song.LowerHash == hash)) != null) {
                return new PrevQualification
                {
                    Time = 0
                };
            }

            return new PrevQualification {
                Time = _context.Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.nominated && lb.Qualification.RTMember == userId)
                .Select(lb => new { time = lb.Difficulty.NominatedTime }).OrderByDescending(lb => lb.time).FirstOrDefault()?.time ?? 0
            };
        }

        [Authorize]
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/voting/spread")]
        public async Task<ActionResult<Dictionary<int, int>>> Spread() {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var result = new Dictionary<int, int>();
            var starsList = _context.RankVotings.Where(v => v.Stars > 0).Select(kv => new { Stars = kv.Stars });
            foreach (var item in starsList)
            {
                int key = (int)Math.Round(item.Stars);
                if (result.ContainsKey(key)) {
                    result[key]++;
                } else {
                    result[key] = 1;
                }
            }
            return result;
        }

        [Authorize]
        [HttpGet("~/grantRTJunior/{playerId}")]
        public async Task<ActionResult> GrantRTJunior(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator") || currentPlayer.Role.Contains("rankoperatorteam")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null) {
                return NotFound();
            }

            if (!player.Role.Contains("juniorrankedteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Append("juniorrankedteam"));
                if (player.Mapper != null) {
                    player.Mapper.Status |= MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/removeRTJunior/{playerId}")]
        public async Task<ActionResult> RemoveRTJunior(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator") || currentPlayer.Role.Contains("rankoperatorteam")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound();
            }

            if (player.Role.Contains("juniorrankedteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(s => s != "juniorrankedteam"));
                if (player.Mapper != null && !player.AnyTeam()) {
                    player.Mapper.Status &= ~MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/grantRTCore/{playerId}")]
        public async Task<ActionResult> GrantRTCore(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator") || currentPlayer.Role.Contains("rankoperatorteam")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null) {
                return NotFound();
            }

            if (!player.Role.Contains("rankedteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Append("rankedteam"));
                if (player.Mapper != null) {
                    player.Mapper.Status |= MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/removeRTCore/{playerId}")]
        public async Task<ActionResult> RemoveRTCore(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator") || currentPlayer.Role.Contains("rankoperatorteam")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound();
            }

            if (player.Role.Contains("rankedteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(s => s != "rankedteam"));
                if (player.Mapper != null && !player.AnyTeam()) {
                    player.Mapper.Status &= ~MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/grantNQTCore/{playerId}")]
        public async Task<ActionResult> GrantNQTCore(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator") || currentPlayer.Role.Contains("rankoperatorteam")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null) {
                return NotFound();
            }

            if (!player.Role.Contains("qualityteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Append("qualityteam"));
                if (player.Mapper != null) {
                    player.Mapper.Status |= MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/removeNQTCore/{playerId}")]
        public async Task<ActionResult> RemoveNQTCore(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator") || currentPlayer.Role.Contains("rankoperatorteam")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound();
            }

            if (player.Role.Contains("qualityteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(s => s != "qualityteam"));
                if (player.Mapper != null && !player.AnyTeam()) {
                    player.Mapper.Status &= ~MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/grantRAT/{playerId}")]
        public async Task<ActionResult> GrantRAT(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null) {
                return NotFound();
            }

            if (!player.Role.Contains("rankoperatorteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Append("rankoperatorteam"));
                if (player.Mapper != null) {
                    player.Mapper.Status |= MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/removeRAT/{playerId}")]
        public async Task<ActionResult> RemoveRAT(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || currentID == playerId || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("creator")))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Mapper).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound();
            }

            if (player.Role.Contains("rankoperatorteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(s => s != "rankoperatorteam"));
                if (player.Mapper != null && !player.AnyTeam()) {
                    player.Mapper.Status &= ~MapperStatus.Team;
                }

                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualification/comment/{id}")]
        public async Task<ActionResult<QualificationCommentary>> PostComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            RankQualification? qualification = await _context
                .RankQualification
                .Include(q => q.Comments)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (qualification == null) {
                return NotFound();
            }

            bool isRT = true;
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam") && !currentPlayer.Role.Contains("qualityteam")))
            {
                if (currentPlayer != null && (currentPlayer.MapperId + "") == qualification?.MapperId) {
                    isRT = false;
                } else {
                    return Unauthorized();
                }
            }

            if (qualification.Comments == null) {
                qualification.Comments = new List<QualificationCommentary>();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var sanitizer = new HtmlSanitizer();
            var commentValue = sanitizer.Sanitize(Encoding.UTF8.GetString(ms.ToArray()));

            var result = new QualificationCommentary {
                PlayerId = currentPlayer.Id,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Value = commentValue,
            };

            qualification.Comments.Add(result);
            await _context.SaveChangesAsync();

            try {
            result.DiscordMessageId = await _rtNominationsForum.PostComment(qualification.DiscordRTChannelId, result.Value, currentPlayer);
            await _context.SaveChangesAsync();
            } catch { }

            return result;
        }

        [Authorize]
        [HttpPut("~/qualification/comment/{id}")]
        public async Task<ActionResult<QualificationCommentary>> UpdateComment(int id) {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            var comment = await _context
                .QualificationCommentary
                .Include(c => c.RankQualification)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (comment == null) {
                return NotFound();
            }
            if (comment.PlayerId != currentPlayer.Id && !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var sanitizer = new HtmlSanitizer();
            var commentValue = sanitizer.Sanitize(Encoding.UTF8.GetString(ms.ToArray()));

            comment.Value = commentValue;
            comment.Edited = true;
            comment.EditTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            await _context.SaveChangesAsync();

            try {
            comment.DiscordMessageId = await _rtNominationsForum.UpdateComment(comment.RankQualification.DiscordRTChannelId, comment.DiscordMessageId, comment.Value, currentPlayer);
            await _context.SaveChangesAsync();
            } catch { }

            return comment;
        }

        [Authorize]
        [HttpDelete("~/qualification/comment/{id}")]
        public async Task<ActionResult> DeleteComment(int id) {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            var comment = await _context.QualificationCommentary.Include(c => c.RankQualification).FirstOrDefaultAsync(c => c.Id == id);
            if (comment == null) {
                return NotFound();
            }
            if (comment.PlayerId != currentPlayer.Id && !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            _context.QualificationCommentary.Remove(comment);
            await _context.SaveChangesAsync();

            try {
            await _rtNominationsForum.DeleteComment(comment.RankQualification.DiscordRTChannelId, comment.DiscordMessageId);
            } catch { }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualification/criteria/{id}")]
        public async Task<ActionResult<CriteriaCommentary>> PostCriteriaComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            RankQualification? qualification = await _context
                .RankQualification
                .Include(q => q.CriteriaComments)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (qualification == null) {
                return NotFound();
            }

            bool isRT = true;
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                if (currentPlayer != null && (currentPlayer.MapperId + "") == qualification?.MapperId) {
                    isRT = false;
                } else {
                    return Unauthorized();
                }
            }

            if (qualification.CriteriaComments == null) {
                qualification.CriteriaComments = new List<CriteriaCommentary>();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var sanitizer = new HtmlSanitizer();
            var commentValue = sanitizer.Sanitize(Encoding.UTF8.GetString(ms.ToArray()));

            var result = new CriteriaCommentary {
                PlayerId = currentPlayer.Id,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Value = commentValue,
            };

            qualification.CriteriaComments.Add(result);
            await _context.SaveChangesAsync();

            try {
            result.DiscordMessageId = await _rtNominationsForum.PostComment(qualification.DiscordRTChannelId, result.Value, currentPlayer);
            await _context.SaveChangesAsync();
            } catch { }

            return result;
        }

        [Authorize]
        [HttpPut("~/qualification/criteria/{id}")]
        public async Task<ActionResult<CriteriaCommentary>> UpdateCriteriaComment(int id) {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            var comment = await _context
                .CriteriaCommentary
                .Include(c => c.RankQualification)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (comment == null) {
                return NotFound();
            }
            if (comment.PlayerId != currentPlayer.Id && !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var sanitizer = new HtmlSanitizer();
            var commentValue = sanitizer.Sanitize(Encoding.UTF8.GetString(ms.ToArray()));

            comment.Value = commentValue;
            comment.Edited = true;
            comment.EditTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            await _context.SaveChangesAsync();

            try {
            comment.DiscordMessageId = await _rtNominationsForum.UpdateComment(comment.RankQualification.DiscordRTChannelId, comment.DiscordMessageId, comment.Value, currentPlayer);
            await _context.SaveChangesAsync();
            } catch { }

            return comment;
        }

        [Authorize]
        [HttpDelete("~/qualification/criteria/{id}")]
        public async Task<ActionResult> DeleteCriteriaomment(int id) {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            var comment = await _context.CriteriaCommentary.Include(c => c.RankQualification).FirstOrDefaultAsync(c => c.Id == id);
            if (comment == null) {
                return NotFound();
            }
            if (comment.PlayerId != currentPlayer.Id && !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            _context.CriteriaCommentary.Remove(comment);
            await _context.SaveChangesAsync();

            try {
            await _rtNominationsForum.DeleteComment(comment.RankQualification.DiscordRTChannelId, comment.DiscordMessageId);
            } catch { }

            return Ok();
        }

        public async Task QualityUnnominate(RankQualification q) {

            Leaderboard? leaderboard = await _context.Leaderboards
                .Include(l => l.Difficulty)
                .Include(l => l.Song)
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Qualification)
                .ThenInclude(q => q.Changes)
                .ThenInclude(ch => ch.OldModifiers)
                .Include(l => l.Qualification)
                .ThenInclude(q => q.Changes)
                .ThenInclude(ch => ch.NewModifiers)
                .Include(l => l.Qualification)
                .ThenInclude(q => q.Modifiers)
                .FirstOrDefaultAsync(l => l.Qualification == q);

            var qualification = leaderboard?.Qualification;

            if (qualification != null && (leaderboard.Difficulty.Status == DifficultyStatus.qualified || leaderboard.Difficulty.Status == DifficultyStatus.nominated))
            {
                QualificationChange qualificationChange = new QualificationChange {
                    PlayerId = AdminController.RankingBotID,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    OldRankability = leaderboard.Difficulty.Status == DifficultyStatus.nominated || leaderboard.Difficulty.Status == DifficultyStatus.qualified ? 1.0f : 0,
                };

                leaderboard.Difficulty.Status = DifficultyStatus.unrankable;
                leaderboard.Difficulty.NominatedTime = 0;
                leaderboard.Difficulty.QualifiedTime = 0;
                leaderboard.Difficulty.Stars = 0;
                leaderboard.Difficulty.ModifierValues = null;

                qualificationChange.NewRankability = 0;

                if (qualification.Changes == null) {
                    qualification.Changes = new List<QualificationChange>();
                }

                qualification.Changes.Add(qualificationChange);

                if (qualification.DiscordRTChannelId.Length > 0)
                {
                    await _rtNominationsForum.CloseNomination(qualification.DiscordRTChannelId);
                }

                var dsClient = nominationDSClient();
                if (dsClient != null)
                {
                    string message = "Bot updated nomination for **" + leaderboard.Song.Name + "**!\n";
                    message += "**Declined!**\n Reason: Reached 3 NQT downvotes.\n";
                    message += "https://beatleader.com/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }

                await _context.SaveChangesAsync();
                await _scoreRefreshController.BulkRefreshScores(leaderboard.Id);
                await _playlistController.RefreshNominatedPlaylist();
                await _playlistController.RefreshQualifiedPlaylist();
            }
        }

        [Authorize]
        [HttpPost("~/qualification/vote/{id}")]
        public async Task<ActionResult<ICollection<QualificationVote>>> AddVote(int id, [FromQuery] MapQuality vote)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            RankQualification? qualification = await _context
                .RankQualification
                .Include(q => q.Votes)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (qualification == null)
            {
                return NotFound();
            }
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("qualityteam")))
            {
                return Unauthorized();
            }

            var result = await _rtNominationsForum.AddVote(_context, qualification, currentPlayer, vote);
            if (qualification.QualityVote == -3) {
                await QualityUnnominate(qualification);
            }
            return result;
        }

        [NonAction]
        public DiscordWebhookClient? nominationDSClient()
        {
            var link = _configuration.GetValue<string?>("NominationDSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }

        [NonAction]
        public DiscordWebhookClient? qualificationDSClient()
        {
            var link = _configuration.GetValue<string?>("QualificationDSHook");
            return link == null ? null : new DiscordWebhookClient(link);
        }
    }
}
