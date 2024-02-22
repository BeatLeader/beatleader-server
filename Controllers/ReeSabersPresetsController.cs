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
using Newtonsoft.Json;
using System.IO.Compression;
using System.Text;

namespace BeatLeader_Server.Controllers
{
    public class ReeSabersPresetsController : Controller
    {

        private readonly AppContext _context;

        private readonly IAmazonS3 _assetsS3Client;
        private readonly CurrentUserController _userController;
        private readonly IWebHostEnvironment _environment;
        private readonly IHttpClientFactory _httpClientFactory;

        public ReeSabersPresetsController(
            AppContext context,
            IWebHostEnvironment env,
            CurrentUserController userController,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _userController = userController;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("/reepresets")]
        public async Task<ActionResult<ResponseWithMetadata<ReeSabersPreset>>> ListPresets(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sort = "downloads",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null)
        {
            var sequence = _context.ReeSabersPresets.AsQueryable();
            switch (sort)
            {
                case "name":
                    sequence = sequence.Order(order, p => p.Name);
                    break;
                case "downloads":
                    sequence = sequence.Order(order, p => p.DownloadsCount);
                    break;
                case "date":
                    sequence = sequence.Order(order, p => p.Timeupdated);
                    break;
                case "likes":
                    sequence = sequence.Order(order, p => p.ReactionsCount);
                    break;
                default:
                    break;
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string lowerCaseSearch = search.ToLower();
                sequence = sequence.Where(p => p.Name.ToLower().Contains(lowerCaseSearch) ||
                                               p.Description.ToLower().Contains(lowerCaseSearch));
            }

            var pageData = await sequence
                .Skip((page - 1) * count)
                .Take(count)
                .Include(p => p.Reactions)
                .ToListAsync();
            return new ResponseWithMetadata<ReeSabersPreset>
            {
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = pageData
            };
        }

        [HttpGet("~/reepreset/{id}")]
        public async Task<ActionResult<ResponseWithMetadataAndContainer<ReeSabersComment, ReeSabersPreset>>> GetPreset(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sort = "pp",
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? capturedLeaderboards = null)
        {
            var preset = _context
                    .ReeSabersPresets
                    .Where(c => c.Id == id)
                    .Include(p => p.Owner)
                    .Include(p => p.Reactions)
                    .ThenInclude(p => p.Author)
                    .Include(p => p.Comments)
                    .ThenInclude(p => p.Player)
                    .FirstOrDefault();
            if (preset == null)
            {
                return NotFound();
            }

            var comments = preset.Comments.AsQueryable();
            switch (sort)
            {
                case "time":
                    comments = comments.Order(order, t => t.Id);
                    break;
                default:
                    break;
            }
            return new ResponseWithMetadataAndContainer<ReeSabersComment, ReeSabersPreset>
            {
                Container = preset,
                Data = comments.Skip((page - 1) * count).Take(count),
                Metadata = new Metadata
                {
                    Page = 1,
                    ItemsPerPage = 10,
                    Total = comments.Count()
                }
            };
        }

        [HttpPost("~/reepreset/create")]
        public async Task<ActionResult<ReeSabersPreset>> CreatePreset(
            [FromForm] string name,
            [FromForm] string description,
            [FromForm] bool commentsDisabled,
            [FromForm] ReeSabersTags tags)
        {
            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId == null)
            {
                currentId = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Preset);
            }
            if (currentId == null)
            {
                return Unauthorized();
            }

            var player = await _context.Players.FindAsync(currentId);
            if (player == null)
            {
                return NotFound("Player not found.");
            }

            if (name.Length < 2 || name.Length > 25) {
                return BadRequest("Please keep the name from 2 to 25 characters long");
            }

            if (!Request.HasFormContentType) {
                return BadRequest("Please attach files in form");
            }

            if (!Request.Form.Files.Any(f => f.FileName.Contains("coverfile") && f.ContentType.Contains("image"))) {
                return BadRequest("Please attach preset cover file in form. The name should contain `coverfile`");
            }

