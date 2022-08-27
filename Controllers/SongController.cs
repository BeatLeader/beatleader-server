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
            if(song is null)
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

        [HttpGet("~/map/modinterface/{hash}")]
        public async Task<ActionResult<IEnumerable<DiffModResponse>>> GetModSongInfos(string hash)
        {
            var resFromLB = _readContext.Leaderboards
                .Where(lb => lb.Song.Hash == hash)
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
                    Song? existingSong = _context.Songs.Include(s => s.Difficulties).FirstOrDefault(i => i.Id == songId);
                    while (existingSong != null)
                    {
                        if (song.Hash.ToLower() == hash.ToLower())
                        {
                            foreach (var item in existingSong.Difficulties)
                            {
                                item.Status = DifficultyStatus.outdated;
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
                        }
                    }
                    _context.Songs.Add(song);
                    await _context.SaveChangesAsync();
                }
            }

            return song;
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
