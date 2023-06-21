using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    public class ClanController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        IAmazonS3 _assetsS3Client;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;

        public ClanController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            ReadAppContext readContext,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            _readContext = readContext;
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpGet("~/clans/")]
        public async Task<ActionResult<ResponseWithMetadata<Clan>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sort = "pp",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? type = null,
            [FromQuery] string? sortBy = null)
        {
            var sequence = _readContext.Clans.AsQueryable();
            if (sortBy != null) {
                sort = sortBy;
            }
            switch (sort)
            {
                case "name":
                    sequence = sequence.Order(order, t => t.Name);
                    break;
                case "pp":
                    sequence = sequence.Order(order, t => t.Pp);
                    break;
                case "acc":
                    sequence = sequence.Where(c => c.PlayersCount > 2).Order(order, t => t.AverageAccuracy);
                    break;
                case "rank":
                    sequence = sequence.Where(c => c.PlayersCount > 2 && c.AverageRank > 0).Order(order.Reverse(), t => t.AverageRank);
                    break;
                case "count":
                    sequence = sequence.Order(order, t => t.PlayersCount);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Where(p => p.Name.ToLower().Contains(lowSearch) ||
                                p.Tag.ToLower().Contains(lowSearch));
            }

            return new ResponseWithMetadata<Clan>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = sequence.Skip((page - 1) * count).Take(count).ToList()
            };
        }

        [HttpGet("~/clan/{tag}")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, Clan>>> GetClan(
            string tag, 
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sort = "pp",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? type = null)
        {
            Clan? clan = null;
            if (tag == "my") {
                string? currentID = HttpContext.CurrentUserID(_readContext);
                var player = await _readContext.Players.FindAsync(currentID);

                if (player == null)
                {
                    return NotFound();
                }
                clan = _readContext
                    .Clans
                    .Where(c => c.LeaderID == currentID)
                    .Include(c => c.Players)
                    .ThenInclude(p => p.ProfileSettings)
                    .FirstOrDefault();
            } else {
                clan = _readContext
                    .Clans
                    .Where(c => c.Tag == tag)
                    .Include(c => c.Players)
                    .ThenInclude(p => p.ProfileSettings)
                    .FirstOrDefault();
            }
            if (clan == null)
            {
                return NotFound();
            }

            IQueryable<Player> players = clan.Players.Where(p => !p.Banned).AsQueryable();
            switch (sort)
            {
                case "pp":
                    players = players.Order(order, t => t.Pp);
                    break;
                case "acc":
                    players = players.Order(order, t => t.ScoreStats.AverageRankedAccuracy);
                    break;
                case "rank":
                    players = players.Order(order, t => t.Rank);
                    break;
                default:
                    break;
            }
            return new ResponseWithMetadataAndContainer<PlayerResponse, Clan> {
                Container = clan,
                Data = players.Skip((page - 1) * count).Take(count).Select(ResponseFromPlayer).Select(PostProcessSettings),
                Metadata = new Metadata {
                    Page = 1,
                    ItemsPerPage = 10,
                    Total = players.Count()
                }
            };
        }

        [HttpPost("~/clan/create")]
        [Authorize]
        public async Task<ActionResult<Clan>> CreateClan(
            [FromQuery] string name,
            [FromQuery] string tag,
            [FromQuery] string color,
            [FromQuery] string description = "",
            [FromQuery] string bio = "")
        {
            string currentID = HttpContext.CurrentUserID(_context);

            var player = _context.Players.Where(p => p.Id == currentID).Include(p => p.Clans).Include(p => p.ScoreStats).FirstOrDefault();
            if (player.Clans.Count == 3) {
                return BadRequest("You can join only up to 3 clans.");
            }

            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }

            string upperTag = tag.ToUpper();

            if (_context.ReservedTags.FirstOrDefault(t => t.Tag == upperTag) != null) {
                return BadRequest("This tag is reserved. Ask someone with #BeatFounder role in discord to allow this tag for you.");
            }

            if (_context.Clans.FirstOrDefault(c => c.Name == name || c.Tag == upperTag) != null)
            {
                return BadRequest("Clan with such name or tag is already exists");
            }

            if (_context.Clans.FirstOrDefault(c => c.LeaderID == currentID) != null)
            {
                return BadRequest("You already have a clan");
            }
            if (upperTag.Length is > 4 or < 2 || !Regex.IsMatch(upperTag, @"^[A-Z0-9]+$")) {
                return BadRequest("Clan tag should be from 2 to 4 capital latin letters or numbers");
            }
            if (name.Length is > 25 or < 2)
            {
                return BadRequest("Clan name should be from 2 to 25 letters");
            }
            int colorLength = color.Length;
            try {
                if (!(colorLength is 7 or 9 && long.Parse(color[1..], System.Globalization.NumberStyles.HexNumber) != 0)) {
                    return BadRequest("Color is not valid");
                }
            } catch {
                return BadRequest("Color is not valid");
            }

            if (description.Length > 100)
            {
                return BadRequest("Description is too long");
            }

            if (bio.Length > 1000)
            {
                return BadRequest("Bio is too long");
            }

            if (player == null)
            {
                return NotFound("wHAT?");
            }

            string fileName = tag + "clan";
            string? icon;
            try
            {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                fileName += extension;

                await _assetsS3Client.UploadAsset(fileName, stream);

                icon = (_environment.IsDevelopment() ? "https://localhost:9191/assets/" : "https://cdn.assets.beatleader.xyz/") + fileName;
            }
            catch (Exception e)
            {
                return BadRequest("Error saving clan icon");
            }

            Clan newClan = new Clan
            {
                Name = name,
                Tag = upperTag,
                Color = color,
                LeaderID = currentID,
                Icon = icon ?? "https://cdn.assets.beatleader.xyz/clan.png",
                Description = description,
                Bio = bio,
                PlayersCount = 1,
                Pp = player.Pp,
                AverageAccuracy = player.ScoreStats.AverageRankedAccuracy,
                AverageRank = player.Rank
            };
            _context.Clans.Add(newClan);
            await _context.SaveChangesAsync();

            player.Clans.Add(newClan);
            await _context.SaveChangesAsync();

            return newClan;
        }

        [HttpDelete("~/clan")]
        [Authorize]
        public async Task<ActionResult> DeleteClan([FromQuery] int? id = null)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.FindAsync(currentID);

            if (player == null)
            {
                return NotFound();
            }

            Clan? clan = null;
            if (id != null && player != null && player.Role.Contains("admin"))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = _context.Clans.FirstOrDefault(c => c.LeaderID == currentID);
            }
            if (clan == null)
            {
                return NotFound();
            }

            _context.Clans.Remove(clan);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("~/clan")]
        [Authorize]
        public async Task<ActionResult> UpdateClan(
            [FromQuery] int? id = null,
            [FromQuery] string? name = null,
            [FromQuery] string? color = null,
            [FromQuery] string description = "",
            [FromQuery] string bio = "")
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var player = await _context.Players.FindAsync(currentID);

            if (player == null)
            {
                return NotFound();
            }

            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }

            Clan? clan = null;
            if (id != null && player != null && player.Role.Contains("admin"))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = _context.Clans.FirstOrDefault(c => c.LeaderID == currentID);
            }
            if (clan == null)
            {
                return NotFound();
            }

            if (name != null) {
                if (name.Length is < 3 or > 30)
                {
                    return BadRequest("Use name between the 3 and 30 symbols");
                }
                if (_context.Clans.FirstOrDefault(c => c.Name == name && c.Id != clan.Id) != null)
                {
                    return BadRequest("Clan with such name is already exists");
                }

                clan.Name = name;
            }

            if (color != null) {
                int colorLength = color.Length;
                try
                {
                    if (!(colorLength is 7 or 9 && long.Parse(color[1..], System.Globalization.NumberStyles.HexNumber) != 0))
                    {
                        return BadRequest("Color is not valid");
                    }
                }
                catch
                {
                    return BadRequest("Color is not valid");
                }

                clan.Color = color;
            }
            Random rnd = new Random();
            string fileName = clan.Tag + "R" + rnd.Next(1, 50) + "clan";
            try
            {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                if (ms.Length > 0) {
                    ms.Position = 0;

                    (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                    fileName += extension;

                    await _assetsS3Client.UploadAsset(fileName, stream);

                    clan.Icon = (_environment.IsDevelopment() ? "https://localhost:9191/assets/" : "https://cdn.assets.beatleader.xyz/") + fileName;
                }
            }
            catch (Exception)
            {
                return BadRequest("Error saving avatar");
            }

            if (description.Length > 100) {
                return BadRequest("Description is too long");
            } else {
                clan.Description = description;
            }

            if (bio.Length > 1000)
            {
                return BadRequest("Bio is too long");
            }
            else
            {
                clan.Bio = bio;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }


        [HttpPost("~/clan/invite")]
        [Authorize]
        public async Task<ActionResult> InviteToClan([FromQuery] string player)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null)
            {
                return NotFound();
            }

            if (currentPlayer.Banned) {
                return BadRequest("You are banned!");
            }

            Clan? clan = _context.Clans.FirstOrDefault(c => c.LeaderID == currentID);
            if (clan == null) {
                return NotFound("Current user is not leader of any clan");
            }

            User? user = await _userController.GetUserLazy(player);
            if (user == null) {
                return NotFound("No such player");
            }

            if (user.Player.Banned)
            {
                return BadRequest("This player are banned!");
            }

            if (user.Player.Clans.Count == 3)
            {
                return BadRequest("User already joined maximum amount of clans.");
            }

            if (user.BannedClans.FirstOrDefault(c => c.Id == clan.Id) != null) {
                return BadRequest("This clan was banned by player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null) {
                return BadRequest("Player already in this clan");
            }

            user.ClanRequest.Add(clan);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/cancelinvite")]
        [Authorize]
        public async Task<ActionResult> CancelinviteToClan([FromQuery] string player)
        {
            string currentID = HttpContext.CurrentUserID(_context);

            Clan? clan = _context.Clans.FirstOrDefault(c => c.LeaderID == currentID);
            if (clan == null)
            {
                return NotFound("Current user is not leader of any clan");
            }

            User? user = await _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.BannedClans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                return BadRequest("This clan was banned by player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                return BadRequest("Player already in this clan");
            }

            Clan? clanRequest = user.ClanRequest.FirstOrDefault(c => c.Id == clan.Id);
            if (clanRequest == null)
            {
                return NotFound("Player did not have request to this clan");
            }

            user.ClanRequest.Remove(clanRequest);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/kickplayer")]
        [Authorize]
        public async Task<ActionResult> KickPlayer(
            [FromQuery] string player,
            [FromQuery] int? id = null)
        {
            string currentID = HttpContext.CurrentUserID(_context);

            Clan? clan;
            var currentPlayer = await _context.Players.FindAsync(currentID);
            
            if (id != null && currentPlayer != null && currentPlayer.Role.Contains("admin"))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = _context.Clans.FirstOrDefault(c => c.LeaderID == currentID);
            }

            if (clan == null)
            {
                return NotFound("Current user is not leader of any clan");
            }
            if (clan.LeaderID == player)
            {
                return BadRequest("You cannot leave your own clan");
            }

            User? user = await _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) == null)
            {
                return NotFound("Player did not belong to this clan");
            }

            user.Player.Clans.Remove(clan);
            
            clan.AverageAccuracy = MathUtils.RemoveFromAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.RemoveFromAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);
            clan.PlayersCount--;
            await _context.SaveChangesAsync();

            clan.Pp = _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/accept")]
        [Authorize]
        public async Task<ActionResult> AcceptRequest([FromQuery] int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);

            User? user = await _userController.GetUserLazy(currentID);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.ClanRequest.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User did not received request to this clan");
            }

            user.ClanRequest.Remove(clan);

            if (user.Player.Clans.Count == 3)
            {
                await _context.SaveChangesAsync();
                return BadRequest("You already joined maximum amount of clans.");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                await _context.SaveChangesAsync();
                return BadRequest("Player already in this clan");
            }

            user.Player.Clans.Add(clan);
            clan.PlayersCount++;
            clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);
            await _context.SaveChangesAsync();

            clan.Pp = _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/reject")]
        [Authorize]
        public async Task<ActionResult> RejectRequest([FromQuery] int id, bool ban = false)
        {
            string currentID = HttpContext.CurrentUserID(_context);

            User? user = await _userController.GetUserLazy(currentID);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.ClanRequest.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User did not received request to this clan");
            }

            user.ClanRequest.Remove(clan);
            if (ban) {
                user.BannedClans.Add(clan);
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/unban")]
        [Authorize]
        public async Task<ActionResult> UnbanClan([FromQuery] int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);

            User? user = await _userController.GetUserLazy(currentID);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.BannedClans.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User did not ban this clan");
            }

            user.BannedClans.Remove(clan);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/leave")]
        [Authorize]
        public async Task<ActionResult> leaveClan([FromQuery] int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);

            User? user = await _userController.GetUserLazy(currentID);
            if (user == null)
            {
                return NotFound("No such player");
            }

            Clan? clan = user.Player.Clans.FirstOrDefault(c => c.Id == id);
            if (clan == null)
            {
                return NotFound("User is not member of this clan");
            }

            if (clan.LeaderID == currentID) {
                return BadRequest("You cannot leave your own clan");
            }

            user.Player.Clans.Remove(clan);
            clan.AverageAccuracy = MathUtils.RemoveFromAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.RemoveFromAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);
            clan.PlayersCount--;
            await _context.SaveChangesAsync();

            clan.Pp = _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/reserve")]
        public async Task<ActionResult> ReserveTag([FromQuery] string tag)
        {
            tag = tag.ToUpper();
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            _context.ReservedTags.Add(new ReservedClanTag {
                Tag = tag
            });
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/clan/reserve")]
        public async Task<ActionResult> AllowTag([FromQuery] string tag)
        {
            tag = tag.ToUpper();
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var rt = _context.ReservedTags.FirstOrDefault(rt => rt.Tag == tag);
            if (rt == null) {
                return NotFound();
            }

            _context.ReservedTags.Remove(rt);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
