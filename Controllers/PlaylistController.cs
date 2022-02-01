using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    public class PlaylistController : Controller
    {
        [ApiController]
        [Route("[controller]")]
        public class CurrentUserController : ControllerBase
        {
            [HttpGet("~/playlists")]
            public String Get()
            {
                String user = HttpContext.User.Claims.First().Value.Split("/").Last();
                return user;
            }

            [HttpGet("~/playlist")]
            public void Post(int id)
            {

            }
        }
    }
}
