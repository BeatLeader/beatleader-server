using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Net;
using BeatLeader_Server.Extensions;
using BeatMapEvaluator;
using BeatLeader_Server.BeatMapEvaluator;
using BeatLeader_Server.Models;
using Google.Apis.YouTube.v3;
using Google.Apis.Services;

namespace BeatLeader_Server.Services {
    public class HourlyRefresh : BackgroundService {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public HourlyRefresh(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                int minuteSpan = 60 - DateTime.Now.Minute;
                int numberOfMinutes = minuteSpan;

                if (minuteSpan == 60)
                {
                    Console.WriteLine("SERVICE-STARTED HourlyRefresh");

                    await RefreshClans();
                    await FetchCurated();
                    await CheckMaps();
                    await CheckNoodleMonday();

                    Console.WriteLine("SERVICE-DONE HourlyRefresh");

                minuteSpan = 60 - DateTime.Now.Minute;
                numberOfMinutes = minuteSpan;
            }

                await Task.Delay(TimeSpan.FromMinutes(numberOfMinutes), stoppingToken);
        }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshClans() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var clans = _context
                    .Clans
                    .Include(c => c.Players.Where(p => !p.Banned))
                    .ThenInclude(p => p.ScoreStats)
                    .ToList();
                foreach (var clan in clans) {
                    if (clan.Players.Count > 0) {
                        clan.AverageAccuracy = clan.Players.Average(p => p.ScoreStats.AverageRankedAccuracy);
                        clan.AverageRank = (float)clan.Players.Average(p => p.Rank);
                        clan.PlayersCount = clan.Players.Count();
                        clan.Pp = _context.RecalculateClanPP(clan.Id);
                    }
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task FetchCurated() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var currentDate = DateTime.UtcNow;
                var lastUpdateDate = _context.SongsLastUpdateTimes.Where(s => s.Status == SongStatus.Curated).FirstOrDefault()?.Date ?? new DateTime(1970, 1, 1);
                if (currentDate.Subtract(lastUpdateDate).TotalHours > 1) {
                    var curated = await SongUtils.GetCuratedSongsFromBeatSaver(lastUpdateDate);
                    var hashes = curated.Select(m => m.Hash.ToLower()).ToList();
                    var songs = _context.Songs.Where(s => hashes.Contains(s.Hash.ToLower())).Include(s => s.ExternalStatuses).ToList();
                    foreach (var map in curated)
                    {
                        var song = songs.FirstOrDefault(s => s.Hash.ToLower() == map.Hash.ToLower());
                        if (song != null && map.ExternalStatuses != null) {
                            if (song.ExternalStatuses == null) {
                                song.ExternalStatuses = new List<ExternalStatus>();
                            }
                            if (song.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.Curated) == null) {
                                foreach (var status in map.ExternalStatuses)
                                {
                                    song.ExternalStatuses.Add(status);
                                }
                            }
                        }
                    }

                    _context.SongsLastUpdateTimes.Add(new SongsLastUpdateTime {
                        Date = currentDate,
                        Status = SongStatus.Curated
                    });
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task CheckNoodleMonday() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                try {
                    var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                    {
                        ApiKey = _configuration.GetValue<string?>("YoutubeAPIKey"),
                        ApplicationName = this.GetType().ToString()
                    });

                    var searchListRequest = youtubeService.Search.List("snippet");
                    searchListRequest.ChannelId = "UCdG9zS8jVcQIKl7plwWXUkg";
                    searchListRequest.MaxResults = 4;
                    searchListRequest.Order = Google.Apis.YouTube.v3.SearchResource.ListRequest.OrderEnum.Date;

                    List<string> videoIds = new List<string>();
                    var searchListResponse = await searchListRequest.ExecuteAsync();
                    foreach (var searchResult in searchListResponse.Items)
                    {
                        if (searchResult == null) continue;
                        if (searchResult.Id.Kind == "youtube#video")
                        {
                            string title = searchResult.Snippet.Title;
                            if (title.Contains("Noodle Map Monday"))
                            {
                                videoIds.Add(searchResult.Id.VideoId);
                            }
                        }
                    }

                    var videoListRequest = youtubeService.Videos.List("snippet");
                    videoListRequest.Id = string.Join(",", videoIds);
                    var videoListResponse = await videoListRequest.ExecuteAsync();

                    List<string> videoUrls = new List<string>();
                    foreach (var video in videoListResponse.Items)
                    {
                        string videoUrl = $"https://www.youtube.com/watch?v={video.Id}";
                        int timeset = (int)(video.Snippet.PublishedAt?.Subtract(new DateTime(1970, 1, 1)).TotalSeconds ?? 0);

                        string id = video.Snippet.Description.Split("https://beatsaver.com/maps/").Last().Split(".").First().Split("\n").First();

                        var lastVersion = await SongUtils.GetSongFromBeatSaverId(id);

                        if (lastVersion == null) continue;
                        var song = _context.Songs.Where(s => s.Hash.ToLower() == lastVersion.Hash.ToLower()).Include(s => s.ExternalStatuses).FirstOrDefault();

                        if (song == null) continue;
                        if (song.ExternalStatuses == null) {
                            song.ExternalStatuses = new List<ExternalStatus>();
                        }
                        if (song.ExternalStatuses.FirstOrDefault(es => es.Status == SongStatus.NoodleMonday) != null) continue;

                        song.ExternalStatuses.Add(new ExternalStatus {
                            Status = SongStatus.NoodleMonday,
                            Timeset = timeset,
                            Link = videoUrl
                        });
                    }

                    _context.SaveChanges();
                } catch { }
            }
        }

        public async Task CheckMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();

                var songs = await _context.Songs.Where(s => !s.Checked).OrderByDescending(s => s.UploadTime).Take(50).Include(s => s.Difficulties).ToListAsync();

                foreach (var song in songs) {
                    try {
                        HttpWebResponse res = (HttpWebResponse)await WebRequest.Create(song.DownloadUrl).GetResponseAsync();
                        if (res.StatusCode != HttpStatusCode.OK) continue;

                        var archive = new ZipArchive(res.GetResponseStream());

                        var infoFile = archive.Entries.FirstOrDefault(e => e.Name.ToLower() == "info.dat");
                        if (infoFile == null) continue;

                        var info = infoFile.Open().ObjectFromStream<json_MapInfo>();
                        if (info == null) continue;

                        foreach (var set in info.beatmapSets) {
                            foreach (var beatmap in set._diffMaps) {
                                var diffFile = archive.Entries.FirstOrDefault(e => e.Name == beatmap._beatmapFilename);
                                if (diffFile == null) continue;

                                var diff = diffFile.Open().ObjectFromStream<DiffFileV3>();
                                if (diff != null) {
                                    if (diff.burstSliders?.Length > 0 || diff.sliders?.Length > 0) {
                                        var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == beatmap._difficulty && d.ModeName == set._beatmapCharacteristicName);
                                        if (songDiff != null) {
                                            songDiff.Requirements |= Models.Requirements.V3;
                                        }
                                    }
                                    if (diff.colorNotes?.FirstOrDefault(n => n.Optional()) != null) {
                                        var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == beatmap._difficulty && d.ModeName == set._beatmapCharacteristicName);
                                        if (songDiff != null) {
                                            songDiff.Requirements |= Models.Requirements.OptionalProperties;
                                        }
                                    }
                                }
                            }
                        }

                        if (info._coverImageFilename != null) {
                            var coverFile = archive.Entries.FirstOrDefault(e => e.Name.ToLower() == info._coverImageFilename.ToLower());
                            if (coverFile != null) {
                                using (var coverStream = coverFile.Open()) {
                                    using (var ms = new MemoryStream(5)) {
                                        await coverStream.CopyToAsync(ms);
                                        var fileName = ($"songcover-{song.Id}-" + info._coverImageFilename).Replace(" ", "").Replace("(", "").Replace(")", "");
                                        
                                        song.FullCoverImage = await _s3Client.UploadAsset(fileName, ms);
                                    }
                                }
                            }
                        }
                    } catch { }

                    foreach (var diff in song.Difficulties) {
                        await RatingUtils.UpdateFromExMachina(diff, song, null);
                    }

                    song.Checked = true;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task RefreshMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();

                var query = _context.Songs.Where(s => !s.Refreshed);
                var count = query.Count();

                for (int i = 0; i < count; i+=1000)
                {
                    var songs = query
                        .OrderByDescending(s => s.UploadTime)
                        .Skip(i)
                        .Take(1000)
                        .Include(s => s.Difficulties)
                        .ThenInclude(d => d.ModifiersRating)
                        .ToList();

                    foreach (var song in songs) {

                        foreach (var diff in song.Difficulties) {
                            if (diff.Status != DifficultyStatus.ranked) {
                                await RatingUtils.UpdateFromExMachina(diff, song, null);
                            }
                        }

                        song.Refreshed = true;
                    }

                    await _context.SaveChangesAsync();
                }
            }
        }
    }
}
