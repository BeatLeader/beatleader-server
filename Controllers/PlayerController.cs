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
            Int64 oculusId = Int64.Parse(id);
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
            string userId = (link != null ? link.SteamID : id);
            Player? player = await _context.Players.Include(p => p.ScoreStats).FirstOrDefaultAsync(p => p.Id == userId);
            if (player == null)
            {
                return NotFound();
            }
            return player;
        }

        public async Task<ActionResult<Player>> GetLazy(string id)
        {
            Player? player = await _context.Players.Include(p => p.ScoreStats).FirstOrDefaultAsync(p => p.Id == id);

            if (player == null) {
                Int64 userId = Int64.Parse(id);
                if (userId > 70000000000000000) {
                    player = await GetPlayerFromSteam(id);
                    if (player == null) {
                        return NotFound();
                    } else {
                        player.Platform = "steam";
                    }
                } else {
                    player = await GetPlayerFromOculus(id);
                    if (player == null)
                    {
                        return NotFound();
                    }
                    else
                    {
                        player.Platform = "oculus";
                    }
                }
                player.Id = id;
                _context.Players.Add(player);
                await _context.SaveChangesAsync();
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
        public async Task<ActionResult<ResponseWithMetadata<Score>>> GetScores(
            string id,
            [FromQuery] string sortBy = "date",
            [FromQuery] string order = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int count = 8,
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null)
        {
            var sequence = _context.Scores.Where(t => t.PlayerId == id);
            switch (sortBy)
            {
                case "date":
                    sequence = sequence.Order(order, t => t.Timeset);
                    break;
                case "pp":
                    sequence = sequence.Order(order, t => t.Pp);
                    break;
                case "acc":
                    sequence = sequence.Order(order, t => t.Accuracy);
                    break;
                case "pauses":
                    sequence = sequence.Order(order, t => t.Pauses);
                    break;
                case "rank":
                    sequence = sequence.Order(order, t => t.Rank);
                    break;
                case "stars":
                    sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Order(order, t => t.Leaderboard.Difficulty.Stars);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Include(lb => lb.Leaderboard)
                    .ThenInclude(lb => lb.Song)
                    .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
            }
            if (diff != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
            }
            if (type != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Ranked : !p.Leaderboard.Difficulty.Ranked);
            }
            if (stars_from != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
            }
            return new Models.ResponseWithMetadata<Score>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = sequence.Skip((page - 1) * count).Take(count).Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Song).ThenInclude(lb => lb.Difficulties).ToList()
            };
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
        public async Task<ActionResult<IEnumerable<Player>>> GetPlayers([FromQuery] string sortBy = "recent", [FromQuery] int page = 1, [FromQuery] int count = 50, [FromQuery] string search = "", [FromQuery] string countries = "")
        {
            IQueryable<Player> request = _context.Players.Include(p => p.ScoreStats);
            if (countries.Length != 0)
            {
                request = request.Where(p => countries.Contains(p.Country));
            }
            if (search.Length != 0)
            {
                request = request.Where(p => p.Name.Contains(search));
            }
            return request.OrderByDescending(p => p.Pp).Skip((page - 1) * count).Take(count).ToList();
        }

        [HttpGet("~/players/count")]
        public async Task<ActionResult<int>> GetPlayers()
        {
            return _context.Players.Count();
        }

        [HttpGet("~/players/refresh")]
        public async Task<ActionResult> RefreshPlayers()
        {
            var ranked = _context.Players.Include(p => p.ScoreStats).ToList();
            Dictionary<string, int> countries = new Dictionary<string, int>();
            foreach (Player p in ranked)
            {
                if (p.ScoreStats == null)
                {
                    p.ScoreStats = new PlayerScoreStats();
                    _context.Stats.Add(p.ScoreStats);
                }
                var allScores = _context.Scores.Where(s => s.PlayerId == p.Id);
                var rankedScores = allScores.Where(s => s.Pp != 0);
                p.ScoreStats.TotalPlayCount = allScores.Count();
                
                p.ScoreStats.RankedPlayCount = rankedScores.Count();
                if (p.ScoreStats.TotalPlayCount > 0)
                {
                    p.ScoreStats.TotalScore = allScores.Sum(s => s.ModifiedScore);
                    p.ScoreStats.AverageAccuracy = allScores.Average(s => s.Accuracy);
                }
                
                if (p.ScoreStats.RankedPlayCount > 0)
                {
                    p.ScoreStats.AverageRankedAccuracy = rankedScores.Average(s => s.Accuracy);
                }
                
                _context.Stats.Update(p.ScoreStats);
                _context.RecalculatePP(p);
            }
            ranked = ranked.OrderByDescending(t => t.Pp).ToList();
            foreach ((int i, Player p) in ranked.Select((value, i) => (i, value)))
            {
                p.Rank = i + 1;
                if (!countries.ContainsKey(p.Country))
                {
                    countries[p.Country] = 1;
                }

                p.CountryRank = countries[p.Country];
                countries[p.Country]++;

                _context.Players.Update(p);
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        public async Task<Player?> GetPlayerFromSteam(string playerID)
        {
            dynamic? info = await GetPlayer("http://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=" + _configuration.GetValue<string>("SteamKey") + "&steamids=" + playerID);

            if (info == null) return null;

            dynamic playerInfo = info.response.players[0];
            Player result = new Player();
            result.Name = playerInfo.personaname;
            result.Avatar = playerInfo.avatarfull;
            if (ExpandantoObject.HasProperty(playerInfo, "loccountrycode"))
            {
                result.Country = playerInfo.loccountrycode;
            }
            else
            {
                result.Country = "not set";
            }

            return result;
        }

        public async Task<Player?> GetPlayerFromOculus(string playerID)
        {
            AuthInfo? authInfo = _context.Auths.First(el => el.Id.ToString() == playerID);

            if (authInfo == null) return null;

            Player result = new Player();
            result.Id = playerID;
            result.Name = authInfo.Login;
            result.Platform = "oculus";
            result.SetDefaultAvatar();

            return result;
        }

        public Task<dynamic?> GetPlayer(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse? response = null;
            dynamic? song = null;
            var stream = 
            Task<(WebResponse?, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
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

        private dynamic? ReadPlayerFromResponse((WebResponse?, dynamic?) response)
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

                    return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                }
            } else {
                return response.Item2;
            }
            
        }

        public static string GolovaID = "76561198059961776";
    }
}
