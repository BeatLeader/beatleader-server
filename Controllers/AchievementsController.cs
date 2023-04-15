using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Lib.AspNetCore.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using static BeatLeader_Server.Services.SearchService;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class AchievementsController : Controller {

        private readonly AppContext _context;

        private readonly IConfiguration _configuration;
        IAmazonS3 _assetsS3Client;
        IWebHostEnvironment _environment;

        private readonly IServerTiming _serverTiming;

        public AchievementsController(
            AppContext context,
            IConfiguration configuration,
            IServerTiming serverTiming,
            IWebHostEnvironment env) {
            _context = context;

            _configuration = configuration;
            _serverTiming = serverTiming;
            _environment = env;
            _assetsS3Client = configuration.GetS3Client();
        }

        [HttpPut("~/achievement")]
        [Authorize]
        public async Task<ActionResult<AchievementDescription>> CreateAchievement(
            [FromQuery] string name,
            [FromQuery] string description,
            [FromQuery] string? link = null) {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            AchievementDescription achievementDescription = new AchievementDescription {
                Name = name,
                Description = description,
                Link = link
            };

            _context.AchievementDescriptions.Add(achievementDescription);
            _context.SaveChanges();

            return achievementDescription;
        }

        [HttpPut("~/achievement/{id}/level")]
        [Authorize]
        public async Task<ActionResult<AchievementLevel>> CreateAchievementLevel(
            int id,
            [FromQuery] string name,
            [FromQuery] string image,
            [FromQuery] string smallImage,
            [FromQuery] int level,
            [FromQuery] string? description = null,
            [FromQuery] string? detailedDescription = null,
            [FromQuery] string? color = null,
            [FromQuery] string? link = null) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            AchievementDescription? achievementDescription = _context
                .AchievementDescriptions
                .Include(a => a.Levels)
                .FirstOrDefault(a => a.Id == id);

            if (achievementDescription == null) {
                return NotFound();
            }

            if (achievementDescription.Levels == null) {
                achievementDescription.Levels = new List<AchievementLevel>();
            }

            AchievementLevel achievementLevel = new AchievementLevel {
                Name = name,
                Description = description,
                DetailedDescription = detailedDescription,
                Image = image,
                SmallImage = smallImage,
                Color = color,
                Level = level
            };
            achievementDescription.Levels.Add(achievementLevel);

            _context.SaveChanges();

            return achievementLevel;
        }

        [HttpGet("~/achievements")]
        public ActionResult<ICollection<AchievementDescription>> AllAchievements(string id) {
            return _context.AchievementDescriptions
                .Include(a => a.Levels)
                .ToList();
        }

        [HttpGet("~/player/{id}/achievements")]
        public ActionResult<ICollection<Achievement>> GetAchievements(string id) {
            return _context.Achievements
                .Where(a => a.PlayerId == id)
                .Include(a => a.Level)
                .Include(a => a.AchievementDescription)
                .ThenInclude(a => a.Levels)
                .ToList();
        }

        [HttpPost("~/player/{id}/achievement")]
        public async Task<ActionResult<Achievement>> GrantPlayerAchievement(
            string id,
            [FromQuery] int achievemntId,
            [FromQuery] int level) {

            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players.Find(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            Player? player = _context
                .Players
                .Include(p => p.Achievements)
                .FirstOrDefault(p => p.Id == id);
            if (player == null) {
                return NotFound("Player not found");
            }

            AchievementDescription? achievementDescription = _context
                .AchievementDescriptions
                .Include(a => a.Levels)
                .FirstOrDefault(a => a.Id == achievemntId);
            if (achievementDescription == null) {
                return NotFound("Achievement not found");
            }

            AchievementLevel? achievementLevel = achievementDescription
                .Levels
                .FirstOrDefault(l => l.Level == level);
            if (achievementLevel == null) {
                return NotFound("Achievement level not found");
            }

            if (player.Achievements == null) {
                player.Achievements = new List<Achievement>();
            }

            var achievement = player.Achievements.FirstOrDefault(a => a.Id == achievemntId);
            if (achievement == null) {
                achievement = new Achievement {
                    AchievementDescription = achievementDescription,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                };
                player.Achievements.Add(achievement);
            }

            achievement.Level = achievementLevel;
            await _context.SaveChangesAsync();

            return achievement;
        }

        [HttpPost("~/survey/filled")]
        public async Task<ActionResult> MarkSurveyFilled(
            [FromQuery] string playerId,
            [FromQuery] string key) {

            if (key != _configuration["SurveyKey"]) {
                return Unauthorized();
            }

            _context.SurveyResponses.Add(new SurveyPassed {
                PlayerId = playerId,
                SurveyId = "",
                Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
            });
            _context.SaveChanges();

            return Ok();
        }

        [HttpGet("~/survey/achievement")]
        public async Task<ActionResult<Achievement>> AssignAchievement() {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = _context.Players
                .Include(p => p.Achievements)
                .ThenInclude(a => a.Level)
                .Include(p => p.Achievements)
                .ThenInclude(a => a.AchievementDescription)
                .FirstOrDefault(p => p.Id == currentId);
            if (currentPlayer == null) {
                return Unauthorized();
            }

            var profileUrl = $"beatleader.xyz/u/{currentId}";
            var existingResponse = _context.SurveyResponses.FirstOrDefault(
                r => r.PlayerId == currentId || 
                r.PlayerId.EndsWith(profileUrl) ||
                r.PlayerId.Contains(profileUrl + "?") ||
                r.PlayerId.Contains(profileUrl + "/"));
            if (existingResponse == null) {
                return NotFound("Please complete the survey first");
            }

            if (currentPlayer.Achievements == null) {
                currentPlayer.Achievements = new List<Achievement>();
            }

            var achievement = currentPlayer
                .Achievements
                .FirstOrDefault(a => a.AchievementDescriptionId == 1);
            if (achievement == null) {
                achievement = new Achievement {
                    AchievementDescriptionId = 1,
                    Level = _context.AchievementLevels.FirstOrDefault(l => l.AchievementDescriptionId == 1 && l.Level == 1),
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                };
                currentPlayer.Achievements.Add(achievement);
                _context.SaveChanges();
            }

            return Ok(achievement);
        }
    }
}
