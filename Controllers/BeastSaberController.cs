using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Specialized;
using System.Web;
using System.Net.Http.Headers;

namespace BeatLeader_Server.Controllers {
    public class BeastSaberController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;
        IHttpClientFactory _httpClientFactory;

        private static Dictionary<string, string> OST_MAP = new Dictionary<string, string> {
            { "Danger", "Extras | Teminite, Boom Kitty - Danger" },
        };

        public BeastSaberController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _environment = env;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/beasties/nominations/")]
        [SwaggerOperation(Summary = "Retrieve a list of nominations for particular leaderboard", Description = "Authenticated player Beasties nominations.")]
        [SwaggerResponse(200, "List of nominations retrieved successfully", typeof(ICollection<BeastiesNomination>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(401, "Autorization failed")]
        public async Task<ActionResult<ICollection<BeastiesNomination>>> GetAll([FromQuery] string leaderboardId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var trimmedId = leaderboardId.Replace("x", "");

            return await _context.BeastiesNominations.Where(n => n.PlayerId == currentID && n.LeaderboardId == trimmedId).ToListAsync();
        }

        public class BestiesNominationResponse {
            public string Message { get; set; }
        }

        [HttpPost("~/beasties/nominate/")]
        [SwaggerOperation(Summary = "Nominate a map for Besties Awards", Description = "Nominates provided leaderboard map for mapping awards in selected category")]
        [SwaggerResponse(200, "Map was nominated")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(401, "Autorization failed")]
        public async Task<ActionResult<BestiesNominationResponse>> Nominate([FromQuery] string leaderboardId, [FromQuery] string category)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var lb = _context.Leaderboards.AsNoTracking().Include(lb => lb.Difficulty).FirstOrDefault(lb => lb.Id == leaderboardId);
            if (lb == null) {
                return NotFound(new BestiesNominationResponse {
                    Message = "Such leaderboard not found"
                });
            }

            var trimmedId = leaderboardId.Replace("x", "");
            var existingVote = 
                await _context
                .BeastiesNominations
                .Where(n => n.PlayerId == currentID && n.LeaderboardId == trimmedId && n.Category == category)
                .FirstOrDefaultAsync();

            if (existingVote != null) {
                return BadRequest(new BestiesNominationResponse {
                    Message = "You already voted for this map in this category"
                });
            }

            var data = new Dictionary<string, string>();

            NameValueCollection outgoingQueryString = HttpUtility.ParseQueryString("");
            if (lb.Difficulty.Status != DifficultyStatus.OST) {
                data["bsrId"] = lb.SongId.Replace("x", "");
            } else { 
                data["OSTname"] = OST_MAP[lb.SongId];
            }
            data["userId"] = currentID;
            data["charecteristic"] = lb.Difficulty.ModeName;
            data["difficulty"] = lb.Difficulty.DifficultyName;
            data["category"] = category;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.GetValue<string>("SaeraphinxSecret"));
            using HttpContent formContent = new FormUrlEncodedContent(data);
            using HttpResponseMessage webResponse = await client.PostAsync("https://mappingawards.saeraphinx.dev/api/beatleader/submitmap", formContent).ConfigureAwait(false);

            var response = (await webResponse.Content.ReadAsStreamAsync()).ObjectFromStream();

            if (response == null) {
                return BadRequest(new BestiesNominationResponse {
                    Message = "Failed to nominate map"
                });
            }
            if (category == "Gen-FullSpread") {
                var associated = _context.Leaderboards.Where(l => l.SongId == lb.SongId).ToList();
                foreach (var item in associated) {
                    _context.BeastiesNominations.Add(new BeastiesNomination {
                        PlayerId = currentID,
                        Category = category,
                        Timepost = Time.UnixNow(),
                        LeaderboardId = item.Id.Replace("x", "")
                    });
                }
            } else {
                _context.BeastiesNominations.Add(new BeastiesNomination {
                    PlayerId = currentID,
                    Category = category,
                    Timepost = Time.UnixNow(),
                    LeaderboardId = trimmedId
                });
            }
            await _context.SaveChangesAsync();

            if (ExpandantoObject.HasProperty(response, "message")) {
                return Ok(new BestiesNominationResponse { Message = (string)((dynamic)response).message });
            } else {
                return Ok();
            }
        }
    }
}
