using System;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    public class ScoreController : Controller
    {
        private readonly AppContext _context;

        public ScoreController(AppContext context)
        {
            _context = context;
        }

        [HttpDelete("~/score/{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteScore(int id)
        {
            string currentId = HttpContext.CurrentUserID();
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var score = await _context.Scores.FindAsync(id);
            if (score == null)
            {
                return NotFound();
            }

            _context.Scores.Remove(score);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("~/scores/refresh")]
        [Authorize]
        public async Task<ActionResult> RefreshScores()
        {
            var allScores = _context.Scores.Where(s => s.Pp != 0).Include(s => s.Leaderboard).ThenInclude(l => l.Difficulty).ToList();
            foreach (Score s in allScores)
            {
                s.Pp = (float)s.ModifiedScore / ((float)s.BaseScore / s.Accuracy) * (float)s.Leaderboard.Difficulty.Stars * 44;
                _context.Scores.Update(s);
            }
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}

