using System;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    }
}

