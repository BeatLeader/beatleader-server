using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

                if (hoursUntil21 == 0)
                {
                    Console.WriteLine("STARTED DailyRefresh");
                    await RefreshPatreon();
                    await RefreshBoosters();
                    await RefreshBanned();
                    //await FetchMapOfTheWeek();
                    await RefreshSongSuggest();

                    hoursUntil21 = 24;

                    Console.WriteLine("DONE DailyRefresh");
                }

                await Task.Delay(TimeSpan.FromHours(hoursUntil21), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
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

        public async Task RefreshBoosters()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
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

        public async Task RefreshBanned() 
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
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

                    if (ban.PlayerId != ban.BannedBy && ban.Duration != 0 && ban.Timeset + ban.Duration < currentTime) {
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

        public async Task FetchMapOfTheWeek() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var currentDate = DateTime.UtcNow;
                var lastUpdateDate = _context.SongsLastUpdateTimes.Where(s => s.Status == SongStatus.MapOfTheWeek).FirstOrDefault()?.Date ?? new DateTime(1970, 1, 1);
                if (currentDate.Subtract(lastUpdateDate).TotalHours > 20)
                {
                    var curated = await SongUtils.GetMapOfTheWeekSongs(lastUpdateDate);
                    var ids = curated.Select(m => m.Id.ToLower()).ToList();
                    var songs = _context.Songs.Where(s => ids.Contains(s.Id.ToLower())).Include(s => s.ExternalStatuses).ToList();
                    foreach (var map in curated)
                    {
                        var song = songs.FirstOrDefault(s => s.Id.ToLower() == map.Id.ToLower());
                        if (song != null && map.ExternalStatuses != null) {
                            if (song.ExternalStatuses == null) {
                                song.ExternalStatuses = new List<ExternalStatus>();
                            }
                            if (song.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.MapOfTheWeek) == null) {
                                foreach (var status in map.ExternalStatuses)
                                {
                                    song.ExternalStatuses.Add(status);
                                }
                            }
                        }
                    }

                    _context.SongsLastUpdateTimes.Add(new SongsLastUpdateTime {
                        Date = currentDate,
                        Status = SongStatus.MapOfTheWeek
                    });
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
    }
}
