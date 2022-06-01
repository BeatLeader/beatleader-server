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

        public RankController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;
            _serverTiming = serverTiming;
            _configuration = configuration;
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
            [FromQuery] string player)
        {
            Int64 oculusId = Int64.Parse(player);
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
            string userId = (link != null ? link.SteamID : player);

            var voting = await _context.RankVotings.FirstOrDefaultAsync(l => l.Hash == hash && l.Diff == diff && l.Mode == mode && l.PlayerId == userId);
            if (voting != null) {
                return VoteStatus.Voted;
            }

            var score = await _context
                .Scores
                .FirstOrDefaultAsync(l => l.Leaderboard.Song.Hash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId);

            if (score != null && !score.Modifiers.Contains("NF"))
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
            string? currentID = HttpContext.CurrentUserID();
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
            string userId = (link != null ? link.SteamID : player);

            var status = (await GetVoteStatus(hash, diff, mode, userId)).Value;

            if (status == VoteStatus.CanVote)
            {
                RankVoting voting = new RankVoting {
                    PlayerId = player,
                    Hash = hash,
                    Diff = diff,
                    Mode = mode,
                    Rankability = rankability,
                    Stars = stars,
                    Type = type
                };
                _context.RankVotings.Add(voting);
                _context.SaveChanges();

                return VoteStatus.Voted;
            }
            else
            {
                return status;
            }
        }
    }
}
