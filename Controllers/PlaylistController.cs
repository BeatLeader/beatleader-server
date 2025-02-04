using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Net;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Services;
using Microsoft.AspNetCore.Http.Extensions;
using static BeatLeader_Server.Utils.ResponseUtils;
using Type = BeatLeader_Server.Enums.Type;
using System.Security.Cryptography;
using System.Dynamic;
using BeatLeader_Server.ControllerHelpers;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlaylistController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;

        public PlaylistController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _s3Client = configuration.GetS3Client();
            _environment = env;
        }

        [Authorize]
        [HttpGet("~/user/oneclickplaylist")]
        public async Task<ActionResult> GetOneClickPlaylist()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            var playlistUrl = await _s3Client.GetPresignedUrl(currentID + "oneclick.bplist", S3Container.playlists);
            if (playlistUrl != null) {
                return Redirect(playlistUrl);
            } else {
                playlistUrl = await _s3Client.GetPresignedUrl("oneclick.bplist", S3Container.playlists);
                if (playlistUrl != null) {
                    return Redirect(playlistUrl);
                } else {
                    return NotFound();
                }
            }
        }

        [Authorize]
        [HttpGet("~/user/oneclickplaylist/link")]
        public async Task<ActionResult<string>> GetOneClickPlaylistUrl()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            var playlistUrl = await _s3Client.GetPresignedUrl(currentID + "oneclick.bplist", S3Container.playlists);
            if (playlistUrl != null) {
                return playlistUrl;
            } else {
                playlistUrl = await _s3Client.GetPresignedUrl("oneclick.bplist", S3Container.playlists);
                if (playlistUrl != null) {
                    return playlistUrl;
                } else {
                    return NotFound();
                }
            }
        }

        [Authorize]
        [HttpPost("~/user/oneclickplaylist")]
        public async Task<ActionResult> UpdateOneClickPlaylist()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            dynamic? mapscontainer = ms.ObjectFromStream();
            if (mapscontainer == null || !ExpandantoObject.HasProperty(mapscontainer, "songs")) {
                return BadRequest("Can't decode songs");
            }

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist(currentID + "oneclick.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                } else {
                    playlist = (await _s3Client.DownloadPlaylist("oneclick.bplist"))?.ObjectFromStream();
                }
            }

            if (playlist == null) {
                playlist = (await _s3Client.DownloadPlaylist("oneclick.bplist"))?.ObjectFromStream();
            }

            if (playlist == null) {
                return BadRequest("Original plist dead. Wake up NSGolova");
            }

            playlist.songs = mapscontainer.songs;

            await S3Helper.UploadPlaylist(_s3Client, currentID + "oneclick.bplist", playlist);

            return Ok();
        }

        [Authorize]
        [HttpGet("~/user/oneclickdone")]
        public async Task<ActionResult> CleanOneClickPlaylist()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist(currentID + "oneclick.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                } else {
                    playlist = (await _s3Client.DownloadPlaylist("oneclick.bplist"))?.ObjectFromStream();
                }
            }

            if (playlist == null) {
                playlist = (await _s3Client.DownloadPlaylist("oneclick.bplist"))?.ObjectFromStream();
            }

            if (playlist == null) {
                return BadRequest("Original plist dead. Wake up NSGolova");
            }

            playlist.songs = new List<string>();

            await S3Helper.UploadPlaylist(_s3Client, currentID + "oneclick.bplist", playlist);

            return Ok();
        }

        [HttpGet("~/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> Get()
        {
            return await _context.Playlists.Where(t => t.IsShared).ToListAsync();
        }

        [NonAction]
        private async Task<ActionResult> DownloadPlaylist(string id) {
            var stream = await _s3Client.DownloadPlaylist(id + ".bplist");
            
            if (stream != null) {
                using (var objectStream = new MemoryStream(5))
                {
                    var outputStream = new MemoryStream(5);
                    stream.CopyTo(objectStream);
                    objectStream.Position = 0;
                    objectStream.CopyTo(outputStream);
                    objectStream.Position = 0;
                    outputStream.Position = 0;

                    dynamic? playlistObject = objectStream.ObjectFromStream();

                    if (playlistObject == null)
                    {
                        return NotFound();
                    }
                    Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{Uri.EscapeUriString(playlistObject.playlistTitle)}.bplist\"");
                    Response.Headers.Add("X-Content-Type-Options", "nosniff");
                    return File(outputStream, "application/json");
                }
                
            } else {
                return NotFound();
            }
        }

        [HttpGet("~/playlist/{id}")]
        public async Task<ActionResult> GetById(string id)
        {
            id = id.Replace(".bplist", "");
            if (int.TryParse(id, out var intId)) {
                string? currentID = HttpContext.CurrentUserID(_context);

                if (intId != 33) {
                    var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.Id == intId);
                    if (playlist == null) {
                        return NotFound();
                    } else if (playlist.OwnerId != currentID && !playlist.IsShared) {
                        return Unauthorized("");
                    }
                }
            }

            return await DownloadPlaylist(id);
        }

        [HttpGet("~/playlist/guid/{guid}")]
        public async Task<ActionResult> GetByGuid(string guid) {
            guid = guid.Replace(".bplist", "");

            var playlist = await _context.Playlists.Where(p => p.Guid == Guid.Parse(guid)).FirstOrDefaultAsync();
            if (playlist == null) {
                return NotFound();
            }

            return await DownloadPlaylist(playlist.Id.ToString());
        }

        [HttpGet("~/playlist/{id}/link")]
        public async Task<ActionResult<string>> GetByIdLink(string id)
        {
            if (int.TryParse(id, out var intId)) {
                string? currentID = HttpContext.CurrentUserID(_context);

                if (intId != 33) {
                    var playlist = await _context.Playlists.FirstOrDefaultAsync(p => (p.OwnerId == currentID || p.IsShared) && p.Id == intId);
                    if (playlist == null) {
                        return Unauthorized("");
                    }
                }
            }

            var playlisUrl = await _s3Client.GetPresignedUrl(id + ".bplist", S3Container.playlists);
            if (playlisUrl != null) {
                return playlisUrl;
            } else {
                return NotFound();
            }
        }

        [HttpGet("~/playlist/image/{id}")]
        public async Task<ActionResult> GetImageById(string id)
        {
            if (int.TryParse(id, out int playlistId)) {
                var playlistRecord = await _context.Playlists.FindAsync(playlistId);
                if (playlistRecord != null && !playlistRecord.IsShared) {
                    return Unauthorized();
                }
            }

            using (var stream = await _s3Client.DownloadPlaylist(id.Split(".").First() + ".bplist")) {
                if (stream == null) {
                    return NotFound();
                }

                dynamic? playlist = stream.ObjectFromStream();

                if (playlist == null)
                {
                    return NotFound();
                }

                string image = playlist.image;
                image = image.Replace("data:image/png;base64,", "").Replace("data:image/jpeg;base64,", "");

                return File(new MemoryStream(Convert.FromBase64String(image)), "image/png");
            }
        }

        [HttpGet("~/user/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> GetAllPlaylists()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            return await _context.Playlists.Where(t => t.OwnerId == currentID).ToListAsync();
        }

        public static string CalculateSha256(dynamic playlist) {
            using (SHA256 sha256 = SHA256.Create()) {
                dynamic? customData = null;
                if (ExpandantoObject.HasProperty(playlist, "customData")) {
                    customData = ((IDictionary<String, object>)playlist)["customData"];
                }
                playlist.customData = null;
                using (Stream memoryStream = new BinaryData(JsonConvert.SerializeObject(playlist)).ToStream()) {
                    byte[] hashBytes = sha256.ComputeHash(memoryStream);
                    playlist.customData = customData;
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        [HttpPost("~/user/playlist")]
        public async Task<ActionResult<PlaylisCustomData>> PostPlaylist([FromQuery] int? id = null, [FromQuery] bool shared = false)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            Playlist? playlistRecord = null;

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            dynamic? playlist = ms.ObjectFromStream();
            if (playlist == null || !ExpandantoObject.HasProperty(playlist, "songs"))
            {
                return BadRequest("Can't decode songs");
            }

            if (id != null) {
                playlistRecord = await _context.Playlists.FirstOrDefaultAsync(t => t.OwnerId == currentID && t.Id == id);
                if (playlistRecord == null) {
                    return NotFound();
                }
            }

            if (playlistRecord == null) {
                playlistRecord = new Playlist {
                    OwnerId = currentID,
                    IsShared = shared,
                    Link = ""
                };
                _context.Playlists.Add(playlistRecord);
                await _context.SaveChangesAsync();
                id = playlistRecord.Id;
            }

            playlistRecord.Hash = CalculateSha256(playlist);
            playlistRecord.IsShared = shared;

            var customData = new PlaylisCustomData { 
                syncURL = "https://api.beatleader.com/playlist/guid/" + playlistRecord.Guid.ToString().Replace("-", ""),
                owner = currentID,
                id = id?.ToString() ?? "",
                hash = playlistRecord.Hash,
                shared = playlistRecord.IsShared
            };
            playlist.customData = customData;
            await _context.SaveChangesAsync();

            await S3Helper.UploadPlaylist(_s3Client, id + ".bplist", playlist);

            return customData;
        }

        [HttpDelete("~/user/playlist")]
        public async Task<ActionResult<int>> DeletePlaylist([FromQuery] int id, [FromQuery] bool shouldDelete = true)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            Playlist? playlistRecord = await _context.Playlists.FirstOrDefaultAsync(t => t.OwnerId == currentID && t.Id == id);

            if (playlistRecord == null)
            {
                return NotFound();
            }
            playlistRecord.Deleted = shouldDelete;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [NonAction]
        public List<string> DeletedSongs() {
            return new List<string> { 
                "9a8581460386e3d3cca3ba62ee4bd621c131980c", 
                "b0872bcae0aafbb54702c2ebfd0fd9cf0df13085",
                "3d9693911c5e357bf4d9323535429a95f80287ac",
                "19ab20b044081cbae0b25c3ed5a1891b6736e56b",
                "cb233e1c3b00356a597c87014dc1c62d3f4cf01a",
                "de7b5e933dd79d06ddee3df71174019cbe9ebd45",
                "cca7d91180a7b58dc627fc2dcbae19cad077db8a",
                "2c2a2a72296b634fc1fe36a7f023e9ac2dbaa4bf",
                "7b86bfc27333e95a2e8803e00341ed3be886513a",
                "953da829bf2d43420c8681b34055d691ed3714fc",
                "c1dd3192c18d7c957d1895746caa67888c039af2",
                "776109a286e36b402ce5ac46d445446f5e8c36cc",
                "2e5b4b6b9f754324185525db563f8663fa91622d",
                "0a183f55cca6bf09e109db0b78c3005a1bca089f",
                "553253d7508b0ec6c31c97f364ccb7825164c964",
                "3975a20d591b36daa9ac00aa562771ac3fb22939",
                "0f1be8e02301d8ddc59dc6cee93e848486e6f603",
                "fa39026be59a0d04e750809da668f650faea6fe7",
                "644c89f0a9bf7b36f38b111ada3f82d7e0d471bb",
                "a3ad382316dc86d3f55a2d4df415b7d2ea8cc537",
                "c9f52dc03620c92d7b34cba1b505e61f890918d0",
                "07f970d15410264bf3b2a32aa0066146f0dca55e",
                "57431519f148298175a53720b68907a6f4a52456",
                "0f7520afd517fd304e125acccbc8e61ae9014bb1",
                "c2c2474fd9a5433b3f2abbbf6cbf7e60d6ac342a",
                "bd8154b15ea629463dfd608ef851b30c0a99e357",
                "5a7c419cb7a3cf61162bd51cdc98db9b72cc37fa",
                "a3b6b0698fba19b277f5533fbfa390724cf04fbe",
                "d7145f9c4346d4f83940aab9d2d85983809359e5",
                "14ed3fb47b8c95c36306b941a9761b203e54f72f",
                "aa41a5468a151c788b5b78d93eb34c284c764f94",
                "985eafb71603c2d363218ec2e9b27afe263c17f3",
                "af066d97fb39f079c32d3dacb2885c1004b748b6",
                "29e24e9217a1ebc72993f66615eb4f4f599ca4a5",
                "50749cc6ee2d4e43abe11bfac1bc0d5c53632d19",
                "61c0d51259f7f851ce53b795b2afb2ad2d3c232d" };
        }

        [Authorize]
        [HttpGet("~/playlist/refreshranked")]
        public async Task<ActionResult> RefreshRankedPlaylist()
        {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist("ranked.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                } else {
                    using (var filestream = new FileStream(_environment.WebRootPath + "/playlists/ranked.bplist", FileMode.Open)) {
                        playlist = filestream.ObjectFromStream();
                    }
                }
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            var deleted = DeletedSongs();

            var songs = await _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).Where(lb => !deleted.Contains(lb.Song.Hash.ToLower())).Select(lb => new {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = lb.Song.Difficulties.Where(d => d.Status == DifficultyStatus.ranked).Select(d => new {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                }),
                rankedTime = lb.Difficulty.RankedTime
            }).ToListAsync();

            playlist.songs = songs.DistinctBy(s => s.hash).OrderByDescending(a => a.rankedTime).ToList();
            playlist.customData = new PlaylisCustomData
            {
                syncURL = "https://api.beatleader.com/playlist/ranked",
                owner = "BeatLeader",
                id = "ranked"
            };

            await S3Helper.UploadPlaylist(_s3Client, "ranked.bplist", playlist);

            return Ok();
        }

        [Authorize]
        [HttpGet("~/playlist/refreshnominated")]
        public async Task<ActionResult> RefreshNominatedPlaylist()
        {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist("nominated.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                } else {
                    using (var filestream = new FileStream(_environment.WebRootPath + "/playlists/nominated.bplist", FileMode.Open)) {
                        playlist = filestream.ObjectFromStream();
                    }
                }
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }


            var deleted = DeletedSongs();

            var songs = await _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.nominated).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).Where(lb => !deleted.Contains(lb.Song.Hash.ToLower())).Select(lb => new
            {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = lb.Song.Difficulties.Where(d => d.Status == DifficultyStatus.nominated).Select(d => new
                {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                }),
                nominatedTime = lb.Difficulty.NominatedTime
            }).ToListAsync();

            playlist.songs = songs.DistinctBy(s => s.hash).OrderByDescending(a => a.nominatedTime).ToList();
            playlist.customData = new PlaylisCustomData
            {
                syncURL = "https://api.beatleader.com/playlist/nominated",
                owner = "BeatLeader",
                id = "nominated"
            };

            await S3Helper.UploadPlaylist(_s3Client, "nominated.bplist", playlist);

            return Ok();
        }

        [Authorize]
        [HttpGet("~/playlist/refreshqualified")]
        public async Task<ActionResult> RefreshQualifiedPlaylist()
        {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist("qualified.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                } else {
                    using (var filestream = new FileStream(_environment.WebRootPath + "/playlists/qualified.bplist", FileMode.Open)) {
                        playlist = filestream.ObjectFromStream();
                    }
                }
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            var deleted = DeletedSongs();

            var songs = await _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.qualified).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).Where(lb => !deleted.Contains(lb.Song.Hash.ToLower())).Select(lb => new
            {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = lb.Song.Difficulties.Where(d => d.Status == DifficultyStatus.qualified).Select(d => new
                {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                }),
                qualifiedTime = lb.Difficulty.QualifiedTime
            }).ToListAsync();

            playlist.songs = songs.DistinctBy(s => s.hash).OrderByDescending(a => a.qualifiedTime).ToList();
            playlist.customData = new PlaylisCustomData
            {
                syncURL = "https://api.beatleader.com/playlist/qualified",
                owner = "BeatLeader",
                id = "qualified"
            };

            await S3Helper.UploadPlaylist(_s3Client, "qualified.bplist", playlist);

            return Ok();
        }

        [HttpGet("~/playlist/generate")]
        public async Task<ActionResult<string>> GetAll(
            [FromQuery] int count = 100,
            [FromQuery] MapSortBy sortBy = MapSortBy.Stars,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] Type type = Type.All,
            [FromQuery] string? mode = null,
            [FromQuery] string? difficulty = null,
            [FromQuery] int? mapType = null,
            [FromQuery] Operation allTypes = Operation.Any,
            [FromQuery] Requirements mapRequirements = Requirements.Ignore,
            [FromQuery] Operation allRequirements = Operation.Any,
            [FromQuery] SongStatus songStatus = SongStatus.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] MyType mytype = MyType.None,
            [FromQuery] string? playlistIds = null,
            [FromBody] List<PlaylistResponse>? playlists = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] float? accrating_from = null,
            [FromQuery] float? accrating_to = null,
            [FromQuery] float? passrating_from = null,
            [FromQuery] float? passrating_to = null,
            [FromQuery] float? techrating_from = null,
            [FromQuery] float? techrating_to = null,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null,
            [FromQuery] string? types = null,
            [FromQuery] string? mappers = null,
            [FromQuery] bool duplicate_diffs = false,
            [FromQuery] string? title = null)
        {
            if (count > 2000) {
                return Unauthorized("Count is too big. 2000 max");
            }

            var sequence = _context.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            int searchCount = 0;
            var playlistList = await LeaderboardControllerHelper.GetPlaylistList(_context, currentID, _s3Client, playlistIds, playlists);
            sequence = sequence
                .Filter(_context, out int? searchId, sortBy, order, search, type, types, mode, difficulty, mapType, allTypes, mapRequirements, allRequirements, songStatus, leaderboardContext, mytype, stars_from, stars_to, accrating_from, accrating_to, passrating_from, passrating_to, techrating_from, techrating_to, date_from, date_to, mappers, playlistList, currentPlayer);
            (sequence, int totalMatches) = await sequence.WherePage(0, count);

            var diffsList = sequence.Select(s => s.Song.Hash).AsEnumerable().Select(((s, i) => new { Hash = s, Index = i })).DistinctBy(lb => lb.Hash);

            var diffsCount = diffsList.Count() == 0 ? 0 : diffsList.Take(count).Last().Index + 1;

            sequence = sequence
                .Include(lb => lb.Difficulty)
                .ThenInclude(lb => lb.ModifierValues)
                .Include(lb => lb.Song)
                .Take(diffsCount);

            var diffs = await sequence.Select(lb => new {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = new List<PlaylistDifficulty> { new PlaylistDifficulty
                    {
                        name = lb.Difficulty.DifficultyName.FirstCharToLower(),
                        characteristic = lb.Difficulty.ModeName
                    }
                }
            }).ToListAsync();

            if (searchId != null) {
                var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                foreach (var item in searchRecords) {
                    _context.SongSearches.Remove(item);
                }
            }

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist("searchresult.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                }
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            if (duplicate_diffs) {
                playlist.songs = diffs.Select(diff => 
                 new
                {
                    hash = diff.hash,
                    songName = diff.songName,
                    levelAuthorName = diff.levelAuthorName,
                    difficulties = diff.difficulties
                }
                ).ToList();
            } else {
                playlist.songs = diffs.GroupBy(s => s.hash).Select(group => 
                 new
                {
                    hash = group.First().hash,
                    songName = group.First().songName,
                    levelAuthorName = group.First().levelAuthorName,
                    difficulties = group.Select(s => s.difficulties.First())
                }
                ).ToList();
            }
            playlist.customData = new PlaylisCustomData
            {
                owner = currentID,
                syncURL = HttpContext.Request.GetDisplayUrl(),
            };

            if (!string.IsNullOrEmpty(title))
            {
                playlist.playlistTitle = title;
            }

            return JsonConvert.SerializeObject(playlist);
        }

        [HttpGet("~/playlist/scores/generate")]
        public async Task<ActionResult<string>> ScoresPlaylist(
            [FromQuery] int count = 100,

            [FromQuery] string playerId = "1",
            [FromQuery] ScoresSortBy sortBy = ScoresSortBy.Date,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] DifficultyStatus? type = null,
            [FromQuery] string? mode = null,
            [FromQuery] Requirements requirements = Requirements.None,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] HMD? hmd = null,
            [FromQuery] string? modifiers = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? time_from = null,
            [FromQuery] int? time_to = null,
            [FromQuery] int? eventId = null,

            [FromQuery] bool duplicate_diffs = false)
        {
            if (count > 2000) {
                return Unauthorized("Count is too big. 2000 max");
            }

            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(playerId);
            }
            catch { 
                return BadRequest("Id should be a number");
            }
            AccountLink? link = null;
            if (oculusId < 1000000000000000)
            {
                link = await _context.AccountLinks.FirstOrDefaultAsync(el => el.OculusID == oculusId);
            }
            if (link == null && oculusId < 70000000000000000)
            {
                link = await _context.AccountLinks.FirstOrDefaultAsync(el => el.PCOculusID == playerId);
            }
            string userId = (link != null ? (link.SteamID.Length > 0 ? link.SteamID : link.PCOculusID) : playerId);

            var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == userId);
            if (player == null) {
                return NotFound();
            }

            IQueryable<IScore> query = leaderboardContext == LeaderboardContexts.General 
                ? _context.Scores
                   .AsNoTracking()
                   .Where(t => t.PlayerId == userId && t.ValidForGeneral)
                   .TagWithCaller()
                : _context.ScoreContextExtensions
                   .AsNoTracking()
                   .Include(ce => ce.ScoreInstance)
                   .Where(t => t.PlayerId == userId && t.Context == leaderboardContext)
                   .TagWithCaller();

            (IQueryable<IScore> sequence, int? searchId) = await query
                .Filter(_context, !player.Banned, false, sortBy, order, search, diff, mode, requirements, ScoreFilterStatus.None, type, hmd, modifiers, stars_from, stars_to, time_from, time_to, eventId); 

            if (await sequence.CountAsync() == 0) { return NotFound(); }

            var diffsCount = sequence.Select(s => s.Leaderboard.Song.Hash).AsEnumerable().Select(((s, i) => new { Hash = s, Index = i })).DistinctBy(lb => lb.Hash).Take(count).Last().Index + 1;

            var diffs = await sequence.Take(diffsCount).Select(s => new {
                hash = s.Leaderboard.Song.Hash,
                songName = s.Leaderboard.Song.Name,
                levelAuthorName = s.Leaderboard.Song.Mapper,
                difficulties = new List<PlaylistDifficulty> { new PlaylistDifficulty
                    {
                        name = s.Leaderboard.Difficulty.DifficultyName.FirstCharToLower(),
                        characteristic = s.Leaderboard.Difficulty.ModeName
                    }
                }
            }).ToListAsync();

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist("scoresearchresult.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                }
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            if (duplicate_diffs) {
                playlist.songs = diffs.Select(diff => 
                 new
                {
                    hash = diff.hash,
                    songName = diff.songName,
                    levelAuthorName = diff.levelAuthorName,
                    difficulties = diff.difficulties
                }
                ).ToList();
            } else {
                playlist.songs = diffs.GroupBy(s => s.hash).Select(group => 
                 new
                {
                    hash = group.First().hash,
                    songName = group.First().songName,
                    levelAuthorName = group.First().levelAuthorName,
                    difficulties = group.Select(s => s.difficulties.First())
                }
                ).ToList();
            }
            playlist.customData = new PlaylisCustomData
            {
                owner = HttpContext.CurrentUserID(_context) ?? ""
            };

            if (searchId != null) {
                HttpContext.Response.OnCompleted(async () => {
                    var searchRecords = await _context.SongSearches.Where(s => s.SearchId == searchId).ToListAsync();
                    foreach (var item in searchRecords) {
                        _context.SongSearches.Remove(item);
                    }
                    await _context.BulkSaveChangesAsync();
                });
            }

            return JsonConvert.SerializeObject(playlist);
        }

        [HttpGet("~/playlists/featured")]
        public async Task<ActionResult<List<FeaturedPlaylistResponse>>> FeaturedPlaylists()
        {
            return await _context
                .FeaturedPlaylist
                .OrderByDescending(p => p.Id)
                .Select(fp => new FeaturedPlaylistResponse {
                    Id = fp.Id,
                    PlaylistLink = fp.PlaylistLink,
                    Cover = fp.Cover,
                    Title = fp.Title,
                    Description = fp.Description,

                    Owner = fp.Owner,
                    OwnerCover = fp.OwnerCover,
                    OwnerLink = fp.OwnerLink,
                }).ToListAsync();
        }

        public class LeaderboardSelection {
            public float Pp { get; set; }
            public string LeaderboardId { get; set; }
            public string Hash { get; set; }
            public string Mapper { get; set; }
            public string Name { get; set; }
            public string DifficultyName { get; set; }
            public string ModeName { get; set; }
        }

        [HttpGet("~/playlist/clan/generate")]
        public async Task<ActionResult<string>> ClanMapsPlaylist(
            [FromQuery] float ppLimit = 100,
            [FromQuery] int clanId = 1,
            [FromQuery] ClanMapsSortBy sortBy = ClanMapsSortBy.Tohold,
            [FromQuery] PlayedStatus playedStatus = PlayedStatus.Any,
            [FromQuery] bool duplicate_diffs = false)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            var clanTag = await _context.Clans.Where(c => c.Id == clanId).Select(c => c.Tag).FirstOrDefaultAsync();
            if (clanTag == null) {
                return NotFound();
            }

            var rankings = _context
                .ClanRanking
                .AsNoTracking()
                .Where(p => p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked && p.ClanId == clanId);

            if (currentID != null && playedStatus != PlayedStatus.Any) {
                if (playedStatus == PlayedStatus.Played) {
                    rankings = rankings.Where(p => p.Leaderboard.Scores.Any(s => s.PlayerId == currentID));
                } else {
                    rankings = rankings.Where(p => !p.Leaderboard.Scores.Any(s => s.PlayerId == currentID));
                }
            }

            Order order = Order.Desc;

            switch (sortBy)
            {
                case ClanMapsSortBy.Tohold:
                    rankings = rankings
                        .Where(cr => cr.Rank == 1 && cr.Leaderboard.ClanRanking.Count > 1)
                        .Order(
                            order.Reverse(), 
                            t => t.Pp - t
                                    .Leaderboard
                                    .ClanRanking
                                    .Where(cr => cr.ClanId != clanId && cr.Rank == 2)
                                    .Select(cr => cr.Pp)
                                    .First());
                    break;
                case ClanMapsSortBy.Toconquer:
                    rankings = rankings
                        .Where(cr => cr.Rank != 1 || cr.Leaderboard.ClanRankingContested)
                        .Order(
                            order, 
                            t => t.Pp - t
                                    .Leaderboard
                                    .ClanRanking
                                    .Where(cr => cr.ClanId != clanId && cr.Rank == 1)
                                    .Select(cr => cr.Pp)
                                    .First());
                    break;
                default:
                    break;
            }

            var rankingList = await rankings
            .TagWithCaller()
            .Select(cr => new LeaderboardSelection {
                Pp = cr.Pp,
                LeaderboardId = cr.LeaderboardId,
                Hash = cr.Leaderboard.Song.Hash,
                Mapper = cr.Leaderboard.Song.Mapper,
                Name = cr.Leaderboard.Song.Name,
                DifficultyName = cr.Leaderboard.Difficulty.DifficultyName,
                ModeName = cr.Leaderboard.Difficulty.ModeName,
            })
            .ToListAsync();

            if (sortBy == ClanMapsSortBy.Tohold || sortBy == ClanMapsSortBy.Toconquer) {
                var pps = await rankings
                    .TagWithCaller()
                    .Select(t => new { t.LeaderboardId, Pp = t.Pp, SecondPp = t
                        .Leaderboard
                        .ClanRanking
                        .Where(cr => cr.ClanId != clanId && (cr.Rank == (sortBy == ClanMapsSortBy.Tohold ? 2 : 1)))
                        .Select(cr => cr.Pp)
                        .FirstOrDefault()
                    })
                    .AsSplitQuery()
                    .ToListAsync();

                foreach (var item in pps)
                {
                    rankingList.First(cr => cr.LeaderboardId == item.LeaderboardId).Pp = item.Pp - item.SecondPp;
                }
            }

            var diffsCount = rankingList.Where(r => Math.Abs(r.Pp) < ppLimit).Select(s => s.Hash).AsEnumerable().Select(((s, i) => new { Hash = s, Index = i })).DistinctBy(lb => lb.Hash).Last().Index + 1;

            var diffs = rankingList.Take(diffsCount).Select(s => new {
                hash = s.Hash,
                songName = s.Name,
                levelAuthorName = s.Mapper,
                difficulties = new List<PlaylistDifficulty> { new PlaylistDifficulty
                    {
                        name = s.DifficultyName.FirstCharToLower(),
                        characteristic = s.ModeName
                    }
                }
            }).ToList();

            dynamic? playlist = null;

            using (var stream = await _s3Client.DownloadPlaylist("clanmapsresult.bplist")) {
                if (stream != null) {
                    playlist = stream.ObjectFromStream();
                }
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            playlist.playlistTitle = "[" + clanTag + "] " + (sortBy == ClanMapsSortBy.Tohold ? "maps to hold" : "maps to conquer");

            if (duplicate_diffs) {
                playlist.songs = diffs.Select(diff => 
                 new
                {
                    hash = diff.hash,
                    songName = diff.songName,
                    levelAuthorName = diff.levelAuthorName,
                    difficulties = diff.difficulties
                }
                ).ToList();
            } else {
                playlist.songs = diffs.GroupBy(s => s.hash).Select(group => 
                 new
                {
                    hash = group.First().hash,
                    songName = group.First().songName,
                    levelAuthorName = group.First().levelAuthorName,
                    difficulties = group.Select(s => s.difficulties.First())
                }
                ).ToList();
            }

            playlist.customData = new PlaylisCustomData
            {
                owner = currentID ?? ""
            };

            return JsonConvert.SerializeObject(playlist);
        }

        [HttpPost("~/user/playlist/{id}/toInstall")]
        public async Task<ActionResult<string>> AddToInstall(string id)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            var user = await _context.Users.Where(u => u.Id == currentID).FirstOrDefaultAsync();
            if (user == null) return Unauthorized();

            if (user.PlaylistsToInstall == null) {
                user.PlaylistsToInstall = "";
            }

            user.PlaylistsToInstall = string.Join(",", user.PlaylistsToInstall.Split(",").Append(id).Distinct().ToList());
            await _context.SaveChangesAsync();

            return user.PlaylistsToInstall;
        }

        [HttpDelete("~/user/playlist/{id}/toInstall")]
        public async Task<ActionResult<string>> RemoveToInstall(string id)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            var user = await _context.Users.Where(u => u.Id == currentID).FirstOrDefaultAsync();
            if (user == null) return Unauthorized();

            if (user.PlaylistsToInstall == null) {
                user.PlaylistsToInstall = "";
            }

            user.PlaylistsToInstall = string.Join(",", user.PlaylistsToInstall.Split(",").Where(p => p != id).Distinct().ToList());
            await _context.SaveChangesAsync();

            return user.PlaylistsToInstall;
        }

        [HttpDelete("~/user/playlist/toInstall")]
        public async Task<ActionResult> RemoveAllToInstall()
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            var user = await _context.Users.Where(u => u.Id == currentID).FirstOrDefaultAsync();
            if (user == null) return Unauthorized();

            user.PlaylistsToInstall = null;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
