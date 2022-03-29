using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
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
                leaderboard = await _context.Leaderboards.Include(lb => lb.Scores.OrderByDescending(s => s.ModifiedScore).Skip((page - 1) * count).Take(count)).ThenInclude(s => s.Player).Include(lb => lb.Difficulty).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).FirstOrDefaultAsync(i => i.Id == id);

            } else
            {
                leaderboard = await _context
                    .Leaderboards
                    .Include(lb => lb.Scores)
                    .ThenInclude(s => s.Player)
                    .Include(lb => lb.Scores
                        .Where(s => countries.ToLower().Contains(s.Player.Country.ToLower()))
                        .OrderByDescending(s => s.ModifiedScore)
                        .Skip((page - 1) * count)
                        .Take(count))
                    .Include(lb => lb.Difficulty)
                    .Include(lb => lb.Song)
                    .ThenInclude(s => s.Difficulties)
                    .FirstOrDefaultAsync(i => i.Id == id);
            }

            if (leaderboard == null) {
                Song? song = (await _songController.Get(id.Substring(0, id.Length - 2))).Value;
                if (song == null) {
                    return NotFound();
                }

                leaderboard = new Leaderboard();
                leaderboard.Song = song;
                Int32 iddd = Int32.Parse(id.Substring(id.Length - 2, 1));
                IEnumerable<DifficultyDescription> difficulties = song.Difficulties.Where(x => x.Mode == Int32.Parse(id.Substring(id.Length - 1, 1)));
                leaderboard.Difficulty = difficulties.First(el => el.Value == Int32.Parse(id.Substring(id.Length - 2, 1)));
                if (leaderboard.Difficulty == null) {
                    return NotFound();
                }
                leaderboard.Scores = new List<Score>();
                leaderboard.Id = id;

                _context.Leaderboards.Add(leaderboard);
                await _context.SaveChangesAsync();
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
    }
}
