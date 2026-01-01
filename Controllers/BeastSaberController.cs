using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Specialized;
using System.Web;
using System.Net.Http.Headers;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers {
    public class BeastSaberController : Controller
    {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;
        IConfiguration _configuration;
        IHttpClientFactory _httpClientFactory;

        private static Dictionary<string, string> OST_MAP = new Dictionary<string, string> {
            { "Danger", "Extras | Teminite, Boom Kitty - Danger" },
        };

        public BeastSaberController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _environment = env;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/beasties/nominations/")]
        [SwaggerOperation(Summary = "Retrieve a list of nominations for particular leaderboard", Description = "Authenticated player Beasties nominations.")]
        [SwaggerResponse(200, "List of nominations retrieved successfully", typeof(ICollection<BeastiesNomination>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(401, "Autorization failed")]
        public async Task<ActionResult<ICollection<BeastiesNomination>>> GetAll([FromQuery] string leaderboardId)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var trimmedId = leaderboardId.Replace("x", "");

            return await _context.BeastiesNominations.Where(n => n.PlayerId == currentID && n.LeaderboardId == trimmedId).ToListAsync();
        }

        [HttpGet("~/beasties/nominations/my/")]
        [SwaggerOperation(Summary = "Retrieve a list of nominations for particular leaderboard", Description = "Authenticated player Beasties nominations.")]
        [SwaggerResponse(200, "List of nominations retrieved successfully", typeof(ICollection<BeastiesNomination>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(401, "Autorization failed")]
        public async Task<ActionResult<ICollection<MapInfoResponseBeasties>>> PlayerNominations()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var nominations = await _context.BeastiesNominations.Where(n => n.PlayerId == currentID && n.Timepost > 1735689600).ToListAsync();
            if (nominations.Count == 0) {
                return NotFound();
            }

            IQueryable<Player>? currentPlayerQuery = currentID != null ? _context
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings) : null;

            Player? currentPlayer = currentPlayerQuery != null ? await currentPlayerQuery.FirstOrDefaultAsync(p => p.Id == currentID) : null;

            var lbIds = nominations.Select(n => n.LeaderboardId).Distinct().ToList();
            var lbSongs = await _context.Leaderboards.AsNoTracking().Where(lb => lbIds.Contains(lb.Id)).Select(lb => new { lb.SongId, Group = lb.LeaderboardGroup != null ? lb.LeaderboardGroup.Leaderboards.Select(l => new { SongId = l.SongId, UploadDate = l.Timestamp }).ToList() : null }).ToListAsync();
            var songIds = lbSongs.Select(s => s.Group?.OrderByDescending(g => g.UploadDate)?.FirstOrDefault()?.SongId ?? s.SongId).Distinct().ToList();


            var songs = await _context
                .Songs
                .AsNoTracking()
                .Where(s => songIds.Contains(s.Id))
                .TagWithCallerS()
                .Select(s => new MapInfoResponseBeasties {
                    Id = s.Id,
                    Hash = s.LowerHash,
                    Name = s.Name,
                    SubName = s.SubName,
                    Author = s.Author,
                    Mapper = s.Mapper,
                    Mappers = s.Mappers != null ? s.Mappers.Select(m => new MapperResponse {
                        Id = m.Id,
                        PlayerId = m.Player != null ? m.Player.Id : null,
                        Name = m.Player != null ? m.Player.Name : m.Name,
                        Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                        Curator = m.Curator,
                        VerifiedMapper = m.VerifiedMapper,
                        Status = m.Status,
                    }).ToList() : null,
                    MapperId = s.MapperId,
                    CollaboratorIds = s.CollaboratorIds,
                    CoverImage = s.CoverImage,
                    FullCoverImage = s.FullCoverImage,
                    DownloadUrl = s.DownloadUrl,
                    Bpm = s.Bpm,
                    Duration = s.Duration,
                    UploadTime = s.UploadTime,
                    Tags = s.Tags,
                    ExternalStatuses = s.ExternalStatuses.Where(es => es.Status == SongStatus.Curated).ToList(),

                    Difficulties = s.Leaderboards
                    .Select(lb => new MapDiffResponseBeasties {
                        Id = lb.Difficulty.Id,
                        Value = lb.Difficulty.Value,
                        Mode = lb.Difficulty.Mode,
                        DifficultyName = lb.Difficulty.DifficultyName,
                        ModeName = lb.Difficulty.ModeName,
                        Status = lb.Difficulty.Status,
                        ModifierValues = lb.Difficulty.ModifierValues,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        NominatedTime = lb.Difficulty.NominatedTime,
                        QualifiedTime = lb.Difficulty.QualifiedTime,
                        RankedTime = lb.Difficulty.RankedTime,
                        LastScoreTime = lb.LastScoreTime,

                        LeaderboardId = lb.Id,
                        Plays = lb.Plays,
                        Attempts = lb.PlayCount,
                        PositiveVotes = lb.PositiveVotes,
                        StarVotes = lb.StarVotes,
                        NegativeVotes = lb.NegativeVotes,

                        Stars = lb.Difficulty.Stars,
                        PredictedAcc = lb.Difficulty.PredictedAcc,
                        PassRating = lb.Difficulty.PassRating,
                        AccRating = lb.Difficulty.AccRating,
                        TechRating = lb.Difficulty.TechRating,
                        Type = lb.Difficulty.Type,

                        SpeedTags = lb.Difficulty.SpeedTags,
                        StyleTags = lb.Difficulty.StyleTags,
                        FeatureTags = lb.Difficulty.FeatureTags,

                        Njs = lb.Difficulty.Njs,
                        Nps = lb.Difficulty.Nps,
                        Notes = lb.Difficulty.Notes,
                        Bombs = lb.Difficulty.Bombs,
                        Walls = lb.Difficulty.Walls,
                        MaxScore = lb.Difficulty.MaxScore,
                        Duration = lb.Difficulty.Duration,

                        Requirements = lb.Difficulty.Requirements,
                        MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID && s.ValidForGeneral && !s.Banned).Select(s => new ScoreResponseWithAcc {
                            Id = s.Id,
                                BaseScore = s.BaseScore,
                                ModifiedScore = s.ModifiedScore,
                                PlayerId = s.PlayerId,
                                Accuracy = s.Accuracy,
                                Pp = s.Pp,
                                FcAccuracy = s.FcAccuracy,
                                FcPp = s.FcPp,
                                BonusPp = s.BonusPp,
                                Rank = s.Rank,
                                Replay = s.Replay,
                                Offsets = s.ReplayOffsets,
                                Modifiers = s.Modifiers,
                                BadCuts = s.BadCuts,
                                MissedNotes = s.MissedNotes,
                                BombCuts = s.BombCuts,
                                WallsHit = s.WallsHit,
                                Pauses = s.Pauses,
                                FullCombo = s.FullCombo,
                                Hmd = s.Hmd,
                                Timeset = s.Timeset,
                                Timepost = s.Timepost,
                                ReplaysWatched = s.ReplayWatchedTotal,
                                LeaderboardId = s.LeaderboardId,
                                Platform = s.Platform,
                                Weight = s.Weight,
                                AccLeft = s.AccLeft,
                                AccRight = s.AccRight,
                                MaxStreak = s.MaxStreak,
                            }).FirstOrDefault(),
                    }).OrderBy(d => d.Mode > 0 ? d.Mode : 2000).ThenBy(d => d.Value).ToList(),
                })
                .ToListAsync();

            if (songs.Count > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var song in songs) {
                    
                    
                    foreach (var diff in song.Difficulties) {
                        if (!showRatings && !diff.Status.WithRating()) {
                            diff.HideRatings();
                        }
                        var diffNominations = nominations.Where(s => s.LeaderboardId == diff.LeaderboardId.Replace("x", "")).ToList();
                        if (diffNominations.Count > 0) {
                            diff.Applicable = true;
                            diff.Nominations = diffNominations.Select(nm => new MapDiffResponseNomination {
                                Timepost = nm.Timepost,
                                Category = nm.Category
                            }).ToList();
                        }
                    }
                    
                }

                foreach (var song in songs) {
                    song.Difficulties = song.Difficulties.OrderByDescending(d => d.Nominations?.OrderByDescending(n => n.Timepost).FirstOrDefault()?.Timepost ?? 0).ToList();
                }

            }

            return songs.OrderByDescending(s => s.Difficulties.OrderByDescending(d => d.Nominations?.FirstOrDefault()?.Timepost ?? 0).FirstOrDefault()?.Nominations?.FirstOrDefault()?.Timepost ?? 0).ToList();
        }

        [HttpGet("~/beasties/nominations/my/finalists/")]
        [SwaggerOperation(Summary = "Retrieve a list of player nominations that became finalists", Description = "Authenticated player Beasties nominations filtered to only finalists.")]
        [SwaggerResponse(200, "List of finalist nominations retrieved successfully", typeof(ICollection<MapInfoResponseBeasties>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(401, "Autorization failed")]
        public async Task<ActionResult<ICollection<MapInfoResponseBeasties>>> PlayerFinalistNominations()
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            // Load finalists data
            var finalistsPath = Path.Combine(_environment.WebRootPath, "beasties-finalists-2025.json");
            if (!System.IO.File.Exists(finalistsPath)) {
                return NotFound();
            }

            var finalistsJson = await System.IO.File.ReadAllTextAsync(finalistsPath);
            var finalistsData = System.Text.Json.JsonSerializer.Deserialize<List<BeastiesFinalistCategory>>(finalistsJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (finalistsData == null) {
                return NotFound();
            }

            // Build lookup of (songId, category) for finalist maps
            var finalistLookup = new HashSet<(string songId, string category)>();
            foreach (var category in finalistsData) {
                if (category.Finalists != null) {
                    foreach (var finalist in category.Finalists) {
                        // Type 0 = maps (BSR ID), Type 3 = OST maps
                        if (finalist.Type == "0" || finalist.Type == "3") {
                            finalistLookup.Add((finalist.Id.Replace("x", ""), category.InternalName));
                        }
                    }
                }
            }

            var nominations = await _context.BeastiesNominations.Where(n => n.PlayerId == currentID && n.Timepost > 1735689600).ToListAsync();
            if (nominations.Count == 0) {
                return NotFound();
            }

            IQueryable<Player>? currentPlayerQuery = currentID != null ? _context
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings) : null;

            Player? currentPlayer = currentPlayerQuery != null ? await currentPlayerQuery.FirstOrDefaultAsync(p => p.Id == currentID) : null;

            var lbIds = nominations.Select(n => n.LeaderboardId).Distinct().ToList();
            var lbSongs = await _context.Leaderboards.AsNoTracking().Where(lb => lbIds.Contains(lb.Id)).Select(lb => new { lb.Id, lb.SongId, Group = lb.LeaderboardGroup != null ? lb.LeaderboardGroup.Leaderboards.Select(l => new { SongId = l.SongId, UploadDate = l.Timestamp }).ToList() : null }).ToListAsync();
            
            // Build leaderboardId -> songId mapping for filtering
            var lbIdToSongId = lbSongs.ToDictionary(x => x.Id.Replace("x", ""), x => x.SongId.Replace("x", ""));

            // Filter nominations to only those that are finalists in the specific category
            nominations = nominations.Where(n => {
                if (!lbIdToSongId.TryGetValue(n.LeaderboardId.Replace("x", ""), out var songId)) {
                    return false;
                }
                return finalistLookup.Contains((songId, n.Category));
            }).ToList();

            if (nominations.Count == 0) {
                return NotFound();
            }

            // Get songIds only for filtered nominations
            var filteredLbIds = nominations.Select(n => n.LeaderboardId).Distinct().ToHashSet();
            var songIds = lbSongs
                .Where(s => filteredLbIds.Contains(s.Id))
                .Select(s => s.Group?.OrderByDescending(g => g.UploadDate)?.FirstOrDefault()?.SongId ?? s.SongId)
                .Distinct()
                .ToList();

            var songs = await _context
                .Songs
                .AsNoTracking()
                .Where(s => songIds.Contains(s.Id))
                .TagWithCallerS()
                .Select(s => new MapInfoResponseBeasties {
                    Id = s.Id,
                    Hash = s.LowerHash,
                    Name = s.Name,
                    SubName = s.SubName,
                    Author = s.Author,
                    Mapper = s.Mapper,
                    Mappers = s.Mappers != null ? s.Mappers.Select(m => new MapperResponse {
                        Id = m.Id,
                        PlayerId = m.Player != null ? m.Player.Id : null,
                        Name = m.Player != null ? m.Player.Name : m.Name,
                        Avatar = m.Player != null ? m.Player.Avatar : m.Avatar,
                        Curator = m.Curator,
                        VerifiedMapper = m.VerifiedMapper,
                        Status = m.Status,
                    }).ToList() : null,
                    MapperId = s.MapperId,
                    CollaboratorIds = s.CollaboratorIds,
                    CoverImage = s.CoverImage,
                    FullCoverImage = s.FullCoverImage,
                    DownloadUrl = s.DownloadUrl,
                    Bpm = s.Bpm,
                    Duration = s.Duration,
                    UploadTime = s.UploadTime,
                    Tags = s.Tags,
                    ExternalStatuses = s.ExternalStatuses.Where(es => es.Status == SongStatus.Curated).ToList(),

                    Difficulties = s.Leaderboards
                    .Select(lb => new MapDiffResponseBeasties {
                        Id = lb.Difficulty.Id,
                        Value = lb.Difficulty.Value,
                        Mode = lb.Difficulty.Mode,
                        DifficultyName = lb.Difficulty.DifficultyName,
                        ModeName = lb.Difficulty.ModeName,
                        Status = lb.Difficulty.Status,
                        ModifierValues = lb.Difficulty.ModifierValues,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        NominatedTime = lb.Difficulty.NominatedTime,
                        QualifiedTime = lb.Difficulty.QualifiedTime,
                        RankedTime = lb.Difficulty.RankedTime,
                        LastScoreTime = lb.LastScoreTime,

                        LeaderboardId = lb.Id,
                        Plays = lb.Plays,
                        Attempts = lb.PlayCount,
                        PositiveVotes = lb.PositiveVotes,
                        StarVotes = lb.StarVotes,
                        NegativeVotes = lb.NegativeVotes,

                        Stars = lb.Difficulty.Stars,
                        PredictedAcc = lb.Difficulty.PredictedAcc,
                        PassRating = lb.Difficulty.PassRating,
                        AccRating = lb.Difficulty.AccRating,
                        TechRating = lb.Difficulty.TechRating,
                        Type = lb.Difficulty.Type,

                        SpeedTags = lb.Difficulty.SpeedTags,
                        StyleTags = lb.Difficulty.StyleTags,
                        FeatureTags = lb.Difficulty.FeatureTags,

                        Njs = lb.Difficulty.Njs,
                        Nps = lb.Difficulty.Nps,
                        Notes = lb.Difficulty.Notes,
                        Bombs = lb.Difficulty.Bombs,
                        Walls = lb.Difficulty.Walls,
                        MaxScore = lb.Difficulty.MaxScore,
                        Duration = lb.Difficulty.Duration,

                        Requirements = lb.Difficulty.Requirements,
                        MyScore = currentID == null ? null : lb.Scores.Where(s => s.PlayerId == currentID && s.ValidForGeneral && !s.Banned).Select(s => new ScoreResponseWithAcc {
                            Id = s.Id,
                                BaseScore = s.BaseScore,
                                ModifiedScore = s.ModifiedScore,
                                PlayerId = s.PlayerId,
                                Accuracy = s.Accuracy,
                                Pp = s.Pp,
                                FcAccuracy = s.FcAccuracy,
                                FcPp = s.FcPp,
                                BonusPp = s.BonusPp,
                                Rank = s.Rank,
                                Replay = s.Replay,
                                Offsets = s.ReplayOffsets,
                                Modifiers = s.Modifiers,
                                BadCuts = s.BadCuts,
                                MissedNotes = s.MissedNotes,
                                BombCuts = s.BombCuts,
                                WallsHit = s.WallsHit,
                                Pauses = s.Pauses,
                                FullCombo = s.FullCombo,
                                Hmd = s.Hmd,
                                Timeset = s.Timeset,
                                Timepost = s.Timepost,
                                ReplaysWatched = s.ReplayWatchedTotal,
                                LeaderboardId = s.LeaderboardId,
                                Platform = s.Platform,
                                Weight = s.Weight,
                                AccLeft = s.AccLeft,
                                AccRight = s.AccRight,
                                MaxStreak = s.MaxStreak,
                            }).FirstOrDefault(),
                    }).OrderBy(d => d.Mode > 0 ? d.Mode : 2000).ThenBy(d => d.Value).ToList(),
                })
                .ToListAsync();

            if (songs.Count > 0) {
                bool showRatings = currentPlayer?.ProfileSettings?.ShowAllRatings ?? false;
                foreach (var song in songs) {
                    foreach (var diff in song.Difficulties) {
                        if (!showRatings && !diff.Status.WithRating()) {
                            diff.HideRatings();
                        }
                        var diffNominations = nominations.Where(s => s.LeaderboardId == diff.LeaderboardId.Replace("x", "")).ToList();
                        if (diffNominations.Count > 0) {
                            diff.Applicable = true;
                            diff.Nominations = diffNominations.Select(nm => new MapDiffResponseNomination {
                                Timepost = nm.Timepost,
                                Category = nm.Category
                            }).ToList();
                        }
                    }
                }

                foreach (var song in songs) {
                    song.Difficulties = song.Difficulties.OrderByDescending(d => d.Nominations?.OrderByDescending(n => n.Timepost).FirstOrDefault()?.Timepost ?? 0).ToList();
                }
            }

            return songs.OrderByDescending(s => s.Difficulties.OrderByDescending(d => d.Nominations?.FirstOrDefault()?.Timepost ?? 0).FirstOrDefault()?.Nominations?.FirstOrDefault()?.Timepost ?? 0).ToList();
        }

        public class BestiesNominationResponse {
            public string Message { get; set; }
        }

        [HttpPost("~/beasties/nominate/")]
        [SwaggerOperation(Summary = "Nominate a map for Besties Awards", Description = "Nominates provided leaderboard map for mapping awards in selected category")]
        [SwaggerResponse(200, "Map was nominated")]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(401, "Autorization failed")]
        public async Task<ActionResult<BestiesNominationResponse>> Nominate([FromQuery] string leaderboardId, [FromQuery] string category)
        {
            string? currentID = HttpContext.CurrentUserID(_context);
            if (currentID == null) {
                return Unauthorized();
            }

            var lb = _context.Leaderboards.AsNoTracking().Include(lb => lb.Difficulty).FirstOrDefault(lb => lb.Id == leaderboardId);
            if (lb == null) {
                return NotFound(new BestiesNominationResponse {
                    Message = "Such leaderboard not found"
                });
            }

            var trimmedId = leaderboardId.Replace("x", "");
            var existingVote = 
                await _context
                .BeastiesNominations
                .Where(n => n.PlayerId == currentID && n.LeaderboardId == trimmedId && n.Category == category)
                .FirstOrDefaultAsync();

            if (existingVote != null) {
                return BadRequest(new BestiesNominationResponse {
                    Message = "You already voted for this map in this category"
                });
            }

            var data = new Dictionary<string, string>();

            NameValueCollection outgoingQueryString = HttpUtility.ParseQueryString("");
            if (lb.Difficulty.Status != DifficultyStatus.OST) {
                data["bsrId"] = lb.SongId.Replace("x", "");
            } else { 
                data["OSTname"] = OST_MAP[lb.SongId];
            }
            data["userId"] = currentID;
            data["charecteristic"] = lb.Difficulty.ModeName;
            data["difficulty"] = lb.Difficulty.DifficultyName;
            data["category"] = category;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.GetValue<string>("SaeraphinxSecret"));
            using HttpContent formContent = new FormUrlEncodedContent(data);
            using HttpResponseMessage webResponse = await client.PostAsync("https://mappingawards.saeraphinx.dev/api/beatleader/submitmap", formContent).ConfigureAwait(false);

            var response = (await webResponse.Content.ReadAsStreamAsync()).ObjectFromStream();

            if (response == null) {
                return BadRequest(new BestiesNominationResponse {
                    Message = "Failed to nominate map"
                });
            }
            if (category == "Gen-FullSpread") {
                var associated = _context.Leaderboards.Where(l => l.SongId == lb.SongId).ToList();
                foreach (var item in associated) {
                    _context.BeastiesNominations.Add(new BeastiesNomination {
                        PlayerId = currentID,
                        Category = category,
                        Timepost = Time.UnixNow(),
                        LeaderboardId = item.Id.Replace("x", "")
                    });
                }
            } else {
                _context.BeastiesNominations.Add(new BeastiesNomination {
                    PlayerId = currentID,
                    Category = category,
                    Timepost = Time.UnixNow(),
                    LeaderboardId = trimmedId
                });
            }
            await _context.SaveChangesAsync();

            if (ExpandantoObject.HasProperty(response, "message")) {
                return Ok(new BestiesNominationResponse { Message = (string)((dynamic)response).message });
            } else {
                return Ok();
            }
        }

        // Helper classes for finalists JSON deserialization
        private class BeastiesFinalistCategory {
            public string InternalName { get; set; } = "";
            public List<BeastiesFinalist>? Finalists { get; set; }
        }

        private class BeastiesFinalist {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
        }
    }
}
