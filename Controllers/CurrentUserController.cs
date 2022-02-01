using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class CurrentUserController : ControllerBase
    {
        [HttpGet("~/user/id")]
        public String GetId()
        {
            return HttpContext.User.Claims.First().Value.Split("/").Last();
        }

        [HttpGet("~/user/playlists")]
        public String GetAllPlaylists()
        {
            return HttpContext.User.Claims.First().Value.Split("/").Last();
        }

        [HttpGet("~/user/playlist")]
        public String GetPlaylist(int id)
        {
            return HttpContext.User.Claims.First().Value.Split("/").Last();
        }

        [HttpGet("~/user/playlistshare")]
        public String SharePlaylist(int id)
        {
            return HttpContext.User.Claims.First().Value.Split("/").Last();
        }

        [HttpPost("~/user/playlist")]
        public String PostPlaylist()
        {
            return HttpContext.User.Claims.First().Value.Split("/").Last();
        }

        [HttpDelete("~/user/playlist")]
        public String DeletePlaylist(int id)
        {
            return HttpContext.User.Claims.First().Value.Split("/").Last();
        }
    }
}