using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers {
    public class BackupController : Controller {

        IWebHostEnvironment _environment;

        public BackupController(
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _environment = env;
        }

        [HttpGet("~/backup/file/{container}/{filename}")]
        public async Task<ActionResult> GetBackupFile(S3Container container, string filename)
        {
            if (!_environment.IsDevelopment()) {
                return Unauthorized();
            }

            string directoryPath = Path.Combine("/root", container.ToString());
            string filePath = Path.Combine(directoryPath, filename);

            if (!System.IO.File.Exists(filePath)) {
                return NotFound();
            }

            return File(new FileStream(filePath, FileMode.Open), "application/octet-stream");
        }
    }
}
