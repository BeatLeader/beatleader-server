using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Controllers
{
    public class PlayerController : Controller
    {
        private readonly AppContext _context;
        private readonly IConfiguration _configuration;

        public PlayerController(AppContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet("~/player/{id}")]
        public async Task<ActionResult<Player>> Get(string id)
        {
            Player? player = await _context.Players.Include(p => p.ScoreStats).FirstOrDefaultAsync(p => p.Id == id);

            if (player == null) {
                Int64 userId = Int64.Parse(id);
                if (userId > 70000000000000000) {
                    player = await GetPlayerFromSteam("http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=" + _configuration.GetValue<string>("SteamKey") + "&steamids=" + id);
                    if (player == null) {
                        return NotFound();
                    } else {
                        player.Id = id;
                        player.Platform = "steam";
                        player.ScoreStats = new PlayerScoreStats();
                        player.Histories = "";
                        player.Role = "";
                        _context.Players.Add(player);
                        await _context.SaveChangesAsync();
                    }
                } else {
                    return NotFound();
                }
            }

            return player;
        }

        [HttpPatch("~/player/{id}")]
        [Authorize]
        public async Task<ActionResult> PatchPlayer(string id, [FromQuery] string? role, [FromQuery] string? country, [FromQuery] string? avatar, [FromQuery] bool? banned)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || (!currentPlayer.Role.Contains("admin") && currentId != GolovaID))
            {
                return Unauthorized();
            }
            if (currentId != GolovaID && (role != null))
            {
                return Unauthorized();
            }
            Player? playerToUpdate = _context.Players.Find(id);
            if (playerToUpdate == null)
            {
                return NotFound();
            }
            if (role != null)
            {
                playerToUpdate.Role = role;
            }
            if (country != null)
            {
                playerToUpdate.Country = country;
            }
            if (avatar != null)
            {
                playerToUpdate.Avatar = avatar;
            }
            if (banned != null)
            {
                playerToUpdate.Banned = (bool)banned;
            }
            _context.Players.Update(playerToUpdate);
            await _context.SaveChangesAsync();

            return Ok();
        }


        [HttpDelete("~/player/{id}")]
        [Authorize]
        public async Task<ActionResult> DeletePlayer(string id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Player? player = await _context.Players.FindAsync(id);

            if (player == null)
            {
                return NotFound();
            }

            _context.Players.Remove(player);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/player/{id}/scores")]
        public async Task<ActionResult<IEnumerable<Score>>> GetScores(string id, [FromQuery] string sortBy = "recent", [FromQuery] int page = 0, [FromQuery] int count = 8)
        {
            var sequence = _context.Scores.Where(t => t.PlayerId == id);
            switch (sortBy)
            {
                case "recent":
                    sequence = sequence.OrderByDescending(t => t.Timeset);
                    break;
                case "topPP":
                    sequence = sequence.OrderByDescending(t => t.Pp);
                    break;
                case "topAcc":
                    sequence = sequence.OrderByDescending(t => t.Accuracy);
                    break;
                case "pauses":
                    sequence = sequence.OrderByDescending(t => t.Pauses);
                    break;
                default:
                    break;
            }
            return sequence.Take(count).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties).ToList();
        }

        [HttpDelete("~/player/{id}/score/{leaderboardID}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(string id, string leaderboardID)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Leaderboard? leaderboard = _context.Leaderboards.Where(l => l.Id == leaderboardID).Include(l => l.Scores).FirstOrDefault();
            if (leaderboard == null) {
                return NotFound();
            } 
            Score? scoreToDelete = leaderboard.Scores.Where(t => t.PlayerId == id).FirstOrDefault();

            if (scoreToDelete == null) {
                return NotFound();
            }

            _context.Scores.Remove(scoreToDelete);
            await _context.SaveChangesAsync();
            return Ok ();

        }

        [HttpGet("~/players")]
        public async Task<ActionResult<IEnumerable<Player>>> GetPlayers([FromQuery] string sortBy = "recent", [FromQuery] int page = 0, [FromQuery] int count = 50, [FromQuery] string search = "", [FromQuery] string countries = "")
        {
            return _context.Players.OrderByDescending(p => p.Pp).OrderByDescending(p => p.Pp).ToList();
        }

        [HttpGet("~/players/count")]
        public async Task<ActionResult<int>> GetPlayers()
        {
            return _context.Players.Count();
        }

        public Task<Player?> GetPlayerFromSteam(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse? response = null;
            Player? song = null;
            var stream = 
            Task<(WebResponse?, Player?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }
            
                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadPlayerFromResponse(t.Result));
        }

        private Player? ReadPlayerFromResponse((WebResponse?, Player?) response)
        {
            if (response.Item1 != null) {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return null;
                    }

                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    if (info == null) return null;

                    dynamic playerInfo = info.response.players[0];
                    Player result = new Player();
                    result.Name = playerInfo.personaname;
                    result.Avatar = playerInfo.avatarfull;
                    if (ExpandantoObject.HasProperty(playerInfo, "loccountrycode")) {
                        result.Country = playerInfo.loccountrycode;
                    } else {
                        result.Country = "not set";
                    }
                    return result;
                }
            } else {
                return response.Item2;
            }
            
        }

        public static string GolovaID = "76561198059961776";
    }
}
