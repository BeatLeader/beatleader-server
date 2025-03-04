using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Net;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Google.Apis.YouTube.v3;
using Google.Apis.Services;
using BeatLeader_Server.Controllers;
using beatleader_parser;
using Parser.Utils;

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

                    try {
                        await SortChannels();
                        await RefreshClans();
                        await RefreshPlays();
                        await FetchCurated();
                        await RefreshMapsPageEndpoints();
                        await CheckMaps();
                        await CheckNoodleMonday();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION HourlyRefresh {e}");
                    }

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
                var clans = await _context
                    .Clans
                    .Include(c => c.Players.Where(p => !p.Banned))
                    .ThenInclude(p => p.ScoreStats)
                    .ToListAsync();
                foreach (var clan in clans) {
                    if (clan.Players.Count > 0) {
                        clan.AverageAccuracy = clan.Players.Average(p => p.ScoreStats.AverageRankedAccuracy);
                        clan.AverageRank = (float)clan.Players.Average(p => p.Rank);
                        clan.PlayersCount = clan.Players.Count();
                        clan.Pp = await _context.RecalculateClanPP(clan.Id);
                    }
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task FetchCurated() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                var currentDate = DateTime.UtcNow;
                var lastUpdateDate = (await _context.SongsLastUpdateTimes.Where(s => s.Status == SongStatus.Curated).OrderByDescending(t => t.Date).FirstOrDefaultAsync())?.Date ?? new DateTime(1970, 1, 1);
                if (currentDate.Subtract(lastUpdateDate).TotalHours > 1) {
                    var curated = await SongUtils.GetCuratedSongsFromBeatSaver(lastUpdateDate);
                    var hashes = curated.Select(m => m.Hash.ToLower()).ToList();
                    var songs = await _context.Songs.Where(s => hashes.Contains(s.Hash.ToLower())).Include(s => s.ExternalStatuses).ToListAsync();
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
                                    song.Status |= SongStatus.Curated;
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
                    searchListRequest.Order = SearchResource.ListRequest.OrderEnum.Date;

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
                        var song = await _context.Songs.Where(s => s.Hash.ToLower() == lastVersion.Hash.ToLower()).Include(s => s.ExternalStatuses).FirstOrDefaultAsync();

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
                        song.Status |= SongStatus.NoodleMonday;
                    }

                    await _context.SaveChangesAsync();
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

                        var memoryStream = new MemoryStream();
                        await res.GetResponseStream().CopyToAsync(memoryStream);
                        var archive = new ZipArchive(memoryStream);

                        var parse = new Parse();
                        memoryStream.Position = 0;
                        var map = parse.TryLoadZip(memoryStream)?.FirstOrDefault();
                        if (map != null) {
                            var info = map.Info;

                            if (info._coverImageFilename != null) {
                                var coverFile = archive.Entries.FirstOrDefault(e => e.Name.ToLower() == info._coverImageFilename.ToLower());
                                if (coverFile != null) {
                                    using (var coverStream = coverFile.Open()) {
                                        using (var ms = new MemoryStream(5)) {
                                            await coverStream.CopyToAsync(ms);
                                            ms.Position = 0;
                                            MemoryStream imageStream = ImageUtils.ResizeToWebp(ms, 512);
                                            var fileName = $"songcover-{song.Id}-full.webp";

                                            song.FullCoverImage = await _s3Client.UploadAsset(fileName, imageStream);

                                            if (song.Explicity.HasFlag(SongExplicitStatus.Cover)) {
                                                if (song.FullCoverImage != null) {
                                                    song.FullCoverImage = System.Text.RegularExpressions.Regex.Replace(song.FullCoverImage, "https?://cdn.assets.beatleader.(?:[a-z]{3})?/", $"https://api.beatleader.com/cover/processed/{song.Id}/");
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            foreach (var set in map.Difficulties)
                            {
                                var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == set.Difficulty && d.ModeName == set.Characteristic);
                                if (songDiff == null) continue;
                                var diff = set.Data;
                                if (diff.Chains.Count > 0 || diff.Arcs.Count > 0) {
                                    songDiff.Requirements |= Models.Requirements.V3;
                                    songDiff.Chains = diff.Chains.Sum(c => c.SliceCount > 1 ? c.SliceCount - 1 : 0);
                                    songDiff.Sliders = diff.Arcs.Count;
                                }
                                //if (diff.Notes.FirstOrDefault(n => n.Optional()) != null) {
                                //    songDiff.Requirements |= Models.Requirements.OptionalProperties;
                                //}
                                if (diff.njsEvents.Count > 0) {
                                    songDiff.Requirements |= Models.Requirements.VNJS;
                                }

                                songDiff.MaxScoreGraph = new MaxScoreGraph();
                                songDiff.MaxScoreGraph.SaveList(set.MaxScoreGraph());
                                if (songDiff.MaxScore == 0) {
                                    songDiff.MaxScore = set.MaxScore();
                                }
                            }

                            foreach (var mode in info._difficultyBeatmapSets) {
                                foreach (var diff in mode._difficultyBeatmaps) {
                                    var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == diff._difficulty && d.ModeName == mode._beatmapCharacteristicName);
                                    if (songDiff == null || diff._customData == null || diff._customData._requirements == null) continue;
                                    if (diff._customData._requirements.Any(r => r.ToLower().Contains("vivify"))) {
                                        songDiff.Requirements |= Models.Requirements.Vivify;
                                    }
                                }
                            }
                        }
                    } catch { }

                    foreach (var diff in song.Difficulties) {
                        if (!diff.Requirements.HasFlag(Requirements.Noodles) && !diff.Requirements.HasFlag(Requirements.MappingExtensions)) {
                            await RatingUtils.UpdateFromExMachina(diff, song, null);
                        }
                    }

                    song.Checked = true;
                }

                await _context.SaveChangesAsync();
            }
        }
        public async Task RefreshMapsPageEndpoints() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var songSuggestController = scope.ServiceProvider.GetRequiredService<SongSuggestController>();
                await songSuggestController.RefreshTrending();
                await songSuggestController.RefreshCurated();
            }
        }

        public async Task SortChannels() {
            int criteriaPosition = await Bot.BotService.GetChannelPosition(1019637152927191150) ?? 50;

            await Bot.BotService.UpdateChannelOrder(1137885973921935372, criteriaPosition + 1);
            await Bot.BotService.UpdateChannelOrder(1137886176947220633, criteriaPosition + 2);
        }

        public async Task RefreshPlays() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var yesterday = Time.UnixNow() - 60 * 60 * 24;
                var lastWeek = Time.UnixNow() - 60 * 60 * 24 * 7;

                var lbs = (await _context.Leaderboards.AsNoTracking().Select(l => new {
                    l.Id,
                    Daily = l.Scores.Where(s => s.Timepost > yesterday).Count(),
                    Weekly = l.Scores.Where(s => s.Timepost > lastWeek).Count()
                }).ToListAsync())
                .Select(lb => new Leaderboard {
                    Id = lb.Id,
                    ThisWeekPlays = lb.Weekly,
                    TodayPlays = lb.Daily
                })
                .ToList();

                await _context.BulkUpdateAsync(lbs, options => options.ColumnInputExpression = c => new { c.ThisWeekPlays, c.TodayPlays });
                await _context.SaveChangesAsync();
            }
        }

        public async Task RefreshMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();

                var query = _context.Songs.Where(s => !s.Refreshed);
                var count = await query.CountAsync();

                for (int i = 0; i < count; i+=1000)
                {
                    var songs = await query
                        .OrderByDescending(s => s.UploadTime)
                        .Skip(i)
                        .Take(1000)
                        .Include(s => s.Difficulties)
                        .ThenInclude(d => d.ModifiersRating)
                        .ToListAsync();

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
