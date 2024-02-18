using Amazon.S3;
using beatleader_parser;
using BeatLeader_Server.Bot;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Models.Models;
using ProtoBuf;
using System.IO.Compression;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SongController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;
        private readonly RTNominationsForum _rtNominationsForum;
        private readonly IAmazonS3 _s3Client;
        private readonly IMemoryCache _cache;
        public SongController(AppContext context, ReadAppContext readContext, RTNominationsForum rtNominationsForum, IConfiguration configuration, IMemoryCache cache)
        {
            _s3Client = configuration.GetS3Client();
            _context = context;      
            _readContext = readContext;
            _rtNominationsForum = rtNominationsForum;
            _cache = cache;
        }

        [HttpGet("~/refreshstatus")]
        public string refreshstatus()
        {
            return _context.Songs.Where(s => s.Refreshed).Count() + " of " +  _context.Songs.Count();
        }

        [HttpGet("~/map/hash/{hash}")]
        public async Task<ActionResult<Song>> GetHash(string hash)
        {
            if (hash.Length >= 40) {
                hash = hash.Substring(0, 40);
            }
            Song? song = await GetOrAddSong(hash);
            if (song is null)
            {
                return NotFound();
            }
            return song;
        }

        [HttpGet("~/map/modinterface/{hash}")]
        public async Task<ActionResult<IEnumerable<DiffModResponse>>> GetModSongInfos(string hash)
        {

            var resFromLB = _readContext.Leaderboards
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
                .ToArray();

            ICollection<DifficultyDescription> difficulties;
            if(resFromLB.Length == 0)
            {
                // We couldnt find any Leaderboard with that hash. Therefor we need to check if we can atleast get the song
                Song? song = await GetOrAddSong(hash);
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
            bool showRatings = currentID != null ? _context.Players.Include(p => p.ProfileSettings).Where(p => p.Id == currentID).Select(p => p.ProfileSettings).FirstOrDefault()?.ShowAllRatings ?? false : false;
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
                // Serialize Hashes, Diffs and Stars
                using MemoryStream originalms = new();
                Serializer.Serialize(
                    originalms,
                    _readContext.Songs.Include(s => s.Difficulties)
                        .SelectMany(s => s.Difficulties, (s, diff) => new { s.Hash, diff })
                        .Where(a => (a.diff.Stars ?? 0) != 0)
                        .Select(d => new HashDiffStarTuple(d.Hash, d.diff.DifficultyName + d.diff.ModeName, d.diff.Stars ?? 0))
                        .ToArray());

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

        [NonAction]
        public async Task MigrateLeaderboards(Song newSong, Song oldSong, Song? baseSong, DifficultyDescription diff)
        {
            var newLeaderboard = await NewLeaderboard(newSong, baseSong, diff.DifficultyName, diff.ModeName);
            if (newLeaderboard != null && diff.Status != DifficultyStatus.ranked && diff.Status != DifficultyStatus.outdated) {
                await RatingUtils.UpdateFromExMachina(newLeaderboard.Difficulty, newSong, null);
                newLeaderboard.Difficulty.Status = diff.Status;
                newLeaderboard.Difficulty.Type = diff.Type;
                newLeaderboard.Difficulty.NominatedTime = diff.NominatedTime;
                newLeaderboard.Difficulty.QualifiedTime = diff.QualifiedTime;
                newLeaderboard.Difficulty.ModifierValues = diff.ModifierValues;
            }

            var oldLeaderboardId = $"{oldSong.Id}{diff.Value}{diff.Mode}";
            var oldLeaderboard = await _context.Leaderboards.Where(lb => lb.Id == oldLeaderboardId).Include(lb => lb.Qualification).FirstOrDefaultAsync();

            if (oldLeaderboard?.Qualification != null) {
                newLeaderboard.Qualification = oldLeaderboard.Qualification;
                newLeaderboard.NegativeVotes = oldLeaderboard.NegativeVotes;
                newLeaderboard.PositiveVotes = oldLeaderboard.PositiveVotes;
                if (oldLeaderboard.Qualification.DiscordRTChannelId.Length > 0 && diff.Status.WithRating()) {
                    await _rtNominationsForum.NominationReuploaded(_context, oldLeaderboard.Qualification, oldLeaderboardId);
                }
                oldLeaderboard.Qualification = null;
            }
        }

        [NonAction]
        public async Task<Song?> GetOrAddSong(string hash)
        {
            Song? song = GetSongWithDiffsFromHash(hash);

            if (song == null)
            {
                song = await SongUtils.GetSongFromBeatSaver(hash);

                if (song == null)
                {
                    return null;
                }
                else
                {
                    string songId = song.Id;
                    Song? existingSong = _context
                        .Songs
                        .Include(s => s.Difficulties)
                        .ThenInclude(d => d.ModifierValues)
                        .FirstOrDefault(i => i.Id == songId);
                    Song? baseSong = existingSong;

                    List<Song> songsToMigrate = new List<Song>();
                    while (existingSong != null)
                    {
                        if (song.Hash.ToLower() == hash.ToLower())
                        {
                            songsToMigrate.Add(existingSong);
                        }
                        songId += "x";
                        existingSong = _context.Songs.Include(s => s.Difficulties).FirstOrDefault(i => i.Id == songId);
                    }

                    song.Id = songId;
                    song.Hash = hash;
                    try {
                        _context.Songs.Add(song);
                        await _context.SaveChangesAsync();
                        SongSearchService.AddNewSong(song);
                    } catch {
                    }
                    
                    foreach (var oldSong in songsToMigrate)
                    {
                        foreach (var item in oldSong.Difficulties)
                        {
                            await MigrateLeaderboards(song, oldSong, baseSong, item);
                            item.Status = DifficultyStatus.outdated;
                            item.Stars = 0;
                        }
                    }
                    try {
                        await _context.SaveChangesAsync();
                    } catch {
                    }
                }
            }

            return song;
        }

        [NonAction]
        public async Task<Leaderboard?> NewLeaderboard(Song song, Song? baseSong, string diff, string mode)
        {
            IEnumerable<DifficultyDescription> difficulties = song.Difficulties.Where(el => el.DifficultyName.ToLower() == diff.ToLower());
            DifficultyDescription? difficulty = difficulties.FirstOrDefault(x => x.ModeName.ToLower() == mode.ToLower());
   
            if (difficulty == null)
            {
                difficulty = difficulties.FirstOrDefault(x => x.ModeName == "Standard");
                if (difficulty == null)
                {
                    return null;
                }
                else
                {
                    CustomMode? customMode = _context.CustomModes.FirstOrDefault(m => m.Name == mode);
                    if (customMode == null)
                    {
                        customMode = new CustomMode
                        {
                            Name = mode
                        };
                        _context.CustomModes.Add(customMode);
                        await _context.SaveChangesAsync();
                    }

                    difficulty = new DifficultyDescription
                    {
                        Value = difficulty.Value,
                        Mode = customMode.Id + 10,
                        DifficultyName = difficulty.DifficultyName,
                        ModeName = mode,

                        Njs = difficulty.Njs,
                        Nps = difficulty.Nps,
                        Notes = difficulty.Notes,
                        Bombs = difficulty.Bombs,
                        Walls = difficulty.Walls,
                    };
                    song.Difficulties.Add(difficulty);
                    await _context.SaveChangesAsync();
                }
            }

            string newLeaderboardId = $"{song.Id}{difficulty.Value}{difficulty.Mode}";
            var leaderboard = _context.Leaderboards.Include(lb => lb.Difficulty).Where(l => l.Id == newLeaderboardId).FirstOrDefault();

            if (leaderboard == null) {
                leaderboard = new Leaderboard();
                leaderboard.SongId = song.Id;

                leaderboard.Difficulty = difficulty;
                leaderboard.Scores = new List<Score>();
                leaderboard.Id = newLeaderboardId;
                leaderboard.Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

                _context.Leaderboards.Add(leaderboard);
            }

            if (baseSong != null) {
                var baseId = $"{baseSong.Id}{difficulty.Value}{difficulty.Mode}";
                var baseLeaderboard = _context.Leaderboards
                    .Include(lb => lb.LeaderboardGroup)
                    .ThenInclude(lbg => lbg.Leaderboards)
                    .FirstOrDefault(lb => lb.Id == baseId);

                if (baseLeaderboard != null) {
                    var group = baseLeaderboard.LeaderboardGroup ?? new LeaderboardGroup {
                        Leaderboards = new List<Leaderboard>()
                    };

                    if (baseLeaderboard.LeaderboardGroup == null) {
                        group.Leaderboards.Add(baseLeaderboard);
                        baseLeaderboard.LeaderboardGroup = group;
                    }

                    if (group.Leaderboards.FirstOrDefault(lb => lb.Id == leaderboard.Id) == null) {
                        group.Leaderboards.Add(leaderboard);

                        leaderboard.LeaderboardGroup = group;
                    }
                }
            }

            await _context.SaveChangesAsync();

            return leaderboard;
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

            Song? baseSong = _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefault(i => i.Id == baseSongId);
            Song? oldSong = _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefault(i => i.Id == oldSongId);
            Song? newSong = _context
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefault(i => i.Id == newSongId);

            if (baseSong == null || oldSong == null || newSong == null) return NotFound();

            foreach (var item in oldSong.Difficulties)
            {
                await MigrateLeaderboards(newSong, oldSong, baseSong, item);
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
                var newLeaderboard = await NewLeaderboard(song, null, diff.DifficultyName, diff.ModeName);
            }

            song.Checked = true;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [NonAction]
        private Song? GetSongWithDiffsFromHash(string hash)
        {
            return _context
                .Songs
                .Where(el => el.Hash == hash)
                .Include(song => song.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(song => song.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefault();
        }
    }
}
