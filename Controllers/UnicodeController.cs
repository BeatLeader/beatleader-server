using Amazon.S3;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    public class UnicodeController : Controller
    {
        private readonly IAmazonS3 _s3Client;

        public UnicodeController(
            IConfiguration configuration)
        {
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/unicode/{name}")]
        public async Task<ActionResult> GetUnicode(string name)
        {
            var stream = await _s3Client.DownloadStream(name, S3Container.unicode);
            if (stream != null) {
                return File(stream, "image/png");
            } else {
                return NotFound();
            }
        }
    }
}
