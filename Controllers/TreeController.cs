using Microsoft.AspNetCore.Mvc;

using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Controllers;

public class TreeVector {
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class TreeQuaternion {
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
    public float w { get; set; }
}

public class TreePose {
    public TreeVector position { get; set; }
    public TreeQuaternion rotation { get; set; }
}

public class BigTreePose {
    public TreeVector position { get; set; } = new TreeVector { x = 2.7f, y = 0, z = 4f };
    public TreeVector scale { get; set; } = new TreeVector { x = 1, y = 1, z = 1 };
    public TreeQuaternion rotation { get; set; } = new TreeQuaternion { x = 0, y = 0, z = 0, w = 1 };
}

public class Ornament { 
    public int bundleId { get; set; }
    public int state { get; set; }
    public TreePose pose { get; set; }
}

public class Tree {
    public BigTreePose gameTreePose { get; set; } = new BigTreePose();
    public BigTreePose webTreePose { get; set; } = new BigTreePose();

    public Ornament[] ornaments { get; set; } = new Ornament[] { };
}

public class DailyTreeStatus {
    public SongResponse song { get; set; }
    public ScoreResponseWithAcc? score { get; set; }
    public int bundleId { get; set; }
    public int startTime { get; set; }
}

public class BonusOrnament {
    public ScoreResponseWithAcc? score { get; set; }
    public int bundleId { get; set; }
    public string description { get; set; }
}

public class GeneralStatus {
    
    public DailyTreeStatus? today { get; set; }
    public DailyTreeStatus[] previousDays { get; set; }
    public BonusOrnament[] bonusOrnaments { get; set; }
}

public class TreePlayerDay {
    public int Day { get; set; }
    public MapOfTheDayPoints? Points { get; set; }
    public int[] Diffs { get; set; }
}

public class TreePlayer {
    public string Id { get; set; }
    public int Rank { get; set; }
    public string Name { get; set; }
    public string Avatar { get; set; }
    public string? AvatarBorder { get; set; }
    public float? Hue { get; set; }
    public int Score { get; set; }

    public List<TreePlayerDay> Days { get; set; } = new List<TreePlayerDay> { };
}

[ApiExplorerSettings(IgnoreApi = true)]
public class TreeController : Controller {
    private readonly IConfiguration _configuration;
    private readonly AppContext _context;
    private readonly StorageContext _storageContext;

    private readonly IAmazonS3 _s3Client;

    private readonly IServerTiming _serverTiming;
    IWebHostEnvironment _environment;

    public TreeController(
        AppContext context,
        StorageContext storageContext,
        IWebHostEnvironment env,
        IServerTiming serverTiming,
        IConfiguration configuration) {
        _context = context;
        _storageContext = storageContext;
        _environment = env;

        _serverTiming = serverTiming;
        _configuration = configuration;
        _s3Client = configuration.GetS3Client();
    }

    private async Task SaveTree(Tree tree, string filename) {
        await _s3Client.UploadStream(filename, S3Container.assets, new BinaryData(JsonConvert.SerializeObject(tree)).ToStream());
    }

    private string[] mapperIds = new string[] { "76561198119612390", "76561198826449821" };

    private List<BonusOrnament> BonusOrnamentsFor(AppContext dbContext, string currentId, string roles) {
        var bonus = new List<BonusOrnament>();

        var score = dbContext.Scores.Where(s => s.PlayerId == currentId && !s.Modifiers.Contains("NF") && s.Leaderboard.SongId == "42939").FirstOrDefault();
        if (score != null) {
             bonus.Add(new BonusOrnament {
                bundleId = 12,
                description = "Bonus Carey!"
            });
        }

        bonus.Add(new BonusOrnament {
            bundleId = 20,
            description = "We are playing Beat Saber!"
        });
        bonus.Add(new BonusOrnament {
            bundleId = 21,
            description = "We are playing Beat Saber!"
        });

        if (Player.RoleIsAnyTeam(roles) || mapperIds.Contains(currentId) || roles.Contains("sponsor")) {
            bonus.Add(new BonusOrnament {
                bundleId = 19,
                description = "Thank you for being our Sponsor!!"
            });
        }

        if (Player.RoleIsAnyTeam(roles) || mapperIds.Contains(currentId) || roles.Contains("sponsor") || roles.Contains("supporter")) {
            bonus.Add(new BonusOrnament {
                bundleId = 18,
                description = "Thank you for supporting us!"
            });
        }

        if (Player.RoleIsAnyTeam(roles) || mapperIds.Contains(currentId) || roles.Contains("sponsor") || roles.Contains("supporter") || roles.Contains("tipper")) {
            bonus.Add(new BonusOrnament {
                bundleId = 17,
                description = "Thank you for supporting us!"
            });
        }

        if (Player.RoleIsAnyTeam(roles) || mapperIds.Contains(currentId) || roles.Contains("booster")) {
            bonus.Add(new BonusOrnament {
                bundleId = 16,
                description = "Thank you for Boosting our server!"
            });
        }

        bonus.Add(new BonusOrnament {
            bundleId = 26,
            description = "Top 1 clan - LUCK!"
        });
        bonus.Add(new BonusOrnament {
            bundleId = 27,
            description = "Top 2 clan - CRAB!"
        });
        bonus.Add(new BonusOrnament {
            bundleId = 23,
            description = "Top 3 clan - SABA!"
        });
        bonus.Add(new BonusOrnament {
            bundleId = 28,
            description = "Top 4 clan - GENX!"
        });
        bonus.Add(new BonusOrnament {
            bundleId = 29,
            description = "Top 5 clan - THUP!"
        });

        bonus.Add(new BonusOrnament {
            bundleId = 25,
            description = "ACC Seal!"
        });

        return bonus;
    }

