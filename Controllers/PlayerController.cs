using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
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

        [HttpGet("~/players/id")]
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
