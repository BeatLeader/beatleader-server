using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Models.SongSuggest;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class SongSuggestController : Controller
    {
        private readonly AppContext _context;

        private readonly IAmazonS3 _s3Client;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;

        public SongSuggestController(
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

        [HttpGet("~/songsuggest")]
        public async Task<ActionResult> GetSongSuggest([FromQuery] int? before_time = null)
        {
            var refresh = _context
                .SongSuggestRefreshes
                .OrderByDescending(s => s.Timeset)
                .Where(s => before_time != null ? s.Timeset < before_time : true)
                .FirstOrDefault();
            if (refresh == null) return NotFound();

            var replayStream = await _s3Client.DownloadAsset(refresh.File);
            if (replayStream == null) {
                return NotFound();
            }
            return File(replayStream, "application/json");
        }

        [HttpGet("~/songsuggest/songs")]
        public async Task<ActionResult<List<SongSuggestSong>>> GetSongSuggestSongs(
            [FromQuery] int? before_time = null) {

            var refresh = _context
                .SongSuggestRefreshes
                .OrderByDescending(s => s.Timeset)
                .Where(s => before_time != null ? s.Timeset < before_time : true)
                .FirstOrDefault();
            if (refresh == null) return NotFound();

            var replayStream = await _s3Client.DownloadAsset(refresh.SongsFile);
            if (replayStream == null) {
                return NotFound();
            }
            return File(replayStream, "application/json");
        }

        [HttpGet("~/songsuggest/refreshTime")]
        public async Task<ActionResult<int>> GetSongSuggestLastRefreshTime([FromQuery] int? before_time = null)
        {
            var refresh = _context
                .SongSuggestRefreshes
                .OrderByDescending(s => s.Timeset)
                .Where(s => before_time != null ? s.Timeset < before_time : true)
                .FirstOrDefault();
            if (refresh == null) return NotFound();

            return refresh.Timeset;
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("~/songsuggest/refresh")]
        public async Task<ActionResult> RefreshSongSuggest()
        {
            if (!HttpContext.ItsAdmin(_context)) return Unauthorized();

            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var activeTreshold = timeset - 60 * 60 * 24 * 31 * 3;
            var scoreTreshold = timeset - 60 * 60 * 24 * 365;

            var list = _context.Scores
                .Where(s => 
                    s.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                    !s.Qualification &&
                    !s.Modifiers.Contains("FS") &&
                    !s.Modifiers.Contains("SF") &&
                    !s.Modifiers.Contains("GN") &&
                    !s.Modifiers.Contains("SS") &&
                    s.Leaderboard.Difficulty.ModeName == "Standard" &&
                    s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked &&
                    s.Player.ScoreStats.RankedPlayCount >= 30 &&
                    s.Player.ScoreStats.LastRankedScoreTime >= activeTreshold)
                .Select(s => new { 
                    score = new Top10kScore {
                        songID = s.LeaderboardId,
                        pp = s.Pp,
                        accuracy = s.Accuracy,
                        timepost = s.Timepost
                    },
                    player = new Top10kPlayer {
                        id = s.Player.Id,
                        name = s.Player.Name,
                        rank = s.Player.Rank
                    }
                })
                .ToList()
                .GroupBy(m => m.player.id)
                .OrderBy(group => group.First().player.rank)
                .Where(g => g.Count() >= 20)
                .Select((group, i) => new Top10kPlayer {
                    id = group.First().player.id,
                    name = group.First().player.name,
                    rank = i + 1,
                    top10kScore = group
                        .Select(m => m.score)
                        .OrderByDescending(s => s.pp)
                        .Take(30)
                        .OrderByDescending(s => s.accuracy)
                        .Take(20)
                        .OrderByDescending(s => s.pp)
                        .Select((score, i) => new Top10kScore {
                            songID = score.songID,
                            pp = score.pp,
                            rank = i + 1,
                            timepost = score.timepost
                        })
                        .ToList()
                })
                .Where(p => p.top10kScore.Last().pp / p.top10kScore.First().pp > 0.7 && p.top10kScore.All(s => s.timepost > scoreTreshold))
                .ToList();

            var filename = $"songsuggestions-{timeset}.json";
            await _s3Client.UploadStream(filename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(list)).ToStream());

            var songs = _context.Leaderboards
                .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked)
                .OrderByDescending(lb => lb.Difficulty.Stars)
                .Select(lb => new SongSuggestSong {
                    ID = lb.Id,
                    name = lb.Song.Name,
                    hash = lb.Song.Hash,
                    difficulty = lb.Difficulty.DifficultyName,
                    mode = lb.Difficulty.ModeName,
                    stars = (float)lb.Difficulty.Stars
                })
                .ToList();

            var songsfilename = $"songsuggestions-{timeset}-songs.json";
            await _s3Client.UploadStream(songsfilename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(songs)).ToStream());

            _context.SongSuggestRefreshes.Add(new SongSuggestRefresh {
                Timeset = timeset,
                File = filename,
                SongsFile = songsfilename
            });
            _context.SaveChanges();

            return Ok();
        }
    }
}
