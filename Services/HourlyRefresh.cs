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

        public float toPass(float original) {
            if (original < 24.4) {
                return original;
            } else {
                return 16 + MathF.Sqrt(original) * 1.7f;
            }
        }

        public async Task CheckMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();

                var songs = await _context.Songs.Where(s => !s.Checked).OrderBy(s => s.Id).Include(s => s.Difficulties).ToListAsync();

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
                                        var fileName = $"songcover-{song.Id}-" + info._coverImageFilename;
                                        await _s3Client.UploadAsset(fileName, ms);
                                        song.FullCoverImage = "https://cdn.assets.beatleader.xyz/" + fileName.Replace(" ", "%20");
                                    }
                                }
                            }
                        }
                    } catch { }

                    foreach (var diff in song.Difficulties) {
                        if (!diff.Status.WithRating() && !diff.Requirements.HasFlag(Requirements.Noodles) && !diff.Requirements.HasFlag(Requirements.MappingExtensions)) {
                            var response = await SongUtils.ExmachinaStars(song.Hash, diff.Value);
                            if (response != null) {
                                diff.PassRating = toPass(response.none.lack_map_calculation.balanced_pass_diff);
                                diff.TechRating = response.none.lack_map_calculation.balanced_tech * 10;
                                diff.PredictedAcc = response.none.AIacc;
                                diff.AccRating = ReplayUtils.AccRating(diff.PredictedAcc, diff.PassRating, diff.TechRating);

                                diff.ModifiersRating = new ModifiersRating {
                                    SSPassRating = toPass(response.SS.lack_map_calculation.balanced_pass_diff),
                                    SSTechRating = response.SS.lack_map_calculation.balanced_tech * 10,
                                    SSPredictedAcc = response.SS.AIacc,
                                    FSPassRating = toPass(response.FS.lack_map_calculation.balanced_pass_diff),
                                    FSTechRating = response.FS.lack_map_calculation.balanced_tech * 10,
                                    FSPredictedAcc = response.FS.AIacc,
                                    SFPassRating = toPass(response.SFS.lack_map_calculation.balanced_pass_diff),
                                    SFTechRating = response.SFS.lack_map_calculation.balanced_tech * 10,
                                    SFPredictedAcc = response.SFS.AIacc,
                                };

                                diff.Stars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);

                                var rating = diff.ModifiersRating;
                                rating.SSAccRating = ReplayUtils.AccRating(
                                        rating.SSPredictedAcc,
                                        rating.SSPassRating,
                                        rating.SSTechRating);
                                rating.SSStars = ReplayUtils.ToStars(rating.SSPredictedAcc, rating.SSPassRating, rating.SSTechRating);
                                rating.FSAccRating = ReplayUtils.AccRating(
                                        rating.FSPredictedAcc,
                                        rating.FSPassRating,
                                        rating.FSTechRating);
                                rating.FSStars = ReplayUtils.ToStars(rating.FSPredictedAcc, rating.FSPassRating, rating.FSTechRating);
                                rating.SFAccRating = ReplayUtils.AccRating(
                                        rating.SFPredictedAcc,
                                        rating.SFPassRating,
                                        rating.SFTechRating);
                                rating.SFStars = ReplayUtils.ToStars(rating.SFPredictedAcc, rating.SFPassRating, rating.SFTechRating);
                            }
                        }
                    }

                    song.Checked = true;
                }

                await _context.SaveChangesAsync();
            }
        }
    }
}
