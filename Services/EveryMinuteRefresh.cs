using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Prometheus.Client;
using System.Net;
using static BeatLeader_Server.ControllerHelpers.LeaderboardControllerHelper;

namespace BeatLeader_Server.Services
{
    public class EveryMinuteRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        public EveryMinuteRefresh(IServiceScopeFactory serviceScopeFactory, IMetricFactory metricFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                try {
                    await RefreshDailyMap();
                    //await RefreshTreeMaps();
                } catch (Exception e) {
                    Console.WriteLine($"EXCEPTION MinuteRefresh {e}");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshDailyMap() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var time = Time.UnixNow();
                var _s3Client = _configuration.GetS3Client();

                var maps = await _context.ScheduledEventMaps.Where(m => m.StartDate <= time && m.EndDate > time).ToListAsync();
                foreach (var map in maps) {
                    var eventDescription = await _context
                        .EventRankings
                        .Where(e => e.Id == map.EventId)
                        .Include(e => e.Leaderboards)
                        .Include(e => e.MapOfTheDays)
                        .ThenInclude(m => m.Leaderboards)
                        .ThenInclude(l => l.Song)
                        .Include(e => e.MapOfTheDays)
                        .ThenInclude(m => m.Leaderboards)
                        .ThenInclude(l => l.Difficulty)
                        .Include(l => l.FeaturedPlaylist)
                        .FirstOrDefaultAsync();
                    if (eventDescription == null || eventDescription.EventType != EventRankingType.MapOfTheDay) continue;

                    var mapOfTheDay = eventDescription.MapOfTheDays.Where(s => s.SongId == map.SongId).FirstOrDefault();
                    if (mapOfTheDay != null) continue;

                    var leaderboard = await _context
                        .Leaderboards
                        .Where(l => l.SongId == map.SongId && l.Difficulty.Mode == 1)
                        .OrderByDescending(l => l.Difficulty.Value)
                        .Include(lb => lb.Song)
                        .Include(lb => lb.Difficulty)
                        .Include(lb => lb.FeaturedPlaylists)
                        .FirstOrDefaultAsync();
                    if (leaderboard == null) continue;

                    mapOfTheDay = new MapOfTheDay {
                        Timestart = map.StartDate,
                        Timeend = map.EndDate,
                        Song = leaderboard.Song,
                        Leaderboards = new List<Leaderboard> {
                            leaderboard
                        }
                    };

                    if (map.VideoUrl != null) {
                        leaderboard.Song.VideoPreviewUrl = map.VideoUrl;
                    }

                    if (leaderboard.Difficulty.Stars == null) {
                        await RatingUtils.UpdateFromExMachina(leaderboard, null);
                    }
                    if (leaderboard.Difficulty.Stars != null) {

                        if (leaderboard.Difficulty.Status == DifficultyStatus.unranked) {
                            leaderboard.Difficulty.Status = DifficultyStatus.inevent;
                            await _context.SaveChangesAsync();

                            await ScoreRefreshControllerHelper.BulkRefreshScores(_context, leaderboard.Id);
                        }
                    } else { continue; }

                    eventDescription.Leaderboards.Add(leaderboard);

                    if (eventDescription.FeaturedPlaylist != null) {
                        if (leaderboard.FeaturedPlaylists == null)
                        {
                            leaderboard.FeaturedPlaylists = new List<FeaturedPlaylist>();
                        }

                        leaderboard.FeaturedPlaylists.Add(eventDescription.FeaturedPlaylist);
                    }

                    eventDescription.MapOfTheDays.Add(mapOfTheDay);
                    _context.ScheduledEventMaps.Remove(map);
                    if (eventDescription?.PlaylistId != null) {
                        dynamic? playlist = null;

                        using (var stream = await _s3Client.DownloadPlaylist($"{eventDescription.PlaylistId}.bplist")) {
                            if (stream != null) {
                                playlist = stream.ObjectFromStream();
                            }
                        }

                        if (playlist != null)
                        {
                            var songs = eventDescription.MapOfTheDays.SelectMany(m => m.Leaderboards).Select(lb => new
                            {
                                hash = lb.Song.LowerHash,
                                songName = lb.Song.Name,
                                levelAuthorName = lb.Song.Mapper,
                                difficulties = new List<PlaylistDifficulty> { new PlaylistDifficulty
                                {
                                    name = lb.Difficulty.DifficultyName.FirstCharToLower(),
                                    characteristic = lb.Difficulty.ModeName
                                } 
                                },
                                lb.Song.UploadTime
                            }).ToList();

                            playlist.songs = songs.DistinctBy(s => s.hash).OrderByDescending(a => a.UploadTime).ToList();

                            await S3Helper.UploadPlaylist(_s3Client, $"{eventDescription.PlaylistId}.bplist", playlist);
                        }
                    }

                await _context.BulkSaveChangesAsync();
            }

            var eventDays = await _context.MapOfTheDay.Where(m => m.Timeend < time && m.Champions.Count == 0).Include(e => e.Champions).Include(e => e.EventRanking).Include(e => e.Leaderboards).ThenInclude(l => l.Scores).ToListAsync();
                foreach (var item in eventDays) {
                    foreach (var lb in item.Leaderboards) {

                        foreach (var score in lb.Scores.Where(s => s.ValidForGeneral && !s.Bot && s.Rank > 0).OrderBy(s => s.Rank).Take(10)) {
                            var player = _context.Players.Where(p => p.Id == score.PlayerId).Include(p => p.EventsParticipating).ThenInclude(e => e.MapOfTheDayPoints).FirstOrDefault();
                            if (player == null) continue;

                            if (player.EventsParticipating == null) {
                                player.EventsParticipating = new List<EventPlayer>();
                            }

                            var currentEvent = player.EventsParticipating.FirstOrDefault(ep => ep.EventRankingId == item.EventRanking.Id);
                            if (currentEvent == null) {
                                currentEvent = new EventPlayer {
                                    PlayerId = player.Id,
                                    Country = player.Country,
                                    EventName = item.EventRanking.Name,
                                    PlayerName = player.Name,
                                    EventRankingId = item.EventRanking.Id,
                                    MapOfTheDayPoints = new List<MapOfTheDayPoints>()
                                };
                                player.EventsParticipating.Add(currentEvent);
                            }

                            item.Champions.Add(currentEvent);

                            var points = new MapOfTheDayPoints {
                                Rank = score.Rank,
                                MapOfTheDay = item
                            };

                            if (score.Rank == 1) {
                                points.Points = 10;
                            } else if (score.Rank == 2) {
                                points.Points = 5;
                            } else if (score.Rank == 3) {
                                points.Points = 3;
                            } else {
                                points.Points = 1;
                            }

                            currentEvent.Pp += points.Points;
                            currentEvent.MapOfTheDayPoints.Add(points);
                        }
                    }

                    await _context.BulkSaveChangesAsync();

                    var eps = _context.EventPlayer.Where(e => e.EventRankingId == item.EventRanking.Id).ToList();
                    Dictionary<string, int> countries = new Dictionary<string, int>();

                    int ii = 0;
                    foreach (EventPlayer p in eps.OrderByDescending(s => (int)s.Pp))
                    {
                        if (p.Rank != 0) {
                            p.Rank = p.Rank;
                        }
                        p.Rank = ii + 1;
                        if (!countries.ContainsKey(p.Country))
                        {
                            countries[p.Country] = 1;
                        }

                        p.CountryRank = countries[p.Country];
                        countries[p.Country]++;
                        ii++;
                    }

                    await _context.BulkSaveChangesAsync();
                }
            }
        }

