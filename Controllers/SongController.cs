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
            if (hash.Length < 40) {
                return BadRequest("Hash is to short");
            } else {
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
        public async Task MigrateLeaderboards(Song newSong, Song oldSong, Song? baseSong, DifficultyDescription diff)
        {
            var newLeaderboard = await NewLeaderboard(newSong, baseSong, diff.DifficultyName, diff.ModeName);
            if (newLeaderboard != null && diff.Status != DifficultyStatus.ranked && diff.Status != DifficultyStatus.outdated) {
                newLeaderboard.Difficulty.Status = diff.Status;
                newLeaderboard.Difficulty.Stars = diff.Stars;
                newLeaderboard.Difficulty.Type = diff.Type;
                newLeaderboard.Difficulty.NominatedTime = diff.NominatedTime;
                newLeaderboard.Difficulty.ModifierValues = diff.ModifierValues;
            }

            var oldLeaderboardId = $"{oldSong.Id}{diff.Value}{diff.Mode}";
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
                song = await SongUtils.GetSongFromBeatSaver("https://api.beatsaver.com/maps/hash/" + hash);

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
                    _context.Songs.Add(song);
                    await _context.SaveChangesAsync();

                    
                    foreach (var oldSong in songsToMigrate)
                    {
                        foreach (var item in oldSong.Difficulties)
                        {
                            await MigrateLeaderboards(song, oldSong, baseSong, item);
                            item.Status = DifficultyStatus.outdated;
                            item.Stars = 0;
                        }
                    }
                    await _context.SaveChangesAsync();
                }
            }

            return song;
        }

        [NonAction]
        public async Task<Leaderboard?> NewLeaderboard(Song song, Song? baseSong, string diff, string mode)
        {
            var leaderboard = new Leaderboard();
            leaderboard.SongId = song.Id;
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

            leaderboard.Difficulty = difficulty;
            leaderboard.Scores = new List<Score>();
            leaderboard.Id = $"{song.Id}{difficulty.Value}{difficulty.Mode}";
            leaderboard.Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

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
                
                    group.Leaderboards.Add(leaderboard);

                    leaderboard.LeaderboardGroup = group;
                }
            }

            _context.Leaderboards.Add(leaderboard);
            await _context.SaveChangesAsync();

            return leaderboard;
        }

        [NonAction]
        private Song? GetSongWithDiffsFromHash(string hash)
        {
            return _context.Songs.Where(el => el.Hash == hash).Include(song => song.Difficulties).ThenInclude(d => d.ModifierValues).FirstOrDefault();
        }
    }
}
