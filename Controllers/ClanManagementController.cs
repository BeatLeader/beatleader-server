using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Ganss.Xss;
using System.Net;

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

            var player = await _context.Players.Where(p => p.Id == currentID).Include(p => p.Clans).Include(p => p.ScoreStats).FirstOrDefaultAsync();
            if (player.AnySupporter()) {
                if (player.Clans.Count >= 5)
                {
                    return BadRequest("5 clans is really a physical limit.");
                }
            } else {
                if (player.Clans.Count >= 3)
                {
                    return BadRequest("You can join only up to 3 clans. Support us on Patreon for more!");
                }
            }

            if (player.Banned)
            {
                return BadRequest("You are banned!");
            }

            string upperTag = tag.ToUpper();

            if ((await _context.ReservedTags.FirstOrDefaultAsync(t => t.Tag == upperTag)) != null)
            {
                return BadRequest("This tag is reserved. Ask someone with #BeatFounder role in discord to allow this tag for you.");
            }

            if ((await _context.Clans.FirstOrDefaultAsync(c => c.Name == name || c.Tag == upperTag)) != null)
            {
                return BadRequest("Clan with such name or tag is already exists");
            }

            if ((await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID)) != null)
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
                Icon = icon ?? "https://cdn.assets.beatleader.com/clan.png",
                Description = description,
                Bio = bio,
                PlayersCount = 1,
                Pp = player.Pp,
                AverageAccuracy = player.ScoreStats.AverageRankedAccuracy,
                AverageRank = player.Rank,
                RankedPoolPercentCaptured = 0,
                PlayerChangesCallback = playerChangesCallback,
                ClanRankingDiscordHook = clanRankingDiscordHook,
                DiscordInvite = ""
            };

            _context.Clans.Add(newClan);
            await _context.SaveChangesAsync();

            player.Clans.Add(newClan);
            player.RefreshClanOrder();
            await _context.SaveChangesAsync();
            await ClanUtils.RecalculateMainCountForPlayer(_context, player.Id);

            ClanTaskService.AddJob(new ClanRankingChangesDescription {
                GlobalMapEvent = GlobalMapEvent.create,
                PlayerId = player.Id,
                ClanId = newClan.Id,
                Clan = newClan
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
                clan = await _context.Clans.Include(c => c.Players).ThenInclude(c => c.Clans).FirstOrDefaultAsync(c => c.Id == id);
            }
            else
            {
                clan = await _context.Clans.Include(c => c.Players).ThenInclude(c => c.Clans).FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }
            if (clan == null)
            {
                return NotFound();
            }

            var clanIds = clan.Players.SelectMany(p => p.Clans.Select(c => c.Id)).Distinct().ToList();

            // Recalculate clanRanking on leaderboards that have this clan in their clanRanking
            var leaderboardsRecalc = await _context
                .Leaderboards
                .Include(lb => lb.ClanRanking)
                .Include(lb => lb.Difficulty)
                .Where(lb => lb.ClanRanking != null ?
                lb.ClanRanking.Any(lbClan => lbClan.Clan.Tag == clan.Tag) && lb.Difficulty.Status == DifficultyStatus.ranked :
                lb.Difficulty.Status == DifficultyStatus.ranked)
                .ToListAsync();

            // Remove the clanRankings
            var clanRankings = await _context.ClanRanking.Where(cr => cr.ClanId == clan.Id).ToListAsync();
            foreach (var cr in clanRankings)
            {
                _context.ClanRanking.Remove(cr);
            }

            var lbs = await _context.Leaderboards.Include(lb => lb.Clan).Where(lb => lb.ClanId == clan.Id).ToListAsync();

            foreach (var lb in lbs)
            {
                if (lb.Clan?.Id == clan.Id) {
                    lb.Clan = null;
                    lb.ClanId = null;
                }
            }

            await _context.BulkSaveChangesAsync();

            await _context.BulkDeleteAsync(_context.ClanUpdates.Where(cu => cu.Clan == clan));

            // Remove the clan
            _context.Clans.Remove(clan);
            
            await _context.BulkSaveChangesAsync();

            foreach (var clanPlayer in clan.Players)
            {
                clanPlayer.RefreshClanOrder();
            }
            await _context.BulkSaveChangesAsync();

            HttpContext.Response.OnCompleted(async () => {
                // Recalculate the clanRankings on each leaderboard where this clan had an impact
                var result = new List<ClanRankingChanges>(); 
                foreach (var leaderboard in leaderboardsRecalc)
                {
                    var changes = await _context.CalculateClanRankingSlow(leaderboard);
                    if (changes != null) {
                        result.AddRange(changes);
                    }
                }
                await _context.BulkSaveChangesAsync();
                await ClanUtils.RecalculateMainCount(_context, clanIds);

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
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }
            if (clan == null)
            {
                return NotFound();
            }

            var manager = await _context.ClanManagers.FirstOrDefaultAsync(cm => cm.ClanId == clan.Id && cm.PlayerId == playerId);
            if (manager == null) {
                manager = new ClanManager {
                    ClanId = clan.Id,
                    PlayerId = playerId
                };
                _context.ClanManagers.Add(manager);
            }
            manager.Permissions = permissions;
            await _context.SaveChangesAsync();

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
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
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
                if ((await _context.Clans.FirstOrDefaultAsync(c => c.Name == name && c.Id != clan.Id)) != null)
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
            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Edit));
            if (id != null && player != null && (clanManager != null || player.Role.Contains("admin")))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }
            if (clan == null)
            {
                return NotFound();
            }

            var timeset = Time.UnixNow();
            try {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                if (ms.Length > 0) {
                    ms.Position = 0;
                    using var sr = new StreamReader(ms);

                    var sanitizer = new HtmlSanitizer();
                    sanitizer.AllowedSchemes.Add("data");
                    var newBio = sanitizer.Sanitize(sr.ReadToEnd());

                    if (clan.RichBioTimeset > 0) {
                        await _s3Client.DeleteAsset($"clan-{clan.Tag}-richbio-{clan.RichBioTimeset}.html");
                    }

                    
                    var fileName = $"clan-{clan.Tag}-richbio-{timeset}.html";

                    clan.RichBioTimeset = timeset;
                    _ = await _s3Client.UploadAsset(fileName, newBio);
                }
            } catch (Exception e) {
                Console.WriteLine($"EXCEPTION: {e}");
                return BadRequest("Failed to save rich bio");
            }

            await _context.SaveChangesAsync();

            return Ok(timeset);
        }

        [HttpPut("~/clan/discordInvite")]
        public async Task<ActionResult> UpdateDiscordInvite(
            [FromQuery] string link,
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
            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Edit));
            if (id != null && player != null && (clanManager != null || player.Role.Contains("admin")))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }
            if (clan == null)
            {
                return NotFound();
            }

            if (link != clan.DiscordInvite) {
                clan.DiscordInvite = link;
                _context.ClanUpdates.Add(new ClanUpdate {
                    Clan = clan,
                    Player = player,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                    ChangeDescription = "Updated Discord invite"
                });
            }

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
            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Invite));
            if (id != null && player != null && (clanManager != null || currentPlayer.Role.Contains("admin")))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }

            if (clan == null)
            {
                return NotFound();
            }

            player = await _context.PlayerIdToMain(player);

            User? user = await _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.Player.Banned)
            {
                return BadRequest("This player is banned!");
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
            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Invite));
            if (id != null && player != null && (clanManager != null || currentPlayer.Role.Contains("admin")))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }

            if (clan == null)
            {
                return NotFound();
            }

            player = await _context.PlayerIdToMain(player);

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

            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == id && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Kick));
            if (id != null && player != null && (clanManager != null || currentPlayer.Role.Contains("admin")))
            {
                clan = await _context.Clans.FindAsync(id);
            }
            else
            {
                clan = await _context.Clans.FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }

            if (clan == null)
            {
                return NotFound("Current user is not leader of any clan");
            }
            if (clan.LeaderID == player)
            {
                return BadRequest("You cannot leave your own clan");
            }

            player = await _context.PlayerIdToMain(player);

            User? user = await _userController.GetUserLazy(player);
            if (user == null)
            {
                return NotFound("No such player");
            }

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) == null)
            {
                return NotFound("Player did not belong to this clan");
            }

            var clanIds = user.Player.Clans.Select(c => c.Id).ToList();

            user.Player.Clans.Remove(clan);
            user.Player.RefreshClanOrder();

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

            clan.Pp = await _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();
            await ClanUtils.RecalculateMainCount(_context, clanIds);

            ClanTaskService.AddJob(new ClanRankingChangesDescription {
                GlobalMapEvent = GlobalMapEvent.kick,
                PlayerId = user.Player.Id,
                Clan = clan,
                ClanId = clan.Id
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

            if (user.Player.AnySupporter()) {
                if (user.Player.Clans.Count >= 5) {
                    return BadRequest("You already joined maximum amount of clans, please leave some or reject invitation.");
                }
            } else {
                if (user.Player.Clans.Count >= 3)
                {
                    return BadRequest("You already joined maximum amount of clans, please leave some or support us on Patreon for increase.");
                }
            }

            user.ClanRequest.Remove(clan);

            if (user.Player.Clans.FirstOrDefault(c => c.Id == clan.Id) != null)
            {
                await _context.SaveChangesAsync();
                return BadRequest("Player already in this clan");
            }

            user.Player.Clans.Add(clan);
            user.Player.RefreshClanOrder();
            clan.PlayersCount++;
            clan.AverageAccuracy = MathUtils.AddToAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.AddToAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);

            await _context.SaveChangesAsync();

            clan.Pp = await _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();
            await ClanUtils.RecalculateMainCountForPlayer(_context, user.Player.Id);

            ClanTaskService.AddJob(new ClanRankingChangesDescription {
                GlobalMapEvent = GlobalMapEvent.join, 
                Clan = clan,
                PlayerId = user.Player.Id,
                ClanId = clan.Id,
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

            var clanIds = user.Player.Clans.Select(c => c.Id).ToList();

            user.Player.Clans.Remove(clan);
            user.Player.RefreshClanOrder();
            clan.AverageAccuracy = MathUtils.RemoveFromAverage(clan.AverageAccuracy, clan.PlayersCount, user.Player.ScoreStats.AverageRankedAccuracy);
            clan.AverageRank = MathUtils.RemoveFromAverage(clan.AverageRank, clan.PlayersCount, user.Player.Rank);
            clan.PlayersCount--;
            await _context.SaveChangesAsync();

            clan.Pp = await _context.RecalculateClanPP(clan.Id);
            await _context.SaveChangesAsync();
            await ClanUtils.RecalculateMainCount(_context, clanIds);

            ClanTaskService.AddJob(new ClanRankingChangesDescription {
                GlobalMapEvent = GlobalMapEvent.leave,
                Clan = clan,
                ClanId = clan.Id,
                PlayerId = user.Player.Id
            });

            return Ok();
        }

        [HttpPut("~/clan/playlist")]
        public async Task<ActionResult<FeaturedPlaylist>> AddPlaylist(
            [FromQuery] string title,
            [FromQuery] string link,
            [FromQuery] int? id = null,
            [FromQuery] int? clanId = null,
            [FromQuery] string? description = null)
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
            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == clanId && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Edit));
            if (clanId != null && player != null && (clanManager != null || player.Role.Contains("admin")))
            {
                clan = await _context.Clans.Include(c => c.FeaturedPlaylists).FirstOrDefaultAsync(c => c.Id == clanId);
            }
            else
            {
                clan = await _context.Clans.Include(c => c.FeaturedPlaylists).FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }

            if (clan == null)
            {
                return NotFound();
            }
            if (clan.FeaturedPlaylists == null) {
                clan.FeaturedPlaylists = new List<FeaturedPlaylist>();
            }

            FeaturedPlaylist? playlist = null;
            if (id != null) {
                playlist = clan.FeaturedPlaylists.FirstOrDefault(p => p.Id == id);
                if (playlist == null) {
                    return NotFound();
                }
            } else {
                if (clan.FeaturedPlaylists.Count > 2) {
                    return BadRequest("Up to 3 playlists allowed");
                }

                playlist = new FeaturedPlaylist();
                clan.FeaturedPlaylists.Add(playlist);
            }
            string playlistLink = "";
            if (link.Contains("beatleader.xyz") || link.Contains("beatleader.com")) {
                playlistLink = "https://api.beatleader.xyz/playlist/" + link.Split("/").Last();
            } else if (link.Contains("beatsaver.com")) {
                playlistLink = $"https://api.beatsaver.com/playlists/id/{link.Split("/").Last()}/download";
            } else if (link.Contains("hitbloq.com")) {
                playlist.Owner = "HitBloq";
                playlist.OwnerCover = "https://cdn.assets.beatleader.xyz/hitbloq-cover.png"; 
                playlist.OwnerLink = "https://hitbloq.com/";

                playlistLink = $"https://hitbloq.com/static/hashlists/{link.Split("/").Last()}.bplist";
            } else {
                return BadRequest("Only BeatLeader, BeatSaver and HitBloq playlist links");
            }

            string fileName = clan.Tag + "-featured-playlist-" + clan.FeaturedPlaylists.Count;
            string? imageUrl = null;
            try
            {
                var ms = new MemoryStream(5);
                await Request.Body.CopyToAsync(ms);
                ms.Position = 0;

                (string extension, MemoryStream stream2) = ImageUtils.GetFormat(ms);
                fileName += extension;

                imageUrl = await _s3Client.UploadAsset(fileName, stream2);
            } catch (Exception)
            {
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(playlistLink);
            var playlistFile = await request.DynamicResponse();
            if (playlistFile == null || !ExpandantoObject.HasProperty(playlistFile, "songs"))
            {
                return BadRequest("Can't decode playlist");
            }

            if (imageUrl == null && playlist.Cover == null) {
                string image = playlistFile.image;
                image = image.Replace("data:image/png;base64,", "").Replace("data:image/jpeg;base64,", "");

                imageUrl = await _s3Client.UploadAsset(fileName, new MemoryStream(Convert.FromBase64String(image)));
            } else {
                imageUrl = playlist.Cover;
            }

            playlist.PlaylistLink = link;
            playlist.Cover = imageUrl;
            playlist.Title = title;
            playlist.Description = description;

            await _context.SaveChangesAsync();

            return playlist;
        }

        [HttpDelete("~/clan/playlist")]
        public async Task<ActionResult<FeaturedPlaylist>> DeletePlaylist(
            [FromQuery] int id,
            [FromQuery] int? clanId = null)
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
            var clanManager = await _context.ClanManagers.FirstOrDefaultAsync(cm => 
                                                cm.ClanId == clanId && 
                                                cm.PlayerId == currentID && 
                                                cm.Permissions.HasFlag(ClanPermissions.Edit));
            if (clanId != null && player != null && (clanManager != null || player.Role.Contains("admin")))
            {
                clan = await _context.Clans.Include(c => c.FeaturedPlaylists).FirstOrDefaultAsync(c => c.Id == clanId);
            }
            else
            {
                clan = await _context.Clans.Include(c => c.FeaturedPlaylists).FirstOrDefaultAsync(c => c.LeaderID == currentID);
            }

            if (clan == null)
            {
                return NotFound();
            }

            FeaturedPlaylist? playlist = clan.FeaturedPlaylists?.FirstOrDefault(p => p.Id == id);
            if (playlist == null) {
                return NotFound();
            }

            clan.FeaturedPlaylists.Remove(playlist);
            await _context.SaveChangesAsync();

            return playlist;
        }
    }
}