            if (!Request.Form.Files.Any(f => f.FileName.Contains("jsonfile") && f.ContentType.Contains("json"))) {
                return BadRequest("Please attach json files of the preset in form. Names should contain `jsonfile`");
            }

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            Random rnd = new Random();
            string fileprefix = $"{timeset}_{"R" + rnd.Next(1, 50)}";

            string? jsonLinks = null;;
            string? textureLinks = null;
            string? coverLink = null;

            try
            {
                if (Request.HasFormContentType && Request.Form.Files.Any())
                {
                    foreach (var file in Request.Form.Files)
                    {
                        var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        ms.Position = 0;

                        string fileName = $"{fileprefix}_{file.FileName}";
                        string fileLink = await _assetsS3Client.UploadReepreset(fileName, ms);

                        if (file.FileName.Contains("jsonfile") && file.ContentType.Contains("json"))
                        {
                            jsonLinks += string.IsNullOrEmpty(jsonLinks) ? fileLink : $",{fileLink}";
                        }
                        else if (file.FileName.Contains("coverfile") && file.ContentType.Contains("image"))
                        {
                            coverLink = fileLink;
                        }
                        else if (file.FileName.Contains("texturefile") && file.ContentType.Contains("image"))
                        {
                            textureLinks += string.IsNullOrEmpty(textureLinks) ? fileLink : $",{fileLink}";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                return BadRequest("Error uploading files to CDN.");
            }

            if (coverLink == null || jsonLinks == null) {
                return BadRequest("Error uploading files to CDN.");
            }

            ReeSabersPreset newPreset = new ReeSabersPreset
            {
                Owner = player,
                Name = name,
                Description = description,
                Tags = tags,
                Timeposted = timeset,
                Timeupdated = timeset,
                CoverLink = coverLink,
                JsonLinks = jsonLinks,
                TextureLinks = textureLinks,
                CommentsDisabled = commentsDisabled,
                Version = "0.1.0"
            };

            _context.ReeSabersPresets.Add(newPreset);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPreset), new { id = newPreset.Id }, newPreset);
        }

