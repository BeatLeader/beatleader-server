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
        public async Task<ActionResult<Leaderboard>> Get(string id)
        {
            Leaderboard? leaderboard = await _context.Leaderboards.Include(lb => lb.Scores).ThenInclude(s => s.Player).Include(lb => lb.Difficulty).Include(lb => lb.Song).ThenInclude(s => s.Difficulties).FirstOrDefaultAsync(i => i.Id == id);

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

            leaderboard.Scores = leaderboard.Scores.OrderByDescending(s => s.ModifiedScore).ToArray();

            return leaderboard;
        }

        [HttpGet("~/leaderboards/")]
        public async Task<ActionResult<List<Leaderboard>>> GetAll() {
            return await _context.Leaderboards.Include(lb => lb.Scores).ThenInclude(s => s.Player).Include(lb => lb.Difficulty).Include(lb => lb.Song).ToListAsync();
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
