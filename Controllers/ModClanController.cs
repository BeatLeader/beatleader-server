using Amazon.S3;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReplayDecoder;
using Swashbuckle.AspNetCore.Annotations;
using System.Data;
using System.Dynamic;
using System.Linq.Expressions;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class ModClanController : Controller {

        private readonly AppContext _context;

        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public ModClanController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [HttpGet("~/v1/clanScores/{hash}/{diff}/{mode}/page")]
        public async Task<ActionResult<ResponseWithMetadataAndSelection<ClanScoreResponse>>> ClanScoresV1(
            string hash,
            string diff,
            string mode,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10)
        {
            var result = new ResponseWithMetadataAndSelection<ClanScoreResponse>
            {
                Data = new List<ClanScoreResponse>(),
                Metadata =
                    {
                        ItemsPerPage = count,
                        Page = page,
                        Total = 0
                    }
            };

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var song = await _context
                .Songs
                .TagWithCallerS()
                .AsNoTracking()
                .Select(s => new { s.Id, s.Hash })
                .FirstOrDefaultAsync(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = Song.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + Song.DiffForDiffName(diff).ToString() + modeValue.ToString();

            var query = _context
                .ClanRanking
                .AsNoTracking()
                .Where(s => s.LeaderboardId == leaderboardId);

            result.Metadata.Total = await query.CountAsync();

            var resultList = (await query
                .TagWithCaller()
                .AsNoTracking()
                .OrderBy(p => p.Rank)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(s => new ClanScoreResponse
                {
                    Id = s.Id,
                    ClanId = s.ClanId ?? 0,
                    ModifiedScore = s.TotalScore,
                    Accuracy = s.AverageAccuracy,
                    Pp = s.Pp,
                    Rank = s.Rank,
                    Timepost = s.LastUpdateTime.ToString(),
                    LeaderboardId = s.LeaderboardId,
                    Clan = new ClanScoreClanResponse
                    {
                        Id = s.Clan.Id,
                        Tag = s.Clan.Tag,
                        Name = s.Clan.Name,
                        Avatar = s.Clan.Icon,
                        Color = s.Clan.Color,

                        Pp = s.Clan.Pp,
                        Rank = s.Clan.Rank,

                        RankedPoolPercentCaptured = s.Clan.RankedPoolPercentCaptured,
                        CaptureLeaderboardsCount = s.Clan.CaptureLeaderboardsCount,
                    },
                })
                .ToListAsync())
                .OrderByDescending(el => Math.Round(el.Pp, 2))
                .ThenByDescending(el => Math.Round(el.Accuracy, 4))
                .ThenBy(el => el.Timepost)
                .ToList();

            result.Data = resultList;

            return result;
        }

        

        [NonAction]
        public async Task<(List<ClanPlayerResponse>?, int)> PlayersList(
            ResponseWithMetadataAndSelection<ClanPlayerResponse> result,
            string leaderboardId, 
            string clanTag,
            string scope,
            int page,
            int count,
            PlayerResponse? currentPlayer,
            bool primaryClan = false) {

            clanTag = clanTag.ToUpper();
            var clanId = await _context.Clans.Where(c => c.Tag == clanTag).Select(c => c.Id).FirstOrDefaultAsync();

            IQueryable<Player> query = _context
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings)
                .Include(p => p.Socials)
                .Where(p => p.Rank != 0);
                

            if (primaryClan) {
                query = query
                        .Where(p => !p.Banned && 
                        p.Clans.OrderBy(c => p.ClanOrder.IndexOf(c.Tag) >= 0 ? p.ClanOrder.IndexOf(c.Tag) : 1000)
                            .ThenBy(c => c.Id)
                            .Take(1)
                            .Any(c => c.Id == clanId));
            } else {
                query = query.Where(p => !p.Banned && p.Clans.Any(c => c.Id == clanId));
            }

            query = query.OrderBy(t => t.Rank);

            if (scope == "around") {
                page += (int)Math.Floor((double)((await query.CountAsync(p => p.Rank < currentPlayer.Rank))) / (double)count);

                result.Metadata.Page = page;
            }

            var players = await query
                .Skip((page - 1) * count)
                .Take(count)
                .Select(p => new ClanPlayerResponse {
                    Player = new PlayerResponse
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Alias = p.Alias,
                        Platform = p.Platform,
                        Avatar = p.Avatar,
                        Country = p.Country,

                        Pp = p.Pp,
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        Role = p.Role,
                        Socials = p.Socials,
                        ProfileSettings = p.ProfileSettings,
                        ClanOrder = p.ClanOrder,
                        Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                    }
                })
                .ToListAsync();

            var playerIds = players.Select(p => p.Player.Id).ToList();
            if (currentPlayer != null) {
                playerIds.Add(currentPlayer.Id);
            }

            var scores = await _context
                .Scores
                .AsNoTracking()
                .TagWithCaller()
                .Where(s => s.ValidForGeneral && 
                        playerIds.Contains(s.PlayerId) &&
                        !s.Banned && 
                        s.LeaderboardId == leaderboardId)
                .Select(s => new ScoreResponseWithHeadsets
                {
                    Id = s.Id,
                    PlayerId = s.PlayerId,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
                    Rank = s.Rank,
                    Replay = s.Replay,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    Pauses = s.Pauses,
                    FullCombo = s.FullCombo,
                    Hmd = s.Hmd,
                    Controller = s.Controller,
                    MaxCombo = s.MaxCombo,
                    Timeset = s.Timeset,
                    Timepost = s.Timepost,
                    Platform = s.Platform,
                    Priority = s.Priority,
                    LeaderboardId = s.LeaderboardId,
                    ScoreImprovement = s.ScoreImprovement
                })
                .ToListAsync();

            foreach (var player in players) {
                player.Score = scores.FirstOrDefault(s => s.PlayerId == player.Player.Id);
            }

            var currentPlayerResponse = players.FirstOrDefault(p => p.Player.Id == currentPlayer.Id);
            if (currentPlayerResponse != null)
            {
                result.Selection = currentPlayerResponse;
            }
            else
            {
                ScoreResponseWithHeadsets? highlightedScore = await _context
                .Scores
                .AsNoTracking()
                .TagWithCaller()
                .Where(s => s.ValidForGeneral && 
                        s.PlayerId == currentPlayer.Id &&
                        !s.Banned && 
                        s.LeaderboardId == leaderboardId).Select(s => new ScoreResponseWithHeadsets
                {
                    Id = s.Id,
                    PlayerId = s.PlayerId,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
                    Rank = s.Rank,
                    Replay = s.Replay,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    Pauses = s.Pauses,
                    FullCombo = s.FullCombo,
                    Hmd = s.Hmd,
                    Controller = s.Controller,
                    MaxCombo = s.MaxCombo,
                    Timeset = s.Timeset,
                    Timepost = s.Timepost,
                    Platform = s.Platform,
                    LeaderboardId = s.LeaderboardId,
                    
                    ScoreImprovement = s.ScoreImprovement
                })
                .FirstOrDefaultAsync();

                result.Selection = new ClanPlayerResponse();
                result.Selection.Player = currentPlayer;
                result.Selection.Player = PostProcessSettings(result.Selection.Player, false);
                result.Selection.Score = highlightedScore;
                result.Selection.Score?.FillNames();
                //result.Selection.Score.Rank = await query.CountAsync(s => s.Rank < result.Selection.Rank) + 1;

                if (page < 1) {
                    page = 1;
                }
            }

            result.Metadata.Total = await query.CountAsync();

            return (players, page);
        }

        [HttpGet("~/v1/clan/players/{tag}/{hash}/{diff}/{mode}/{scope}")]
        public async Task<ActionResult<ResponseWithMetadataAndSelection<ClanPlayerResponse>>> GetByHashPlayers(
            string tag,
            string hash,
            string diff,
            string mode,
            string scope,
            [FromQuery] string player,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] bool primaryClan = false) {
            var result = new ResponseWithMetadataAndSelection<ClanPlayerResponse> {
                Data = new List<ClanPlayerResponse>(),
                Metadata =
                    {
                        ItemsPerPage = count,
                        Page = page,
                        Total = 0
                    }
            };

            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }

            var song = await _context
                .Songs
                .TagWithCallerS()
                .AsNoTracking()
                .Select(s => new { Id = s.Id, Hash = s.Hash })
                .FirstOrDefaultAsync(s => s.Hash == hash);
            if (song == null) {
                return result;
            }

            if (mode.EndsWith("OldDots")) {
                mode = mode.Replace("OldDots", "");
            }

            int modeValue = Song.ModeForModeName(mode);
            if (modeValue == 0) {
                var customMode = await _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                if (customMode != null) {
                    modeValue = customMode.Id + 10;
                } else {
                    return result;
                }
            }

            var leaderboardId = song.Id + Song.DiffForDiffName(diff).ToString() + modeValue.ToString();

            PlayerResponse? currentPlayer = 
                await _context
                .Players
                .TagWithCallerS()
                .AsNoTracking()
                .Select(p => new PlayerResponse {
                    Id = p.Id,
                    Name = p.Name,
                    Alias = p.Alias,
                    Platform = p.Platform,
                    Avatar = p.Avatar,
                    Country = p.Country,

                    Pp = p.Pp,
                    Rank = p.Rank,
                    CountryRank = p.CountryRank,
                    Role = p.Role,
                    Socials = p.Socials,
                    ProfileSettings = p.ProfileSettings,
                    Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                })
                .FirstOrDefaultAsync(p => p.Id == player);

            (var resultList, page) = await PlayersList(result, leaderboardId, tag, scope, page, count, currentPlayer, primaryClan);

            if (resultList != null) {
                for (int i = 0; i < resultList.Count; i++) {
                    var score = resultList[i];
                    score.Player = PostProcessSettings(score.Player, false);
                    score.Score?.FillNames();
                }
                result.Data = resultList;
            }

            return result;
        }
    }
}
