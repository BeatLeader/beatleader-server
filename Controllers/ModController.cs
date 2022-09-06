using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BeatLeader_Server.Controllers
{
    public class ModController : Controller
    {
        private readonly IConfiguration _configuration;

        public class ModVersion {
            public string Version { get; set; }
            public string Link { get; set; }
        }
        public class ModVersions {
            public ModVersion Pc { get; set; }
            public ModVersion Quest { get; set; }
        }

        public ModController(
            IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("~/mod/lastVersions")]
        public ActionResult<ModVersions> GetLastVersions()
        {
            return new ModVersions {
                Pc = new ModVersion {
                    Version = "0.4.3",
                    Link = "https://github.com/BeatLeader/beatleader-mod/releases/tag/v0.4.3"
                },
                Quest = new ModVersion {
                    Version = "0.4.1",
                    Link = "https://github.com/BeatLeader/beatleader-qmod/releases/tag/v0.4.1"
                }
            };
        }

        [HttpGet("~/servername")]
        public string ServerName()
        {
            return _configuration.GetValue<string>("ServerName");
        }
    }
}
