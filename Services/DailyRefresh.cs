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

                if (hoursUntil21 == 0)
                {
                    Console.WriteLine("SERVICE-STARTED DailyRefresh");

                    try {
                        await RefreshPatreon();
                        await RefreshBoosters();
                        await RefreshBanned();
                        await FetchMapOfTheWeek();
                        await RefreshSongSuggest();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION DailyRefresh {e}");
                    }

                    hoursUntil21 = 24;

                    Console.WriteLine("SERVICE-DONE DailyRefresh");
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

                var existingMOTWs = _context.Songs.Where(s => 
                    s.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.MapOfTheWeek) != null)
                    .Include(es => es.ExternalStatuses)
                    .ToList();

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.beatsaver.com/playlists/id/7483/download/beatsaver-7483.bplist");
                dynamic? playlist = await request.DynamicResponse();

                Song? lastMotw = null;
                var hashes = new List<string>();

                foreach (var song in playlist.songs)
                {
                    string hash = song.hash.ToLower();
                    hashes.Add(hash);

                    var existingMOTW = existingMOTWs.FirstOrDefault(s => s.Hash.ToLower() == hash);
                    if (existingMOTW == null) {
                        var newMOTW = _context.Songs.Where(s => s.Hash.ToLower() == hash).Include(s => s.ExternalStatuses).FirstOrDefault();
                        if (newMOTW != null) {
                            int timeset = lastMotw?.ExternalStatuses?.First(s => s.Status == SongStatus.MapOfTheWeek).Timeset ?? 1620421200;
                            if (newMOTW.ExternalStatuses == null) { 
                                newMOTW.ExternalStatuses = new List<ExternalStatus>();
                            }
                            newMOTW.ExternalStatuses.Add(new ExternalStatus {
                                Status = SongStatus.MapOfTheWeek,
                                Timeset = timeset + 60 * 60 * 24 * 7,
                                Link = "https://beatsaver.com/playlists/7483"
                            });
                            existingMOTW = newMOTW;
                        }
                    }
                    lastMotw = existingMOTW;
                }

                foreach (var existingMOTW in existingMOTWs)
                {
                    if (hashes.Contains(existingMOTW.Hash.ToLower())) continue;

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
    }
}