    [HttpGet("~/projecttree/status")]
    public async Task<ActionResult> GetProjectStatus() {
        var currentId = HttpContext.CurrentUserID(_context);

        var now = Time.UnixNow();
        var previousDays = new List<DailyTreeStatus>();
        var result = new GeneralStatus {
        };

        var maps = await _context.TreeMaps.Where(m => m.Timestart < now).OrderBy(m => m.Timestart).Take(13).ToListAsync();

        for (int i = 0; i < maps.Count; i++) {
            var map = maps[i];

            var song = await _context.Songs.Where(s => s.Id == map.SongId).Select(s => new SongResponse {
                Id = s.Id,
                Hash = s.LowerHash,
                Name = s.Name,
                SubName = s.SubName,
                Author = s.Author,
                Mapper = s.Mapper,
                MapperId  = s.MapperId,
                CoverImage   = s.CoverImage,
                FullCoverImage = s.FullCoverImage,
                DownloadUrl = s.DownloadUrl,
                Bpm = s.Bpm,
                Duration = s.Duration,
                UploadTime = s.UploadTime,
                Difficulties = s.Difficulties
            }).FirstOrDefaultAsync();
            if (song == null) continue;

            var score = currentId == null ? null : (await _context.Scores.Where(s => s.PlayerId == currentId && !s.Modifiers.Contains("NF") && s.Leaderboard.SongId == map.SongId).Select(s => new ScoreResponseWithAcc {
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
                Player = new PlayerResponse
                {
                    Id = s.Player.Id,
                    Name = s.Player.Name,
                    Alias = s.Player.Alias,
                    Platform = s.Player.Platform,
                    Avatar = s.Player.Avatar,
                    Country = s.Player.Country,

                    Pp = s.Player.Pp,
                    Rank = s.Player.Rank,
                    CountryRank = s.Player.CountryRank,
                    Role = s.Player.Role,
                    Socials = s.Player.Socials,
                    ProfileSettings = s.Player.ProfileSettings,
                    Clans = s.Player.Clans != null ? s.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color }) : null
                },
            }).FirstOrDefaultAsync());

            var treeStatus = new DailyTreeStatus {
                song = song,
                score = score,
                bundleId = map.BundleId,
                startTime = i == 12 ? (int)DateTime.Now.Date.Subtract(new DateTime(1970, 1, 1)).TotalSeconds : map.Timestart,
            };
            if (i == 12) {
                result.today = new DailyTreeStatus {
                    song = song,
                    score = score,
                    bundleId = map.BundleId,
                    startTime = (int)DateTime.Now.Date.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                };
            }

            previousDays.Add(new DailyTreeStatus {
                song = song,
                score = score,
                bundleId = map.BundleId,
                startTime = map.Timestart,
            });
        }

        result.previousDays = previousDays.ToArray();

