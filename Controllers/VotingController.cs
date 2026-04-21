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

namespace BeatLeader_Server.Controllers {
    public class VotingController : Controller {

        private readonly AppContext _context;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly ScoreRefreshController _scoreRefreshController;
        private readonly PlayerRefreshController _playerRefreshController;
        private readonly PlaylistController _playlistController;
        private readonly RTNominationsForum _rtNominationsForum;

        public VotingController(
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
            string mode)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            string? userId = HttpContext.CurrentUserID(_context);
            if (userId == null) {
                return Unauthorized();
            }

            var songId = await _context.DifficultyDescription.Where(d => d.DifficultyName == diff && d.ModeName == mode && d.Hash == hash).Select(d => new { d.SongId, d.Value, d.Mode }).FirstOrDefaultAsync();
            if (songId == null) {
                return VoteStatus.CantVote;
            }

            var lbId = songId.SongId + songId.Value + songId.Mode;

            var score = await _context
                .Scores
                .TagWithCaller()
                .AsNoTracking()
                .Where(l => l.LeaderboardId == lbId && l.PlayerId == userId)
                .Select(s => new { s.Modifiers, s.Id })
                .FirstOrDefaultAsync();

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

        public class VoteStatusExpandedResponse {
            public VoteStatus Status { get; set; }
            public bool? VoteValue { get; set; }
            public FavoriteMap? Favorite { get; set; }

            public int PositiveVotes { get; set; }
            public int NegativeVotes { get; set; }
            public int FavoriteCount { get; set; }

            public List<string> FavoritePictures { get; set; }
        }

