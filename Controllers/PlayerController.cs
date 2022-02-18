using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
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
            Player? player = await _context.Players.FindAsync(id);

            if (player == null) {
                Int64 userId = Int64.Parse(id);
                if (userId > 70000000000000000) {
                    player = await GetPlayerFromSteam("http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=" + _configuration.GetValue<string>("SteamKey") + "&steamids=" + id);
                    if (player == null) {
                        return NotFound();
                    } else {
                        player.Id = id;
                        player.Platform = "steam";
                        _context.Players.Add(player);
                        await _context.SaveChangesAsync();
                    }
                } else {
                    return NotFound();
                }
            }

            return player;
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
    }
}
