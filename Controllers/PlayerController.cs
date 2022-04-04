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
            Int64 oculusId = 0;
            try
            {
                oculusId = Int64.Parse(id);
            } catch {}
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == oculusId);
            string userId = (link != null ? link.SteamID : id);
            Player? player = await _context.Players.Include(p => p.ScoreStats).Include(p => p.Badges).FirstOrDefaultAsync(p => p.Id == userId);
            if (player == null)
            {
                return await GetLazy(id, false);
            }
            return player;
        }

        [NonAction]
        public async Task<ActionResult<Player>> GetLazy(string id, bool addToBase = true)
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
                if (addToBase) {
                    _context.Players.Add(player);
                    await _context.SaveChangesAsync();
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

        [HttpGet("~/playerlink/{login}")]
        [Authorize]
        public async Task<ActionResult<AccountLink>> GetPlayerLink(string login)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (info == null)
            {
                return NotFound("No info");
            }
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == info.Id);
            if (link == null)
            {
                return NotFound("No link");
            }

            return link;
        }

        [HttpDelete("~/authinfo/{login}")]
        [Authorize]
        public async Task<ActionResult> DeleteAuthInfo(string login)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (info == null)
            {
                return NotFound("No info");
            }
            _context.Auths.Remove(info);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/authips/")]
        [Authorize]
        public async Task<ActionResult> DeleteAuthIps()
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var info = _context.AuthIPs.ToArray();
            foreach (var item in info)
            {
                _context.AuthIPs.Remove(item);
            }
            
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/playerlink/{id}")]
        [Authorize]
        public async Task<ActionResult> DeletePlayerLinked(string id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.SteamID == id);
            if (link == null)
            {
                return NotFound();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Id == link.OculusID);
            if (info == null)
            {
                return NotFound();
            }
            _context.AccountLinks.Remove(link);
            _context.Auths.Remove(info);

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

        [HttpGet("~/player/{id}/scorevalue/{hash}/{difficulty}/{mode}")]
        public ActionResult<int> GetScoreValue(string id, string hash, string difficulty, string mode)
        {
            Score? score = _context
                .Scores
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Where(s => s.PlayerId == id && s.Leaderboard.Song.Hash == hash && s.Leaderboard.Difficulty.DifficultyName == difficulty && s.Leaderboard.Difficulty.ModeName == mode)
                .FirstOrDefault();

            if (score == null)
            {
                return NotFound();
            }

            return score.ModifiedScore;

        }

        [HttpGet("~/players")]
        public async Task<ActionResult<ResponseWithMetadata<Player>>> GetPlayers([FromQuery] string sortBy = "recent", [FromQuery] int page = 1, [FromQuery] int count = 50, [FromQuery] string search = "", [FromQuery] string countries = "")
        {
            IQueryable<Player> request = _context.Players;
            if (countries.Length != 0)
            {
                request = request.Where(p => countries.Contains(p.Country));
            }
            if (search.Length != 0)
            {
                request = request.Where(p => p.Name.Contains(search));
            }
            return new ResponseWithMetadata<Player>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = request.Count()
                },
                Data = await request.OrderByDescending(p => p.Pp).Skip((page - 1) * count).Take(count).Include(p => p.ScoreStats).ToListAsync()
            };
        }

        [HttpGet("~/players/count")]
        public async Task<ActionResult<int>> GetPlayers()
        {
            return _context.Players.Count();
        }

        [HttpGet("~/players/sethistories")]
        public async Task<ActionResult> SetHistories()
        {
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            CronTimestamps? cronTimestamps = _context.cronTimestamps.Find(1);
            if (cronTimestamps != null) {
                if ((timestamp - cronTimestamps.HistoriesTimestamp) < 60 * 60 * 24 - 5 * 60)
                {
                    return BadRequest("Allowed only at midnight");
                } else
                {
                    cronTimestamps.HistoriesTimestamp = timestamp;
                    _context.Update(cronTimestamps);
                }
            } else
            {
                cronTimestamps = new CronTimestamps();
                cronTimestamps.HistoriesTimestamp = timestamp;
                _context.Add(cronTimestamps);
            }
            var ranked = _context.Players.Include(p => p.ScoreStats).ToList();
            foreach (Player p in ranked)
            {
                var histories = p.Histories.Length == 0 ? new string[0] : p.Histories.Split(",");
                if (histories.Length == 7)
                {
                    histories = histories.Skip(1).Take(6).ToArray();
                }
                histories = histories.Append(p.Rank.ToString()).ToArray();
                p.Histories = string.Join(",", histories);
                _context.Players.Update(p);
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/players/steam/refresh")]
        public async Task<ActionResult> RefreshSteamPlayers()
        {
            var players = _context.Players.ToList();
            foreach (Player p in players)
            {
                if (Int64.Parse(p.Id) <= 70000000000000000) { continue; }
                Player? update = await GetPlayerFromSteam(p.Id);

                if (update != null)
                {
                    p.ExternalProfileUrl = update.ExternalProfileUrl;
                    p.AllTime = update.AllTime;
                    p.LastTwoWeeksTime = update.LastTwoWeeksTime;

                    if (p.Avatar.Contains("steamcdn"))
                    {
                        p.Avatar = update.Avatar;
                    }

                    if (p.Country == "not set" && update.Country != "not set")
                    {
                        p.Country = update.Country;
                    }

                    _context.Players.Update(p);
                    await _context.SaveChangesAsync();
                }
            }

            return Ok();
        }

        [HttpGet("~/player/{id}/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshPlayer(string id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            Player? p = _context.Players.Find(id);
            if (p == null)
            {
                return NotFound();
            }

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
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/players/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshPlayers()
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var players = _context.Players.ToList();
            Dictionary<string, int> countries = new Dictionary<string, int>();
            foreach (Player p in players)
            {
                await RefreshPlayer(p.Id);
            }
            var ranked = _context.Players.OrderByDescending(t => t.Pp).ToList();
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
            var playersWithStats = _context.Players.Include(p => p.ScoreStats).ToList();

            foreach (Player p in playersWithStats)
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
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("~/badge")]
        [Authorize]
        public ActionResult<Badge> CreateBadge([FromQuery] string description, [FromQuery] string image) {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Badge badge = new Badge {
                Description = description,
                Image = image
            };

            _context.Badges.Add(badge);
            _context.SaveChanges();

            return badge;
        }

        [HttpPut("~/player/badge/{playerId}/{badgeId}")]
        [Authorize]
        public async Task<ActionResult<Player>> AddBadge(string playerId, int badgeId)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Player? player = await _context.Players.Include(p => p.Badges).FirstOrDefaultAsync(p => p.Id == playerId);
            if (player == null)
            {
                return NotFound("Player not found");
            }

            Badge? badge = _context.Badges.Find(badgeId);
            if (badge == null)
            {
                return NotFound("Badge not found");
            }
            if (player.Badges == null) {
                player.Badges = new List<Badge>();
            }

            player.Badges.Add(badge);
            _context.SaveChanges();

            return player;
        }

        [NonAction]
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
            result.ExternalProfileUrl = playerInfo.profileurl;

            dynamic? gamesInfo = await GetPlayer("http://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v0001/?key=" + _configuration.GetValue<string>("SteamKey") + "&steamid=" + playerID);
            if (gamesInfo != null && ExpandantoObject.HasProperty(gamesInfo, "response") && ExpandantoObject.HasProperty(gamesInfo.response, "total_count"))
            {
                dynamic response = gamesInfo.response;
                dynamic? beatSaber = null;

                for (int i = 0; i < response.total_count; i++)
                {
                    if (response.games[i].appid == 620980)
                    {
                        beatSaber = response.games[i];
                    }
                }

                if (beatSaber != null)
                {
                    result.AllTime = beatSaber.playtime_forever / 60.0f;
                    result.LastTwoWeeksTime = beatSaber.playtime_2weeks / 60.0f;
                }
            }

            return result;
        }

        [NonAction]
        public async Task<Player?> GetPlayerFromOculus(string playerID)
        {
            AuthInfo? authInfo = _context.Auths.FirstOrDefault(el => el.Id.ToString() == playerID);

            if (authInfo == null) return null;

            Player result = new Player();
            result.Id = playerID;
            result.Name = authInfo.Login;
            result.Platform = "oculus";
            result.SetDefaultAvatar();

            return result;
        }

        [NonAction]
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

        [NonAction]
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