        public async Task RefreshTreeMaps() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var now = Time.UnixNow();
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();
                var maps = await _context.TreeMaps.Where(m => m.Timestart < now).OrderByDescending(m => m.Timestart).ToListAsync();

                var ids = maps.Select(m => m.SongId).ToList();

                var songs = _context.Songs.Where(s => ids.Contains(s.Id)).Include(s => s.Leaderboards).ThenInclude(l => l.Difficulty).ToList();
                foreach (var song in songs) {
                    foreach (var lb in song.Leaderboards) {
                        if (lb.Difficulty.Status != Models.DifficultyStatus.inevent) {
                            lb.Difficulty.Status = Models.DifficultyStatus.inevent;
                            await RatingUtils.UpdateFromExMachina(lb, null);
                            await _context.SaveChangesAsync();
                            await ScoreRefreshControllerHelper.BulkRefreshScores(_context, lb.Id);
                        }
                    }
                }

                dynamic? playlist = null;

                using (var stream = await _s3Client.DownloadPlaylist("83999.bplist")) {
                    if (stream != null) {
                        playlist = stream.ObjectFromStream();
                    }
                }

                if (playlist == null)
                {
                    return;
                }

                var psongs = songs.Select(s => new
                {
                    hash = s.LowerHash,
                    songName = s.Name,
                    levelAuthorName = s.Mapper,
                    difficulties = s.Difficulties.Select(d => new
                    {
                        name = d.DifficultyName.FirstCharToLower(),
                        characteristic = d.ModeName
                    })
                }).ToList();

                playlist.songs = psongs;

                await S3Helper.UploadPlaylist(_s3Client, "83999.bplist", playlist);
            }
        }
    }
}
