using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlaylistController : Controller
    {
        private readonly AppContext _context;
        BlobContainerClient _playlistContainerClient;
        IWebHostEnvironment _environment;

        public PlaylistController(
            AppContext context,
            IOptions<AzureStorageConfig> config,
            IWebHostEnvironment env)
        {
            _context = context;
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
            _environment = env;
        }

        [Authorize]
        [HttpGet("~/user/oneclickplaylist")]
        public async Task<ActionResult> GetOneClickPlaylist()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
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

        ExpandoObject? ObjectFromStream(MemoryStream ms) {
            using (StreamReader reader = new StreamReader(ms))
            {
                string results = reader.ReadToEnd();
                if (string.IsNullOrEmpty(results))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
            }
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

            dynamic? mapscontainer = ObjectFromStream(ms);
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

            dynamic? playlist = ObjectFromStream(stream);
            if (playlist == null)
            {
                stream.Position = 0;
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
                await blobClient.DownloadToAsync(stream);
                playlist = ObjectFromStream(stream);
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

            dynamic? playlist = ObjectFromStream(stream);
            if (playlist == null)
            {
                stream.Position = 0;
                blobClient = _playlistContainerClient.GetBlobClient("oneclick.bplist");
                await blobClient.DownloadToAsync(stream);
                playlist = ObjectFromStream(stream);
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
        public async Task<ActionResult<IEnumerable<Playlist>>> Get()
        {
            return await _context.Playlists.Where(t=>t.IsShared).ToListAsync();
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

        [HttpGet("~/playlist/qualified")]
        public async Task<ActionResult> GetQualified()
        {
            BlobClient blobClient = _playlistContainerClient.GetBlobClient("qualified.bplist");
            MemoryStream stream = new MemoryStream(5);

            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            return File(stream, "application/json");
        }

        [HttpGet("~/user/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> GetAllPlaylists()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            return await _context.Playlists.Where(t => t.OwnerId == currentID).ToListAsync();
        }

        [HttpPost("~/user/playlist")]
        public async Task<ActionResult<int>> PostPlaylist([FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) return Unauthorized();

            Playlist? playlistRecord = null;
            
            if (id != null) {
               playlistRecord = await _context.Playlists.Where(t => t.OwnerId == currentID && t.Id == id).FirstOrDefaultAsync();
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

            dynamic? playlist = ObjectFromStream(ms);
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

            Playlist? playlistRecord = await _context.Playlists.Where(t => t.OwnerId == currentID && t.Id == id).FirstOrDefaultAsync();

            if (playlistRecord == null)
            {
                return NotFound();
            }

            await _playlistContainerClient.DeleteBlobIfExistsAsync(id + ".bplist");
            _context.Playlists.Remove(playlistRecord);
            await _context.SaveChangesAsync();

            return Ok();
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

            dynamic? playlist = ObjectFromStream(stream);

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            var deleted = new List<string> { "9a8581460386e3d3cca3ba62ee4bd621c131980c" };

            var songs = _context.Songs.Include(s => s.Difficulties).Where(s => !deleted.Contains(s.Hash.ToLower()) && s.Difficulties.FirstOrDefault(d => d.Ranked) != null).Select(s => new {
                hash = s.Hash,
                songName = s.Name,
                levelAuthorName = s.Mapper,
                difficulties = s.Difficulties.Where(d => d.Ranked).Select(d => new {
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

            dynamic? playlist = ObjectFromStream(stream);

            if (playlist == null)
            {
                return BadRequest("Original plist dead. Wake up NSGolova!");
            }

            var songs = _context.Songs.Include(s => s.Difficulties).Where(s => s.Difficulties.FirstOrDefault(d => d.Qualified) != null).Select(s => new {
                hash = s.Hash,
                songName = s.Name,
                levelAuthorName = s.Mapper,
                difficulties = s.Difficulties.Where(d => d.Qualified).Select(d => new {
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

    }
}
