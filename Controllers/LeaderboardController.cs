using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class LeaderboardController : Controller
    {
        private readonly AppContext _context;
        private readonly SongController _songController;

        public LeaderboardController(AppContext context, SongController songController)
        {
            _context = context;
            _songController = songController;
        }

        [HttpGet("~/leaderboard/id/{id}")]
        public async Task<ActionResult<Leaderboard>> Get(string id, [FromQuery] int page = 1, [FromQuery] int count = 10, [FromQuery] string? countries = null)
        {
            Leaderboard? leaderboard;

            if (countries == null)
            {
                leaderboard = await _context
                    .Leaderboards
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Scores
                        .OrderByDescending(s => s.ModifiedScore)
                        .Skip((page - 1) * count)
                        .Take(count))
                    .ThenInclude(s => s.Player)
                    .ThenInclude(s => s.Clans)
                    .Include(lb => lb.Difficulty)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties)
                    .FirstOrDefaultAsync();
            } else
            {
                leaderboard = await _context
                    .Leaderboards
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Scores)
                    .ThenInclude(s => s.Player)
                    .ThenInclude(s => s.Clans)
                    .Include(lb => lb.Scores
                        .Where(s => countries.ToLower().Contains(s.Player.Country.ToLower()))
                        .OrderByDescending(s => s.ModifiedScore)
                        .Skip((page - 1) * count)
                        .Take(count))
                    .Include(lb => lb.Difficulty)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties)
                    .FirstOrDefaultAsync();
            }

            if (leaderboard == null) {
                return NotFound();
            }

            return leaderboard;
        }

        public class DifficultyResponse
        {
            public int Value { get; set; }
            public int Mode { get; set; }
            public bool Ranked { get; set; }
            public float? Stars { get; set; }
            public int Notes { get; set; }
            public int MaxScore { get; set; }
            public string SongId { get; set; }
        }

        public DifficultyResponse DifficultyToResponse(DifficultyDescription difficulty) {
            return new DifficultyResponse {
                Value = difficulty.Value,
                Mode = difficulty.Mode,
                Ranked = difficulty.Ranked,
                Stars = difficulty.Stars,
                Notes = difficulty.Notes,
                MaxScore = difficulty.MaxScore,
                SongId = difficulty.SongId,
            };
        }

        [NonAction]
        public async Task<ActionResult<DifficultyResponse>> GetDifficulty(string hash, string diff, string mode)
        {
            var songId = await _context.Songs.Select(s => new { Id = s.Id, Hash = s.Hash }).FirstOrDefaultAsync(s => s.Hash == hash);
            if (songId != null)
            {
                int modeValue = SongUtils.ModeForModeName(mode);
                if (modeValue == 0)
                {
                    var customMode = _context.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                    if (customMode != null)
                    {
                        modeValue = customMode.Id + 10;
                    }
                }
                if (modeValue != 0)
                {
                    string id = songId.Id;
                    int diffValue = SongUtils.DiffForDiffName(diff);
                    string leaderboardId = id + diffValue + modeValue;
                    Leaderboard? leaderboard1 = await _context.Leaderboards.Where(l => l.Id == leaderboardId).Include(l => l.Difficulty).FirstOrDefaultAsync();
                    if (leaderboard1 != null) {
                        return DifficultyToResponse(leaderboard1.Difficulty);
                    }
                }
            }

            Leaderboard? leaderboard = null;

            Song? song = null;
            if (songId != null) {
                song = await _context.Songs.Include(s => s.Difficulties).FirstOrDefaultAsync(s => s.Id == songId.Id);
            } else {
                song = (await _songController.GetHash(hash)).Value;
            }
            if (song == null)
            {
                return NotFound();
            }

            leaderboard = new Leaderboard();
            leaderboard.Song = song;
            IEnumerable<DifficultyDescription> difficulties = song.Difficulties.Where(el => el.DifficultyName == diff);
            DifficultyDescription? difficulty = difficulties.FirstOrDefault(x => x.ModeName == mode);
            if (difficulty == null)
            {
                difficulty = difficulties.FirstOrDefault(x => x.ModeName == "Standard");
                if (difficulty == null)
                {
                    return NotFound();
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
                        _context.SaveChanges();
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
                    _context.SaveChanges();
                }
            }

            leaderboard.Difficulty = difficulty;
            leaderboard.Scores = new List<Score>();
            leaderboard.Id = song.Id + difficulty.Value.ToString() + difficulty.Mode.ToString();

            _context.Leaderboards.Add(leaderboard);
            await _context.SaveChangesAsync();

            return DifficultyToResponse(leaderboard.Difficulty);
        }

        [NonAction]
        public async Task<ActionResult<Leaderboard>> GetByIdFull(string id, AppContext context) {
            Leaderboard? leaderboard;

            leaderboard = await context
                    .Leaderboards
                    .Where(lb => lb.Id == id)
                    .Include(lb => lb.Difficulty)
                    .Include(lb => lb.Scores)
                    .ThenInclude(score => score.Identification)
                    .FirstOrDefaultAsync();

            if (leaderboard == null)
            {
                return NotFound();
            }

            return leaderboard;
        }

        [HttpGet("~/leaderboards/")]
        public async Task<ActionResult<ResponseWithMetadata<Leaderboard>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] string sortBy = "stars",
            [FromQuery] string order = "desc",
            [FromQuery] string? search = null,
            [FromQuery] string? diff = null,
            [FromQuery] string? type = null,
            [FromQuery] float? stars_from = null,
            [FromQuery] float? stars_to = null) {

            var sequence = _context.Leaderboards.AsQueryable();
            switch (sortBy)
            {
                case "ranked":
                    sequence = sequence.Order(order, t => t.Difficulty.RankedTime);
                    break;
                case "qualified":
                    sequence = sequence.Order(order, t => t.Difficulty.QualifiedTime);
                    break;
                case "stars":
                    sequence = sequence.Include(lb => lb.Difficulty).Order(order, t => t.Difficulty.Stars);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Include(lb => lb.Song)
                    .Where(p => p.Song.Author.ToLower().Contains(lowSearch) ||
                                p.Song.Mapper.ToLower().Contains(lowSearch) ||
                                p.Song.Name.ToLower().Contains(lowSearch));
            }
            if (diff != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
            }
            if (type != null && type.Length != 0)
            {
                if (type == "ranked")
                {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Ranked);
                } else if (type == "qualified")
                {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Qualified);
                } else {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => !p.Difficulty.Ranked);
                }
            }
            if (stars_from != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Stars <= stars_to);
            }

            return new Models.ResponseWithMetadata<Leaderboard>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = sequence.Count()
                },
                Data = await sequence.Skip((page - 1) * count).Take(count).Include(lb => lb.Difficulty).Include(lb => lb.Song).ToListAsync()
            };
        }

        [HttpGet("~/leaderboards/refresh")]
        public ActionResult Refresh()
        {
            var leaderboards = _context.Leaderboards.Include(lb => lb.Scores).ToArray();
            foreach (var leaderboard in leaderboards)
            {
                var rankedScores = leaderboard.Scores.OrderByDescending(el => el.ModifiedScore).ToList();
                foreach ((int i, Score s) in rankedScores.Select((value, i) => (i, value)))
                {
                    s.Rank = i + 1;
                    _context.Scores.Update(s);
                }
                leaderboard.Scores = rankedScores;
                _context.Leaderboards.Update(leaderboard);
            }
            _context.SaveChanges();
            

            return Ok();
        }

        [Authorize]
        [HttpGet("~/map/star/{hash}/{difficulty}")]
        public async Task<ActionResult> SetStarValue(string hash, string difficulty, [FromQuery] float star)
        {
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

            string userId = accountLink != null ? accountLink.SteamID : currentID;
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Song? song = (await _songController.GetHash(hash)).Value;

            if (song != null)
            {
                DifficultyDescription? diff = song.Difficulties.FirstOrDefault(d => d.DifficultyName.ToLower() == difficulty.ToLower());
                diff.Ranked = true;
                diff.Stars = star;

                _context.Update(song);

                Leaderboard? leaderboard = (await Get(song.Id + SongUtils.DiffForDiffName(difficulty) + SongUtils.ModeForModeName("Standard"))).Value;
                if (leaderboard != null)
                {
                    leaderboard = await _context.Leaderboards.Include(l => l.Difficulty).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);
                    leaderboard.Difficulty.Stars = star;
                    leaderboard.Difficulty.Ranked = true;

                    _context.Update(leaderboard);
                }
            }

            _context.SaveChanges();

            return Ok();
        }

        [Authorize]
        [HttpGet("~/map/rdate/{hash}")]
        public async Task<ActionResult> SetStarValue(string hash, [FromQuery] int diff, [FromQuery] string date)
        {
            string currentID = HttpContext.CurrentUserID();
            long intId = Int64.Parse(currentID);
            AccountLink? accountLink = _context.AccountLinks.FirstOrDefault(el => el.OculusID == intId);

            string userId = accountLink != null ? accountLink.SteamID : currentID;
            var currentPlayer = await _context.Players.FindAsync(userId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            Song? song = (await _songController.GetHash(hash)).Value;
            DateTime dateTime = DateTime.Parse(date);

            string timestamp = Convert.ToString((int)dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);

            if (song != null)
            {
                DifficultyDescription? diffy = song.Difficulties.FirstOrDefault(d => d.DifficultyName == SongUtils.DiffNameForDiff(diff));
                diffy.Ranked = true;
                diffy.RankedTime = timestamp;

                _context.Update(song);

                Leaderboard? leaderboard = (await Get(song.Id + diff + SongUtils.ModeForModeName("Standard"))).Value;
                if (leaderboard != null)
                {
                    leaderboard = await _context.Leaderboards.Include(l => l.Difficulty).FirstOrDefaultAsync(i => i.Id == leaderboard.Id);

                    leaderboard.Difficulty.Ranked = true;
                    leaderboard.Difficulty.RankedTime = timestamp;

                    _context.Update(leaderboard);
                }
            }

            _context.SaveChanges();

            return Ok();
        }
    }
}
