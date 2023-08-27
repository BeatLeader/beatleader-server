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
    public class RankController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly ScoreRefreshController _scoreRefreshController;
        private readonly MapEvaluationController _mapEvaluationController;
        private readonly PlayerRefreshController _playerRefreshController;
        private readonly PlaylistController _playlistController;

        private readonly NominationsForum _nominationsForum;
        private readonly RTNominationsForum _rtNominationsForum;

        public RankController(
            AppContext context,
            ReadAppContext readContext,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            ScoreRefreshController scoreRefreshController,
            MapEvaluationController mapEvaluationController,
            PlayerRefreshController playerRefreshController,
            PlaylistController playlistController,
            NominationsForum nominationsForum,
            RTNominationsForum rtNominationsForum)
        {
            _context = context;
            _readContext = readContext;

            _serverTiming = serverTiming;
            _configuration = configuration;
            _scoreRefreshController = scoreRefreshController;
            _mapEvaluationController = mapEvaluationController;
            _playerRefreshController = playerRefreshController;
            _playlistController = playlistController;

            _nominationsForum = nominationsForum;
            _rtNominationsForum = rtNominationsForum;
        }

        public enum VoteStatus
        {
            CantVote = 1,
            CanVote = 2,
            Voted = 3,
        }

        [HttpGet("~/votestatus/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatus>> GetVoteStatus(
            string hash,
            string diff,
            string mode,
            [FromQuery] string? player = null)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }

            string? userId = player ?? HttpContext.CurrentUserID(_context);
            if (userId == null) {
                return BadRequest("Provide player or authenticate");
            }

            var score = _context
                .Scores
                .FirstOrDefault(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId);

            if (score == null) {
                return VoteStatus.CantVote;
            }

            var voting = await _context.RankVotings.FindAsync(score.Id);
            if (voting != null)
            {
                return VoteStatus.Voted;
            }

            if (!score.Modifiers.Contains("NF"))
            {
                return VoteStatus.CanVote;
            }
            else
            {
                return VoteStatus.CantVote;
            }
        }

        [HttpPost("~/vote/{hash}/{diff}/{mode}/")]
        [Authorize]
        public async Task<ActionResult<VoteStatus>> Vote(
            string hash,
            string diff,
            string mode,
            [FromQuery] float rankability,
            [FromQuery] string stars = "",
            [FromQuery] int type = 0)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return VoteStatus.CantVote;
            }

            float fixedStars = 0;
            if (stars.Length > 0)
            {
                var parts = stars.Split(",");
                if (parts.Length != 2)
                {
                    parts = stars.Split(".");
                }
                if (parts.Length == 2)
                {
                    fixedStars = float.Parse(parts[0]) + float.Parse(parts[1]) / MathF.Pow(10, parts[1].Length);
                }
            }
            return await VotePrivate(hash, diff, mode, currentID, rankability, fixedStars, type);
        }

        [HttpPost("~/vote/steam/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatus>> VoteSteam(
            string hash,
            string diff,
            string mode,
            [FromQuery] string ticket,
            [FromQuery] float rankability,
            [FromQuery] string stars = "",
            [FromQuery] int type = 0)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            (string? id, string? error) = await SteamHelper.GetPlayerIDFromTicket(ticket, _configuration);
            if (id == null && error != null)
            {
                return VoteStatus.CantVote;
            }
            long intId = Int64.Parse(id);
            if (intId < 70000000000000000)
            {
                AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.PCOculusID == id);
                if (accountLink != null && accountLink.SteamID.Length > 0) {
                    id = accountLink.SteamID;
                }
            }

            float fixedStars = 0;
            if (stars.Length > 0)
            {
                var parts = stars.Split(",");
                if (parts.Length != 2)
                {
                    parts = stars.Split(".");
                }
                if (parts.Length == 2)
                {
                    fixedStars = float.Parse(parts[0]) + float.Parse(parts[1]) / MathF.Pow(10, parts[1].Length);
                }
            }

            return await VotePrivate(hash, diff, mode, id, rankability, fixedStars, type);
        }

        [NonAction]
        public async Task<ActionResult<VoteStatus>> VotePrivate(
            string hash,
            string diff,
            string mode,
            [FromQuery] string player,
            [FromQuery] float rankability,
            [FromQuery] float stars = 0,
            [FromQuery] int type = 0)
        {
            Int64 oculusId = Int64.Parse(player);
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : player);

            var score = _context
                .Scores
                .Include(s => s.RankVoting)
                .Include(s => s.Leaderboard)
                .FirstOrDefault(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId);

            if (score == null)
            {
                return VoteStatus.CantVote;
            }
            if (score.RankVoting != null)
            {
                return VoteStatus.Voted;
            }

            if (!score.Modifiers.Contains("NF"))
            {
                RankVoting voting = new RankVoting
                {
                    PlayerId = userId,
                    Hash = hash,
                    Diff = diff,
                    Mode = mode,
                    Rankability = rankability,
                    Stars = stars,
                    Type = type,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                };
                score.RankVoting = voting;
                if (rankability > 0) {
                    score.Leaderboard.PositiveVotes++;
                } else {
                    score.Leaderboard.NegativeVotes++;
                }
                if (stars > 0) {
                    score.Leaderboard.StarVotes++;
                    score.Leaderboard.VoteStars = MathUtils.AddToAverage(score.Leaderboard.VoteStars, score.Leaderboard.StarVotes, stars);
                }
                await _context.SaveChangesAsync();

                return VoteStatus.Voted;
            }
            else
            {
                return VoteStatus.CantVote;
            }
        }

        [Authorize]
        [HttpPost("~/votefeedback/")]
        public async Task<ActionResult> MakeVoteFeedback([FromQuery] int scoreId, [FromQuery] float value)
        {
            string userId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var voting = _context.RankVotings.Where(v => v.ScoreId == scoreId).Include(v => v.Feedbacks).FirstOrDefault();
            if (voting == null) {
                return NotFound("No such voting");
            }
            if (voting.PlayerId == userId) {
                return BadRequest("You may not provided feedback on yourself");
            }
            if (voting.Feedbacks?.Where(f => f.RTMember == userId).FirstOrDefault() != null) {
                return BadRequest("Feedback from this member already exists");
            }

            if (voting.Feedbacks == null) {
                voting.Feedbacks = new List<VoterFeedback>();
            }

            voting.Feedbacks.Add(new VoterFeedback {
                RTMember = userId,
                Value = value
            });
            await _context.SaveChangesAsync();


            return Ok();
        }

        [Authorize]
        [HttpPost("~/nominate/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> NominateMap(
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
            var currentPlayer = await _context.Players.Where(p => p.Id == currentID).Include(p => p.Socials).FirstOrDefaultAsync();

            Leaderboard? leaderboard = _context.Leaderboards.Include(l => l.Difficulty).Include(l => l.Song).FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            bool isRT = true;
            bool verified = false;
            if (currentPlayer == null || !(currentPlayer.Role.Contains("admin") || currentPlayer.Role.Contains("qualityteam") || (currentPlayer.Role.Contains("rankedteam") && !currentPlayer.Role.Contains("juniorrankedteam"))))
            {
                return Unauthorized();
            }

            if (leaderboard != null)
            {
                var qualifiedLeaderboards = _context
                       .Leaderboards
                       .Include(lb => lb.Song)
                       .Include(lb => lb.Qualification)
                       .Include(lb => lb.Difficulty)
                       .ThenInclude(d => d.ModifierValues)
                       .Include(lb => lb.Difficulty)
                       .ThenInclude(d => d.ModifiersRating)
                       .Where(lb => lb.Song.Id == leaderboard.Song.Id && lb.Qualification != null).ToList();
                string? alreadyApproved = qualifiedLeaderboards.Count() == 0 ? null : qualifiedLeaderboards.FirstOrDefault(lb => lb.Qualification.MapperAllowed)?.Qualification.MapperId;

                if (!isRT) {
                    (Player? _, verified) = await PlayerUtils.GetPlayerFromBeatSaver(currentPlayer.MapperId.ToString());
                }

                if (!isRT && alreadyApproved == null) {
                    int? previous = PrevQualificationTime(leaderboard.Song.Hash).Value?.Time;
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

                var response = await SongUtils.ExmachinaStars(leaderboard.Song.Hash, difficulty.Value, difficulty.ModeName);
                if (response != null) {
                    difficulty.PassRating = response.none.lack_map_calculation.balanced_pass_diff;
                    difficulty.TechRating = response.none.lack_map_calculation.balanced_tech * 10;
                    difficulty.PredictedAcc = response.none.AIacc;
                    difficulty.AccRating = ReplayUtils.AccRating(response.none.AIacc, response.none.lack_map_calculation.balanced_pass_diff, response.none.lack_map_calculation.balanced_tech * 10);

                    var modifiersRating = new ModifiersRating {
                        SSPassRating = response.SS.lack_map_calculation.balanced_pass_diff,
                        SSTechRating = response.SS.lack_map_calculation.balanced_tech * 10,
                        SSPredictedAcc = response.SS.AIacc,
                        SSAccRating = ReplayUtils.AccRating(response.SS.AIacc, response.SS.lack_map_calculation.balanced_pass_diff, response.SS.lack_map_calculation.balanced_tech * 10),
                        SFPassRating = response.SFS.lack_map_calculation.balanced_pass_diff,
                        SFTechRating = response.SFS.lack_map_calculation.balanced_tech * 10,
                        SFPredictedAcc = response.SFS.AIacc,
                        SFAccRating = ReplayUtils.AccRating(response.SFS.AIacc, response.SFS.lack_map_calculation.balanced_pass_diff, response.SFS.lack_map_calculation.balanced_tech * 10),
                        FSPassRating = response.FS.lack_map_calculation.balanced_pass_diff,
                        FSTechRating = response.FS.lack_map_calculation.balanced_tech * 10,
                        FSPredictedAcc = response.FS.AIacc,
                        FSAccRating = ReplayUtils.AccRating(response.FS.AIacc, response.FS.lack_map_calculation.balanced_pass_diff, response.FS.lack_map_calculation.balanced_tech * 10),
                    };
                    difficulty.ModifiersRating = modifiersRating;

                    difficulty.Stars = ReplayUtils.ToStars(difficulty.AccRating ?? 0, difficulty.PassRating ?? 0, difficulty.TechRating ?? 0);
                    modifiersRating.SSStars = ReplayUtils.ToStars(modifiersRating.SSAccRating, modifiersRating.SSPassRating, modifiersRating.SSTechRating);
                    modifiersRating.SFStars = ReplayUtils.ToStars(modifiersRating.SFAccRating, modifiersRating.SFPassRating, modifiersRating.SFTechRating);
                    modifiersRating.FSStars = ReplayUtils.ToStars(modifiersRating.FSAccRating, modifiersRating.FSPassRating, modifiersRating.FSTechRating);

                } else {
                    difficulty.PassRating = 0;
                    difficulty.AccRating = 0;
                    difficulty.TechRating = 0;
                }

                string? criteriaCheck = qualifiedLeaderboards.FirstOrDefault(lb => lb.Qualification.CriteriaCheck != null)?.Qualification.CriteriaCheck;
                if (criteriaCheck == null) {
                    try {
                        MapCheckResult? mapCheckResult = (await _mapEvaluationController.Get(leaderboard.Song.Id)).Value;
                        criteriaCheck = JsonExtensions.SerializeObject(mapCheckResult);
                    } catch { }
                }

                leaderboard.Qualification = new RankQualification {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    RTMember = currentID,
                    MapperId = !isRT ? currentID : alreadyApproved,
                    MapperAllowed = !isRT || alreadyApproved != null,
                    MapperQualification = !isRT,
                    Modifiers = modifierValues,
                    CriteriaCheck = criteriaCheck
                };
                
                difficulty.ModifierValues = modifierValues;
                await _context.SaveChangesAsync();
                await _scoreRefreshController.RefreshScores(leaderboard.Id);
                await _playlistController.RefreshNominatedPlaylist();

                var dsClient = nominationDSClient();

                if (dsClient != null)
                {
                    string message = "**" + currentPlayer.Name + "** nominated **" + diff + "** diff of **" + leaderboard.Song.Name + "**! \n";
                    if (isRT || verified) {
                        message += $"Acc: {difficulty.AccRating:0.00}★\nPass: {difficulty.PassRating:0.00}★\nTech: {difficulty.TechRating:0.00}★\n";
                    }
                    message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }

                var mapper = _context.Players.Include(p => p.Socials).FirstOrDefault(p => p.MapperId == leaderboard.Song.MapperId);

                leaderboard.Qualification.DiscordChannelId = (await _nominationsForum.OpenNomination(mapper?.Name ?? currentPlayer.Name, leaderboard)).ToString();
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
            [FromQuery] int? type,
            [FromQuery] int? criteriaCheck,
            [FromQuery] string? criteriaCommentary,
            [FromQuery] string? modifiers = null)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            Leaderboard? leaderboard = _context.Leaderboards
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
                .FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

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
                    && qualification.RTMember != currentID
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

                        if (qualification.DiscordChannelId.Length > 0) {
                            await _nominationsForum.NominationQualified(qualification.DiscordChannelId);
                        }

                        if (qualification.DiscordRTChannelId.Length > 0) {
                            await _rtNominationsForum.NominationQualified(qualification.DiscordRTChannelId);
                        }

                        if (dsClient != null)
                        {
                            string message = currentPlayer.Name + " qualified **" + diff + "** diff of **" + leaderboard.Song.Name + "**! \n";
                            var mapper = _context.Players.Include(p => p.Socials).FirstOrDefault(p => p.Id == qualification.MapperId);
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
                            message += FormatUtils.DescribeType(difficulty.Type);
                            message += "\n";
                            message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

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
                            leaderboard.Difficulty.Type = (int)type;
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

                        if (qualificationChange.NewRankability <= 0 && qualification.DiscordChannelId.Length > 0)
                        {
                            await _nominationsForum.CloseNomination(qualification.DiscordChannelId);
                        }

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
                            message += "\nhttps://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

                            await dsClient.SendMessageAsync(message);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await _scoreRefreshController.RefreshScores(leaderboard.Id);
                await _playlistController.RefreshNominatedPlaylist();
                await _playlistController.RefreshQualifiedPlaylist();
            }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/reweight/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> UpdateReweight(
            string hash,
            string diff,
            string mode,
            [FromQuery] bool? keep,
            [FromQuery] float? stars,
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

            Leaderboard? leaderboard = _context.Leaderboards
                .Include(l => l.Difficulty)
                .Include(l => l.Song)
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Reweight)
                .ThenInclude(q => q.Changes)
                .Include(l => l.Reweight)
                .ThenInclude(q => q.Modifiers)
                .FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

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
                    Stars = stars ?? (float)leaderboard.Difficulty.Stars,
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
                        if (reweight.Stars != leaderboard.Difficulty.Stars) {
                            message += "★ " + leaderboard.Difficulty.Stars + " → " + reweight.Stars + "\n";
                        }
                        message += FormatUtils.DescribeTypeChanges(leaderboard.Difficulty.Type, reweight.Type);
                        message += FormatUtils.DescribeModifiersChanges(leaderboard.Difficulty.ModifierValues, reweight.Modifiers);
                    }
                    message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

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
                    OldStars = reweight.Stars,
                    OldType = reweight.Type,
                    OldCriteriaMet = reweight.CriteriaMet,
                    OldCriteriaCommentary = reweight.CriteriaCommentary,
                    OldModifiers = reweight.Modifiers,
                };

                if (stars != null)
                {
                    reweight.Stars = (float)stars;
                }
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
                rankUpdateChange.NewStars = reweight.Stars;
                rankUpdateChange.NewType = reweight.Type;
                rankUpdateChange.NewCriteriaMet = reweight.CriteriaMet;
                rankUpdateChange.NewCriteriaCommentary = reweight.CriteriaCommentary;
                rankUpdateChange.NewModifiers = reweight.Modifiers;

                if (rankUpdateChange.NewKeep != rankUpdateChange.OldKeep
                    || rankUpdateChange.NewStars != rankUpdateChange.OldStars
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
            Leaderboard? leaderboard = _context.Leaderboards
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Song)
                .Include(l => l.Changes)
                .Include(l => l.Reweight)
                .ThenInclude(r => r.Modifiers)
                .FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

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
                    OldStars = difficulty.Stars ?? 0,
                    OldType = difficulty.Type,
                    OldModifiers = difficulty.ModifierValues,
                    OldCriteriaMet = difficulty.Status == DifficultyStatus.ranked ? 1 : 0,
                    NewRankability = reweight.Keep ? 1 : 0,
                    NewStars = reweight.Stars,
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
                        if (reweight.Stars != difficulty.Stars)
                        {
                            message += "★ " + difficulty.Stars + " → " + reweight.Stars;
                        }
                        message += FormatUtils.DescribeTypeChanges(leaderboard.Difficulty.Type, reweight.Type);
                        message += FormatUtils.DescribeModifiersChanges(leaderboard.Difficulty.ModifierValues, reweight.Modifiers);
                    }
                    message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

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
                difficulty.Stars = reweight.Stars;
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

            Leaderboard? leaderboard = _context.Leaderboards
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Song)
                .Include(l => l.Reweight)
                .FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

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
                    message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }
            }

            return Ok();
        }


        [Authorize]
        [HttpPost("~/rank/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> SetStarValue(
            string hash,
            string diff,
            string mode,
            [FromQuery] float rankability,
            [FromQuery] float accRating = 0,
            [FromQuery] float passRating = 0,
            [FromQuery] float techRating = 0,
            [FromQuery] int type = 0)
        {
            if (hash.Length < 40) {
                return BadRequest("Hash is too short");
            } else {
                hash = hash.Substring(0, 40);
            }
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var transaction = await _context.Database.BeginTransactionAsync();
            Leaderboard? leaderboard = _context.Leaderboards
                .Include(l => l.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(l => l.Song)
                .Include(l => l.Qualification)
                .Include(l => l.Changes)
                .Include(l => l.ClanRanking)
                .FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null)
            {
                DifficultyDescription? difficulty = leaderboard.Difficulty;
                if (leaderboard.Changes == null)
                {
                    leaderboard.Changes = new List<LeaderboardChange>();
                }
                LeaderboardChange rankChange = new LeaderboardChange
                {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    PlayerId = currentID,
                    OldRankability = difficulty.Status == DifficultyStatus.ranked ? 1 : 0,
                    OldAccRating = difficulty.AccRating ?? 0,
                    OldPassRating = difficulty.PassRating ?? 0,
                    OldTechRating = difficulty.TechRating ?? 0,
                    OldType = difficulty.Type,
                    NewRankability = rankability,
                    NewAccRating = ReplayUtils.AccRating(
                                accRating, 
                                passRating, 
                                techRating),
                    NewPassRating = passRating,
                    NewTechRating = techRating,
                    NewType = type
                };
                leaderboard.Changes.Add(rankChange);

                // Calculate clanRanking for this map because it has just been ranked
                leaderboard.ClanRanking = _context.CalculateClanRankingSlow(leaderboard);

                bool updatePlaylists = (difficulty.Status == DifficultyStatus.ranked) != (rankability > 0); 

                if (difficulty.Status != DifficultyStatus.ranked && rankability > 0) {
                    difficulty.RankedTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                }

                if (rankability <= 0) {
                    difficulty.ModifierValues = null;
                }

                if (rankability > 0) {

                difficulty.Status =  DifficultyStatus.ranked;
                difficulty.PredictedAcc = accRating;
                difficulty.PassRating = passRating;
                difficulty.TechRating = techRating;
                difficulty.AccRating = ReplayUtils.AccRating(
                                accRating, 
                                passRating, 
                                techRating);
                } else {
                    difficulty.Status = DifficultyStatus.unranked;
                difficulty.PredictedAcc = 0;
                difficulty.PassRating = 0;
                difficulty.TechRating = 0;
                difficulty.AccRating = 0;
                }

                difficulty.Type = type;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (updatePlaylists) {
                    await _playlistController.RefreshNominatedPlaylist();
                    await _playlistController.RefreshQualifiedPlaylist();
                    await _playlistController.RefreshRankedPlaylist();
                }

                await _scoreRefreshController.RefreshScores(leaderboard.Id);

                HttpContext.Response.OnCompleted(async () => {
                    await _playerRefreshController.RefreshLeaderboardPlayers(leaderboard.Id);
                });
            }

            return Ok();
        }

        public class PrevQualification {
            public int Time { get; set; }
        }

        [Authorize]
        [HttpGet("~/prevQualTime/{hash}")]
        public ActionResult<PrevQualification> PrevQualificationTime(string hash)
        {
            string? userId = HttpContext.CurrentUserID(_context);

            if (_context.Leaderboards.FirstOrDefault(lb => lb.Difficulty.Status == DifficultyStatus.nominated && lb.Song.Hash == hash) != null) {
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
        [HttpGet("~/voting/spread")]
        public ActionResult<Dictionary<int, int>> Spread() {
            string? currentID = HttpContext.CurrentUserID(_readContext);
            var currentPlayer = _readContext.Players.Find(currentID);

            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var result = new Dictionary<int, int>();
            var starsList = _readContext.RankVotings.Where(v => v.Stars > 0).Select(kv => new { Stars = kv.Stars });
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
        public ActionResult GrantRTJunior(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || currentID == playerId || currentPlayer.Role.Contains("juniorrankedteam") || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            Player? player = _context.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) {
                return NotFound();
            }

            if (!player.Role.Contains("juniorrankedteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Append("juniorrankedteam"));

                _context.SaveChanges();
            }

            return Ok();
        }

        [Authorize]
        [HttpGet("~/removeRTJunior/{playerId}")]
        public ActionResult RemoveRTJunior(
            string playerId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = _context.Players.Find(currentID);

            if (currentPlayer == null || currentID == playerId || currentPlayer.Role.Contains("juniorrankedteam") || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            Player? player = _context.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound();
            }

            if (player.Role.Contains("juniorrankedteam"))
            {
                player.Role = string.Join(",", player.Role.Split(",").Where(s => s != "juniorrankedteam"));

                _context.SaveChanges();
            }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualification/comment/{id}")]
        public async Task<ActionResult<QualificationCommentary>> PostComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            RankQualification? qualification = _context
                .RankQualification
                .Include(q => q.Comments)
                .FirstOrDefault(l => l.Id == id);

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

            var result = new QualificationCommentary {
                PlayerId = currentPlayer.Id,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Value = Encoding.UTF8.GetString(ms.ToArray()),
            };

            qualification.Comments.Add(result);
            _context.SaveChanges();

            try {
            result.DiscordMessageId = await _nominationsForum.PostComment(qualification.DiscordChannelId, result.Value, currentPlayer);
            _context.SaveChanges();
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

            comment.Value = Encoding.UTF8.GetString(ms.ToArray());
            comment.Edited = true;
            comment.EditTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            _context.SaveChanges();

            try {
            comment.DiscordMessageId = await _nominationsForum.UpdateComment(comment.RankQualification.DiscordChannelId, comment.DiscordMessageId, comment.Value, currentPlayer);
            _context.SaveChanges();
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
            _context.SaveChanges();

            try {
            await _nominationsForum.DeleteComment(comment.RankQualification.DiscordChannelId, comment.DiscordMessageId);
            } catch { }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualification/criteria/{id}")]
        public async Task<ActionResult<CriteriaCommentary>> PostCriteriaComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            RankQualification? qualification = _context
                .RankQualification
                .Include(q => q.CriteriaComments)
                .FirstOrDefault(l => l.Id == id);

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

            var result = new CriteriaCommentary {
                PlayerId = currentPlayer.Id,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Value = Encoding.UTF8.GetString(ms.ToArray()),
            };

            qualification.CriteriaComments.Add(result);
            _context.SaveChanges();

            try {
            result.DiscordMessageId = await _rtNominationsForum.PostComment(qualification.DiscordRTChannelId, result.Value, currentPlayer);
            _context.SaveChanges();
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

            comment.Value = Encoding.UTF8.GetString(ms.ToArray());
            comment.Edited = true;
            comment.EditTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            _context.SaveChanges();

            try {
            comment.DiscordMessageId = await _rtNominationsForum.UpdateComment(comment.RankQualification.DiscordRTChannelId, comment.DiscordMessageId, comment.Value, currentPlayer);
            _context.SaveChanges();
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
            _context.SaveChanges();

            try {
            await _rtNominationsForum.DeleteComment(comment.RankQualification.DiscordRTChannelId, comment.DiscordMessageId);
            } catch { }

            return Ok();
        }

        public async Task QualityUnnominate(RankQualification q) {

            Leaderboard? leaderboard = _context.Leaderboards
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
                .FirstOrDefault(l => l.Qualification == q);

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

                if (qualification.DiscordChannelId.Length > 0)
                {
                    await _nominationsForum.CloseNomination(qualification.DiscordChannelId);
                }

                if (qualification.DiscordRTChannelId.Length > 0)
                {
                    await _rtNominationsForum.CloseNomination(qualification.DiscordRTChannelId);
                }

                var dsClient = nominationDSClient();
                if (dsClient != null)
                {
                    string message = "Bot updated nomination for **" + leaderboard.Song.Name + "**!\n";
                    message += "**Declined!**\n Reason: Reached 3 NQT downvotes.\n";
                    message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

                    await dsClient.SendMessageAsync(message);
                }

                await _context.SaveChangesAsync();
                await _scoreRefreshController.RefreshScores(leaderboard.Id);
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

            RankQualification? qualification = _context
                .RankQualification
                .Include(q => q.Votes)
                .FirstOrDefault(l => l.Id == id);

            if (qualification == null)
            {
                return NotFound();
            }
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("qualityteam")))
            {
                return Unauthorized();
            }

            var result = await _nominationsForum.AddVote(_context, qualification, currentPlayer, vote);
            if (qualification.QualityVote == -3) {
                await QualityUnnominate(qualification);
            }
            return result;
        }

        [NonAction]
        public DiscordWebhookClient? reweightDSClient()
        {
            var link = _configuration.GetValue<string?>("ReweightDSHook");
            return link == null ? null : new DiscordWebhookClient(link);
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
