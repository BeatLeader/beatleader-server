using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Controllers;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace BeatLeader_Server.Services
{
    public class DailyRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public DailyRefresh(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                int hoursUntil21 = (21 - (int)DateTime.Now.Hour + 24) % 24;

                if (hoursUntil21 == 0) {
                    Console.WriteLine("SERVICE-STARTED DailyRefresh");

                    try {
                        await CleanSearches();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }

                    try {
                        await FetchMapOfTheWeek();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }
                    try {
                        await RefreshSongSuggest();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }
                    try {
                        await RefreshPatreon();
                        await RefreshBoosters();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }
                    try {
                        await RefreshBanned();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }

                    try {
                        await RefreshSteamPlayers();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }

                    try {
                        await RefreshPlayerStats();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }

                    hoursUntil21 = (21 - (int)DateTime.Now.Hour + 24) % 24;

                    Console.WriteLine("SERVICE-DONE DailyRefresh");
                }

                await Task.Delay(TimeSpan.FromHours(hoursUntil21), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task CleanSearches()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                _context.SongSearches.BulkDelete(_context.SongSearches);
                _context.PlayerSearches.BulkDelete(_context.PlayerSearches);

                await _context.Database.ExecuteSqlRawAsync(
                    """
                    DBCC CHECKIDENT ('dbo.SongSearches', RESEED, 0);
                    DBCC CHECKIDENT ('dbo.PlayerSearches', RESEED, 0);
                    """);
            }
        }

        public async Task RefreshPatreon()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var _patreonController = scope.ServiceProvider.GetRequiredService<PatreonController>();
                await _patreonController.RefreshPatreon();
            }
        }

        public class PlayerSelect {
            public string Id { get; set; }
            public string Avatar { get; set; }
            public string Country { get; set; }
            public string ExternalProfileUrl { get; set; }
            public int? ScoreStatsId { get; set; }
            public int Rank { get; set; }
        }

        public static List<string> FakedStats = new List<string> { "76561198448231046", "76561198417741030", "76561198124313944", "76561198277807887", "76561198254561713", "76561198260711461", "76561198799965092", "76561199182738448" };

        public async Task<Player?> PlayerUpdates(PlayerSelect p) {
            Player? update = await PlayerUtils.GetPlayerFromSteam(_configuration.GetValue<string>("SteamApi"), p.Id, _configuration.GetValue<string>("SteamKey"));

            if (update != null)
            {
                var player = new Player() { 
                    Id = p.Id,
                    ExternalProfileUrl = p.ExternalProfileUrl,
                    Avatar = p.Avatar,
                    Country = p.Country
                };
                bool urlChanged = false, avatarChanged = false, countryChanged = false;

                if (update.ExternalProfileUrl != p.ExternalProfileUrl) {
                    player.ExternalProfileUrl = update.ExternalProfileUrl;
                    urlChanged = true;
                }

                if (p.Avatar.Contains("steamcdn") && p.Avatar != update.Avatar)
                {
                    player.Avatar = update.Avatar;
                    avatarChanged = true;
                }

                if (p.Country == "not set" && update.Country != "not set")
                {
                    player.Country = update.Country;
                    countryChanged = true;
                }

                if (urlChanged || avatarChanged || countryChanged) {
                    return player;
                }
            }

            return null;
        }

        public async Task<PlayerScoreStats?> PlayerStatsUpdates(PlayerSelect p) {
            if (p.ScoreStatsId != null) {
                var playtime = await PlayerUtils.GetPlaytimeFromSteam(_configuration.GetValue<string>("SteamApi"), p.Id, _configuration.GetValue<string>("SteamKey"));
                if (playtime != null) {
                    return new PlayerScoreStats {
                        Id = (int)p.ScoreStatsId,
                        SteamPlaytime2Weeks = (int)(playtime?.Item1 ?? 0L),
                        SteamPlaytimeForever = (int)(playtime?.Item2 ?? 0L)
                    };
                }
            }

            return null;
        }

        public async Task RefreshSteamPlayers()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                int playerCount = await _context.Players.Where(p => p.Rank != 0).CountAsync();

                for (int i = 0; i < playerCount; i += 100)
                {
                    var players = (await _context.Players
                        .Where(p => p.Rank != 0)
                        .OrderBy(p => p.Rank)
                        .Skip(i)
                        .Take(100)
                        .Select(p => new PlayerSelect { 
                            Id = p.Id,
                            Avatar = p.Avatar,
                            Country = p.Country,
                            ExternalProfileUrl = p.ExternalProfileUrl,
                            ScoreStatsId = p.ScoreStatsId,
                            Rank = p.Rank
                        })
                        .ToListAsync())
                        .Where(p => Int64.Parse(p.Id) >= 70000000000000000)
                        .ToList();

                    var playerUpdates = await Task.WhenAll(players.Select(PlayerUpdates).ToList());
                    var statsUpdates = await Task.WhenAll(players.Where(p => !FakedStats.Contains(p.Id)).Select(PlayerStatsUpdates).ToList());

                    await _context.BulkUpdateAsync(playerUpdates.Where(p => p != null).ToList(), options => options.ColumnInputExpression = c => new { c.ExternalProfileUrl, c.Avatar, c.Country });
                    await _context.BulkUpdateAsync(statsUpdates.Where(p => p != null).ToList(), options => options.ColumnInputExpression = c => new { c.SteamPlaytime2Weeks, c.SteamPlaytimeForever });
                    await _context.BulkSaveChangesAsync();

                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }

        public async Task RefreshBoosters()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var players = await _context.Players.Where(p => p.Socials.FirstOrDefault(s => s.Service == "Discord") != null).ToListAsync();
                foreach (var player in players) {
                    var link = await _context.DiscordLinks.FirstOrDefaultAsync(l => l.Id == player.Id);
                    if (link != null) {
                        ulong ulongId = 0;
                        if (ulong.TryParse(link.DiscordId, out ulongId)) {
                            await PlayerUtils.RefreshBoosterRole(_context, player, ulongId);
                        }
                    }
                }
            }
        }

        public async Task RefreshBanned() 
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _storageContext = scope.ServiceProvider.GetRequiredService<StorageContext>();

                var bannedPlayers = await _context.Players.Where(p => p.Banned && !p.Bot).ToListAsync();
                var deletionList = new List<string>();
                var unbanlist = new List<string>();

                var currentTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                var threshold = currentTime - 60 * 60 * 24 * 30 * 6;
                foreach (var player in bannedPlayers) {
                    var ban = await _context.Bans.OrderByDescending(b => b.Timeset).Where(b => b.PlayerId == player.Id).FirstOrDefaultAsync();
                    if (ban == null) continue;

                    if (ban.PlayerId == ban.BannedBy && ban.Timeset < threshold) {
                        deletionList.Add(ban.PlayerId);
                    }

                    if (ban.PlayerId != ban.BannedBy && ban.Duration != 0 && ban.Timeset + ban.Duration < currentTime) {
                        unbanlist.Add(ban.PlayerId);
                    }
                }

                foreach (var playerToDelete in deletionList) {
                    await PlayerControllerHelper.DeletePlayer(_context, _storageContext, _configuration.GetS3Client(), playerToDelete);
                }

                var userController = scope.ServiceProvider.GetRequiredService<CurrentUserController>();
                foreach (var playerToUnban in unbanlist) {
                    await userController.Unban(playerToUnban);
                }
            }
        }

        public class GitHubFile {
            public string name { get; set; }
            public string download_url { get; set; }
        }

        public async Task FetchMapOfTheWeek() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var existingMOTWs = await _context.Songs.Where(s => 
                    s.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.MapOfTheWeek) != null)
                    .Include(es => es.ExternalStatuses)
                    .ToListAsync();

                // Get repository contents using GitHub API
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/beastsaber/bsaber/contents/src/collections/map-of-the-week");
                request.UserAgent = "BeatLeader";
                var contents = await request.DynamicResponse<List<GitHubFile>>();
                if (contents == null) return;

                var processedHashes = new List<string>();

                // Process all .md files
                foreach (var file in contents) {
                    if (!file.name.EndsWith(".md")) continue;

                    // Get file content
                    request = (HttpWebRequest)WebRequest.Create(file.download_url);
                    request.UserAgent = "BeatLeader";
                    var motwData = await request.GetResponseAsync();
                    var content = await new StreamReader(motwData.GetResponseStream()).ReadToEndAsync();

                    // Parse YAML frontmatter
                    var mapId = content.Split("mapId:")[1].Split('\n')[0].Trim().Replace("\"", "").Replace("'", "");
                    var startDateStr = content.Split("startDate:")[1].Split('\n')[0].Trim().Replace("\"", "").Replace("'", "");
                    var startDate = DateTime.Parse(startDateStr);
                    var timeset = (int)startDate.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                    // Add MOTW status to map
                    var motwMaps = await _context.Songs.Where(s => s.Id == mapId || s.Id.StartsWith(mapId + "x")).Include(s => s.ExternalStatuses).ToListAsync();

                    foreach (var motwMap in motwMaps) {
                        processedHashes.Add(motwMap.Hash.ToLower());
                        if (motwMap.ExternalStatuses == null) {
                            motwMap.ExternalStatuses = new List<ExternalStatus>();
                        }
                        
                        var existingStatus = motwMap.ExternalStatuses.FirstOrDefault(s => s.Status == SongStatus.MapOfTheWeek);
                        if (existingStatus == null) {
                            motwMap.ExternalStatuses.Add(new ExternalStatus {
                                Status = SongStatus.MapOfTheWeek,
                                Timeset = timeset,
                                Link = "https://bsaber.com/maps-of-the-week/1"
                            });
                            motwMap.Status |= SongStatus.MapOfTheWeek;
                        }
                    }
                }

                // Remove MOTW status from maps not in the current list
                foreach (var existingMOTW in existingMOTWs) {
                    if (processedHashes.Contains(existingMOTW.Hash.ToLower())) continue;

                    existingMOTW.Status &= ~SongStatus.MapOfTheWeek;
                    existingMOTW.ExternalStatuses.Remove(existingMOTW.ExternalStatuses.First(s => s.Status == SongStatus.MapOfTheWeek));
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task RefreshSongSuggest() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var songSuggestController = scope.ServiceProvider.GetRequiredService<SongSuggestController>();
                await songSuggestController.RefreshSongSuggest();
            }
        }

        public async Task RefreshPlayerStats() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                await _playerController.RefreshPlayersStatsSlowly();

                var _playerContextController = scope.ServiceProvider.GetRequiredService<PlayerContextRefreshController>();
                await _playerContextController.RefreshPlayersStatsAllContextsSlowly();
            }
        }
    }
}