        [HttpGet("~/v2/votestatus/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatusExpandedResponse>> VoteStatusExpanded(
            string hash,
            string diff,
            string mode)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var songId = await _context.DifficultyDescription.Where(d => d.DifficultyName == diff && d.ModeName == mode && d.Hash == hash).Select(d => new { d.SongId, d.Value, d.Mode }).FirstOrDefaultAsync();
            if (songId == null) {
                return NotFound();
            }

            var leaderboardId = songId.SongId + songId.Value + songId.Mode;
            var result = await _context
                .Leaderboards
                .Where(lb => lb.Id == leaderboardId)
                .AsNoTracking()
                .Select(lb => new VoteStatusExpandedResponse {
                    PositiveVotes = lb.PositiveVotes,
                    NegativeVotes = lb.NegativeVotes,
                    FavoriteCount = lb.FansCount,
                    FavoritePictures = lb.FavoriteMaps.OrderByDescending(m => m.Timeset).Select(fm => fm.Player.WebAvatar).Take(3).ToList()
                })
                .FirstOrDefaultAsync();

            if (result == null) {
                return NotFound();
            }

            string? userId = HttpContext.CurrentUserID(_context);

            if (userId != null) {
                var voting = await _context
                    .RankVotings
                    .Where(rv => rv.PlayerId == userId && rv.LeaderboardId == leaderboardId)
                    .Select(rv => new {
                        rv.Rankability,
                        rv.FavoriteMap
                    })
                    .FirstOrDefaultAsync();
                if (voting != null)
                {
                    result.Status = VoteStatus.Voted;
                    result.Favorite = voting.FavoriteMap;
                    result.VoteValue = voting.Rankability > 0;
                } else {

                    var score = await _context
                        .Scores
                        .TagWithCaller()
                        .AsNoTracking()
                        .Where(l => l.LeaderboardId == leaderboardId && l.PlayerId == userId)
                        .Select(s => new { s.Modifiers, s.Id })
                        .FirstOrDefaultAsync();

                    if (score == null) {
                        result.Status = VoteStatus.CantVote;
                    } else if (!score.Modifiers.Contains("NF")) {
                        result.Status = VoteStatus.CanVote;
                    }
                }
            }

            return result;
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
            if (hash.Length >= 40) {
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
            if (hash.Length >= 40) {
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
                AccountLink? accountLink = await _context.AccountLinks.FirstOrDefaultAsync(el => el.PCOculusID == id);
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
            AccountLink? link = await _context.AccountLinks.FirstOrDefaultAsync(el => el.OculusID == oculusId);
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : player);

            var score = await _context
                .Scores
                .Include(s => s.RankVoting)
                .Include(s => s.Leaderboard)
                .TagWithCaller()
                .FirstOrDefaultAsync(l => l.Leaderboard.Song.LowerHash == hash && l.Leaderboard.Difficulty.DifficultyName == diff && l.Leaderboard.Difficulty.ModeName == mode && l.PlayerId == userId);

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
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    LeaderboardId = score.LeaderboardId
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

        [HttpPost("~/v2/vote/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatus>> VoteV2(
            string hash,
            string diff,
            string mode,
            [FromQuery] float value)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var songId = await _context.DifficultyDescription.Where(d => d.DifficultyName == diff && d.ModeName == mode && d.Hash == hash).Select(d => new { d.SongId, d.Value, d.Mode }).FirstOrDefaultAsync();
            if (songId == null) {
                return NotFound();
            }

            var leaderboardId = songId.SongId + songId.Value + songId.Mode;

            var scoreId = await _context
                .Scores
                .Where(s => s.LeaderboardId == leaderboardId && s.PlayerId == currentID && !s.IgnoreForStats)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();

            if (scoreId == 0)
            {
                return VoteStatus.CantVote;
            }

            var leaderboard = await _context
                .Leaderboards
                .AsNoTracking()
                .Where(l => l.Id == leaderboardId)
                .Select(l => new Leaderboard { 
                    Id = leaderboardId, 
                    PositiveVotes = l.PositiveVotes, 
                    NegativeVotes = l.NegativeVotes,
                    FansCount = l.FansCount
                })
                .FirstOrDefaultAsync();

            if (leaderboard == null) {
                return VoteStatus.CantVote;
            }

            var voting = _context.RankVotings.Where(rv => rv.ScoreId == scoreId).Include(rv => rv.FavoriteMap).FirstOrDefault();
            bool hadVote = true;
                
            if (voting == null) {
                voting = new RankVoting
                {
                    ScoreId = scoreId,
                    PlayerId = currentID,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    LeaderboardId = leaderboardId
                };
                _context.RankVotings.Add(voting);
                hadVote = false;
            }
            voting.Rankability = value;

            if (value > 0) {
                leaderboard.PositiveVotes++;
                if (hadVote) {
                    leaderboard.NegativeVotes--;
                }
            } else {
                leaderboard.NegativeVotes++;
                if (hadVote) {
                    leaderboard.PositiveVotes--;
                }
                if (voting.FavoriteMap != null) {
                    voting.FavoriteMap = null;
                    leaderboard.FansCount--;
                }
            }

            await _context.BulkUpdateAsync(new List<Leaderboard> { leaderboard }, options => options.ColumnInputExpression = c => new { c.PositiveVotes, c.NegativeVotes, c.FansCount });
            await _context.BulkSaveChangesAsync();

            return VoteStatus.Voted;
        }

        [HttpDelete("~/v2/vote/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatus>> RemoveVoteV2(
            string hash,
            string diff,
            string mode)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var songId = await _context.DifficultyDescription.Where(d => d.DifficultyName == diff && d.ModeName == mode && d.Hash == hash).Select(d => new { d.SongId, d.Value, d.Mode }).FirstOrDefaultAsync();
            if (songId == null) {
                return NotFound();
            }

            var leaderboardId = songId.SongId + songId.Value + songId.Mode;

            var scoreId = await _context
                .Scores
                .Where(s => s.LeaderboardId == leaderboardId && s.PlayerId == currentID && !s.IgnoreForStats)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();

            if (scoreId == 0)
            {
                return VoteStatus.CantVote;
            }

            var leaderboard = await _context
                .Leaderboards
                .AsNoTracking()
                .Where(l => l.Id == leaderboardId)
                .Select(l => new Leaderboard { 
                    Id = leaderboardId, 
                    PositiveVotes = l.PositiveVotes, 
                    NegativeVotes = l.NegativeVotes,
                    FansCount = l.FansCount
                })
                .FirstOrDefaultAsync();

            if (leaderboard == null) {
                return VoteStatus.CantVote;
            }

            var voting = _context.RankVotings.Where(rv => rv.ScoreId == scoreId).Include(rv => rv.FavoriteMap).FirstOrDefault();
                
            if (voting != null) {
                if (voting.Rankability > 0) {
                    leaderboard.PositiveVotes--;
                } else {
                    leaderboard.NegativeVotes--;
                    if (voting.FavoriteMap != null) {
                        voting.FavoriteMap = null;
                        leaderboard.FansCount--;
                    }
                }
                _context.RankVotings.Remove(voting);

                await _context.BulkUpdateAsync(new List<Leaderboard> { leaderboard }, options => options.ColumnInputExpression = c => new { c.PositiveVotes, c.NegativeVotes, c.FansCount });
                await _context.BulkSaveChangesAsync();
            }

            return VoteStatus.Voted;
        }

        [HttpPost("~/leaderboard/favorite/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatus>> FavoriteLeaderboard(
            string hash,
            string diff,
            string mode,
            [FromQuery] FavoriteMapAspect aspect,
            [FromQuery] string? comment) {
            string? currentID = HttpContext.CurrentUserID(_context);

            if (comment != null && comment.Length > 500) {
                return BadRequest("Comment must be shorter than 500 characters");
            }

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var songId = await _context.DifficultyDescription.Where(d => d.DifficultyName == diff && d.ModeName == mode && d.Hash == hash).Select(d => new { d.SongId, d.Value, d.Mode }).FirstOrDefaultAsync();
            if (songId == null) {
                return NotFound();
            }

            var leaderboardId = songId.SongId + songId.Value + songId.Mode;

            var voting = await _context
                .RankVotings
                .Include(s => s.FavoriteMap)
                .FirstOrDefaultAsync(s => s.LeaderboardId == leaderboardId && s.PlayerId == currentID);

            if (voting == null) {
                return VoteStatus.CantVote;
            }

            if (voting.FavoriteMap == null) {

                var leaderboard = await _context
                .Leaderboards
                .AsNoTracking()
                .Where(l => l.Id == leaderboardId)
                .Select(l => new Leaderboard { 
                    Id = leaderboardId,
                    FansCount = l.FansCount
                })
                .FirstOrDefaultAsync();
                leaderboard.FansCount++;
                await _context.BulkUpdateAsync(new List<Leaderboard> { leaderboard }, options => options.ColumnInputExpression = c => new { c.FansCount });

                voting.FavoriteMap = new FavoriteMap {
                    PlayerId = currentID,
                    LeaderboardId = leaderboardId
                };
            }

            voting.FavoriteMap.Aspect = aspect;
            voting.FavoriteMap.Comment = comment;

            await _context.BulkSaveChangesAsync();

            return VoteStatus.Voted;
        }

        [HttpDelete("~/leaderboard/favorite/{hash}/{diff}/{mode}/")]
        public async Task<ActionResult<VoteStatus>> UnFavoriteLeaderboard(
            string hash,
            string diff,
            string mode,
            [FromQuery] string? playerId = null) {
            string? currentID = HttpContext.CurrentUserID(_context);

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var songId = await _context.DifficultyDescription.Where(d => d.DifficultyName == diff && d.ModeName == mode && d.Hash == hash).Select(d => new { d.SongId, d.Value, d.Mode }).FirstOrDefaultAsync();
            if (songId == null) {
                return NotFound();
            }

            var leaderboardId = songId.SongId + songId.Value + songId.Mode;

            if (playerId != null) {
                var currentPlayer = await _context.Players.FindAsync(currentID);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }

                currentID = playerId;
            }

            var voting = await _context
                .RankVotings
                .Include(s => s.FavoriteMap)
                .FirstOrDefaultAsync(s => s.LeaderboardId == leaderboardId && s.PlayerId == currentID);

            if (voting == null) {
                return VoteStatus.CantVote;
            }

            if (voting.FavoriteMap != null) {

                var leaderboard = await _context
                .Leaderboards
                .AsNoTracking()
                .Where(l => l.Id == leaderboardId)
                .Select(l => new Leaderboard { 
                    Id = leaderboardId,
                    FansCount = l.FansCount
                })
                .FirstOrDefaultAsync();
                leaderboard.FansCount--;
                await _context.BulkUpdateAsync(new List<Leaderboard> { leaderboard }, options => options.ColumnInputExpression = c => new { c.FansCount });

                voting.FavoriteMap = null;
            }

            await _context.BulkSaveChangesAsync();

            return VoteStatus.Voted;
        }
    }
}
