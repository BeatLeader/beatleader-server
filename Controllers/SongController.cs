using Amazon.S3;
using beatleader_parser;
using Parser.Utils;
using BeatLeader_Server.Bot;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Models.Models;
using ProtoBuf;
using System.IO.Compression;
using static BeatLeader_Server.Utils.ResponseUtils;
using BeatLeader_Server.ControllerHelpers;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SongController : Controller
    {
        private readonly AppContext _context;
        private readonly RTNominationsForum _rtNominationsForum;
        private readonly IAmazonS3 _s3Client;
        private readonly IMemoryCache _cache;
        public SongController(AppContext context, RTNominationsForum rtNominationsForum, IConfiguration configuration, IMemoryCache cache)
        {
            _s3Client = configuration.GetS3Client();
            _context = context;      
            _rtNominationsForum = rtNominationsForum;
            _cache = cache;
        }

        [HttpGet("~/refreshstatus")]
        public async Task<string> refreshstatus()
        {
            return (await _context.Songs.Where(s => s.Refreshed).CountAsync()) + " of " +  (await _context.Songs.CountAsync());
        }

        [HttpGet("~/map/hash/{hash}")]
        public async Task<ActionResult<Song>> GetHash(string hash)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }
            Song? song = await SongControllerHelper.GetOrAddSong(_context, hash);
            if (song is null)
            {
                return NotFound();
            }
            return song;
        }

        [HttpGet("~/map/modinterface/{hash}")]
        public async Task<ActionResult<IEnumerable<DiffModResponse>>> GetModSongInfos(string hash)
        {

            var resFromLB = await _context.Leaderboards
                .Where(lb => lb.Song.Hash == hash)
                .Include(lb => lb.Difficulty)
                    .ThenInclude(diff => diff.ModifierValues)
                .Include(lb => lb.Difficulty)
                    .ThenInclude(diff => diff.ModifiersRating)
                .Include(lb => lb.Clan)
                .Select(lb => new { 
                    DiffModResponse = new DiffModResponse {
                        DifficultyName = lb.Difficulty.DifficultyName,
                        ModeName = lb.Difficulty.ModeName,
                        Stars = lb.Difficulty.Stars,
                        Status = lb.Difficulty.Status,
                        Type = lb.Difficulty.Type,
                        Votes = lb.Scores
                            .Where(score => score.RankVoting != null)
                            .Select(score => score.RankVoting!.Rankability)
                            .ToArray(),
                        ModifierValues = lb.Difficulty.ModifierValues,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        PassRating = lb.Difficulty.PassRating,
                        AccRating = lb.Difficulty.AccRating,
                        TechRating = lb.Difficulty.TechRating,
                        ClanStatus = new ClanRankingStatus {
                            Clan = lb.Clan != null ? new ClanResponse {
                                Id = lb.Clan.Id,
                                Color = lb.Clan.Color,
                                Tag = lb.Clan.Tag,
                                Name = lb.Clan.Name,
                            } : null,
                            ClanRankingContested = lb.ClanRankingContested,
                            Applicable = lb.Difficulty.Status == DifficultyStatus.ranked
                        }
                    }, 
                    SongDiffs = lb.Song.Difficulties 
                })
                .ToArrayAsync();

            ICollection<DifficultyDescription> difficulties;
            if(resFromLB.Length == 0)
            {
                // We couldnt find any Leaderboard with that hash. Therefor we need to check if we can atleast get the song
                Song? song = await SongControllerHelper.GetOrAddSong(_context, hash);
                // Otherwise the song does not exist
                if (song is null)
                {
                    return NotFound();
                }
                difficulties = song.Difficulties;
            }
            else
            {
                // Else we can use the found difficulties of the song
                difficulties = resFromLB[0].SongDiffs;
            }

            // Now we need to return the LB DiffModResponses. If there are diffs in the song, that have no leaderboard we return the diffs without votes, as no leaderboard = no scores = no votes
            var result = difficulties.Select(diff =>
                resFromLB.FirstOrDefault(element => element.DiffModResponse.DifficultyName == diff.DifficultyName && element.DiffModResponse.ModeName == diff.ModeName)?.DiffModResponse
                ?? ResponseUtils.DiffModResponseFromDiffAndVotes(diff, Array.Empty<float>())).ToArray();
            
            string? currentID = HttpContext.CurrentUserID(_context);
            bool showRatings = currentID != null ? (await _context.Players.Include(p => p.ProfileSettings).Where(p => p.Id == currentID).Select(p => p.ProfileSettings).FirstOrDefaultAsync())?.ShowAllRatings ?? false : false;
            foreach (var item in result) {
                if (!showRatings && !item.Status.WithRating()) {
                    item.HideRatings();
                }
            }

            return result;
        }

        const string allStarsZipFile = "allStarsZipFile";

        [HttpGet("~/map/allstars")]
        public async Task<ActionResult> GetAllStars()
        {
            if(!_cache.TryGetValue(allStarsZipFile, out byte[]? zipFile) || zipFile is null)
            {
                var songs = (await _context.Songs
                        .Select(s => 
                            s.Difficulties.Where(d => d.Stars > 0).Select(d => 
                                new HashDiffStarTuple(
                                    s.Hash, 
                                    d.DifficultyName + d.ModeName, 
                                    (float)(d.Stars != null ? d.Stars : 0))).ToArray())
                        .ToArrayAsync())
                        .SelectMany(x => x)
                        .Distinct()
                        .Where(d => d.Stars > 0)
                        .ToArray();

                // Serialize Hashes, Diffs and Stars
                using MemoryStream originalms = new();
                Serializer.Serialize(originalms, songs);

                // Zip them in a gzip file
                originalms.Position = 0;
                using MemoryStream compressedms = new();
                using (var compressor = new GZipStream(compressedms, CompressionLevel.SmallestSize, true))
                {
                    await originalms.CopyToAsync(compressor);
                }

                // And cache the Result until midnight
                zipFile = compressedms.ToArray();
                _cache.Set(allStarsZipFile, zipFile, DateTimeOffset.UtcNow.AddDays(1).Date);
            }
            return File(zipFile, "application/gzip", "Testfile");
        }
        
        [HttpGet("~/map/migratenominations")]
        public async Task<ActionResult> MigrateNominations([FromQuery] string baseSongId, [FromQuery] string oldSongId, [FromQuery] string newSongId)
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Song? baseSong = await _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync(i => i.Id == baseSongId);
            Song? oldSong = await _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync(i => i.Id == oldSongId);
            Song? newSong = await _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync(i => i.Id == newSongId);

            if (baseSong == null || oldSong == null || newSong == null) return NotFound();

            foreach (var item in oldSong.Difficulties)
            {
                await SongControllerHelper.MigrateLeaderboards(_context, newSong, oldSong, baseSong, item);
                item.Status = DifficultyStatus.outdated;
                item.Stars = 0;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("~/map/uploadOst")]
        public async Task<ActionResult> UploadOstMap()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var parse = new Parse(); 
            var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            var map = parse.TryLoadZip(ms)?.FirstOrDefault();
            if (map == null) return BadRequest();
            var info = map.Info;

            var song = new Song
            {
                Id = info._songFilename.Replace(".ogg", ""),
                Hash = info._songFilename.Replace(".ogg", ""),
                Name = info._songName + " [OST I]",
                SubName = info._songSubName,
                Author = info._songAuthorName,
                Mapper = "Beat Games",
                Bpm = info._beatsPerMinute,
                Duration = map.SongLength,
                UploadTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            };

            var diffs = new List<DifficultyDescription>();

            foreach (var set in map.Difficulties)
            {

                var diff = info
                    ._difficultyBeatmapSets
                    .First(s => s._beatmapCharacteristicName == set.Characteristic)
                    ._difficultyBeatmaps
                    .First(b => b._difficulty == set.Difficulty);

                var newDD = new DifficultyDescription
                {
                    Value = Song.DiffForDiffName(set.Difficulty),
                    Mode = Song.ModeForModeName(set.Characteristic),
                    DifficultyName = set.Difficulty,
                    ModeName = set.Characteristic,
                    Status = DifficultyStatus.OST,

                    Njs = diff._noteJumpMovementSpeed,
                    Nps = set.Data.Notes.Count() / (float)map.SongLength,
                    Notes = set.Data.Notes.Count(),
                    Bombs = set.Data.Bombs.Count(),
                    Walls = set.Data.Walls.Count(),
                    MaxScore = set.MaxScore(),
                    Duration = map.SongLength,
                };

                if (set.Data.Chains.Count > 0 || set.Data.Arcs.Count > 0)
                {
                    newDD.Requirements |= Requirements.V3;
                }

                diffs.Add(newDD);
            }
            song.Difficulties = diffs;

            ms.Position = 0;
            var archive = new ZipArchive(ms);

            if (info._coverImageFilename != null)
            {
                var coverFile = archive.Entries.FirstOrDefault(e => e.Name.ToLower() == info._coverImageFilename.ToLower());
                if (coverFile != null)
                {
                    using (var coverStream = coverFile.Open())
                    {
                        using (var coverMs = new MemoryStream(5))
                        {
                            await coverStream.CopyToAsync(coverMs);
                            var fileName = ($"songcover-{song.Id}-" + info._coverImageFilename).Replace(" ", "").Replace("(", "").Replace(")", "");

                            song.FullCoverImage = await _s3Client.UploadAsset(fileName, coverMs);
                            song.CoverImage = song.FullCoverImage;
                        }
                    }
                }
            }

            ms.Position = 0;
            song.DownloadUrl = await _s3Client.UploadSong(song.Hash + ".zip", ms);
            _context.Songs.Add(song);

            foreach (var diff in song.Difficulties) {
                await RatingUtils.UpdateFromExMachina(diff, song.DownloadUrl, null);
                var newLeaderboard = await SongControllerHelper.NewLeaderboard(_context, song, null, diff.DifficultyName, diff.ModeName);
            }

            song.Checked = true;

            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