        if (currentId != null) {
            string roles = (await _context.Players.Where(p => p.Id == currentId).Select(p => p.Role).FirstOrDefaultAsync()) ?? "";
            var bonus = await _context.PlayerTreeOrnaments.Where(to => to.PlayerId == currentId).Select(to => new BonusOrnament {
                bundleId = to.Ornament.BundleId,
                description = to.Ornament.Description,
                score = to.Score == null ? null : new ScoreResponseWithAcc {
                    Id = to.Score.Id,
                    BaseScore = to.Score.BaseScore,
                    ModifiedScore = to.Score.ModifiedScore,
                    PlayerId = to.Score.PlayerId,
                    Accuracy = to.Score.Accuracy,
                    Pp = to.Score.Pp,
                    FcAccuracy = to.Score.FcAccuracy,
                    FcPp = to.Score.FcPp,
                    BonusPp = to.Score.BonusPp,
                    Rank = to.Score.Rank,
                    Replay = to.Score.Replay,
                    Offsets = to.Score.ReplayOffsets,
                    Modifiers = to.Score.Modifiers,
                    BadCuts = to.Score.BadCuts,
                    MissedNotes = to.Score.MissedNotes,
                    BombCuts = to.Score.BombCuts,
                    WallsHit = to.Score.WallsHit,
                    Pauses = to.Score.Pauses,
                    FullCombo = to.Score.FullCombo,
                    Hmd = to.Score.Hmd,
                    Timeset = to.Score.Timeset,
                    Timepost = to.Score.Timepost,
                    ReplaysWatched = to.Score.ReplayWatchedTotal,
                    LeaderboardId = to.Score.LeaderboardId,
                    Platform = to.Score.Platform,
                    Weight = to.Score.Weight,
                    AccLeft = to.Score.AccLeft,
                    AccRight = to.Score.AccRight,
                    MaxStreak = to.Score.MaxStreak,
                    Player = new PlayerResponse
                    {
                        Id = to.Score.Player.Id,
                        Name = to.Score.Player.Name,
                        Alias = to.Score.Player.Alias,
                        Platform = to.Score.Player.Platform,
                        Avatar = to.Score.Player.Avatar,
                        Country = to.Score.Player.Country,

                        Pp = to.Score.Player.Pp,
                        Rank = to.Score.Player.Rank,
                        CountryRank = to.Score.Player.CountryRank,
                        Role = to.Score.Player.Role,
                        Socials = to.Score.Player.Socials,
                        ProfileSettings = to.Score.Player.ProfileSettings,
                        Clans = to.Score.Player.Clans != null ? to.Score.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color }) : null
                    },
                }
            }).ToListAsync();
            bonus.AddRange(BonusOrnamentsFor(_context, currentId, roles));

            result.bonusOrnaments = bonus.ToArray();
        } else {
            result.bonusOrnaments = new BonusOrnament[] { };
        }

        return Ok(result);
    }

    [HttpGet("~/projecttree/players")]
    public async Task<ActionResult> GetPlayers() {
        var result = await _context.EventPlayer.Where(ep => ep.EventRankingId == 62).Select(p => new TreePlayer {
            Id = p.PlayerId,
            Rank = p.Rank,
            Name = p.PlayerName,
            Avatar = p.Player.Avatar,
            AvatarBorder = p.Player.ProfileSettings != null ? p.Player.ProfileSettings.EffectName : null,
            Hue = p.Player.ProfileSettings != null ? p.Player.ProfileSettings.Hue : null,
            Score = (int)p.Pp
        }).ToArrayAsync();

        var champions = await _context.TreeChampions.ToListAsync();
        foreach (var item in result) {
            item.Days.AddRange(champions.Where(c => c.PlayerId == item.Id).GroupBy(c => c.Day).Select(c => new TreePlayerDay {
                Day = c.Key,
                Diffs = c.Select(d => d.Diffs).ToArray()
            }));
        }

        return Ok(result.OrderBy(c => c.Rank));
    }

    [NonAction]
    private string PlayerTreesFilename(string playerId) {
        return $"projectree-config-player-{playerId}.json";
    }

    [HttpGet("~/projecttree")]
    public async Task<ActionResult> GetTree([FromQuery] string? playerId = null) {
        var currentId = playerId ?? HttpContext.CurrentUserID(_context);
        if (string.IsNullOrEmpty(currentId)) return Unauthorized();

        var filename = PlayerTreesFilename(currentId);
        var stream = await _s3Client.DownloadAsset(filename);

        if (stream != null) {
            Response.Headers.Add("Content-Type", "application/json");
            return Ok(stream);
        }

        var tree = new Tree();
        await SaveTree(tree, filename);

        return Ok(tree);
    }

    [HttpGet("~/projecttree/{playerId}")]
    public async Task<ActionResult> GetTreePlayer(string playerId) {

        var filename = PlayerTreesFilename(playerId);
        var stream = await _s3Client.DownloadAsset(filename);

        if (stream != null) {
            Response.Headers.Add("Content-Type", "application/json");
            return Ok(stream);
        } else {
            return NotFound();
        }
    }

    [HttpPost("~/projecttree/game")]
    public async Task<ActionResult> UpdateGameTreePose([FromBody] BigTreePose pose) {
        var currentId = HttpContext.CurrentUserID(_context);
        if (string.IsNullOrEmpty(currentId)) return Unauthorized();

        var filename = PlayerTreesFilename(currentId);
        var stream = await _s3Client.DownloadAsset(filename);

        var tree = stream?.ObjectFromStream<Tree>() ?? new Tree();
        tree.gameTreePose = pose;
        await SaveTree(tree, filename);
        return Ok();
    }

    [HttpPost("~/projecttree/ornaments")]
    public async Task<ActionResult> UpdateGameTreePose([FromBody] Ornament[] ornaments) {
        var currentId = HttpContext.CurrentUserID(_context);
        if (string.IsNullOrEmpty(currentId)) return Unauthorized();

        var filename = PlayerTreesFilename(currentId);
        var stream = await _s3Client.DownloadAsset(filename);
        string roles = (await _context.Players.Where(p => p.Id == currentId).Select(p => p.Role).FirstOrDefaultAsync()) ?? "";

        var tree = stream?.ObjectFromStream<Tree>() ?? new Tree();

        var maps = await _context.TreeMaps.ToListAsync();
        var songIds = maps.Select(m => m.SongId).ToList();
        var scores = await _context.Scores.Where(s => s.PlayerId == currentId && !s.Modifiers.Contains("NF") && songIds.Contains(s.Leaderboard.SongId)).Select(s => s.Leaderboard.SongId).ToListAsync();
        var available = await _context.PlayerTreeOrnaments.Where(o => o.PlayerId == currentId).Select(o => o.Ornament.BundleId).ToListAsync();
        var bonus = BonusOrnamentsFor(_context, currentId, roles);

        foreach (var bonusOr in bonus) {
            available.Add(bonusOr.bundleId);
        }

        foreach (var score in scores) {
            var map = maps.FirstOrDefault(m => m.SongId == score);
            if (map != null) {
                available.Add(map.BundleId);
            }
        }

        tree.ornaments = ornaments.Where(o => available.Contains(o.bundleId) && Math.Abs(o.pose.position.x) < 2 && Math.Abs(o.pose.position.y) < 6 && Math.Abs(o.pose.position.z) < 2).Take(200).ToArray();

        await SaveTree(tree, filename);
        return Ok();
    }

    [HttpPost("~/projecttree/web")]
    public async Task<ActionResult> UpdateWebTreePose([FromBody] BigTreePose pose) {
        var currentId = HttpContext.CurrentUserID(_context);
        if (string.IsNullOrEmpty(currentId)) return Unauthorized();

        var filename = PlayerTreesFilename(currentId);
        var stream = await _s3Client.DownloadAsset(filename);

        var tree = stream?.ObjectFromStream<Tree>() ?? new Tree();

        tree.webTreePose = pose;
        await SaveTree(tree, filename);
        return Ok();
    }

    [HttpPost("~/admin/projecttree/web")]
    public async Task<ActionResult> AddMap([FromQuery] string songId, [FromQuery] int bundleId, [FromQuery] int timeset) {

        string currentID = HttpContext.CurrentUserID(_context);
        var currentPlayer = await _context.Players.FindAsync(currentID);

        if (currentPlayer == null || !currentPlayer.Role.Contains("admin")) {
            return Unauthorized();
        }

        _context.TreeMaps.Add(new TreeMap {
            SongId = songId,
            BundleId = bundleId,
            Timestart = timeset
        });
        _context.SaveChanges();

        return Ok();
    }

    //[HttpPost("~/projecttree/ornament")]
    //public async Task<ActionResult> AddOrnament([FromBody] Ornament ornament, [FromQuery] string? playerId = null) {
    //    var currentId = playerId ?? HttpContext.CurrentUserID(_context);
    //    if (string.IsNullOrEmpty(currentId)) return Unauthorized();

    //    var tree = _trees.GetOrAdd(currentId, new Tree());

    //    var ornamentsList = tree.ornaments.ToList();
    //    ornamentsList.Add(ornament);
    //    tree.ornaments = ornamentsList.ToArray();
        
    //    SaveTrees();
    //    return Ok(tree.ornaments);
    //}

    //[HttpPut("~/projecttree/ornament/{index}")]
    //public async Task<ActionResult> UpdateOrnament(int index, [FromBody] Ornament ornament, [FromQuery] string? playerId = null) {
    //    var currentId = playerId ?? HttpContext.CurrentUserID(_context);
    //    if (string.IsNullOrEmpty(currentId)) return Unauthorized();

    //    if (!_trees.TryGetValue(currentId, out Tree tree)) {
    //        return NotFound();
    //    }

    //    if (index < 0 || index >= tree.ornaments.Length) {
    //        return BadRequest("Invalid ornament index");
    //    }

    //    tree.ornaments[index] = ornament;
    //    SaveTrees();
    //    return Ok(tree.ornaments);
    //}
}
