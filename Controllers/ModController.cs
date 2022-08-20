using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    public class ModController : Controller
    {
        public class ModVersion {
            public string Version { get; set; }
            public string Link { get; set; }
        }
        public class ModVersions {
            public ModVersion Pc { get; set; }
            public ModVersion Quest { get; set; }
        }

        [HttpGet("~/mod/lastVersions")]
        public ActionResult<ModVersions> GetLastVersions()
        {
            return new ModVersions {
                Pc = new ModVersion {
                    Version = "0.4.2",
                    Link = "https://github.com/BeatLeader/beatleader-mod/releases/tag/v0.4.2"
                },
                Quest = new ModVersion {
                    Version = "0.4.1",
                    Link = "https://github.com/BeatLeader/beatleader-qmod/releases/tag/v0.4.1"
                }
            };
        }
    }
}
