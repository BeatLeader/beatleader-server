using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Net;
using BeatLeader_Server.Extensions;
using BeatMapEvaluator;
using BeatLeader_Server.BeatMapEvaluator;
using BeatLeader_Server.Models;

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

                if (minuteSpan == 60) {
                    await RefreshClans();
                    await CheckMaps();

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

        public async Task CheckMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();

                var songs = await _context.Songs.Where(s => !s.Checked).OrderBy(s => s.Id).Take(100).Include(s => s.Difficulties).ToListAsync();

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
                                if (diff != null && (diff.burstSliders?.Length > 0 || diff.sliders?.Length > 0)) {
                                    var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == beatmap._difficulty && d.ModeName == set._beatmapCharacteristicName);
                                    if (songDiff != null) {
                                        songDiff.Requirements |= Models.Requirements.V3;
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
                                        var fileName = ($"songcover-{song.Id}-" + info._coverImageFilename).Replace(" ", "");
                                        
                                        song.FullCoverImage = await _s3Client.UploadAsset(fileName, ms);
                                    }
                                }
                            }
                        }
                    } catch { }

                    foreach (var diff in song.Difficulties) {
                        await RatingUtils.SetRating(diff, song);
                    }

                    song.Checked = true;
                }

                await _context.SaveChangesAsync();
            }
        }
    }
}
