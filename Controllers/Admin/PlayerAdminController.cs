﻿using Amazon.S3;
using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class PlayerAdminController : Controller
    {
        private readonly AppContext _context;
        private readonly StorageContext _storageContext;
        CurrentUserController _currentUserController;
        ScoreRefreshController _scoreRefreshController;
        ReplayController _replayController;
        IWebHostEnvironment _environment;
        private readonly IAmazonS3 _s3Client;

        public PlayerAdminController(
            AppContext context,
            StorageContext storageContext,
            IWebHostEnvironment env,
            CurrentUserController currentUserController,
            ScoreRefreshController scoreRefreshController,
            ReplayController replayController,
            IConfiguration configuration)
        {
            _context = context;
            _storageContext = storageContext;
            _currentUserController = currentUserController;
            _scoreRefreshController = scoreRefreshController;
            _replayController = replayController;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/playerlink/{login}")]
        [Authorize]
        public async Task<ActionResult<AccountLink>> GetPlayerLink(string login)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AuthInfo? info = _context.Auths.FirstOrDefault(el => el.Login == login);
            if (info == null)
            {
                return NotFound("No info");
            }
            AccountLink? link = _context.AccountLinks.FirstOrDefault(el => el.OculusID == info.Id);
            if (link == null)
            {
                return NotFound("No link");
            }
            
            return link;
        }

        [HttpDelete("~/authinfo/{login}")]
        [Authorize]
        public async Task<ActionResult> DeleteAuthInfo(string login)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AuthInfo? info = await _context.Auths.FirstOrDefaultAsync(el => el.Login == login);
            if (info == null)
            {
                return NotFound("No info");
            }
            _context.Auths.Remove(info);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/authips/")]
        [Authorize]
        public async Task<ActionResult> DeleteAuthIps()
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var info = await _context.AuthIPs.ToArrayAsync();
            foreach (var item in info)
            {
                _context.AuthIPs.Remove(item);
            }
            
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/attempts/")]
        [Authorize]
        public async Task<ActionResult> DeleteAttempts()
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            var info = await _context.LoginAttempts.ToArrayAsync();
            foreach (var item in info)
            {
                _context.LoginAttempts.Remove(item);
            }
            
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/playerlink/{id}")]
        [Authorize]
        public async Task<ActionResult> DeletePlayerLinked(string id)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }
            AccountLink? link = await _context.AccountLinks.FirstOrDefaultAsync(el => el.SteamID == id);
            if (link == null)
            {
                return NotFound();
            }
            AuthInfo? info = await _context.Auths.FirstOrDefaultAsync(el => el.Id == link.OculusID);
            if (info == null)
            {
                return NotFound();
            }
            _context.AccountLinks.Remove(link);

            await _context.SaveChangesAsync();

            return Ok();
        }

        public class HMDStat {
            public string Headset { get; set; }
            public string Value { get; set; }

            [System.Text.Json.Serialization.JsonIgnore]
            public float FloatValue { get; set; }
        }

        [HttpGet("~/admin/headsetStats")]
        [Authorize]
        public async Task<ActionResult<List<HMDStat>>> GetHeadsetStats()
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var activeTreshold = timeset - 60 * 60 * 24 * 31 * 3;

            var scores = await _context
                .Scores
                .Where(s => s.Pp > 0 && s.Timepost > activeTreshold)
                .Select(s => new {
                    s.PlayerId,
                    s.Hmd,
                    s.Platform
                })
                .ToListAsync();

            var keys = scores.DistinctBy(s => s.Hmd.ToString() + "," + s.Platform.Split(",").First()).Select(s => s.Hmd.ToString() + "," + s.Platform.Split(",").First()).ToList();
            var groups = scores.GroupBy(s => s.PlayerId + s.Hmd + s.Platform.Split(",").First()).ToList();

            var totalCount = groups.Count();
            var result = new List<HMDStat>();
            foreach (var key in keys)
            {
                float value = ((float)groups.Count(g => g.First().Hmd.ToString() + "," + g.First().Platform.Split(",").First() == key) / totalCount) * 100f;

                result.Add(new HMDStat {
                    Headset = key,
                    Value = Math.Round(value, 2) + "%",
                    FloatValue = value
                });
            }
            
            return result.OrderByDescending(s => s.FloatValue).ToList();
        }

        [HttpGet("~/admin/versionStats")]
        [Authorize]
        public async Task<ActionResult<VersionsReport>> GetVersionStats([FromQuery] int time = 60 * 60 * 24 * 7 * 2)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            return await PlayerAdminControllerHelper.GetVersionStats(_context, time);
        }

        [HttpGet("~/admin/versionStatsScores")]
        [Authorize]
        public async Task<ActionResult<VersionsReport>> GetVersionStatsScores([FromQuery] int time = 60 * 60 * 24 * 7 * 2)
        {
            string currentId = HttpContext.CurrentUserID(_context);
            Player? currentPlayer = await _context.Players.FindAsync(currentId);
            if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var activeTreshold = timeset - time;

            var scores = (await _context
                .Scores
                .AsNoTracking()
                .Where(s => s.Timepost > activeTreshold)
                .Select(s => new {
                    s.PlayerId,
                    s.Platform
                })
                .ToListAsync())
                .Select(s => new {
                    s.PlayerId,
                    Platform = s.Platform.Split(",")[1].Split("_").First() + (s.Platform.Split(",")[0] == "oculus" ? "quest" : "pc")
                })
                .ToList();

            var keys = scores.DistinctBy(s => s.Platform).Select(s => s.Platform).ToList();
            var groups = scores.GroupBy(s => s.Platform).ToList();

            var totalCount = scores.Count();
            var result = new List<VersionStat>();
            foreach (var group in groups)
            {
                float value = ((float)group.Count() / totalCount) * 100f;

                result.Add(new VersionStat {
                    Version = group.Key,
                    Value = Math.Round(value, 2) + "%",
                    FloatValue = value
                });
            }

            return new VersionsReport {
                Stats = result.OrderByDescending(s => s.FloatValue).ToList(),
                Count = totalCount
            };
        }

        [HttpPost("~/clan/reserve")]
        public async Task<ActionResult> ReserveTag([FromQuery] string tag)
        {
            tag = tag.ToUpper();
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            _context.ReservedTags.Add(new ReservedClanTag
            {
                Tag = tag
            });
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("~/clan/reserve")]
        public async Task<ActionResult> AllowTag([FromQuery] string tag)
        {
            tag = tag.ToUpper();
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            var rt = await _context.ReservedTags.FirstOrDefaultAsync(rt => rt.Tag == tag);
            if (rt == null)
            {
                return NotFound();
            }

            _context.ReservedTags.Remove(rt);
            await _context.SaveChangesAsync();

            return Ok();
        }

        //[HttpDelete("~/player/{id}")]
        //[Authorize]
        [NonAction]
        public async Task<ActionResult> DeletePlayer(string id)
        {
            if (HttpContext != null) {
                string currentId = HttpContext.CurrentUserID(_context);
                Player? currentPlayer = await _context.Players.FindAsync(currentId);
                if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                {
                    return Unauthorized();
                }
            }

            await PlayerControllerHelper.DeletePlayer(_context, _storageContext, _s3Client, id);

            return Ok();
        }

        [HttpGet("~/theBestTriangles")]
        public async Task<ActionResult> theBestTriangles()
        {
            string currentID = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentID);

            if (!currentPlayer.Role.Contains("admin"))
            {
                return Unauthorized();
            }

            return Ok((await _context
                .Players
                .Where(p => !p.Banned && p.Pp > 16000)
                .Select(p => new { p.Id, PassPp = p.PassPp / 6000f, TechPp = p.TechPp / 1300f, AccPp = p.AccPp / 15000f })
                .ToListAsync())
                .OrderBy(p => Math.Abs(p.PassPp - 0.5) + Math.Abs(p.TechPp - 0.5) + Math.Abs(p.AccPp - 0.5))
                .ThenByDescending(p => p.PassPp + p.AccPp + p.TechPp)
                .Take(3));
        }
    }
}
