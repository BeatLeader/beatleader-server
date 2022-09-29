using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SongController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        public SongController(AppContext context, ReadAppContext readContext)
        {
            _context = context;
            _readContext = readContext;
        }

        [HttpGet("~/map/hash/{hash}")]
        public async Task<ActionResult<Song>> GetHash(string hash)
        {
            Song? song = await GetOrAddSong(hash);
            if (song is null)
            {
                return NotFound();
            }
            return song;
        }

        [HttpGet("~/map/refresh/{hash}")]
        public async Task<ActionResult<Song>> RefreshHash(string hash)
        {
            Song? song = GetSongWithDiffsFromHash(hash);

            if (song != null)
            {
                Song? updatedSong = await GetSongFromBeatSaver("https://api.beatsaver.com/maps/hash/" + hash);

                if (updatedSong != null)
                {
                    for (int i = 0; i < song.Difficulties.Count; i++)
                    {
                        song.Difficulties.ElementAt(i).MaxScore = updatedSong.Difficulties.ElementAt(i).MaxScore;
                    }
                    song.MapperId = updatedSong.MapperId;
                    _context.Songs.Update(song);
                    _context.SaveChanges();
                }
            }

            return song;
        }

        public class ApprovalInfo {
            public string Mapper { get; set; }
            public int Count { get; set; }
        }

        [HttpGet("~/notApprovedMappersRating")]
        public async Task<IEnumerable<ApprovalInfo>> RefreshdwHash()
        {
            var lbs = _context
                .Leaderboards
                .Include(lb => lb.Song)
                .Include(lb => lb.Difficulty)
                    .Where(lb => lb.Difficulty.Status == DifficultyStatus.ranked && lb.Difficulty.RankedTime == 0)
                    .ToList();
            return lbs
                .GroupBy(lb => lb.Song.MapperId)
                    .Where(gp => gp.All(a => a.Difficulty.MapperApproval == MapperApproval.unknown))
                .Select(gp => new ApprovalInfo {
                    Count = gp.Count(),
                    Mapper = gp.First().Song.Mapper
                }).OrderByDescending(s => s.Count);
        }


        [HttpGet("~/map/modinterface/{hash}")]
        public async Task<ActionResult<IEnumerable<DiffModResponse>>> GetModSongInfos(string hash)
        {
            var resFromLB = _readContext.Leaderboards
                .Where(lb => lb.Song.Hash == hash)
                .Include(lb => lb.Difficulty)
                .ThenInclude(diff => diff.ModifierValues)
                .Select(lb => new { 
                    DiffModResponse = ResponseUtils.DiffModResponseFromDiffAndVotes(lb.Difficulty, lb.Scores.Where(score => score.RankVoting != null).Select(score => score.RankVoting!.Rankability).ToArray()), 
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
            return difficulties.Select(diff =>
                resFromLB.FirstOrDefault(element => element.DiffModResponse.DifficultyName == diff.DifficultyName && element.DiffModResponse.ModeName == diff.ModeName)?.DiffModResponse
                ?? ResponseUtils.DiffModResponseFromDiffAndVotes(diff, Array.Empty<float>())).ToArray();
        }

        [HttpGet("~/maps/forapprove")]
        public async Task<ActionResult<IEnumerable<Song>>> MapsForApprove()
        {
            string userId = HttpContext.CurrentUserID(_readContext);
            var player = _readContext.Players.Find(userId);
            if (player == null)
            {
                return NotFound();
            }

            return _context.Songs
                .Where(s => s.MapperId == player.MapperId && s.Difficulties
                    .FirstOrDefault(d => d.Status == DifficultyStatus.ranked && d.QualifiedTime == 0) != null)
                .Include(s => s.Difficulties.Where(d => d.Status == DifficultyStatus.ranked)).ToList();
        }

        public class TotalPPResult {
            public float TotalPP { get; set; }
            public int PlayerCount { get; set; }
        }

        [HttpGet("~/map/totalpp")]
        public async Task<ActionResult<TotalPPResult>> Totalpp([FromQuery] string songId)
        {
            string userId = HttpContext.CurrentUserID(_readContext);
            var player = _readContext.Players.Find(userId);
            if (player == null)
            {
                return NotFound();
            }

            var leaderboards = _context.Leaderboards.Where(lb => lb.SongId == songId && lb.Song.MapperId == player.MapperId).Select(lb => lb.Scores.Select(s => new { pp = s.Pp * s.Weight, player = s.PlayerId })).ToList();
            if (leaderboards.Count() == 0) {

                return NotFound();
            }
            return new TotalPPResult {
                TotalPP = leaderboards.Sum(lb => lb.Sum(s => s.pp)),
                PlayerCount = leaderboards.Sum(sc => sc.DistinctBy(s => s.player).Count())
            };
        }

        [HttpPost("~/maps/approve")]
        public async Task<ActionResult> ApproveMap([FromQuery] string songId, [FromQuery] MapperApproval approval)
        {
            string userId = HttpContext.CurrentUserID(_readContext);
            var player = _readContext.Players.Find(userId);
            if (player == null)
            {
                return NotFound();
            }

            var map = _context.Songs
                .Where(s => s.Id == songId && s.MapperId == player.MapperId && s.Difficulties
                    .FirstOrDefault(d => d.Status == DifficultyStatus.ranked && d.QualifiedTime == 0) != null)
                .Include(s => s.Difficulties.Where(d => d.Status == DifficultyStatus.ranked)).FirstOrDefault();

            foreach (var item in map.Difficulties)
            {
                item.MapperApproval = approval;
            }
            _context.SaveChanges();

            return Ok();
        }

        [NonAction]
        public async Task MigrateQualification(Song newSong, Song oldSong, DifficultyDescription diff)
        {
            var newLeaderboard = await NewLeaderboard(newSong, diff.DifficultyName, diff.ModeName);
            if (newLeaderboard != null) {
                newLeaderboard.Difficulty.Status = DifficultyStatus.nominated;
                newLeaderboard.Difficulty.Stars = diff.Stars;
                newLeaderboard.Difficulty.Type = diff.Type;
                newLeaderboard.Difficulty.NominatedTime = diff.NominatedTime;
                newLeaderboard.Difficulty.ModifierValues = diff.ModifierValues;
            }

            var oldLeaderboardId = oldSong.Id + diff.Value.ToString() + diff.Mode.ToString();
            var oldLeaderboard = await _context.Leaderboards.Where(lb => lb.Id == oldLeaderboardId).Include(lb => lb.Qualification).FirstOrDefaultAsync();

            if (oldLeaderboard != null) {

                newLeaderboard.Qualification = oldLeaderboard.Qualification;
                oldLeaderboard.Qualification = null;
            }
        }

        [NonAction]
        public async Task<Song?> GetOrAddSong(string hash)
        {
            Song? song = GetSongWithDiffsFromHash(hash);

            if (song == null)
            {
                song = await GetSongFromBeatSaver("https://api.beatsaver.com/maps/hash/" + hash);

                if (song == null)
                {
                    return null;
                }
                else
                {
                    string songId = song.Id;
                    Song? existingSong = _context.Songs.Include(s => s.Difficulties).ThenInclude(d => d.ModifierValues).FirstOrDefault(i => i.Id == songId);
                    while (existingSong != null)
                    {
                        if (song.Hash.ToLower() == hash.ToLower())
                        {
                            foreach (var item in existingSong.Difficulties)
                            {
                                if (item.Status == DifficultyStatus.nominated) {
                                    await MigrateQualification(song, existingSong, item);
                                }
                                item.Status = DifficultyStatus.outdated;
                                item.Stars = 0;
                            }
                        }
                        songId += "x";
                        existingSong = _context.Songs.Include(s => s.Difficulties).FirstOrDefault(i => i.Id == songId);
                    }
                    song.Id = songId;
                    song.Hash = hash;
                    if (song.Hash.ToLower() != hash.ToLower())
                    {
                        foreach (var item in song.Difficulties)
                        {
                            item.Status = DifficultyStatus.outdated;
                            item.Stars = 0;
                        }
                    }
                    _context.Songs.Add(song);
                    await _context.SaveChangesAsync();
                }
            }

            return song;
        }

        [NonAction]
        public async Task<Leaderboard?> NewLeaderboard(Song song, string diff, string mode)
        {
            var leaderboard = new Leaderboard();
            leaderboard.SongId = song.Id;
            IEnumerable<DifficultyDescription> difficulties = song.Difficulties.Where(el => el.DifficultyName == diff);
            DifficultyDescription? difficulty = difficulties.FirstOrDefault(x => x.ModeName == mode);
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

            leaderboard.Difficulty = difficulty;
            leaderboard.Scores = new List<Score>();
            leaderboard.Id = song.Id + difficulty.Value.ToString() + difficulty.Mode.ToString();

            _context.Leaderboards.Add(leaderboard);
            await _context.SaveChangesAsync();

            return leaderboard;
        }

        [NonAction]
        private Song? GetSongWithDiffsFromHash(string hash)
        {
            return _context.Songs.Where(el => el.Hash == hash).Include(song => song.Difficulties).FirstOrDefault();
        }

        [NonAction]
        public Task<Song?> GetSongFromBeatSaver(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse? response = null;
            Song? song = null;
            var stream = 
            Task<(WebResponse?, Song?)>.Factory.FromAsync(request.BeginGetResponse, result =>
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

            return stream.ContinueWith(t => ReadSongFromResponse(t.Result));
        }

        [NonAction]
        private Song? ReadSongFromResponse((WebResponse?, Song?) response)
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
                    Song result = new Song();
                    result.Author = info.metadata.songAuthorName;
                    result.Mapper = info.metadata.levelAuthorName;
                    result.Name = info.metadata.songName;
                    result.SubName = info.metadata.songSubName;
                    result.Duration = info.metadata.duration;
                    result.Bpm = info.metadata.bpm;
                    result.MapperId = (int)info.uploader.id;
                    result.UploadTime = (int)info.uploaded.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    if (ExpandantoObject.HasProperty(info, "tags")) {
                        result.Tags = string.Join(",", info.tags);
                    }

                    dynamic currentVersion = info.versions[0];
                    result.CoverImage = currentVersion.coverURL;
                    result.DownloadUrl = currentVersion.downloadURL;
                    result.Hash = currentVersion.hash;
                    if (ExpandantoObject.HasProperty(info, "id"))
                    {
                        result.Id = info.id;
                    } else
                    {
                        result.Id = currentVersion.key;
                    }

                    List<DifficultyDescription> difficulties = new List<DifficultyDescription>();
                    dynamic diffs = currentVersion.diffs;
                    foreach (dynamic diff in diffs) {
                        DifficultyDescription difficulty = new DifficultyDescription();
                        difficulty.ModeName = diff.characteristic;
                        difficulty.Mode = SongUtils.ModeForModeName(diff.characteristic);
                        difficulty.DifficultyName = diff.difficulty;
                        difficulty.Value = SongUtils.DiffForDiffName(diff.difficulty);
                        
                        difficulty.Njs = (float)diff.njs;
                        difficulty.Notes = (int)diff.notes;
                        difficulty.Bombs = (int)diff.bombs;
                        difficulty.Nps = (float)diff.nps;
                        difficulty.Walls = (int)diff.obstacles;
                        difficulty.MaxScore = (int)diff.maxScore;

                        difficulties.Add(difficulty);
                    }
                    result.Difficulties = difficulties;

                    return result;
                }
            } else {
                return response.Item2;
            }   
        }
    }
}
