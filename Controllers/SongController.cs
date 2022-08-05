using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SongController : Controller
    {
        private readonly AppContext _context;

        public SongController(AppContext context)
        {
            _context = context;
        }

        [HttpGet("~/map/hash/{hash}")]
        public async Task<ActionResult<Song>> GetHash(string hash)
        {
            Song? song = _context.Songs.Where(el => el.Hash == hash).Include(song => song.Difficulties).FirstOrDefault();

            if (song == null) {
                song = await GetSongFromBeatSaver("https://api.beatsaver.com/maps/hash/" + hash);

                if (song == null) {
                    return NotFound();
                } else {
                    string songId = song.Id;
                    while (await _context.Songs.FirstOrDefaultAsync(i => i.Id == songId) != null)
                    {
                        songId += "x";
                    }
                    song.Id = songId;
                    if (song.Hash != hash) {
                        foreach (var diff in song.Difficulties)
                        {
                            diff.Ranked = false;
                        }
                    }
                    song.Hash = hash;
                    _context.Songs.Add(song);
                    await _context.SaveChangesAsync();
                }
            }

            return song;
        }

        [HttpGet("~/map/refresh/{hash}")]
        public async Task<ActionResult<Song>> RefreshHash(string hash)
        {
            Song? song = _context.Songs.Where(el => el.Hash == hash).Include(song => song.Difficulties).FirstOrDefault();

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

        [HttpGet("~/maps")]
        public async Task<ActionResult<ICollection<Song>>> GetAll([FromQuery] bool ranked = false, [FromQuery] int page = 0, [FromQuery] int count = 100)
        {
            return _context.Songs.Where(s => s.Difficulties.First(d => d.Ranked == ranked) != null).Include(s => s.Difficulties).ToList();
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
                        if (ExpandantoObject.HasProperty(diff, "stars")) {
                            difficulty.Stars = (float)diff.stars;
                            difficulty.Ranked = true;
                        }
                        
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
