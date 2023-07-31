using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services {
    public class DailyService : BackgroundService {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public DailyService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {

                do {
                    int hourSpan = 24 - DateTime.Now.Hour;
                    int numberOfHours = hourSpan;

                    if (hourSpan == 24) {
                        await RefreshSteamPlayers();
                        await RefreshStats();
                        await RefreshPatreon();
                        await RefreshBoosters();
                        await RefreshBanned();

                        await SetHistories();
                        await SetLastWeek();

                        hourSpan = 24 - DateTime.Now.Hour;
                        numberOfHours = hourSpan;
                    }

                    await Task.Delay(TimeSpan.FromHours(numberOfHours), stoppingToken);
                }
                while (!stoppingToken.IsCancellationRequested);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshSteamPlayers() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                int playerCount = await _context.Players.CountAsync();

                for (int i = 0; i < playerCount; i += 2000) {
                    var players = await _context.Players.OrderByDescending(p => p.Id).Select(p => new {
                        Id = p.Id,
                        Avatar = p.Avatar,
                        Country = p.Country,
                        ExternalProfileUrl = p.ExternalProfileUrl
                    }).Skip(i).Take(2000).ToListAsync();
                    foreach (var p in players) {
                        if (Int64.Parse(p.Id) <= 70000000000000000) { continue; }
                        Player? update = await PlayerUtils.GetPlayerFromSteam(_configuration.GetValue<string>("SteamApi"), p.Id, _configuration.GetValue<string>("SteamKey"));

                        if (update != null) {
                            var player = new Player() { Id = p.Id };
                            bool urlChanged = false, avatarChanged = false, countryChanged = false;

                            if (update.ExternalProfileUrl != p.ExternalProfileUrl) {
                                player.ExternalProfileUrl = update.ExternalProfileUrl;
                                urlChanged = true;
                            }

                            if (p.Avatar.Contains("steamcdn") && p.Avatar != update.Avatar) {
                                player.Avatar = update.Avatar;
                                avatarChanged = true;
                            }

                            if (p.Country == "not set" && update.Country != "not set") {
                                player.Country = update.Country;
                                countryChanged = true;
                            }

                            if (urlChanged || avatarChanged || countryChanged) {
                                _context.Players.Attach(player);
                            }
                            if (urlChanged) _context.Entry(player).Property(x => x.ExternalProfileUrl).IsModified = true;
                            if (avatarChanged) _context.Entry(player).Property(x => x.Avatar).IsModified = true;
                            if (countryChanged) _context.Entry(player).Property(x => x.Country).IsModified = true;
                        }
                    }

                    await _context.BulkSaveChangesAsync();
                }
            }
        }

        public async Task RefreshStats() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var _playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                await _playerController.RefreshPlayersStats();
            }
        }

        public async Task RefreshPatreon() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var _patreonController = scope.ServiceProvider.GetRequiredService<PatreonController>();
                await _patreonController.RefreshPatreon();
            }
        }

        public async Task RefreshBoosters() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var players = _context.Players.Where(p => p.Socials.FirstOrDefault(s => s.Service == "Discord") != null).ToList();
                foreach (var player in players) {
                    var link = _context.DiscordLinks.FirstOrDefault(l => l.Id == player.Id);
                    if (link != null) {
                        ulong ulongId = 0;
                        if (ulong.TryParse(link.DiscordId, out ulongId)) {
                            await PlayerUtils.RefreshBoosterRole(_context, player, ulongId);
                        }
                    }
                }
            }
        }

        public async Task RefreshBanned() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var bannedPlayers = _context.Players.Where(p => p.Banned && !p.Bot).ToList();
                var deletionList = new List<string>();
                var unbanlist = new List<string>();

                var currentTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                var threshold = currentTime - 60 * 60 * 24 * 30 * 6;
                foreach (var player in bannedPlayers) {
                    var ban = _context.Bans.OrderByDescending(b => b.Timeset).Where(b => b.PlayerId == player.Id).FirstOrDefault();
                    if (ban == null) continue;

                    if (ban.PlayerId == ban.BannedBy && ban.Timeset < threshold) {
                        deletionList.Add(ban.PlayerId);
                    }

                    if (ban.PlayerId != ban.BannedBy && ban.Timeset + ban.Duration > currentTime) {
                        unbanlist.Add(ban.PlayerId);
                    }
                }

                var playerController = scope.ServiceProvider.GetRequiredService<PlayerController>();
                foreach (var playerToDelete in deletionList) {
                    await playerController.DeletePlayer(playerToDelete);
                }

                var userController = scope.ServiceProvider.GetRequiredService<CurrentUserController>();
                foreach (var playerToUnban in unbanlist) {
                    await userController.Unban(playerToUnban);
                }
            }
        }

        public async Task SetHistories() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                var lastHistory = _context.PlayerScoreStatsHistory.OrderByDescending(ps => ps.Timestamp).FirstOrDefault();
                if (lastHistory != null && lastHistory.Timestamp > timeset - 60 * 60 * 12) {
                    return;
                }
                var playersCount = _context.Players.Where(p => !p.Banned).Count();
                for (int i = 0; i < playersCount; i += 5000) {
                    var ranked = _context
                        .Players
                        .OrderBy(p => p.Id)
                        .Where(p => !p.Banned || p.Bot)
                        .Include(p => p.ScoreStats)
                        .Skip(i)
                        .Take(5000)
                        .Select(p => new {
                            Pp = p.Pp,
                            ScoreStats = p.ScoreStats,
                            Rank = p.Rank,
                            CountryRank = p.CountryRank,
                            Id = p.Id
                        })
                        .ToList();
                    foreach (var p in ranked) {
                        if (p.ScoreStats == null) continue;
                        _context.PlayerScoreStatsHistory.Add(new PlayerScoreStatsHistory {
                            PlayerId = p.Id,
                            Timestamp = timeset,
                            Rank = p.Rank,
                            Pp = p.Pp,
                            CountryRank = p.CountryRank,

                            TotalScore = p.ScoreStats.TotalScore,
                            TotalUnrankedScore = p.ScoreStats.TotalUnrankedScore,
                            TotalRankedScore = p.ScoreStats.TotalRankedScore,

                            LastScoreTime = p.ScoreStats.LastScoreTime,
                            LastUnrankedScoreTime = p.ScoreStats.LastUnrankedScoreTime,
                            LastRankedScoreTime = p.ScoreStats.LastRankedScoreTime,

                            AverageRankedAccuracy = p.ScoreStats.AverageRankedAccuracy,
                            AverageWeightedRankedAccuracy = p.ScoreStats.AverageWeightedRankedAccuracy,
                            AverageUnrankedAccuracy = p.ScoreStats.AverageUnrankedAccuracy,
                            AverageAccuracy = p.ScoreStats.AverageAccuracy,

                            MedianRankedAccuracy = p.ScoreStats.MedianRankedAccuracy,
                            MedianAccuracy = p.ScoreStats.MedianAccuracy,

                            TopRankedAccuracy = p.ScoreStats.TopRankedAccuracy,
                            TopUnrankedAccuracy = p.ScoreStats.TopUnrankedAccuracy,
                            TopAccuracy = p.ScoreStats.TopAccuracy,

                            TopPp = p.ScoreStats.TopPp,
                            TopBonusPP = p.ScoreStats.TopBonusPP,

                            PeakRank = p.ScoreStats.PeakRank,
                            MaxStreak = p.ScoreStats.MaxStreak,
                            AverageLeftTiming = p.ScoreStats.AverageLeftTiming,
                            AverageRightTiming = p.ScoreStats.AverageRightTiming,

                            RankedPlayCount = p.ScoreStats.RankedPlayCount,
                            UnrankedPlayCount = p.ScoreStats.UnrankedPlayCount,
                            TotalPlayCount = p.ScoreStats.TotalPlayCount,

                            RankedImprovementsCount = p.ScoreStats.RankedImprovementsCount,
                            UnrankedImprovementsCount = p.ScoreStats.UnrankedImprovementsCount,
                            TotalImprovementsCount = p.ScoreStats.TotalImprovementsCount,

                            AverageRankedRank = p.ScoreStats.AverageRankedRank,
                            AverageWeightedRankedRank = p.ScoreStats.AverageWeightedRankedRank,
                            AverageUnrankedRank = p.ScoreStats.AverageUnrankedRank,
                            AverageRank = p.ScoreStats.AverageRank,

                            SSPPlays = p.ScoreStats.SSPPlays,
                            SSPlays = p.ScoreStats.SSPlays,
                            SPPlays = p.ScoreStats.SPPlays,
                            SPlays = p.ScoreStats.SPlays,
                            APlays = p.ScoreStats.APlays,

                            TopPlatform = p.ScoreStats.TopPlatform,
                            TopHMD = p.ScoreStats.TopHMD,

                            ReplaysWatched = p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched,
                            WatchedReplays = p.ScoreStats.WatchedReplays,
                        });
                    }
                    await _context.BulkSaveChangesAsync();
                }

            }
        }

        public async Task SetLastWeek() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                int timesetFrom = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7 - 60 * 60 * 2;
                int timesetTo = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7 + 60 * 60 * 2;

                var ranked = (await _context
                    .PlayerScoreStatsHistory
                    .Where(p => p.Timestamp > timesetFrom && p.Timestamp < timesetTo)
                    .Select(p => new {
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        Pp = p.Pp,
                        Id = p.PlayerId,
                        Timestamp = p.Timestamp,
                    })
                    .ToListAsync())
                    .DistinctBy(p => p.Id)
                    .ToList();
                foreach (var p in ranked) {
                    if (p.Id != null) {
                        var player = new Player() { Id = p.Id, LastWeekRank = p.Rank, LastWeekPp = p.Pp, LastWeekCountryRank = p.CountryRank };
                        _context.Players.Attach(player);
                        _context.Entry(player).Property(x => x.LastWeekRank).IsModified = true;
                        _context.Entry(player).Property(x => x.LastWeekPp).IsModified = true;
                        _context.Entry(player).Property(x => x.LastWeekCountryRank).IsModified = true;
                    }
                }

                await _context.BulkSaveChangesAsync();
            }
        }
    }
}
