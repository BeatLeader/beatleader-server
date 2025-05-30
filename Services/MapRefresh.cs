﻿using BeatLeader_Server.Utils;
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
using Parser.Map.Difficulty.V3.Grid;
using Parser.Map;

namespace BeatLeader_Server.Services {
    public class MapRefresh : BackgroundService {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public MapRefresh(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                int minuteSpan = 60 - DateTime.Now.Minute;
                int numberOfMinutes = minuteSpan;

                if (minuteSpan == 60) {
                    Console.WriteLine("SERVICE-STARTED MapRefresh");

                    try {
                        await CheckMaps();
                    } catch (Exception e) {
                        Console.WriteLine($"EXCEPTION MapRefresh {e}");
                    }

                    Console.WriteLine("SERVICE-DONE MapRefresh");

                    minuteSpan = 60 - DateTime.Now.Minute;
                    numberOfMinutes = minuteSpan;
                }

                await Task.Delay(TimeSpan.FromMinutes(numberOfMinutes), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested) ;
        }

        private bool Overlaps(List<Chain> chains, List<Arc> arcs) {
            foreach (var arc in arcs) {
                if (chains.Any(c => c.BpmTime == arc.BpmTime && (c.x == arc.x && c.y == arc.y))) {
                    return true;
                }
            }

            return false;
        }

        public async Task CheckMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();
                var downloader = new MapDownloader(_configuration.GetValue<string>("MapsPath") ?? "/home/maps");
                
                var songs = await _context.Songs.Where(s => !s.Checked).OrderBy(s => s.Id).OrderByDescending(s => s.UploadTime).Take(50).Include(s => s.Difficulties).ToListAsync();

                foreach (var song in songs) {
                    try {
                        var mapPath = downloader.Map(song.Hash);
                        if (mapPath == null) continue;

                        var parse = new Parse();
                        BeatmapV3? mapset = null;
                        try
                        {
                            mapset = parse.TryLoadPath(mapPath);
                        }
                        catch (FileNotFoundException e)
                        {
                            Directory.Delete(mapPath, true);
                            mapPath = downloader.Map(song.Hash);
                            mapset = parse.TryLoadPath(mapPath);
                        }

                        if (mapset != null) {
                            var info = mapset.Info;

                            if (info._coverImageFilename != null) {
                                var coverFile = mapPath + "/" + info._coverImageFilename;
                                if (System.IO.File.Exists(coverFile)) {
                                    using (var coverStream = System.IO.File.OpenRead(coverFile)) {
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

                            foreach (var set in mapset.Difficulties)
                            {
                                var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == set.Difficulty && d.ModeName == set.Characteristic);
                                if (songDiff == null) continue;
                                var diff = set.Data;
                                if (diff.Chains.Count > 0 || diff.Arcs.Count > 0) {
                                    songDiff.Requirements |= Models.Requirements.V3;
                                    songDiff.Chains = diff.Chains.Sum(c => c.SliceCount > 1 ? c.SliceCount - 1 : 0);
                                    songDiff.Sliders = diff.Arcs.Count;

                                    //if (Overlaps(diff.Chains, diff.Arcs)) {
                                    //    songDiff.Requirements |= Models.Requirements.V3Pepega;
                                    //}
                                }
                                //if (diff.Notes.FirstOrDefault(n => n.Optional()) != null) {
                                //    songDiff.Requirements |= Models.Requirements.OptionalProperties;
                                //}
                                if (diff.njsEvents.Count > 0) {
                                    songDiff.Requirements |= Models.Requirements.VNJS;
                                }
                                if (diff.lightColorEventBoxGroups.Count > 0 ||
                                    diff.lightRotationEventBoxGroups.Count > 0 ||
                                    diff.lightTranslationEventBoxGroups.Count > 0 ||
                                    diff.vfxEventBoxGroups?.Count() > 0) {
                                    songDiff.Requirements |= Models.Requirements.GroupLighting;
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
                    } 
                    catch (Exception e) { 
                        Console.WriteLine($"MapRefresh EXPETION: {e}");
                    }

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
    }
}
