using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    public class ModController : Controller
    {
        private readonly IConfiguration _configuration;

        public ModController(
            IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("~/mod/lastVersions")]
        public ActionResult GetLastVersions()
        {
            if (HttpContext.Request.Headers["User-Agent"].Contains("0.8.0")) {
                return Content(_configuration.GetValue<string>("ModVersionsLatest"), "application/json");
            } else {
                return Content(_configuration.GetValue<string>("ModVersions"), "application/json");
            }
        }

        [HttpGet("~/servername")]
        public string? ServerName()
        {
            return _configuration.GetValue<string>("ServerName");
        }
    }
}
