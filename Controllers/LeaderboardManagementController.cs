using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lib.ServerTiming;
using System.Net;

namespace BeatLeader_Server.Controllers {
    public class LeaderboardManagementController : Controller {

        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;

        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;

        public LeaderboardManagementController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            IConfiguration configuration,
            IServerTiming serverTiming) {
            _context = context;
            _dbFactory = dbFactory;
            _s3Client = configuration.GetS3Client();
            _serverTiming = serverTiming;
        }

        [HttpPost("~/leaderboard/tags")]
        public async Task<ActionResult> UpdateTags(
            [FromQuery] string id,
            [FromQuery] string tagType, 
            [FromQuery] int tagValue) {

            string? currentID = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = currentID != null ? await _context
                .Players
                .Include(p => p.ProfileSettings)
                .FirstOrDefaultAsync(p => p.Id == currentID) : null;

            bool isLoloppe = currentPlayer?.Id == "76561198073989976" || currentPlayer?.Role?.Contains("admin") == true;
            if (!isLoloppe) {
                return BadRequest("Not Loloppe");
            }

            var lb = _context.Leaderboards.Where(lb => lb.Id == id).Include(lb => lb.Difficulty).FirstOrDefault();
            if (lb == null) {
                return NotFound();
            }

            switch (tagType)
            {
                case "speed":
                    lb.Difficulty.SpeedTags = tagValue;
                    break;
                case "style":
                    lb.Difficulty.StyleTags = tagValue;
                    break;
                case "features":
                    lb.Difficulty.FeatureTags = tagValue;
                    break;
                default:
                    break;
            }

            _context.SaveChanges();

            return Ok();
        }

        //[HttpDelete("~/leaderboard/{id}")]
        [NonAction]
        public async Task<ActionResult> Delete(
            string id) {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var lb = await _context.Leaderboards.FirstOrDefaultAsync(lb => lb.Id == id);

            if (lb != null) {
                _context.Leaderboards.Remove(lb);
                await _context.SaveChangesAsync();
            } else {
                return NotFound();
            }

            return Ok();
        }

        [HttpPost("~/leaderboards/feature")]
        public async Task<ActionResult> FeatureLeaderboards(
               [FromQuery] string title,
               [FromQuery] string? owner = null,
               [FromQuery] string? ownerCover = null,
               [FromQuery] string? ownerLink = null,
               [FromQuery] int? id = null,
               [FromQuery] string? playlistLink = null,
               [FromQuery] string? linkToSave = null)
        {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            dynamic? playlist = null;

            if (id != null) {
                using (var stream = await _s3Client.DownloadPlaylist(id + ".bplist"))
                {
                    if (stream != null)
                    {
                        playlist = stream.ObjectFromStream();
                    }
                }
            } else {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(playlistLink);
                playlist = await request.DynamicResponse();
            }

            if (playlist == null)
            {
                return BadRequest("Can't find such plist");
            }

            string fileName = id + "-featured";
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
                return BadRequest("Error saving avatar");
            }

            var featuredPlaylist = new FeaturedPlaylist
            {
                PlaylistLink = linkToSave ?? $"https://beatleader.xyz/playlist/{id}",
                Cover = imageUrl,
                Title = title,

                Owner = owner,
                OwnerCover = ownerCover,
                OwnerLink = ownerLink
            };

            var leaderboards = new List<Leaderboard>();
            foreach (var song in playlist.songs)
            {
                string hash = song.hash.ToLower();
                if (ExpandantoObject.HasProperty(song, "difficulties")){
                    foreach (var diff in song.difficulties)
                    {
                        string diffName = diff.name.ToLower();
                        string characteristic = diff.characteristic.ToLower();

                        var lb = await _context.Leaderboards.Where(lb =>
                                lb.Song.Hash.ToLower() == hash &&
                                lb.Difficulty.DifficultyName.ToLower() == diffName &&
                                lb.Difficulty.ModeName.ToLower() == characteristic)
                                .Include(lb => lb.FeaturedPlaylists)
                                .FirstOrDefaultAsync();

                        if (lb != null)
                        {
                            if (lb.FeaturedPlaylists == null)
                            {
                                lb.FeaturedPlaylists = new List<FeaturedPlaylist>();
                            }

                            lb.FeaturedPlaylists.Add(featuredPlaylist);
                        }
                    }
                } else {
                    var lbs = await _context.Leaderboards.Where(lb =>
                                lb.Song.Hash.ToLower() == hash)
                                .Include(lb => lb.FeaturedPlaylists)
                                .ToListAsync();
                    foreach (var lb in lbs)
                    {
                        if (lb != null)
                        {
                            if (lb.FeaturedPlaylists == null)
                            {
                                lb.FeaturedPlaylists = new List<FeaturedPlaylist>();
                            }

                            lb.FeaturedPlaylists.Add(featuredPlaylist);
                        }
                    }  
                }
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/leaderboards/feature/{id}")]
        public async Task<ActionResult> DeleteFeatureLeaderboards(
               int id)
        {
            if (HttpContext != null)
            {
                string userId = HttpContext.CurrentUserID(_context);
                var currentPlayer = await _context.Players.FindAsync(userId);

                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            var featuredPlaylist = await _context.FeaturedPlaylist.FindAsync(id);
            _context.FeaturedPlaylist.Remove(featuredPlaylist);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
