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

        public RankController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration,
            ScoreController scoreController,
            PlayerController playerController)
        {
            _context = context;
            _serverTiming = serverTiming;
            _configuration = configuration;
            _scoreController = scoreController;
            _playerController = playerController;
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
            [FromQuery] float stars = 0,
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
            return await VotePrivate(hash, diff, mode, id, rankability, stars, type);
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
                difficulty.Stars = stars;
                difficulty.RankedTime = "" + DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                difficulty.Type = type;
                _context.SaveChanges();
                await _scoreController.RefreshScores(leaderboard.Id);
                await _playerController.RefreshLeaderboardPlayers(leaderboard.Id);
            }

            return Ok();
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
