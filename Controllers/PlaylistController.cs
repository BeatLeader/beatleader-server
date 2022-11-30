using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Migrations.ReadApp;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlaylistController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readAppContext;

        BlobContainerClient _playlistContainerClient;
        BlobContainerClient _assetsContainerClient;
        ScoreRefreshController _scoreRefreshController;
        IWebHostEnvironment _environment;

        public PlaylistController(
            AppContext context,
            ReadAppContext readAppContext,
            ScoreRefreshController scoreRefreshController,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env)
        {
            _context = context;
            _readAppContext = readAppContext;
            _scoreRefreshController = scoreRefreshController;
            if (env.IsDevelopment())
            {
                _playlistContainerClient = new BlobContainerClient(config.Value.AccountName, config.Value.PlaylistContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.PlaylistContainerName);

                _playlistContainerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
            if (env.IsDevelopment())
            {
                _assetsContainerClient = new BlobContainerClient(config.Value.AccountName, config.Value.AssetsContainerName);
            }
            else
            {
                string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                        config.Value.AccountName,
                                                       config.Value.AssetsContainerName);

                _assetsContainerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
            }
            _environment = env;
        }

        [Authorize]
        [HttpGet("~/user/oneclickplaylist")]
        public async Task<ActionResult> GetOneClickPlaylist()
        {
            string? currentID = HttpContext.CurrentUserID(_readAppContext);
            if (currentID == null) return Unauthorized();

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(currentID + "oneclick.bplist");
            MemoryStream stream = new MemoryStream(5);
            if (!(await blobClient.ExistsAsync())) {
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
            }

            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            return File(stream, "application/json"); ;
        }

        public class CustomData
        {
            public string syncURL { get; set; }
            public string owner { get; set; }
            public string id { get; set; }
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

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(currentID + "oneclick.bplist");
            MemoryStream stream = new MemoryStream(5);
            if (!(await blobClient.ExistsAsync()))
            {
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
            }
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();
            if (playlist == null)
            {
                stream.Position = 0;
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
                await blobClient.DownloadToAsync(stream);
                playlist = stream.ObjectFromStream();
            }

            if (playlist == null) {
                return BadRequest("Original plist dead. Wake up NSGolova");
            }

            playlist.songs = mapscontainer.songs;

            await _playlistContainerClient.DeleteBlobIfExistsAsync(currentID + "oneclick.bplist");
            await _playlistContainerClient.UploadBlobAsync(currentID + "oneclick.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return Ok();
        }

        [Authorize]
        [HttpGet("~/user/oneclickdone")]
        public async Task<ActionResult> CleanOneClickPlaylist()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(currentID + "oneclick.bplist");
            MemoryStream stream = new MemoryStream(5);
            if (!(await blobClient.ExistsAsync()))
            {
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
            }
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();
            if (playlist == null)
            {
                stream.Position = 0;
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
                await blobClient.DownloadToAsync(stream);
                playlist = stream.ObjectFromStream();
            }

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova");
            }

            playlist.songs = new List<string>();

            await _playlistContainerClient.DeleteBlobIfExistsAsync(currentID + "oneclick.bplist");
            await _playlistContainerClient.UploadBlobAsync(currentID + "oneclick.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return Ok();
        }

        [HttpGet("~/playlists")]
        public ActionResult<IEnumerable<Playlist>> Get()
        {
            return _readAppContext.Playlists.Where(t=>t.IsShared).ToList();
        }

        [HttpGet("~/playlist/{id}")]
        public async Task<ActionResult> GetById(string id)
        {
            BlobClient blobClient = _playlistContainerClient.GetBlobClient(id + ".bplist");
            MemoryStream stream = new MemoryStream(5);

            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            return File(stream, "application/json");
        }

        [HttpGet("~/user/playlists")]
        public ActionResult<IEnumerable<Playlist>> GetAllPlaylists()
        {
            string? currentID = HttpContext.CurrentUserID(_readAppContext);
            if (currentID == null) return Unauthorized();

            return _readAppContext.Playlists.Where(t => t.OwnerId == currentID).ToList();
        }

        [HttpPost("~/user/playlist")]
        public async Task<ActionResult<int>> PostPlaylist([FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            Playlist? playlistRecord = null;
            
            if (id != null) {
               playlistRecord = _context.Playlists.FirstOrDefault(t => t.OwnerId == currentID && t.Id == id);
            }

            if (playlistRecord == null) {
                playlistRecord = new Playlist {
                    OwnerId = currentID,
                    Link = ""
                };
                _context.Playlists.Add(playlistRecord);
                await _context.SaveChangesAsync();
                id = playlistRecord.Id;
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            dynamic? playlist = ms.ObjectFromStream();
            if (playlist == null || !ExpandantoObject.HasProperty(playlist, "songs"))
            {
                return BadRequest("Can't decode songs");
            }
            playlist.customData = new CustomData { 
                syncURL = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/playlists/" : "https://cdn.beatleader.xyz/playlists/") + id + ".bplist",
                owner = currentID,
                id = id.ToString()
            };

            await _playlistContainerClient.DeleteBlobIfExistsAsync(id + ".bplist");
            await _playlistContainerClient.UploadBlobAsync(id + ".bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return id;
        }

        [HttpDelete("~/user/playlist")]
        public async Task<ActionResult<int>> DeletePlaylist([FromQuery] int id)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            Playlist? playlistRecord = _context.Playlists.FirstOrDefault(t => t.OwnerId == currentID && t.Id == id);

            if (playlistRecord == null)
            {
                return NotFound();
            }

            await _playlistContainerClient.DeleteBlobIfExistsAsync(id + ".bplist");
            _context.Playlists.Remove(playlistRecord);
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

            BlobClient blobClient = _playlistContainerClient.GetBlobClient("ranked.bplist");
            MemoryStream stream = new MemoryStream(5);
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }


            var deleted = DeletedSongs();

            var songs = _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).Where(lb => !deleted.Contains(lb.Song.Hash.ToLower())).Select(lb => new {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = lb.Song.Difficulties.Where(d => d.Status == DifficultyStatus.ranked).Select(d => new {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                })
            }).ToList();

            playlist.songs = songs.DistinctBy(s => s.hash).ToList();
            playlist.customData = new CustomData { 
                syncURL = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/playlists/" : "https://cdn.beatleader.xyz/playlists/") + "ranked.bplist",
                owner = "BeatLeader",
                id = "ranked"
            };

            await _playlistContainerClient.DeleteBlobIfExistsAsync("ranked.bplist");
            await _playlistContainerClient.UploadBlobAsync("ranked.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

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

            BlobClient blobClient = _playlistContainerClient.GetBlobClient("nominated.bplist");
            MemoryStream stream = new MemoryStream(5);
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }


            var deleted = DeletedSongs();

            var songs = _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.nominated).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).Where(lb => !deleted.Contains(lb.Song.Hash.ToLower())).Select(lb => new {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = lb.Song.Difficulties.Where(d => d.Status == DifficultyStatus.nominated).Select(d => new {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                })
            }).ToList();

            playlist.songs = songs.DistinctBy(s => s.hash).ToList();
            playlist.customData = new CustomData
            {
                syncURL = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/playlists/" : "https://cdn.beatleader.xyz/playlists/") + "nominated.bplist",
                owner = "BeatLeader",
                id = "nominated"
            };

            await _playlistContainerClient.DeleteBlobIfExistsAsync("nominated.bplist");
            await _playlistContainerClient.UploadBlobAsync("nominated.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

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

            BlobClient blobClient = _playlistContainerClient.GetBlobClient("qualified.bplist");
            MemoryStream stream = new MemoryStream(5);
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            var deleted = DeletedSongs();

            var songs = _context.Leaderboards.Where(lb => lb.Difficulty.Status == DifficultyStatus.qualified).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).Where(lb => !deleted.Contains(lb.Song.Hash.ToLower())).Select(lb => new {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = lb.Song.Difficulties.Where(d => d.Status == DifficultyStatus.qualified).Select(d => new {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                })
            }).ToList();

            playlist.songs = songs.DistinctBy(s => s.hash).ToList();
            playlist.customData = new CustomData { 
                syncURL = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/playlists/" : "https://cdn.beatleader.xyz/playlists/") + "qualified.bplist",
                owner = "BeatLeader",
                id = "qualified"
            };

            await _playlistContainerClient.DeleteBlobIfExistsAsync("qualified.bplist");
            await _playlistContainerClient.UploadBlobAsync("qualified.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return Ok();
        }

        public class PlaylistDifficulty {
            public string name { get; set; }
            public string characteristic { get; set; }
        }

        [HttpGet("~/playlist/generate")]
        public async Task<ActionResult<string>> GetAll(
            [FromQuery] int count = 100,
            [FromQuery] string sortBy = "stars",
            [FromQuery] string order = "desc",
            [FromQuery] string? search = null,
            [FromQuery] string? type = null,
            [FromQuery] int? mapType = null,
            [FromQuery] int allTypes = 0,
            [FromQuery] string? mytype = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null,
            [FromQuery] int? date_from = null,
            [FromQuery] int? date_to = null,
            [FromQuery] bool duplicate_diffs = false)
        {
            if (count > 2000) {
                return Unauthorized("Count is too big. 2000 max");
            }

            var sequence = _readAppContext.Leaderboards.AsQueryable();
            string? currentID = HttpContext.CurrentUserID(_readAppContext);

            sequence = sequence.Filter(_readAppContext, sortBy, order, search, type, mapType, allTypes, mytype, stars_from, stars_to, date_from, date_to, currentID);

            var diffsCount = sequence.Select(s => s.Song.Hash).AsEnumerable().Select(((s, i) => new { Hash = s, Index = i })).DistinctBy(lb => lb.Hash).Take(count).Last().Index + 1;

            sequence = sequence
                .Include(lb => lb.Difficulty)
                .ThenInclude(lb => lb.ModifierValues)
                .Include(lb => lb.Song)
                .Take(diffsCount);

            var diffs = sequence.Select(lb => new {
                hash = lb.Song.Hash,
                songName = lb.Song.Name,
                levelAuthorName = lb.Song.Mapper,
                difficulties = new List<PlaylistDifficulty> { new PlaylistDifficulty
                    {
                        name = lb.Difficulty.DifficultyName.FirstCharToLower(),
                        characteristic = lb.Difficulty.ModeName
                    }
                }
            }).ToList();

            BlobClient blobClient = _playlistContainerClient.GetBlobClient("searchresult.bplist");
            MemoryStream stream = new MemoryStream(5);
            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();

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
            playlist.customData = new CustomData
            {
                owner = currentID
            };

            return JsonConvert.SerializeObject(playlist);
        }

        [HttpPost("~/event/start/{id}")]
        public async Task<ActionResult> StartEvent(
               int id, 
               [FromQuery] string name,
               [FromQuery] int endDate)
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

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(id + ".bplist");
            MemoryStream stream = new MemoryStream(5);

            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            dynamic? playlist = stream.ObjectFromStream();

            if (playlist == null)
            {
                return BadRequest("Can't find such plist");
            }

            string fileName = id + "-event";
            try
            {
                await _assetsContainerClient.CreateIfNotExistsAsync();

                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream2) = ImageUtils.GetFormatAndResize(ms);
                fileName += extension;

                await _assetsContainerClient.DeleteBlobIfExistsAsync(fileName);
                await _assetsContainerClient.UploadBlobAsync(fileName, stream2);
            }
            catch (Exception)
            {
                return BadRequest("Error saving avatar");
            }

            var leaderboards = new List<Leaderboard>();
            var players = new List<EventPlayer>();
            var basicPlayers = new List<Player>();
            var playerScores = new Dictionary<string, List<Score>>();

            foreach (var song in playlist.songs) {
                foreach (var diff in song.difficulties)
                {
                    string hash = song.hash.ToLower();
                    string diffName = diff.name.ToLower();
                    string characteristic = diff.characteristic.ToLower();

                    var lb = _context.Leaderboards.Where(lb => 
                        lb.Song.Hash.ToLower() == hash && 
                        lb.Difficulty.DifficultyName.ToLower() == diffName &&
                        lb.Difficulty.ModeName.ToLower() == characteristic).Include(lb => lb.Difficulty).Include(lb => lb.Scores).ThenInclude(s => s.Player).FirstOrDefault();

                    if (lb != null && lb.Difficulty.Status != DifficultyStatus.outdated) {

                        if (lb.Difficulty.Status == DifficultyStatus.unranked || lb.Difficulty.Status == DifficultyStatus.inevent)
                        {
                            var stars = await ExmachinaStars(hash, lb.Difficulty.Value);
                            if (stars != null) {

                                lb.Difficulty.Status = DifficultyStatus.inevent;
                                lb.Difficulty.Stars = stars;
                                await _context.SaveChangesAsync();

                                await _scoreRefreshController.RefreshScores(lb.Id);
                                leaderboards.Add(lb);
                            } else { continue; }
                        } else {
                            leaderboards.Add(lb);
                        }
                        
                        foreach (var score in lb.Scores)
                        {
                            if (players.FirstOrDefault(p => p.PlayerId == score.PlayerId) == null) {
                                players.Add(new EventPlayer {
                                    PlayerId = score.PlayerId,
                                    Country = score.Player.Country,
                                    Name = name,
                                });
                                basicPlayers.Add(score.Player);
                                playerScores[score.PlayerId] = new List<Score> { score };
                            } else {
                                playerScores[score.PlayerId].Add(score);
                            }
                        }
                    }
                }
            }

            foreach (var player in players) {
                float resultPP = 0f;
                foreach ((int i, Score s) in playerScores[player.PlayerId].OrderByDescending(s => s.Pp).Select((value, i) => (i, value)))
                {

                    resultPP += s.Pp * MathF.Pow(0.925f, i);
                }

                player.Pp = resultPP;
            }

            Dictionary<string, int> countries = new Dictionary<string, int>();
            
            int ii = 0;
            foreach (EventPlayer p in players.OrderByDescending(s => s.Pp))
            {
                if (p.Rank != 0) {
                    p.Rank = p.Rank;
                }
                p.Rank = ii + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;
                ii++;
            }

            var eventRanking = new EventRanking
            {
                Name = name,
                Leaderboards = leaderboards,
                Players = players,
                EndDate = endDate,
                PlaylistId = id,
                Image = (_environment.IsDevelopment() ? "http://127.0.0.1:10000/devstoreaccount1/assets/" : "https://cdn.beatleader.xyz/assets/") + fileName
            };

            _context.EventRankings.Add(eventRanking);

            await _context.SaveChangesAsync();

            foreach (var player in players)
            {
                var basicPlayer = basicPlayers.FirstOrDefault(p => p.Id == player.PlayerId);
                if (basicPlayer != null) {
                    if (basicPlayer.EventsParticipating == null) {
                        basicPlayer.EventsParticipating = new List<EventPlayer>();
                    }
                    basicPlayer.EventsParticipating.Add(player);
                }
                player.EventId = eventRanking.Id;
                player.Name = eventRanking.Name;
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [NonAction]
        public async Task<float?> ExmachinaStars(string hash, int diff) {

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://bs-replays-ai.azurewebsites.net/json/" + hash + "/" + diff + "/basic");
            request.Method = "GET";
            request.Proxy = null;

            try {
                var response = await request.DynamicResponse();
                return (float?)response?.balanced;
            } catch { return 4.2f; }

            
        }

        [HttpGet("~/event/{id}/refresh")]
        public async Task<ActionResult> RefreshEvent(int id)
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

            var eventRanking = _context.EventRankings
                .Where(e => e.Id == id)
                .Include(e => e.Leaderboards)
                .ThenInclude(lb => lb.Scores)
                .ThenInclude(s => s.Player)
                .ThenInclude(pl => pl.EventsParticipating).FirstOrDefault();

            List<Player> players = new List<Player>();

            foreach (var lb in eventRanking.Leaderboards)
            {
                foreach (var score in lb.Scores)
                {
                    if (!players.Contains(score.Player)) {
                        players.Add(score.Player);
                    }
                }
            }

            foreach (var player in players)
            {
                _context.RecalculateEventsPP(player, eventRanking.Leaderboards.First());
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/event/{id}")]
        public ActionResult<EventRanking?> GetEvent(int id) {
            return _readAppContext.EventRankings.FirstOrDefault(e => e.Id == id);
        }

        [HttpGet("~/events")]
        public ActionResult<ResponseWithMetadata<EventResponse>> GetEvents(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string? sortBy = "date",
            [FromQuery] string? search = null,
            [FromQuery] string? order = "desc")
        {
            IQueryable<EventRanking> query = _readAppContext.EventRankings.Include(e => e.Players);

            switch (sortBy)
            {
                case "date":
                    query = query.Order(order, p => p.EndDate);
                    break;
                default:
                    break;
            }

            var result = new ResponseWithMetadata<EventResponse>
            {
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = query.Count()
                }
            };

            result.Data = query.Select(e => new EventResponse {
                Id = e.Id,
                Name = e.Name,
                EndDate = e.EndDate,
                PlaylistId = e.PlaylistId,
                Image = e.Image,

                PlayerCount = e.Players.Count(),
                Leader = new PlayerResponse {
                    Id = e.Players.OrderByDescending(p => p.Pp).FirstOrDefault().PlayerId
                }
            }).Skip((page - 1) * count).Take(count).ToList();

            foreach (var item in result.Data)
            {
                item.Leader = ResponseFromPlayer(_context.Players.Include(p => p.Clans).FirstOrDefault(p => p.Id == item.Leader.Id));
            }

            return result;
        }
    }
}
