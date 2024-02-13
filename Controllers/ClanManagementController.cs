using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BeatLeader_Server.Controllers
{
    public class ClanManagementController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        CurrentUserController _userController;
        IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public ClanManagementController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            IConfiguration configuration)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
            _configuration = configuration;
        }

        [NonAction]
        public async Task<List<ClanRankingChanges>?> RecalculateClanRanking(string playerId) {
            var leaderboardsRecalc = _context
                .Scores
                .Where(s => s.Pp > 0 && !s.Qualification && s.PlayerId == playerId)
                .Include(s => s.Leaderboard)
                .ThenInclude(lb => lb.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(lb => lb.ClanRanking)
                .Select(s => s.Leaderboard)
                .ToList();
            var result = new List<ClanRankingChanges>(); 
            foreach (var leaderboard in leaderboardsRecalc)
            {
                var changes = _context.CalculateClanRankingSlow(leaderboard);
                if (changes != null) {
                    result.AddRange(changes);
                }
            }
            await _context.BulkSaveChangesAsync();
            return result;
        }

        [HttpPost("~/clan/create")]
        public async Task<ActionResult<Clan>> CreateClan(
            [FromQuery] string name,
            [FromQuery] string tag,
            [FromQuery] string color,
            [FromQuery] string description = "",
            [FromQuery] string bio = "",
            [FromQuery] string? clanRankingDiscordHook = null,
            [FromQuery] string? playerChangesCallback = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }
            if (currentID == null) {
                return Unauthorized();
            }

            var player = _context.Players.Where(p => p.Id == currentID).Include(p => p.Clans).Include(p => p.ScoreStats).FirstOrDefault();
            if (player.Clans.Count == 3)
            {
                return BadRequest("You can join only up to 3 clans.");
            }

            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }

            string upperTag = tag.ToUpper();

            if (_context.ReservedTags.FirstOrDefault(t => t.Tag == upperTag) != null)
            {
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
            if (upperTag.Length is > 4 or < 2 || !Regex.IsMatch(upperTag, @"^[A-Z0-9]+$"))
            {
                return BadRequest("Clan tag should be from 2 to 4 capital latin letters or numbers");
            }
            if (name.Length is > 25 or < 2)
            {
                return BadRequest("Clan name should be from 2 to 25 letters");
            }
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

                icon = await _s3Client.UploadAsset(fileName, stream);
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
                AverageRank = player.Rank,
                RankedPoolPercentCaptured = 0,
                PlayerChangesCallback = playerChangesCallback,
                ClanRankingDiscordHook = clanRankingDiscordHook
            };

            _context.Clans.Add(newClan);
            await _context.SaveChangesAsync();

            player.Clans.Add(newClan);
            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {

                ClanTaskService.AddJob(new ClanRankingChangesDescription {
                    GlobalMapEvent = GlobalMapEvent.create,
                    PlayerId = player.Id,
                    ClanId = newClan.Id,
                    Clan = newClan,
                    Changes = await RecalculateClanRanking(currentID)
                });
            });

            return newClan;
        }

        [HttpDelete("~/clan")]
        public async Task<ActionResult> DeleteClan([FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }
            if (currentID == null) {
                return Unauthorized();
            }

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

            // Recalculate clanRanking on leaderboards that have this clan in their clanRanking
            var leaderboardsRecalc = _context
                .Leaderboards
                .Include(lb => lb.ClanRanking)
                .Include(lb => lb.Difficulty)
                .Where(lb => lb.ClanRanking != null ?
                lb.ClanRanking.Any(lbClan => lbClan.Clan.Tag == clan.Tag) && lb.Difficulty.Status == DifficultyStatus.ranked :
                lb.Difficulty.Status == DifficultyStatus.ranked)
                .ToList();

            // Remove the clanRankings
            var clanRankings = _context.ClanRanking.Where(cr => cr.ClanId == clan.Id).ToList();
            foreach (var cr in clanRankings)
            {
                _context.ClanRanking.Remove(cr);
            }

            var lbs = _context.Leaderboards.Include(lb => lb.Clan).Where(lb => lb.ClanId == clan.Id).ToList();

            foreach (var lb in lbs)
            {
                if (lb.Clan?.Id == clan.Id) {
                    lb.Clan = null;
                    lb.ClanId = null;
                }
            }

            await _context.BulkSaveChangesAsync();

            // Remove the clan
            _context.Clans.Remove(clan);
            
            await _context.BulkSaveChangesAsync();
            HttpContext.Response.OnCompleted(async () => {
                // Recalculate the clanRankings on each leaderboard where this clan had an impact
                var result = new List<ClanRankingChanges>(); 
                foreach (var leaderboard in leaderboardsRecalc)
                {
                    var changes = _context.CalculateClanRankingSlow(leaderboard);
                    if (changes != null) {
                        result.AddRange(changes);
                    }
                }
                await _context.BulkSaveChangesAsync();

                ClanTaskService.AddJob(new ClanRankingChangesDescription {
                    GlobalMapEvent = GlobalMapEvent.dismantle,
                    PlayerId = player.Id,
                    Clan = clan,
                    ClanId = clan.Id,
                    Changes = result,
                });
            });

            return Ok();
        }

        [HttpPost("~/clan/manager")]
        public async Task<ActionResult> UpdateManagers(
            [FromQuery] string playerId, 
            [FromQuery] ClanPermissions permissions,
            [FromQuery] int? id = null) 
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }

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

            var manager = _context.ClanManagers.FirstOrDefault(cm => cm.ClanId == clan.Id && cm.PlayerId == playerId);
            if (manager == null) {
                manager = new ClanManager {
                    ClanId = clan.Id,
                    PlayerId = playerId
                };
                _context.ClanManagers.Add(manager);
            }
            manager.Permissions = permissions;
            _context.SaveChanges();

            return Ok();
        }

        [HttpPut("~/clan")]
        public async Task<ActionResult> UpdateClan(
            [FromQuery] int? id = null,
            [FromQuery] string? name = null,
            [FromQuery] string? color = null,
            [FromQuery] string? clanRankingDiscordHook = null,
            [FromQuery] string? playerChangesCallback = null,
            [FromQuery] string description = "",
            [FromQuery] string bio = "")
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }

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

            var changeDescription = "Updated ";

            if (name != null)
            {
                if (name.Length is < 3 or > 30)
                {
                    return BadRequest("Use name between the 3 and 30 symbols");
                }
                if (_context.Clans.FirstOrDefault(c => c.Name == name && c.Id != clan.Id) != null)
                {
                    return BadRequest("Clan with such name is already exists");
                }

                if (name != clan.Name) {
                    changeDescription += "name, ";
                }

                clan.Name = name;
            }

            if (color != null)
            {
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

                if (clan.Color != color) {
                    changeDescription += "color, ";
                }

                clan.Color = color;
            }
            Random rnd = new Random();
            string fileName = clan.Tag + "R" + rnd.Next(1, 50) + "clan";
            try
            {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                if (ms.Length > 0)
                {
                    ms.Position = 0;

                    (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(ms);
                    fileName += extension;

                    clan.Icon = await _s3Client.UploadAsset(fileName, stream);
                    changeDescription += "icon, ";
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"EXCEPTION: {e}");
            }

            if (description.Length > 100)
            {
                return BadRequest("Description is too long");
            }
            else
            {
                if (clan.Description != description) {
                    changeDescription += "description, ";
                }
                clan.Description = description;
            }

            if (bio.Length > 1000)
            {
                return BadRequest("Bio is too long");
            }
            else
            {
                if (clan.Bio != bio) {
                    changeDescription += "bio, ";
                }
                clan.Bio = bio;
            }

            if (clanRankingDiscordHook != null)
            {
                if (clan.ClanRankingDiscordHook != clanRankingDiscordHook) {
                    changeDescription += "Discord hook, ";
                }
                clan.ClanRankingDiscordHook = clanRankingDiscordHook;
            }

            if (playerChangesCallback != null) {
                if (clan.PlayerChangesCallback != playerChangesCallback) {
                    changeDescription += "changes callback, ";
                }
                clan.PlayerChangesCallback = playerChangesCallback;
            }

            _context.ClanUpdates.Add(new ClanUpdate {
                Clan = clan,
                Player = player,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                ChangeDescription = changeDescription
            });

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPut("~/clan/richbio")]
        public async Task<ActionResult> UpdateClanRichBio(
            [FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }

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
            var clanManager = _context.ClanManagers.FirstOrDefault(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Edit));
            if (id != null && player != null && (clanManager != null || player.Role.Contains("admin")))
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

            try
            {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                if (ms.Length > 0)
                {
                    ms.Position = 0;
                    using var sr = new StreamReader(ms);

                    clan.RichBio = sr.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"EXCEPTION: {e}");
            }

            _context.ClanUpdates.Add(new ClanUpdate {
                Clan = clan,
                Player = player,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                ChangeDescription = "Updated rich bio"
            });

            await _context.SaveChangesAsync();

            return Ok();
        }


        [HttpPost("~/clan/invite")]
        public async Task<ActionResult> InviteToClan(
            [FromQuery] string player,
            [FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }

            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null)
            {
                return NotFound();
            }

            if (currentPlayer.Banned)
            {
                return BadRequest("You are banned!");
            }

            Clan? clan = null;
            var clanManager = _context.ClanManagers.FirstOrDefault(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Invite));
            if (id != null && player != null && (clanManager != null || currentPlayer.Role.Contains("admin")))
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

            User? user = await _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.Player.Banned)
            {
                return BadRequest("This player are banned!");
            }

            if (user.BannedClans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                return BadRequest("This clan was banned by player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                return BadRequest("Player already in this clan");
            }

            user.ClanRequest.Add(clan);
            _context.ClanUpdates.Add(new ClanUpdate {
                Clan = clan,
                Player = currentPlayer,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                ChangeDescription = "Invited " + user.Player.Name
            });
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/cancelinvite")]
        public async Task<ActionResult> CancelinviteToClan(
            [FromQuery] string player,
            [FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }

            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null)
            {
                return NotFound();
            }

            if (currentPlayer.Banned)
            {
                return BadRequest("You are banned!");
            }

            Clan? clan = null;
            var clanManager = _context.ClanManagers.FirstOrDefault(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Invite));
            if (id != null && player != null && (clanManager != null || currentPlayer.Role.Contains("admin")))
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
        public async Task<ActionResult> KickPlayer(
            [FromQuery] string player,
            [FromQuery] int? id = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }
            if (currentID == null) {
                return Unauthorized();
            }

            Clan? clan;
            var currentPlayer = await _context.Players.FindAsync(currentID);

            var clanManager = _context.ClanManagers.FirstOrDefault(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Kick));
            if (id != null && player != null && (clanManager != null || currentPlayer.Role.Contains("admin")))
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

            _context.ClanUpdates.Add(new ClanUpdate {
                Clan = clan,
                Player = currentPlayer,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                ChangeDescription = "Kicked out " + user.Player.Name
            });

            await _context.SaveChangesAsync();

            clan.Pp = _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                ClanTaskService.AddJob(new ClanRankingChangesDescription {
                    GlobalMapEvent = GlobalMapEvent.kick,
                    PlayerId = user.Player.Id,
                    Clan = clan,
                    ClanId = clan.Id,
                    Changes = await RecalculateClanRanking(player),
                });
            });

            return Ok();
        }

        [HttpPost("~/clan/accept")]
        public async Task<ActionResult> AcceptRequest([FromQuery] int id)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }
            if (currentID == null) {
                return Unauthorized();
            }

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

            if (user.Player.Clans.Count == 3)
            {
                return BadRequest("You already joined maximum amount of clans, please leave some or reject.");
            }

            user.ClanRequest.Remove(clan);

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                await _context.SaveChangesAsync();
                return BadRequest("Player already in this clan");
            }

            user.Player.Clans.Add(clan);
            clan.PlayersCount++;
            clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);
            if (user.Player.ClanOrder.Length > 0) {
                user.Player.ClanOrder += $",{clan.Tag}";
            }

            await _context.SaveChangesAsync();

            clan.Pp = _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                ClanTaskService.AddJob(new ClanRankingChangesDescription {
                    GlobalMapEvent = GlobalMapEvent.join, 
                    Clan = clan,
                    PlayerId = user.Player.Id,
                    ClanId = clan.Id,
                    Changes = await RecalculateClanRanking(currentID)
                });
            });

            return Ok();
        }

        [HttpPost("~/clan/reject")]
        public async Task<ActionResult> RejectRequest([FromQuery] int id, bool ban = false)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }

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
            if (ban)
            {
                user.BannedClans.Add(clan);
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/clan/unban")]
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

            if (clan.PlayerChangesCallback != null) {
                ClanTaskService.AddJob(new ClanRankingChangesDescription {
                    GlobalMapEvent = GlobalMapEvent.reject,
                    PlayerId = user.Player.Id,
                    Clan = clan,
                    ClanId = clan.Id
                });
            }

            return Ok();
        }

        [HttpPost("~/clan/leave")]
        public async Task<ActionResult> leaveClan([FromQuery] int id)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                currentID = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Clan);
            }
            if (currentID == null) {
                return Unauthorized();
            }

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

            if (clan.LeaderID == currentID)
            {
                return BadRequest("You cannot leave your own clan");
            }

            user.Player.Clans.Remove(clan);
            clan.AverageAccuracy = MathUtils.RemoveFromAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.RemoveFromAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);
            clan.PlayersCount--;
            if (user.Player.ClanOrder.Length > 0) {
                user.Player.ClanOrder = string.Join(",", user.Player.ClanOrder.Split(",").Where(c => c != clan.Tag).ToList());
            }
            await _context.SaveChangesAsync();

            clan.Pp = _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                ClanTaskService.AddJob(new ClanRankingChangesDescription {
                    GlobalMapEvent = GlobalMapEvent.leave,
                    Clan = clan,
                    ClanId = clan.Id,
                    PlayerId = user.Player.Id,
                    Changes = await RecalculateClanRanking(currentID)
                });
            });

            return Ok();
        }
    }
}
