using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class RankController : Controller
    {
        private readonly AppContext _context;
        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly ScoreController _scoreController;
        private readonly PlayerController _playerController;
        private readonly PlaylistController _playlistController;

        public RankController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            ScoreController scoreController,
            PlayerController playerController,
            PlaylistController playlistController)
        {
            _context = context;
            _serverTiming = serverTiming;
            _configuration = configuration;
            _scoreController = scoreController;
            _playerController = playerController;
            _playlistController = playlistController;
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
            if (player == null && HttpContext.CurrentUserID() == null) {
                return BadRequest("Provide player or authenticate");
            }
            player = player ?? HttpContext.CurrentUserID();
            Int64 oculusId = Int64.Parse(player);
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId || el.PCOculusID == player);
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : player);

            var score = await _context
                .Scores
                .FirstOrDefaultAsync(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId);

            if (score == null) {
                return VoteStatus.CantVote;
            }

            var voting = _context.RankVotings.Find(score.Id);
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
            [FromQuery] float stars = 0,
            [FromQuery] int type = 0)
        {

            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return VoteStatus.CantVote;
            }
            return await VotePrivate(hash, diff, mode, currentID, rankability, stars, type);
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

            var score = await _context
                .Scores
                .Include(s => s.RankVoting)
                .FirstOrDefaultAsync(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId);

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
                _context.SaveChanges();

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
                return BadRequest("You can't feedback yourself");
            }
            if (voting.Feedbacks?.Where(f => f.RTMember == userId).FirstOrDefault() != null) {
                return BadRequest("Feedback from this member already exist");
            }

            if (voting.Feedbacks == null) {
                voting.Feedbacks = new List<VoterFeedback>();
            }

            voting.Feedbacks.Add(new VoterFeedback {
                RTMember = userId,
                Value = value
            });
            _context.SaveChanges();


            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualify/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> QualifyMap(
            string hash,
            string diff,
            string mode,
            [FromQuery] float stars = 0,
            [FromQuery] int type = 0,
            [FromQuery] bool allowed = false)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            Leaderboard? leaderboard = _context.Leaderboards.Include(l => l.Difficulty).Include(l => l.Song).FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            bool isRT = true;
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                if (currentPlayer != null && currentPlayer.MapperId == leaderboard?.Song.MapperId) {
                    isRT = false;
                } else {
                    return Unauthorized();
                }
            }

            if (leaderboard != null)
            {
                var qualifiedLeaderboards = _context
                       .Leaderboards
                       .Include(lb => lb.Song)
                       .Include(lb => lb.Qualification)
                       .Where(lb => lb.Song.Id == leaderboard.Song.Id && lb.Qualification != null).ToList();
                string? alreadyApproved = qualifiedLeaderboards.Count() == 0 ? null : qualifiedLeaderboards.FirstOrDefault(lb => lb.Qualification.MapperAllowed)?.Qualification.MapperId;

                if (!isRT && alreadyApproved == null) {
                    int previous = (await PrevQualificationTime(leaderboard.Song.Hash)).Value.Time;
                    int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    if (previous != null && (timestamp - previous) < 60 * 60 * 24 * 7)
                    {
                        return BadRequest("Error. You can qualify new map after " + (int)(7 - (timestamp - previous) / (60 * 60 * 24)) + " day(s)");
                    }
                }
                
                DifficultyDescription? difficulty = leaderboard.Difficulty;

                if (difficulty.Qualified || difficulty.Ranked)
                {
                    return BadRequest("Already qualified or ranked");
                }

                difficulty.Qualified = true;
                difficulty.QualifiedTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                difficulty.Stars = stars;
                leaderboard.Qualification = new RankQualification {
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    RTMember = currentID,
                    MapperId = !isRT ? currentID : alreadyApproved,
                    MapperAllowed = !isRT || alreadyApproved != null,
                    MapperQualification = !isRT
                };
                difficulty.Type = type;
                _context.SaveChanges();
                await _scoreController.RefreshScores(leaderboard.Id);
                await _playlistController.RefreshQualifiedPlaylist();
            }

            return Ok();
        }

        [Authorize]
        [HttpPost("~/qualification/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult> UpdateMapQualification(
            string hash,
            string diff,
            string mode,
            [FromQuery] bool? stilQualifying,
            [FromQuery] float? stars,
            [FromQuery] int? type,
            [FromQuery] bool? allowed,
            [FromQuery] int? criteriaCheck,
            [FromQuery] string? criteriaCommentary)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            Leaderboard? leaderboard = _context.Leaderboards
                .Include(l => l.Difficulty)
                .Include(l => l.Song)
                .Include(l => l.Qualification)
                .FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            bool isRT = true;
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && !currentPlayer.Role.Contains("rankedteam")))
            {
                return Unauthorized();
            }

            var qualification = leaderboard?.Qualification;

            if (qualification != null)
            {
                if (stilQualifying == false) {
                    leaderboard.Difficulty.Qualified = false;
                    leaderboard.Difficulty.QualifiedTime = 0;
                    leaderboard.Difficulty.Stars = 0;
                } else {
                    if (stars != null)
                    {
                        leaderboard.Difficulty.Stars = stars;
                    }
                    if (type != null)
                    {
                        leaderboard.Difficulty.Type = (int)type;
                    }
                }

                if (!qualification.MapperAllowed && allowed != null)
                {
                    qualification.MapperAllowed = (bool)allowed;
                    if ((bool)allowed) {
                        qualification.MapperId = currentID;
                    }
                }

                if (criteriaCheck != null) {
                    qualification.CriteriaMet = (int)criteriaCheck;
                    qualification.CriteriaTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    qualification.CriteriaChecker = currentID;
                }
                if (criteriaCommentary != null) {
                    qualification.CriteriaCommentary = criteriaCommentary;
                }

                _context.SaveChanges();
                await _scoreController.RefreshScores(leaderboard.Id);
                await _playlistController.RefreshQualifiedPlaylist();
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
            [FromQuery] float stars = 0,
            [FromQuery] int type = 0)
        {
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

            string userId = accountLink != null ? accountLink.SteamID : currentID;
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Leaderboard? leaderboard = _context.Leaderboards.Include(l => l.Difficulty).Include(l => l.Song).FirstOrDefault(l => l.Song.Hash == hash && l.Difficulty.DifficultyName == diff && l.Difficulty.ModeName == mode);

            if (leaderboard != null)
            {
                DifficultyDescription? difficulty = leaderboard.Difficulty;
                RankChange rankChange = new RankChange
                {
                    PlayerId = userId,
                    Hash = hash,
                    Diff = diff,
                    Mode = mode,
                    OldRankability = difficulty.Ranked ? 1 : 0,
                    OldStars = difficulty.Stars ?? 0,
                    OldType = difficulty.Type,
                    NewRankability = rankability,
                    NewStars = stars,
                    NewType = type
                };
                _context.RankChanges.Add(rankChange);

                difficulty.Ranked = rankability > 0;
                if (difficulty.Qualified) {
                    difficulty.Qualified = false;
                }
                leaderboard.Qualification = null;
                difficulty.Stars = stars;
                difficulty.RankedTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                difficulty.Type = type;
                _context.SaveChanges();
                await _scoreController.RefreshScores(leaderboard.Id);
                await _playerController.RefreshLeaderboardPlayers(leaderboard.Id);
                await _playlistController.RefreshRankedPlaylist();
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
            string userId = HttpContext.CurrentUserID(_context);

            if (_context.Leaderboards.Where(lb => lb.Difficulty.Qualified && lb.Song.Hash.ToLower() == hash.ToLower()).FirstOrDefault() != null) {
                return new PrevQualification
                {
                    Time = 0
                };
            }

            return new PrevQualification {
                Time = _context.Leaderboards
                .Where(lb => lb.Qualification != null && lb.Qualification.RTMember == userId)
                .Select(lb => new { time = lb.Difficulty.QualifiedTime }).FirstOrDefault()?.time ?? 0
            };
        }

        [Authorize]
        [HttpPost("~/rankabunch/")]
        public async Task<ActionResult> SetStarValues([FromBody] Dictionary<string, float> values) {
            string userId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var allKeys = values.Keys.Select(k => k.Split(",").First());
            var leaderboards = _context.Leaderboards.Where(lb => allKeys.Contains(lb.Song.Hash.ToUpper())).Include(lb => lb.Song).Include(lb => lb.Difficulty).ToList();
            foreach (var lb in leaderboards)
            {
                if (lb.Difficulty.Ranked && values.ContainsKey(lb.Song.Hash.ToUpper() + "," + lb.Difficulty.DifficultyName + "," + lb.Difficulty.ModeName)) {
                    lb.Difficulty.Ranked = true;
                    lb.Difficulty.Stars = values[lb.Song.Hash.ToUpper() + "," + lb.Difficulty.DifficultyName + "," + lb.Difficulty.ModeName];
                }

            }
            await _context.SaveChangesAsync();
            return Ok();

        }

        [Authorize]
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

        //[Authorize]
        //[HttpGet("~/map/rdate/{hash}")]
        //public async Task<ActionResult> SetStarValue(string hash, [FromQuery] int diff, [FromQuery] string date)
        //{
        //    string currentID = HttpContext.CurrentUserID();
        //    long intId = Int64.Parse(currentID);
        //    AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

        //    string userId = accountLink != null ? accountLink.SteamID : currentID;
        //    var currentPlayer = await _context.Players.FindAsync(userId);

        //    if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
        //    {
        //        return Unauthorized();
        //    }

        //    Song? song = (await _songController.GetHash(hash)).Value;
        //    DateTime dateTime = DateTime.Parse(date);

        //    string timestamp = Convert.ToString((int)dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);

        //    if (song != null)
        //    {
        //        DifficultyDescription? diffy = song.Difficulties.FirstOrDefault(d => d.DifficultyName == SongUtils.DiffNameForDiff(diff));
        //        diffy.Ranked = true;
        //        diffy.RankedTime = timestamp;

        //        Leaderboard? leaderboard = (await Get(song.Id + diff + SongUtils.ModeForModeName("Standard"))).Value;
        //        if (leaderboard != null)
        //        {
        //            leaderboard = await _context.Leaderboards.Include(l => l.Difficulty).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);

        //            leaderboard.Difficulty.Ranked = true;
        //            leaderboard.Difficulty.RankedTime = timestamp;
        //        }
        //    }

        //    _context.SaveChanges();

        //    return Ok();
        //}
    }
}
