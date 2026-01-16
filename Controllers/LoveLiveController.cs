using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lib.ServerTiming;
using Newtonsoft.Json;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {

    // Response class for a single song status with idol info and player score
    public class IdolSongStatus {
        public IdolDescription IdolDescription { get; set; }
        public SongResponse Song { get; set; }
        public CompactScore? Score { get; set; }
        public bool IsNew { get; set; } // True if this idol hasn't been seen by the player yet
    }

    // Main response class for player's Love Live status
    public class PlayerIdolStatus {
        public ICollection<IdolSongStatus> Songs { get; set; }
        public ICollection<IdolDecoration> Decorations { get; set; }
        public ICollection<IdolDescription> BonusIdols { get; set; }
        public ICollection<IdolBackground> Backgrounds { get; set; }
        public string? CanvasState { get; set; }
        public int BackgroundId { get; set; }
    }

    // Request class for a single sticker placement
    public class PlacedSticker {
        public string Type { get; set; } // "idol", "bonus_idol", or "decoration"
        public int StickerId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Rotation { get; set; }
        public float Scale { get; set; }
        public bool Mirrored { get; set; }
        public int LayerIndex { get; set; }
    }

    // Request class for saving canvas state
    public class SaveCanvasRequest {
        public List<PlacedSticker> Stickers { get; set; }
        public int BackgroundId { get; set; }
    }

    [ApiExplorerSettings(IgnoreApi = true)]
    public class LoveLiveController : Controller {
        private readonly AppContext _context;
        private readonly IDbContextFactory<AppContext> _dbFactory;
        private readonly IAmazonS3 _s3Client;
        private readonly IServerTiming _serverTiming;

        private const int MaxStickers = 100;
        private const float MinScale = 0.2f;
        private const float MaxScale = 5.0f;

        public LoveLiveController(
            AppContext context,
            IDbContextFactory<AppContext> dbFactory,
            IConfiguration configuration,
            IServerTiming serverTiming) {
            _context = context;
            _dbFactory = dbFactory;
            _s3Client = configuration.GetS3Client();
            _serverTiming = serverTiming;
        }

        /// <summary>
        /// Get player's Love Live event status including songs, scores, and available stickers
        /// </summary>
        [HttpGet("~/event/lovelive/status")]
        public async Task<ActionResult<PlayerIdolStatus>> GetStatus(string? overrideId = null) {
            var currentId = overrideId ?? HttpContext.CurrentUserID(_context);

            var result = new PlayerIdolStatus {
                Songs = new List<IdolSongStatus>(),
                Decorations = new List<IdolDecoration>(),
                BonusIdols = new List<IdolDescription>(),
                Backgrounds = new List<IdolBackground>()
            };

            // Get all idol maps with their descriptions
            var idolMaps = await _context.Songs
                .Where(s => s.IdolDescription != null)
                .Select(s => new SongResponse {
                    Id = s.Id,
                    Hash = s.LowerHash,
                    Name = s.Name,
                    SubName = s.SubName,
                    Author = s.Author,
                    Mapper = s.Mapper,
                    MapperId = s.MapperId,
                    CoverImage = s.CoverImage,
                    FullCoverImage = s.FullCoverImage,
                    DownloadUrl = s.DownloadUrl,
                    Bpm = s.Bpm,
                    Duration = s.Duration,
                    UploadTime = s.UploadTime,
                    Difficulties = s.Difficulties,
                    IdolDescription = s.IdolDescription
                })
                .OrderBy(m => m.IdolDescription.Birthday)
                .ToListAsync();

            // Get songs info
            var songs = idolMaps.ToDictionary(s => s.Id);
            var songIds = idolMaps.Select(s => s.Id).Distinct().ToList();

            // Get player's canvas state (needed for seen idol IDs)
                var canvas = currentId == null ? null : await _context.IdolCanvases
                    .Where(c => c.PlayerId == currentId)
                    .FirstOrDefaultAsync();

            if (currentId != null) {
                // Get player's scores for these songs (without NF modifier)
                var playerScores = await _context.Scores
                    .Where(s => s.PlayerId == currentId && s.ValidForGeneral &&
                                !s.Modifiers.Contains("NF") && 
                                songIds.Contains(s.Leaderboard.SongId))
                    .Select(s => new {
                        SongId = s.Leaderboard.SongId,
                        Score = new CompactScore {
                            Id = s.Id,
                            BaseScore = s.BaseScore,
                            ModifiedScore = s.ModifiedScore,
                            Modifiers = s.Modifiers,
                            FullCombo = s.FullCombo,
                            MaxCombo = s.MaxCombo,
                            MissedNotes = s.MissedNotes,
                            BadCuts = s.BadCuts,
                            Hmd = s.Hmd,
                            Controller = s.Controller,
                            Accuracy = s.Accuracy,
                            Pp = s.Pp,
                            EpochTime = s.Timepost
                        }
                    })
                    .ToListAsync();

                var scoresBySong = playerScores
                    .GroupBy(s => s.SongId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Score.Accuracy).First().Score);
            
                // Parse seen idol IDs from JSON array
                var seenIdolIds = new HashSet<int>();
                if (!string.IsNullOrEmpty(canvas?.SeenIdolIds)) {
                    try {
                        var ids = JsonConvert.DeserializeObject<List<int>>(canvas.SeenIdolIds);
                        if (ids != null) {
                            seenIdolIds = ids.ToHashSet();
                        }
                    } catch { }
                }

                // Build song status list
                foreach (var idolMap in idolMaps) {
                    songs.TryGetValue(idolMap.Id, out var song);
                    scoresBySong.TryGetValue(idolMap.Id, out var score);

                    // IsNew = player has unlocked (has score) but hasn't seen yet
                    var hasScore = score != null;
                    var hasSeen = seenIdolIds.Contains(idolMap.IdolDescription.Id);

                    ((List<IdolSongStatus>)result.Songs).Add(new IdolSongStatus {
                        IdolDescription = idolMap.IdolDescription,
                        Song = song,
                        Score = score,
                        IsNew = hasScore && !hasSeen
                    });
                }
            } else {
                foreach (var idolMap in idolMaps) {
                    songs.TryGetValue(idolMap.Id, out var song);

                    ((List<IdolSongStatus>)result.Songs).Add(new IdolSongStatus {
                        IdolDescription = idolMap.IdolDescription,
                        Song = song
                    });
                }
            }

            var globalBonusIdols = await _context.IdolDescriptions
                .Where(i => i.GloballyAvailable && i.Bonus)
                .ToListAsync();

            if (currentId != null) {
                // Get bonus idols: player-specific + globally available
                var playerBonusIdols = await _context.PlayerBonusIdols
                    .Where(b => b.PlayerId == currentId)
                    .Include(b => b.IdolDescription)
                    .Select(b => b.IdolDescription)
                    .ToListAsync();

                var allBonusIdols = playerBonusIdols
                    .Union(globalBonusIdols)
                    .DistinctBy(i => i.Id)
                    .ToList();
                result.BonusIdols = allBonusIdols;
            } else {
                result.BonusIdols = globalBonusIdols;
            }

            var globalDecorations = await _context.IdolDecorations
                .Where(d => d.GloballyAvailable)
                .ToListAsync();

            if (currentId != null) {
                // Get decorations: player-specific + globally available
                var playerDecorations = await _context.PlayerIdolDecorations
                    .Where(d => d.PlayerId == currentId)
                    .Include(d => d.IdolDecoration)
                    .Select(d => d.IdolDecoration)
                    .ToListAsync();

                var allDecorations = playerDecorations
                    .Union(globalDecorations)
                    .DistinctBy(d => d.Id)
                    .ToList();
                result.Decorations = allDecorations;
            } else {
                result.Decorations = globalDecorations;
            }

            // Get available backgrounds (globally available)
            var backgrounds = await _context.IdolBackgrounds
                .Where(b => b.GloballyAvailable)
                .ToListAsync();
            result.Backgrounds = backgrounds;

            // Set canvas state from earlier query
            result.CanvasState = canvas?.CanvasState;
            result.BackgroundId = canvas?.BackgroundId ?? 0;

            return Ok(result);
        }

        /// <summary>
        /// Save player's canvas state with sticker placements
        /// </summary>
        [HttpPost("~/event/lovelive/canvas")]
        public async Task<ActionResult> SaveCanvas([FromBody] SaveCanvasRequest request) {
            var currentId = HttpContext.CurrentUserID(_context);
            if (string.IsNullOrEmpty(currentId)) {
                return Unauthorized("Please sign in to save your canvas.");
            }

            if (request?.Stickers == null) {
                return BadRequest("Invalid canvas data.");
            }

            // Limit number of stickers
            if (request.Stickers.Count > MaxStickers) {
                return BadRequest($"Maximum {MaxStickers} stickers allowed.");
            }

            // Get player's available stickers for validation
            var availableIdolIds = new HashSet<int>();
            var availableBonusIdolIds = new HashSet<int>();
            var availableDecorationIds = new HashSet<int>();

            // Get idols from passed songs
            var idolMaps = await _context.Songs.Where(s => s.IdolDescription != null).AsNoTracking().ToListAsync();
            var songIds = idolMaps.Select(m => m.Id).Distinct().ToList();

            var passedSongIds = await _context.Scores
                .Where(s => s.PlayerId == currentId && s.ValidForGeneral &&
                            !s.Modifiers.Contains("NF") && 
                            songIds.Contains(s.Leaderboard.SongId))
                .Select(s => s.Leaderboard.SongId)
                .Distinct()
                .ToListAsync();

            foreach (var idolMap in idolMaps) {
                if (passedSongIds.Contains(idolMap.Id)) {
                    availableIdolIds.Add(idolMap.IdolDescriptionId ?? 0);
                }
            }

            // Get bonus idols: player-specific + globally available
            var playerBonusIdolIds = await _context.PlayerBonusIdols
                .Where(b => b.PlayerId == currentId)
                .Select(b => b.IdolDescriptionId)
                .ToListAsync();
            foreach (var id in playerBonusIdolIds) {
                availableBonusIdolIds.Add(id);
            }

            var globalBonusIdolIds = await _context.IdolDescriptions
                .Where(i => i.GloballyAvailable && i.Bonus)
                .Select(i => i.Id)
                .ToListAsync();
            foreach (var id in globalBonusIdolIds) {
                availableBonusIdolIds.Add(id);
            }

            // Get decorations: player-specific + globally available
            var playerDecorationIds = await _context.PlayerIdolDecorations
                .Where(d => d.PlayerId == currentId)
                .Select(d => d.IdolDecorationId)
                .ToListAsync();
            foreach (var id in playerDecorationIds) {
                availableDecorationIds.Add(id);
            }

            var globalDecorationIds = await _context.IdolDecorations
                .Where(d => d.GloballyAvailable)
                .Select(d => d.Id)
                .ToListAsync();
            foreach (var id in globalDecorationIds) {
                availableDecorationIds.Add(id);
            }

            // Validate and sanitize stickers
            var validatedStickers = new List<PlacedSticker>();
            foreach (var sticker in request.Stickers) {
                bool isValid = sticker.Type switch {
                    "idol" => availableIdolIds.Contains(sticker.StickerId),
                    "bonus_idol" => availableBonusIdolIds.Contains(sticker.StickerId),
                    "decoration" => availableDecorationIds.Contains(sticker.StickerId),
                    _ => false
                };

                if (!isValid) continue;

                // Clamp values to valid ranges
                validatedStickers.Add(new PlacedSticker {
                    Type = sticker.Type,
                    StickerId = sticker.StickerId,
                    X = sticker.X,
                    Y = sticker.Y,
                    Rotation = sticker.Rotation % 360,
                    Mirrored = sticker.Mirrored,
                    LayerIndex = sticker.LayerIndex,
                    Scale = Math.Clamp(sticker.Scale, MinScale, MaxScale)
                });
            }

            // Validate background ID if provided
            int validBackgroundId = 0;
            if (request.BackgroundId != 0) {
                var background = await _context.IdolBackgrounds.FindAsync(request.BackgroundId);
                if (background != null && background.GloballyAvailable) {
                    validBackgroundId = background.Id;
                }
            } else {
                validBackgroundId = 0;
            }

            // Save canvas state
            var canvas = await _context.IdolCanvases
                .Where(c => c.PlayerId == currentId)
                .FirstOrDefaultAsync();

            var canvasJson = JsonExtensions.SerializeObject(validatedStickers);

            if (canvas == null) {
                canvas = new IdolCanvas {
                    PlayerId = currentId,
                    CanvasState = canvasJson,
                    BackgroundId = validBackgroundId,
                    LastUpdated = Time.UnixNow()
                };
                _context.IdolCanvases.Add(canvas);
            } else {
                canvas.CanvasState = canvasJson;
                canvas.BackgroundId = validBackgroundId;
                canvas.LastUpdated = Time.UnixNow();
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("~/event/lovelive/canvas")]
        public async Task<ActionResult> DeleteCanvas([FromBody] SaveCanvasRequest request) {
            var currentId = HttpContext.CurrentUserID(_context);
            if (string.IsNullOrEmpty(currentId)) {
                return Unauthorized("Please sign in to save your canvas.");
            }

            var canvas = await _context.IdolCanvases
                .Where(c => c.PlayerId == currentId)
                .FirstOrDefaultAsync();
            if (canvas != null) {
                _context.IdolCanvases.Remove(canvas);
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        /// <summary>
        /// Mark all unlocked idols as seen (clears "new" indicators for idols the player has scores on)
        /// </summary>
        [HttpPost("~/event/lovelive/mark-idols-seen")]
        public async Task<ActionResult> MarkIdolsSeen() {
            var currentId = HttpContext.CurrentUserID(_context);
            if (string.IsNullOrEmpty(currentId)) {
                return Unauthorized("Please sign in.");
            }

            // Get song IDs for idol maps
            var idolSongIds = await _context.Songs
                .Where(s => s.IdolDescriptionId != null)
                .Select(s => s.Id)
                .ToListAsync();

            // Get idol IDs where the player has scores (unlocked idols)
            var unlockedIdolIds = await _context.Scores
                .Where(s => s.PlayerId == currentId && s.ValidForGeneral &&
                            !s.Modifiers.Contains("NF") && 
                            idolSongIds.Contains(s.Leaderboard.SongId))
                .Select(s => s.Leaderboard.Song.IdolDescriptionId!.Value)
                .Distinct()
                .ToListAsync();

            var canvas = await _context.IdolCanvases
                .Where(c => c.PlayerId == currentId)
                .FirstOrDefaultAsync();

            // Merge with existing seen IDs (don't lose previously seen idols)
            var seenIdolIds = new HashSet<int>();
            if (!string.IsNullOrEmpty(canvas?.SeenIdolIds)) {
                try {
                    var ids = JsonConvert.DeserializeObject<List<int>>(canvas.SeenIdolIds);
                    if (ids != null) {
                        seenIdolIds = ids.ToHashSet();
                    }
                } catch { }
            }
            foreach (var id in unlockedIdolIds) {
                seenIdolIds.Add(id);
            }
            var seenIdsJson = JsonConvert.SerializeObject(seenIdolIds.ToList());

            if (canvas == null) {
                canvas = new IdolCanvas {
                    PlayerId = currentId,
                    CanvasState = "[]",
                    BackgroundId = 0,
                    SeenIdolIds = seenIdsJson,
                    LastUpdated = Time.UnixNow()
                };
                _context.IdolCanvases.Add(canvas);
            } else {
                canvas.SeenIdolIds = seenIdsJson;
            }

            await _context.SaveChangesAsync();
            return Ok(new { MarkedSeenCount = unlockedIdolIds.Count, TotalSeen = seenIdolIds.Count });
        }

        /// <summary>
        /// Get a specific player's canvas state (public view)
        /// </summary>
        [HttpGet("~/event/lovelive/canvas/{playerId}")]
        public async Task<ActionResult<PlayerIdolStatus>> GetPlayerCanvas(string playerId) {
            var canvas = await _context.IdolCanvases
                .Where(c => c.PlayerId == playerId)
                .FirstOrDefaultAsync();

            if (canvas == null) {
                return NotFound("Canvas not found for this player.");
            }

            return await GetStatus(playerId);
        }

        #region Admin Endpoints

        /// <summary>
        /// Admin: Add a new idol description
        /// </summary>
        [HttpPost("~/admin/lovelive/idol")]
        public async Task<ActionResult<IdolDescription>> AddIdol([FromBody] IdolDescription idol, [FromQuery] string? songId = null) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var newIdol = new IdolDescription {
                Name = idol.Name,
                Bonus = idol.Bonus,
                GloballyAvailable = idol.GloballyAvailable,
                Birthday = idol.Birthday,
                SmallPictureRegular = idol.SmallPictureRegular,
                BigPictureRegular = idol.BigPictureRegular,
                SmallPicturePro = idol.SmallPicturePro,
                BigPicturePro = idol.BigPicturePro,
                Description = idol.Description
            };

            _context.IdolDescriptions.Add(newIdol);
            await _context.SaveChangesAsync();

            if (songId != null) {
                var song = await _context.Songs.FindAsync(songId);
                if (song == null) {
                    return NotFound("Song not found.");
                }

                song.IdolDescription = idol;
                await _context.SaveChangesAsync();
            }

            return Ok(newIdol);
        }

        /// <summary>
        /// Admin: Update an idol description
        /// </summary>
        [HttpPut("~/admin/lovelive/idol/{id}")]
        public async Task<ActionResult<IdolDescription>> UpdateIdol(int id, [FromBody] IdolDescription idol) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var existingIdol = await _context.IdolDescriptions.FindAsync(id);
            if (existingIdol == null) {
                return NotFound("Idol not found.");
            }

            existingIdol.Name = idol.Name;
            existingIdol.Bonus = idol.Bonus;
            existingIdol.GloballyAvailable = idol.GloballyAvailable;
            existingIdol.Birthday = idol.Birthday;
            existingIdol.SmallPictureRegular = idol.SmallPictureRegular;
            existingIdol.BigPictureRegular = idol.BigPictureRegular;
            existingIdol.SmallPicturePro = idol.SmallPicturePro;
            existingIdol.BigPicturePro = idol.BigPicturePro;
            existingIdol.Description = idol.Description;

            await _context.SaveChangesAsync();
            return Ok(existingIdol);
        }

        /// <summary>
        /// Admin: Delete an idol description
        /// </summary>
        [HttpDelete("~/admin/lovelive/idol/{id}")]
        public async Task<ActionResult> DeleteIdol(int id) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var idol = await _context.IdolDescriptions.FindAsync(id);
            if (idol == null) {
                return NotFound("Idol not found.");
            }

            _context.IdolDescriptions.Remove(idol);
            await _context.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Admin: Get all idols
        /// </summary>
        [HttpGet("~/admin/lovelive/idols")]
        public async Task<ActionResult<List<IdolDescription>>> GetAllIdols() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var idols = await _context.IdolDescriptions.ToListAsync();
            return Ok(idols);
        }

        /// <summary>
        /// Admin: Map an idol to a song
        /// </summary>
        [HttpPost("~/admin/lovelive/idolmap")]
        public async Task<ActionResult> AddIdolMap(
            [FromQuery] int idolId, 
            [FromQuery] string songId) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var idol = await _context.IdolDescriptions.FindAsync(idolId);
            if (idol == null) {
                return NotFound("Idol not found.");
            }

            var song = await _context.Songs.FindAsync(songId);
            if (song == null) {
                return NotFound("Song not found.");
            }

            song.IdolDescription = idol;
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Admin: Delete an idol-song mapping
        /// </summary>
        [HttpDelete("~/admin/lovelive/idolmap/{id}")]
        public async Task<ActionResult> DeleteIdolMap(string id) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var idolMap = await _context.Songs.FindAsync(id);
            if (idolMap == null) {
                return NotFound("Idol map not found.");
            }

            idolMap.IdolDescription = null;
            await _context.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Admin: Add a decoration
        /// </summary>
        [HttpPost("~/admin/lovelive/decoration")]
        public async Task<ActionResult<IdolDecoration>> AddDecoration([FromBody] IdolDecoration decoration) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var newDecoration = new IdolDecoration {
                Name = decoration.Name,
                GloballyAvailable = decoration.GloballyAvailable,
                SmallPictureRegular = decoration.SmallPictureRegular,
                BigPictureRegular = decoration.BigPictureRegular,
                SmallPicturePro = decoration.SmallPicturePro,
                BigPicturePro = decoration.BigPicturePro,
                Description = decoration.Description
            };

            _context.IdolDecorations.Add(newDecoration);
            await _context.SaveChangesAsync();

            return Ok(newDecoration);
        }

        /// <summary>
        /// Admin: Delete a decoration
        /// </summary>
        [HttpDelete("~/admin/lovelive/decoration/{id}")]
        public async Task<ActionResult> DeleteDecoration(int id) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var decoration = await _context.IdolDecorations.FindAsync(id);
            if (decoration == null) {
                return NotFound("Decoration not found.");
            }

            _context.IdolDecorations.Remove(decoration);
            await _context.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Admin: Get all decorations
        /// </summary>
        [HttpGet("~/admin/lovelive/decorations")]
        public async Task<ActionResult<List<IdolDecoration>>> GetAllDecorations() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var decorations = await _context.IdolDecorations.ToListAsync();
            return Ok(decorations);
        }

        /// <summary>
        /// Admin: Add a background
        /// </summary>
        [HttpPost("~/admin/lovelive/background")]
        public async Task<ActionResult<IdolBackground>> AddBackground([FromBody] IdolBackground background) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var newBackground = new IdolBackground {
                Name = background.Name,
                GloballyAvailable = background.GloballyAvailable,
                ImageUrl = background.ImageUrl,
                ThumbnailUrl = background.ThumbnailUrl,
                Description = background.Description
            };

            _context.IdolBackgrounds.Add(newBackground);
            await _context.SaveChangesAsync();
            return Ok(newBackground);
        }

        /// <summary>
        /// Admin: Update a background
        /// </summary>
        [HttpPut("~/admin/lovelive/background/{id}")]
        public async Task<ActionResult<IdolBackground>> UpdateBackground(int id, [FromBody] IdolBackground background) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var existingBackground = await _context.IdolBackgrounds.FindAsync(id);
            if (existingBackground == null) {
                return NotFound("Background not found.");
            }

            existingBackground.Name = background.Name;
            existingBackground.GloballyAvailable = background.GloballyAvailable;
            existingBackground.ImageUrl = background.ImageUrl;
            existingBackground.ThumbnailUrl = background.ThumbnailUrl;
            existingBackground.Description = background.Description;

            await _context.SaveChangesAsync();
            return Ok(existingBackground);
        }

        /// <summary>
        /// Admin: Delete a background
        /// </summary>
        [HttpDelete("~/admin/lovelive/background/{id}")]
        public async Task<ActionResult> DeleteBackground(int id) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var background = await _context.IdolBackgrounds.FindAsync(id);
            if (background == null) {
                return NotFound("Background not found.");
            }

            _context.IdolBackgrounds.Remove(background);
            await _context.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Admin: Get all backgrounds
        /// </summary>
        [HttpGet("~/admin/lovelive/backgrounds")]
        public async Task<ActionResult<List<IdolBackground>>> GetAllBackgrounds() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var backgrounds = await _context.IdolBackgrounds.ToListAsync();
            return Ok(backgrounds);
        }

        /// <summary>
        /// Admin: Award a bonus idol to a player
        /// </summary>
        [HttpPost("~/admin/lovelive/player/{playerId}/bonusidol")]
        public async Task<ActionResult> AwardBonusIdol(string playerId, [FromQuery] int idolId, [FromQuery] string reason) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var player = await _context.Players.FindAsync(playerId);
            if (player == null) {
                return NotFound("Player not found.");
            }

            var idol = await _context.IdolDescriptions.FindAsync(idolId);
            if (idol == null) {
                return NotFound("Idol not found.");
            }

            // Check if already awarded
            var existing = await _context.PlayerBonusIdols
                .Where(b => b.PlayerId == playerId && b.IdolDescriptionId == idolId)
                .FirstOrDefaultAsync();

            if (existing != null) {
                return BadRequest("Player already has this bonus idol.");
            }

            var bonusIdol = new PlayerBonusIdol {
                PlayerId = playerId,
                IdolDescriptionId = idolId,
                Reason = reason
            };

            _context.PlayerBonusIdols.Add(bonusIdol);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Admin: Remove a bonus idol from a player
        /// </summary>
        [HttpDelete("~/admin/lovelive/player/{playerId}/bonusidol/{idolId}")]
        public async Task<ActionResult> RemoveBonusIdol(string playerId, int idolId) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var bonusIdol = await _context.PlayerBonusIdols
                .Where(b => b.PlayerId == playerId && b.IdolDescriptionId == idolId)
                .FirstOrDefaultAsync();

            if (bonusIdol == null) {
                return NotFound("Bonus idol not found for this player.");
            }

            _context.PlayerBonusIdols.Remove(bonusIdol);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Admin: Award a decoration to a player
        /// </summary>
        [HttpPost("~/admin/lovelive/player/{playerId}/decoration")]
        public async Task<ActionResult> AwardDecoration(string playerId, [FromQuery] int decorationId, [FromQuery] string reason) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var player = await _context.Players.FindAsync(playerId);
            if (player == null) {
                return NotFound("Player not found.");
            }

            var decoration = await _context.IdolDecorations.FindAsync(decorationId);
            if (decoration == null) {
                return NotFound("Decoration not found.");
            }

            // Check if already awarded
            var existing = await _context.PlayerIdolDecorations
                .Where(d => d.PlayerId == playerId && d.IdolDecorationId == decorationId)
                .FirstOrDefaultAsync();

            if (existing != null) {
                return BadRequest("Player already has this decoration.");
            }

            var playerDecoration = new PlayerIdolDecoration {
                PlayerId = playerId,
                IdolDecorationId = decorationId,
                Reason = reason
            };

            _context.PlayerIdolDecorations.Add(playerDecoration);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Admin: Remove a decoration from a player
        /// </summary>
        [HttpDelete("~/admin/lovelive/player/{playerId}/decoration/{decorationId}")]
        public async Task<ActionResult> RemoveDecoration(string playerId, int decorationId) {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var playerDecoration = await _context.PlayerIdolDecorations
                .Where(d => d.PlayerId == playerId && d.IdolDecorationId == decorationId)
                .FirstOrDefaultAsync();

            if (playerDecoration == null) {
                return NotFound("Decoration not found for this player.");
            }

            _context.PlayerIdolDecorations.Remove(playerDecoration);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Admin: Seed all Love Live idols at once
        /// </summary>
        [HttpPost("~/admin/lovelive/seed-all-idols")]
        public async Task<ActionResult<List<IdolDescription>>> SeedAllIdols() {
            var currentId = HttpContext.CurrentUserID(_context);
            var currentPlayer = await _context.Players.FindAsync(currentId);

            if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
                return Unauthorized();
            }

            var idolsWithSongs = GetAllLoveLiveIdols();
            var addedIdols = new List<IdolDescription>();
            var linkedSongs = new List<string>();

            foreach (var (idol, songId) in idolsWithSongs) {
                // Check if idol already exists by name
                var existing = await _context.IdolDescriptions
                    .FirstOrDefaultAsync(i => i.Birthday == idol.Birthday);

                if (existing == null) {
                    _context.IdolDescriptions.Add(idol);
                    addedIdols.Add(idol);

                    // Link song to idol if songId is provided
                    if (!string.IsNullOrEmpty(songId)) {
                        var song = await _context.Songs.FirstOrDefaultAsync(s => s.Id == songId);
                        if (song != null) {
                            song.IdolDescription = idol;
                            linkedSongs.Add($"{idol.Name} -> {songId}");
                        }
                    }
                } else {
                    existing.Name = idol.Name;
                    existing.BigPictureRegular = idol.BigPictureRegular;
                    existing.SmallPictureRegular = idol.SmallPictureRegular;
                    existing.BigPicturePro = idol.BigPicturePro;
                    existing.SmallPicturePro = idol.SmallPicturePro;
                    existing.Description = idol.Description;
                    existing.RewardGif = idol.RewardGif;
                }
            }

            await _context.BulkSaveChangesAsync();

            return Ok(new { 
                Added = addedIdols.Count, 
                Skipped = idolsWithSongs.Count - addedIdols.Count,
                LinkedSongs = linkedSongs.Count,
                Idols = addedIdols 
            });
        }

        private static int DayOfYear(int month, int day) {
            // Using 2024 as reference (leap year to handle Feb 29)
            return (int)new DateTime(2024, month, day).Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        }

        private static string IdolImage(int index, string size, bool pro = false) {
            var suffix = pro ? "_pro" : "";
            return $"https://cdn.assets.beatleader.com/lovelive_idol_{index}_{size}{suffix}.png";
        }

        private static string IdolImageX(int index, string size, bool pro = false) {
            var suffix = pro ? "_pro" : "";
            return $"https://cdn.assets.beatleader.com/lovelive_idol_{index}x_{size}{suffix}.png";
        }

        private static string IdolGif(int index) {
            return $"https://cdn.assets.beatleader.com/lovelive_congratulations_{index}.gif";
        }

        private static List<(IdolDescription idol, string? songId)> GetAllLoveLiveIdols() {
            var idols = new List<(IdolDescription idol, string? songId)>();
            int index = 1;

            // ==================== Sorted by Birthday (Groups kept together) ====================

            // January 1 - Dia Kurosawa (Aqours)
            idols.Add((new IdolDescription {
                Name = "Dia Kurosawa",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(1, 1),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "The student council president and Ruby's older sister. Dia is strict and proper but secretly a huge µ's fan, especially of Eli. She initially opposed Aqours before revealing her idol otaku side."
            }, "387e2"));
            index++;

            // January 6 - A-RISE (all 3 members share this birthday)
            idols.Add((new IdolDescription {
                Name = "A-RISE",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(1, 6),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A rival trio to μ's, who originally inspired Honoka to be a school idol. Formed and led by Tsubasa Kira. Other members are: Erena Todo, Anju Yuki"
            }, "38a12"));
            index++;

            // January 13 - Sayaka Murano (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Sayaka Murano",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(1, 13),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A former track and field athlete who joins the idol club. Sayaka brings her athletic abilities to performances and has a straightforward, reliable personality."
            }, "38dc8"));
            index++;

            // January 17 - Hanayo Koizumi (µ's)
            idols.Add((new IdolDescription {
                Name = "Hanayo Koizumi",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(1, 17),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A shy first-year student with an immense love for rice and school idols. Hanayo is a dedicated idol otaku whose dream of becoming an idol came true when she joined µ's with encouragement from Rin."
            }, "38fba"));
            index++;

            // January 20 - Wien Margarete (Liella!)
            idols.Add((new IdolDescription {
                Name = "Wien Margarete",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(1, 20),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A transfer student from Vienna, Austria who is a classically trained musician. Margarete initially challenged Liella! as a rival but eventually joined them, bringing her musical expertise."
            }, "3918e"));
            index++;

            // January 23 - Kasumi Nakasu (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Kasumi Nakasu",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(1, 23),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A first-year student who strives to be the cutest idol. Kasumi is mischievous and competitive but genuinely cares for her clubmates. Everyone calls her 'Kasumin' to reflect her playful character."
            }, "3935a"));
            index++;

            // February 5 - Emma Verde (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Emma Verde",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(2, 5),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A third-year exchange student from Switzerland. Emma came to Japan because of her love for school idols. She's gentle, caring, and brings a warm, healing presence to the club."
            }, "39a90"));
            index++;

            // February 10 - Kanan Matsuura (Aqours)
            idols.Add((new IdolDescription {
                Name = "Kanan Matsuura",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(2, 10),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A third-year student who helps run her family's diving shop. Kanan is athletic, level-headed, and deeply caring for her friends on an emotional level."
            }, "39db6"));
            index++;

            // February 15 - Lanzhu Zhong (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Lanzhu Zhong",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(2, 15),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A transfer student from Hong Kong and childhood friend of Shioriko. Lanzhu is confident and skilled, initially pursuing solo idol activities before understanding the value of the club's bonds."
            }, "39fce"));
            index++;

            // February 25 - Chisato Arashi (Liella!)
            idols.Add((new IdolDescription {
                Name = "Chisato Arashi",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(2, 25),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Initially wanting to focus on her talent for dancing, Chisato hesitated to become a school idol with her childhood friend, Kanon, but was eventually inspired and motivated by her to become one. She also loves round objects, shown by her two side-buns in her hairstyle!"
            }, "3a5c5"));
            index++;

            // February 28 - Kosuzu Kachimachi (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Kosuzu Kachimachi",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(2, 28),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A small and cute member of Hasunosora's idol club. Kosuzu may be petite but has a big heart and brings her own special charm to performances."
            }, "44c1a"));
            index++;

            // March 1 - Ayumu Uehara (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Ayumu Uehara",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(3, 1),
                SmallPictureRegular = IdolImageX(index, "small"),
                BigPictureRegular = IdolImageX(index, "big"),
                SmallPicturePro = IdolImageX(index, "small", true),
                BigPicturePro = IdolImageX(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A second-year student who discovered school idols through her childhood friend Yu. Ayumu is earnest and hardworking, striving to become an idol that can make everyone smile with her warm heart. She loves cute and adorable things, shown especially in her sense of fashion!"
            }, "3a7b9"));
            index++;

            // March 4 - Hanamaru Kunikida (Aqours)
            idols.Add((new IdolDescription {
                Name = "Hanamaru Kunikida",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(3, 4),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A bookworm first-year student from a temple family. Hanamaru is Ruby's best friend, known for her love of reading, fascination with modern technology ('Mirai zura!'), and warm personality."
            }, "3a91e"));
            index++;

            // March 15 - Umi Sonoda (µ's)
            idols.Add((new IdolDescription {
                Name = "Umi Sonoda",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(3, 15),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Coming from a traditional family of Japanese dance and archery, Umi is the lyricist for µ's. She is known for her serious demeanor, discipline, and occasional stage fright. She is also Honoka's childhood friend."
            }, "39a3d"));
            index++;

            // March 28 - Sunny Passion (both members share this birthday)
            idols.Add((new IdolDescription {
                Name = "Sunny Passion",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(3, 28),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A group formed by Yuna Hijirisawa and Mao Hiiragi. Sunny Passion was a group Keke essentially worshipped before becoming a school idol herself, and as Liella! grew, SunnyPa later developed a friendly rivalry with their group."
            }, "3b47d"));
            index++;

            // April 3 - Shizuku Osaka (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Shizuku Osaka",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(4, 3),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A first-year student in the drama club who joins the idol club. Shizuku is talented at acting and singing, often playing different characters in performances. Not good at sports though..."
            }, "3b7df"));
            index++;

            // April 10 - Kinako Sakurakoji (Liella!)
            idols.Add((new IdolDescription {
                Name = "Kinako Sakurakoji",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(4, 10),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "While not very athletic at first, Kinako isn't one to back down from a challenge easily, and is determined to continually improve. She's also well-seasoned with care for animals, as her family owns a ranch estate."
            }, "3ba73"));
            index++;

            // April 17 - You Watanabe (Aqours)
            idols.Add((new IdolDescription {
                Name = "You Watanabe",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(4, 17),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "You is a skilled swimmer, with dreams of one day becoming a ship captain. You designs and creates Aqours' costumes, and is known for her cheerful 'Yousoro!' catchphrase. She is also Chika's childhood friend!"
            }, "3bd54"));
            index++;

            // April 19 - Maki Nishikino (µ's)
            idols.Add((new IdolDescription {
                Name = "Maki Nishikino",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(4, 19),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "The composer for µ's and daughter of a wealthy family that runs a hospital. Maki is a talented pianist with a tsundere personality, often hiding her true feelings behind a cool exterior."
            }, "3be3c"));
            index++;

            // May 1 - Kanon Shibuya (Liella!)
            idols.Add((new IdolDescription {
                Name = "Kanon Shibuya",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(5, 1),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A founding member of Liella! from Yuigaoka Girls' High School. Kanon had stage fright that prevented her from pursuing music until she discovered school idols and found her voice again."
            }, "3c3d6"));
            index++;

            // May 4 - Saint Snow (Sarah leads, Leah follows - kept together)
            idols.Add((new IdolDescription {
                Name = "Sarah Kazuno",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(5, 4),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Sarah Kazuno is the older sister and leader of Saint Snow from Hokkaido. Sarah is passionate about being the best and initially saw Aqours as rivals before becoming friends and allies."
            }, "3c531"));
            index++;

            // May 22 - Kaho Hinoshita (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Kaho Hinoshita",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(5, 22),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "The protagonist of Hasunosora who transferred to pursue her idol dreams. Kaho is optimistic and passionate, leading the school idol club with her bright energy and determination."
            }, "3cdeb"));
            index++;

            // May 30 - Ai Miyashita (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Ai Miyashita",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(5, 30),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A second-year gyaru who loves puns and making others laugh. Ai is the mood maker of the club, always energetic and positive. She's skilled at dancing and brings joy wherever she goes."
            }, "3d166"));
            index++;

            // June 9 - Nozomi Tojo (µ's)
            idols.Add((new IdolDescription {
                Name = "Nozomi Tojo",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(6, 9),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Nozomi is the Vice-President of the student council at Otonokizaka High School. While she's known for her spiritual nature and tarot card readings, she is also the emotional heart of µ's who helped bring the group together to begin with!"
            }, "3d697"));
            index++;

            // June 13 - Mari Ohara (Aqours)
            idols.Add((new IdolDescription {
                Name = "Mari Ohara",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(6, 13),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Mari is a theatrical, wealthy, and goofy third-year student at Uranohoshi High who loves mixing English into her speech. She returned to Uranohoshi after studying abroad to revive her original school idol group with childhood friends Dia and Kanan."
            }, "3d8a3"));
            index++;

            // June 15 - Kozue Otomune (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Kozue Otomune",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(6, 15),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A gentle and soft-spoken member of Hasunosora's idol club. Kozue has a calming presence and speaks in a dreamy, poetic manner that matches her ethereal image."
            }, "3da39"));
            index++;

            // June 17 - Shiki Wakana (Liella!)
            idols.Add((new IdolDescription {
                Name = "Shiki Wakana",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(6, 17),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Shiki is a quiet and mysterious girl who is obsessed with studying insects and creating insane gadgets. She is best friends with Mei Yoneme, and prefers quaint and relaxing environments."
            }, "3db34"));
            index++;

            // June 29 - Karin Asaka (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Karin Asaka",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(6, 29),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "As a third-year student who is a professional model, Karin presents a cool and confident outward image, but is actually a very caring and emotional person. She joins Nijigasaki's School Idol club as a new and unique way to express herself, as she does with her modeling."
            }, "3e050"));
            index++;

            // July 13 - Yoshiko Tsushima (Aqours)
            idols.Add((new IdolDescription {
                Name = "Yoshiko Tsushima",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(7, 13),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A first-year student who calls herself 'Yohane', a fallen angel from the heavens. Yoshiko has a chuunibyou personality and believes she brings bad luck, but her unique charm won fans' hearts."
            }, "3e5c3"));
            index++;

            // July 17 - Keke Tang (Liella!)
            idols.Add((new IdolDescription {
                Name = "Keke Tang",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(7, 17),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A transfer student from Shanghai who came to Japan for school idols. Keke is passionate, expressive, and founded Liella! with Kanon. Her enthusiasm often shows through her mixed language."
            }, "3e828"));
            index++;

            // July 22 - Nico Yazawa (µ's)
            idols.Add((new IdolDescription {
                Name = "Nico Yazawa",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(7, 22),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A third-year student and self-proclaimed 'number one idol in the universe'. Nico is known for her signature catchphrase 'Nico Nico Nii~', her idol expertise, and caring for her younger siblings."
            }, "3ebfa"));
            index++;

            // August 1 - Chika Takami (Aqours)
            idols.Add((new IdolDescription {
                Name = "Chika Takami",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(8, 1),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "The leader of Aqours from Uranohoshi Girls' High School in Uchiura. Chika was inspired by µ's and started her own school idol group to bring shine to her beloved town and school."
            }, "3f09d"));
            index++;

            // August 3 - Honoka Kosaka (µ's)
            idols.Add((new IdolDescription {
                Name = "Honoka Kosaka",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(8, 3),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "The leader and founder of µ's from Otonokizaka High School. Honoka is an energetic and optimistic girl whose determination to save her school from closing led to the formation of the school idol group."
            }, "3f163"));
            index++;

            // August 7 - Natsumi Onitsuka (Liella!)
            idols.Add((new IdolDescription {
                Name = "Natsumi Onitsuka",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(8, 7),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Initially blinded by her belief that making money was all she was good at, Natsumi saw Liella! as a lucrative business opportunity before falling in love with them and discovering her true happiness in performing as a school idol. She runs her own channel as an 'L-Tuber' (Live-Tuber) and frequently posts behind-the-scenes videos of Liella! hard at work!"
            }, "3f51f"));
            index++;

            // August 8 - Setsuna Yuki (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Setsuna Yuki",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(8, 8),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "The cold, serious, and highly-intelligent Student Council President of Nijigasaki High School, 'Nana Nakagawa', has a secret identity: she's actually the eccentric and mysterious idol named Setsuna Yuki! She uses this personality to hide her true interests from her strict parents, and has many otaku-like hobbies, like anime, video games, and of course, idols!"
            }, "3e507"));
            index++;

            // August 31 - Rurino Osawa (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Rurino Osawa",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(8, 31),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "An energetic and cheerful member of Hasunosora's idol club. Rurino is always full of spirit and brings positivity to the group with her lively personality."
            }, "3f533"));
            index++;

            // September 12 - Kotori Minami (µ's)
            idols.Add((new IdolDescription {
                Name = "Kotori Minami",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(9, 12),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Kotori is the outfit designer for µ's and is generally known for her commendable kindness she expresses to everyone she meets. She is childhood friends with Honoka and Umi, and was known as the legendary maid 'Minalinsky' thanks to her selflessness and hospitality she provided to others while she was secretly working as one!"
            }, "4021f"));
            index++;

            // September 19 - Riko Sakurauchi (Aqours)
            idols.Add((new IdolDescription {
                Name = "Riko Sakurauchi",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(9, 19),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Riko initially struggled with competitions, making mistakes due to her anxiety when under high amounts of pressure to perform, but when her friend Chika ropes her into becoming an idol with her, it reignites her passion and helps propel her forward!"
            }, "4050f"));
            index++;

            // September 21 - Ruby Kurosawa (Aqours)
            idols.Add((new IdolDescription {
                Name = "Ruby Kurosawa",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(9, 21),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Ruby is a first-year student who, despite her timid and shy nature, loves school idols, and keeps that secret from her older sister Dia, who also loves them. Her catchphrase, 'Ganbarubi!', can be loosely interpreted as 'Do your Rubesty!'."
            }, "405a1"));
            index++;

            // September 24 - Hime Anyoji (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Hime Anyoji",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(9, 24),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A noble and elegant member of Hasunosora's idol club. Hime carries herself with grace and poise, bringing sophistication to the group."
            }, "4aa8f"));
            index++;

            // September 28 - Sumire Heanna (Liella!)
            idols.Add((new IdolDescription {
                Name = "Sumire Heanna",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(9, 28),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "As a former child actress, Sumire struggles to find work as she gets older, but is still determined to find ways to stand out and get scouted by a talent agency. When she joins Liella! at Yuigaoka High, she is determined to stop being defined by silver medals and finally be recognized for her extensive efforts to be the best that she can be."
            }, "4083c"));
            index++;

            // October 5 - Shioriko Mifune (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Shioriko Mifune",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(10, 5),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Coming from a prestigious and wealthy family, Shioriko is a serious student who genuinely wants the best for everyone in Nijigasaki High, even if her overly-formal communication to others comes off as harsh at times. She takes on leadership roles in the student council and helps many students with her insightful suggestions."
            }, "40ac6"));
            index++;

            // October 20 - Ginko Momose (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Ginko Momose",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(10, 20),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A unique and mysterious member of Hasunosora's idol club. Ginko has an eccentric charm and brings an interesting perspective to the group's activities."
            }, "4b6bc"));
            index++;

            // October 21 - Eli Ayase (µ's)
            idols.Add((new IdolDescription {
                Name = "Eli Ayase",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(10, 21),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A third-year student and student council president at Otonokizaka. Eli is part Russian and a former ballet dancer with a mature, responsible personality who initially opposed µ's before joining."
            }, "410a4"));
            index++;

            // October 29 - Mei Yoneme (Liella!)
            idols.Add((new IdolDescription {
                Name = "Mei Yoneme",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(10, 29),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Mei is a cool and collected girl who enrolled in Yuigaoka's second year, initially just as a fan of Liella! and school idols, until she was personally invited by the club members after they saw how passionate she was about them. She is best friends with Shiki, and is commonly misconstrued with having scary eyes, when she just has poor eyesight and squints all the time."
            }, "413cc"));
            index++;

            // November 1 - Rin Hoshizora (µ's)
            idols.Add((new IdolDescription {
                Name = "Rin Hoshizora",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(11, 1),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "An energetic and athletic first-year student who loves ramen and cats. Rin initially lacked confidence in her femininity but grew to embrace her charm as an idol with her signature catchphrase 'nya'."
            }, "4143a"));
            index++;

            // November 13 - Rina Tennoji (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Rina Tennoji",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(11, 13),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A first-year student who has difficulty expressing emotions. Rina created the 'Rina-chan Board' to show her feelings through drawn faces. She's tech-savvy and produces electronic music."
            }, "419f4"));
            index++;

            // November 17 - Tsuzuri Yugiri (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Tsuzuri Yugiri",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(11, 17),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A traditional and refined member of Hasunosora's idol club. Tsuzuri comes from a prestigious family and brings elegance to the group with her graceful demeanor."
            }, "41af3"));
            index++;

            // November 24 - Ren Hazuki (Liella!)
            idols.Add((new IdolDescription {
                Name = "Ren Hazuki",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(11, 24),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Known for her elegance and perfectionism, Ren is Yuigaoka's student council president. Originally opposing the School Idol club, she joins after being inspired by their dedication to helping her save the school. She is also a Beat Saber player (kinda)!"
            }, "41e30"));
            index++;

            // December 2 - Mia Taylor (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Mia Taylor",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(12, 2),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A young musical prodigy from New York who came to Japan. Mia initially seemed cold but joined the club after finding inspiration. She composes songs and sings with her incredible talent."
            }, "42333"));
            index++;

            idols.Add((new IdolDescription {
                Name = "Leah Kazuno",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(12, 12),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Sarah's younger sister and the other half of Saint Snow. Leah deeply admires and supports her older sister, working tirelessly to match her dedication. She formed a special bond with Ruby Kurosawa through their shared experiences as younger siblings of passionate idol leaders."
            }, "42556"));
            index++;

            // December 16 - Kanata Konoe (Nijigasaki)
            idols.Add((new IdolDescription {
                Name = "Kanata Konoe",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(12, 16),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A third-year student known for her love of sleeping and dreamy demeanor. Kanata works hard at her part-time jobs to support her family and younger sister Haruka with a gentle, relaxed personality."
            }, "426dc"));
            index++;

            // December 20 - Megumi Fujishima (Hasunosora)
            idols.Add((new IdolDescription {
                Name = "Megumi Fujishima",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(12, 20),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "A reliable and mature member of Hasunosora's idol club. Megumi acts as a supportive figure for the group with her caring and responsible nature."
            }, "427b4"));
            index++;

            // December 28 - Tomari Onitsuka (Liella!)
            idols.Add((new IdolDescription {
                Name = "Tomari Onitsuka",
                Bonus = false,
                GloballyAvailable = false,
                Birthday = DayOfYear(12, 28),
                SmallPictureRegular = IdolImage(index, "small"),
                BigPictureRegular = IdolImage(index, "big"),
                SmallPicturePro = IdolImage(index, "small", true),
                BigPicturePro = IdolImage(index, "big", true),
                RewardGif = IdolGif(index),
                Description = "Enrolling in Yuigaoka's third year, Tomari is a reserved and organized girl who generally comes off as stoic and overly-analytical to most people at first, but is genuinely kind and harbors good intentions for those she cares about. She has a natural talent for dancing, and is infatuated with jellyfish."
            }, "428d1"));
            index++;

            return idols;
        }

        #endregion
    }
}