        [HttpPost("~/reepresets/{id}/reaction/toggle")]
        public async Task<ActionResult> ToggleReaction(int id, [FromQuery] ReeSaberReaction reaction)
        {
            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId == null)
            {
                currentId = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Preset);
            }
            if (currentId == null)
            {
                return Unauthorized();
            }

            var preset = await _context.ReeSabersPresets.Where(p => p.Id == id).Include(p => p.Reactions).FirstOrDefaultAsync();
            if (preset == null)
            {
                return NotFound();
            }
            var re = preset.Reactions.FirstOrDefault(r => r.Reaction == reaction && r.AuthorId == currentId);

            if (re == null) {
                preset.Reactions.Add(new ReeSabersReaction {
                    AuthorId = currentId,
                    Reaction = reaction
                });
                preset.ReactionsCount++;
            } else {
                preset.Reactions.Remove(re);
                preset.ReactionsCount--;
            }
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/reepresets/{id}/download/quest")]
        public async Task<IActionResult> DownloadPresetQmod(int id)
        {
            var preset = await _context.ReeSabersPresets.Where(p => p.Id == id).Include(p => p.Owner).FirstOrDefaultAsync();
            if (preset == null)
            {
                return NotFound();
            }

            var httpClient = _httpClientFactory.CreateClient();

            using var memoryStream = await ReepresetsUtils.QuestMod(preset, httpClient);

            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId != null)
            {
               if (_context.ReePresetDownloads.FirstOrDefault(pd => pd.Player == currentId && pd.PresetId == id) == null) {
                    preset.DownloadsCount++;
                    preset.QuestDownloadsCount++;
                    _context.ReePresetDownloads.Add(new ReePresetDownload {
                        Player = currentId,
                        PresetId = id,
                    });
                    await _context.SaveChangesAsync();
               }
            }

            var outputStream = new MemoryStream();
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(outputStream);
            outputStream.Position = 0;
            return File(outputStream, "application/zip", $"{preset.Name}_ReePreset.qmod");
        }
        
        [HttpGet("~/reepresets/{id}/download/pc")]
        public async Task<IActionResult> DownloadPresetZip(int id)
        {
            var preset = await _context.ReeSabersPresets.Where(p => p.Id == id).Include(p => p.Owner).FirstOrDefaultAsync();
            if (preset == null)
            {
                return NotFound();
            }

            var httpClient = _httpClientFactory.CreateClient();

            using var memoryStream = await ReepresetsUtils.PCMod(preset, httpClient);

            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId != null)
            {
               if (_context.ReePresetDownloads.FirstOrDefault(pd => pd.Player == currentId && pd.PresetId == id) == null) {
                    preset.DownloadsCount++;
                    preset.PCDownloadsCount++;
                    _context.ReePresetDownloads.Add(new ReePresetDownload {
                        Player = currentId,
                        PresetId = id,
                    });
                    await _context.SaveChangesAsync();
               }
            }

            var outputStream = new MemoryStream();
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(outputStream);
            outputStream.Position = 0;
            return File(outputStream, "application/zip", $"{preset.Name}_ReePreset.zip");
        }

        [HttpPut("~/reepreset/{id}")]
        public async Task<ActionResult<ReeSabersPreset>> UpdatePreset(
            int id,
            [FromForm] string? name,
            [FromForm] string? description,
            [FromForm] bool? commentsDisabled,
            [FromForm] string? filesToDelete,
            [FromForm] ReeSabersTags? tags)
        {
            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId == null)
            {
                currentId = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Preset);
            }
            if (currentId == null)
            {
                return Unauthorized();
            }

            var preset = await _context.ReeSabersPresets.FindAsync(id);
            if (preset == null)
            {
                return NotFound("Preset not found.");
            }

            int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            Random rnd = new Random();
            string fileprefix = $"{timeset}_{"R" + rnd.Next(1, 50)}";

            string? jsonLinks = null;;
            string? textureLinks = null;
            string? coverLink = null;

            try
            {
                if (Request.HasFormContentType && Request.Form.Files.Any())
                {
                    foreach (var file in Request.Form.Files)
                    {
                        var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        ms.Position = 0;

                        string fileName = $"{fileprefix}_{file.FileName}";
                        string fileLink = await _assetsS3Client.UploadReepreset(fileName, ms);

                        if (file.FileName.Contains("jsonfile") && file.ContentType.Contains("json"))
                        {
                            jsonLinks += string.IsNullOrEmpty(jsonLinks) ? fileLink : $",{fileLink}";
                        }
                        else if (file.FileName.Contains("coverfile") && file.ContentType.Contains("image"))
                        {
                            coverLink = fileLink;
                        }
                        else if (file.FileName.Contains("texturefile") && file.ContentType.Contains("image"))
                        {
                            textureLinks += string.IsNullOrEmpty(textureLinks) ? fileLink : $",{fileLink}";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                return BadRequest("Error uploading files to CDN.");
            }

            if (name != null) {
                if (name.Length < 2 || name.Length > 25) {
                    return BadRequest("Please keep the name from 2 to 25 characters long");
                }
                preset.Name = name;
            }

            if (description != null) {
                preset.Description = description;
            }

            if (tags != null) {
                preset.Tags = tags ?? ReeSabersTags.None;
            }

            if (coverLink != null) {
                preset.CoverLink = coverLink;
            }

            if (commentsDisabled != null) {
                preset.CommentsDisabled = (bool)commentsDisabled;
            }

            var currentJsonLinks = preset.JsonLinks;
            var currentTextureLinks = preset.TextureLinks;

            if (filesToDelete != null)
            {
                var filesToDeleteList = filesToDelete.Split(",");
                foreach (var toDelete in filesToDeleteList)
                {
                    if (!string.IsNullOrEmpty(toDelete)) {
                        currentJsonLinks = currentJsonLinks.Replace(toDelete, "");
                        currentTextureLinks = currentTextureLinks?.Replace(toDelete, "");
                    }
                }
            }

            if (jsonLinks != null) {
                currentJsonLinks += string.IsNullOrEmpty(currentJsonLinks) ? jsonLinks : $",{jsonLinks}";
            }

            if (textureLinks != null) {
                currentTextureLinks += string.IsNullOrEmpty(currentTextureLinks) ? textureLinks : $",{textureLinks}";
            }

            if (currentJsonLinks != null) {
                currentJsonLinks = string.Join(",", currentJsonLinks.Split(",").Where(s => !string.IsNullOrEmpty(s)));
            }

            if (currentTextureLinks != null) {
                currentTextureLinks = string.Join(",", currentTextureLinks.Split(",").Where(s => !string.IsNullOrEmpty(s)));
            }

            preset.JsonLinks = currentJsonLinks;
            preset.TextureLinks = currentTextureLinks;
            preset.Timeupdated = timeset;

            await _context.SaveChangesAsync();

            return preset;
        }

        [HttpDelete("~/reepreset/{id}")]
        public async Task<IActionResult> DeletePreset(int id)
        {
            var preset = _context
                .ReeSabersPresets
                .Where(rp => rp.Id == id)
                .Include(rp => rp.Comments)
                .Include(rp => rp.Reactions)
                .FirstOrDefault();

            if (preset == null)
            {
                return NotFound();
            }

            var currentId = HttpContext.CurrentUserID(_context);
            if (currentId == null)
            {
                currentId = await HttpContext.CurrentOauthUserID(_context, CustomScopes.Preset);
            }
            if (currentId == null || preset.OwnerId != currentId)
            {
                return Unauthorized();
            }

            foreach (var item in preset.Comments)
            {
                preset.Comments.Remove(item);
            }

            foreach (var item in preset.Reactions)
            {
                preset.Reactions.Remove(item);
            }

            _context.ReeSabersPresets.Remove(preset);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/preset/comment/{id}")]
        public async Task<ActionResult<ReeSabersComment>> PostComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            var preset = _context
                .ReeSabersPresets
                .Include(q => q.Comments)
                .FirstOrDefault(l => l.Id == id);

            if (preset == null)
            {
                return NotFound();
            }

            if (currentPlayer == null || preset.CommentsDisabled)
            {
                return Unauthorized();
            }

            if (preset.Comments == null)
            {
                preset.Comments = new List<ReeSabersComment>();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            var result = new ReeSabersComment
            {
                PlayerId = currentPlayer.Id,
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                Value = Encoding.UTF8.GetString(ms.ToArray()),
            };

            preset.Comments.Add(result);
            await _context.SaveChangesAsync();

            return result;
        }

        [HttpPut("~/preset/comment/{id}")]
        public async Task<ActionResult<ReeSabersComment>> UpdateComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Id == currentID);

            var comment = await _context
                .ReeSabersComment
                .FirstOrDefaultAsync(c => c.Id == id);
            if (comment == null)
            {
                return NotFound();
            }
            if (comment.PlayerId != currentPlayer.Id && !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var ms = new MemoryStream(5);
            await Request.Body.CopyToAsync(ms);
            ms.Position = 0;

            comment.Value = Encoding.UTF8.GetString(ms.ToArray());
            comment.Edited = true;
            comment.EditTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            await _context.SaveChangesAsync();

            return comment;
        }

        [Authorize]
        [HttpDelete("~/preset/comment/{id}")]
        public async Task<ActionResult> DeleteComment(int id)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            var comment = await _context.ReeSabersComment.FirstOrDefaultAsync(c => c.Id == id);
            if (comment == null)
            {
                return NotFound();
            }
            if (comment.PlayerId != currentPlayer.Id && !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            _context.ReeSabersComment.Remove(comment);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
