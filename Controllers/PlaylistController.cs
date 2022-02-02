using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlaylistController : Controller
    {
        private readonly AppContext _context;

        public PlaylistController(AppContext context)
        {
            _context = context;
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
    }
}
