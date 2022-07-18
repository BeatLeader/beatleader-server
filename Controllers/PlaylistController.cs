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
        }

        [Authorize]
        [HttpGet("~/user/oneclickplaylist")]
        public async Task<ActionResult> GetOneClickPlaylist()
        {
            string userId = HttpContext.CurrentUserID(_context);

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(userId + "oneclick.bplist");
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

        [Authorize]
        [HttpPost("~/user/oneclickplaylist")]
        public async Task<ActionResult> UpdateOneClickPlaylist()
        {
            string userId = HttpContext.CurrentUserID(_context);

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            dynamic? mapscontainer = ObjectFromStream(ms);
            if (mapscontainer == null || !ExpandantoObject.HasProperty(mapscontainer, "songs")) {
                return BadRequest("Can't decode songs");
            }

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(userId + "oneclick.bplist");
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

            await _playlistContainerClient.DeleteBlobIfExistsAsync(userId + "oneclick.bplist");
            await _playlistContainerClient.UploadBlobAsync(userId + "oneclick.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return Ok();
        }

        [Authorize]
        [HttpGet("~/user/oneclickdone")]
        public async Task<ActionResult> CleanOneClickPlaylist()
        {
            string userId = HttpContext.CurrentUserID(_context);

            BlobClient blobClient = _playlistContainerClient.GetBlobClient(userId + "oneclick.bplist");
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

            await _playlistContainerClient.DeleteBlobIfExistsAsync(userId + "oneclick.bplist");
            await _playlistContainerClient.UploadBlobAsync(userId + "oneclick.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return Ok();
        }

        [HttpGet("~/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> Get()
        {
            return await _context.Playlists.Where(t=>t.IsShared).ToListAsync();
        }

        [HttpGet("~/playlist")]
        public async Task<ActionResult<Playlist>> Get([FromQuery] int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);

            if (playlist == null)
            {
                return NotFound();
            }

            if (!playlist.IsShared && playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }

            return playlist;
        }

        [HttpGet("~/playlist/ranked")]
        public async Task<ActionResult> GetRanked()
        {
            BlobClient blobClient = _playlistContainerClient.GetBlobClient("ranked.bplist");
            MemoryStream stream = new MemoryStream(5);

            await blobClient.DownloadToAsync(stream);
            stream.Position = 0;

            return File(stream, "application/json");
        }

        [HttpGet("~/user/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> GetAllPlaylists()
        {
            string currentId = HttpContext.CurrentUserID();
            return await _context.Playlists.Where(t => t.OwnerId == currentId).ToListAsync();
        }

        [HttpGet("~/user/playlist")]
        public async Task<ActionResult<Playlist>> GetFromUser([FromQuery] int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);

            if (playlist == null)
            {
                return NotFound();
            }

            if (!playlist.IsShared && playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }

            return playlist;
        }

        [HttpPost("~/user/playlist")]
        public async Task<ActionResult<int>> PostPlaylist([FromBody] dynamic content, [FromQuery] bool shared)
        {
            Playlist newPlaylist = new Playlist();
            //newPlaylist.Value = content.GetRawText();
            newPlaylist.OwnerId = HttpContext.CurrentUserID();
            newPlaylist.IsShared = shared;
            _context.Playlists.Add(newPlaylist);

            await _context.SaveChangesAsync();

            return CreatedAtAction("PostPlaylist", new { id = newPlaylist.Id }, newPlaylist);
        }

        [HttpDelete("~/user/playlist")]
        public async Task<ActionResult<int>> DeletePlaylist([FromQuery] int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);
            if (playlist == null)
            {
                return NotFound();
            }

            if (playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }
            _context.Playlists.Remove(playlist);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPatch("~/user/playlist")]
        public async Task<ActionResult<Playlist>> SharePlaylist([FromBody] dynamic content, [FromQuery] bool shared, [FromQuery] int id)
        {
            var playlist = await _context.Playlists.FindAsync(id);

            if (playlist == null)
            {
                return NotFound();
            }

            if (playlist.OwnerId != HttpContext.CurrentUserID())
            {
                return Unauthorized();
            }

            //playlist.Value = content.GetRawText();
            playlist.IsShared = shared;

            _context.Playlists.Update(playlist);

            await _context.SaveChangesAsync();

            return playlist;
        }

        [Authorize]
        [HttpGet("~/playlist/refreshranked")]
        public async Task<ActionResult> RefreshRankedPlaylist()
        {
            string userId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
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

            playlist.songs = _context.Songs.Include(s => s.Difficulties).Where(s => s.Difficulties.FirstOrDefault(d => d.Ranked) != null).Select(s => new {
                hash = s.Hash,
                songName = s.Name,
                levelAuthorName = s.Mapper,
                difficulties = s.Difficulties.Where(d => d.Ranked).Select(d => new {
                    name = d.DifficultyName.FirstCharToLower(),
                    characteristic = d.ModeName
                })
            }).ToList();

            await _playlistContainerClient.DeleteBlobIfExistsAsync("ranked.bplist");
            await _playlistContainerClient.UploadBlobAsync("ranked.bplist", new BinaryData(JsonConvert.SerializeObject(playlist)));

            return Ok();
        }
    }
}
