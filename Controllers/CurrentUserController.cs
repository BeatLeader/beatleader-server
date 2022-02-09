using Azure.Identity;
using Azure.Storage.Blobs;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class CurrentUserController : ControllerBase
    {
        private readonly AppContext _context;
        BlobContainerClient _containerClient;

        public CurrentUserController(AppContext context, IOptions<AzureStorageConfig> config, IWebHostEnvironment env)
        {
            _context = context;
   //         if (env.IsDevelopment())
			//{
			//	_containerClient = new BlobContainerClient(config.Value.AccountName, config.Value.ContainerName);
			//}
			//else
			//{
			//	string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
			//											config.Value.AccountName,
			//										   config.Value.ContainerName);

			//	_containerClient = new BlobContainerClient(new Uri(containerEndpoint), new DefaultAzureCredential());
			//}
        }

        [HttpGet("~/user/id")]
        public string GetId()
        {
            return HttpContext.CurrentUserID();
        }

        [HttpGet("~/user/playlists")]
        public async Task<ActionResult<IEnumerable<Playlist>>> GetAllPlaylists()
        {
            string currentId = HttpContext.CurrentUserID();
            return await _context.Playlists.Where(t => t.OwnerId == currentId).ToListAsync();
        }

        [HttpGet("~/user/playlist")]
        public async Task<ActionResult<Playlist>> Get([FromQuery]int id)
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
    }
}