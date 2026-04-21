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
using Parser.Map.Difficulty.V3.Grid;
using Parser.Map;
using MapPostprocessor;
using BeatLeader_Server.ControllerHelpers;
using RatingAPI.Controllers;

namespace BeatLeader_Server.Services {
    public class MapRefresh : BackgroundService {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly InferPublish _aiSession;

        public MapRefresh(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration) {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            _aiSession = new InferPublish();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            do {
                try {
                    await CheckMaps();
                } catch (Exception e) {
                    Console.WriteLine($"EXCEPTION MapRefresh {e}");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested) ;
        }

        public async Task CheckMaps() {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();
                var downloader = new MapDownloader(_configuration.GetValue<string>("MapsPath") ?? "/home/maps");
                
                var songs = await _context.Songs.Where(s => !s.Checked).OrderBy(s => s.Id).OrderByDescending(s => s.UploadTime).Take(50).Include(s => s.Difficulties).ToListAsync();

                foreach (var song in songs) {
                    try {
                        var mapPath = await downloader.Map(song.LowerHash);
                        if (mapPath == null) { 
                            if (song.Difficulties.Any(d => d.Status == DifficultyStatus.outdated)) {
                                song.Checked = true;
                            }

                            await Task.Delay(TimeSpan.FromSeconds(2));

                            continue;
                        }

                        BeatmapV3? mapset = null;
                        try
                        {
                            mapset = MapParser.TryLoadPath(mapPath);
                        }
                        catch (FileNotFoundException e)
                        {
                            Directory.Delete(mapPath, true);
                            mapPath = await downloader.Map(song.LowerHash);
                            mapset = MapParser.TryLoadPath(mapPath);
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

                            song.MapVersion = info._version;

                            foreach (var mode in info._difficultyBeatmapSets) {
                                foreach (var diff in mode._difficultyBeatmaps) {
                                    var songDiff = song.Difficulties.FirstOrDefault(d => d.DifficultyName == diff._difficulty && d.ModeName == mode._beatmapCharacteristicName);
                                    if (songDiff == null || diff._customData == null || diff._customData._requirements == null) continue;

                                    if (diff._customData._requirements.Any(r => r.ToLower().Contains("vivify"))) {
                                        songDiff.Requirements |= Models.Requirements.Vivify;
                                        songDiff.RequiresVivify = true;
                                    }

                                    if (diff._customData._requirements.Any(r => r.ToLower().Contains("noodle"))) {
                                        songDiff.Requirements |= Models.Requirements.Noodles;
                                        songDiff.RequiresNoodles = true;
                                    }

                                    if (diff._customData._requirements.Any(r => r.ToLower().Contains("mapping"))) {
                                        songDiff.Requirements |= Models.Requirements.MappingExtensions;
                                        songDiff.RequiresMappingExtensions = true;
                                    }

                                    if (diff._customData._requirements.Any(r => r.ToLower().Contains("chroma"))) {
                                        songDiff.Requirements |= Models.Requirements.Chroma;
                                        songDiff.RequiresChroma = true;
                                    }

                                    if (diff._customData._requirements.Any(r => r.ToLower().Contains("cinema"))) {
                                        songDiff.Requirements |= Models.Requirements.Cinema;
                                        songDiff.RequiresCinema = true;
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
                                    songDiff.RequiresV3 = true;
                                    songDiff.Chains = diff.Chains.Sum(c => c.SliceCount > 1 ? c.SliceCount - 1 : 0);
                                    songDiff.Sliders = diff.Arcs.Count;

                                    if (set.IsV3Pepega()) {
                                        songDiff.Requirements |= Models.Requirements.V3Pepega;
                                        songDiff.RequiresV3Pepega = true;
                                    }
                                }
                                if (diff.njsEvents.Count > 0) {
                                    songDiff.Requirements |= Models.Requirements.VNJS;
                                    songDiff.RequiresVNJS = true;
                                }
                                if (diff.lightColorEventBoxGroups.Count > 0 ||
                                    diff.lightRotationEventBoxGroups.Count > 0 ||
                                    diff.lightTranslationEventBoxGroups.Count > 0 ||
                                    diff.vfxEventBoxGroups?.Count() > 0) {
                                    songDiff.Requirements |= Models.Requirements.GroupLighting;
                                    songDiff.RequiresGroupLighting = true;
                                }

                                var mapDiff = mapset.Info._difficultyBeatmapSets.First(d => d._beatmapCharacteristicName == songDiff.ModeName)._difficultyBeatmaps.First(d => d._difficulty == songDiff.DifficultyName);
                                songDiff.Njs = mapDiff._noteJumpMovementSpeed;
                                songDiff.NoteJumpStartBeatOffset = mapDiff._noteJumpStartBeatOffset;
                                songDiff.MapVersion = diff.Version;
                                var response = (!songDiff.Requirements.HasFlag(Requirements.Noodles) && !songDiff.Requirements.HasFlag(Requirements.MappingExtensions)) ? await RatingsGenerator.RatingAPI.Calculate(mapset, _aiSession, songDiff.ModeName, songDiff.Value) : null;
                                if (response != null && response["none"] != null) {
                                    //var rankChange = new RankUpdateChange
                                    var modrating = songDiff.ModifiersRating;

                                    if (songDiff.Status != DifficultyStatus.ranked) {
                                        songDiff.PassRating = (float)response["none"].Ratings.PassRating;
                                        songDiff.TechRating = (float)response["none"].Ratings.TechRating;
                                        songDiff.PredictedAcc = (float)response["none"].PredictedAcc;
                                        songDiff.AccRating = (float)response["none"].AccRating;

                                        modrating = songDiff.ModifiersRating = new ModifiersRating {
                                            SSPassRating = (float)response["SS"].Ratings.PassRating,
                                            SSTechRating = (float)response["SS"].Ratings.TechRating,
                                            SSPredictedAcc = (float)response["SS"].PredictedAcc,
                                            SSAccRating = (float)response["SS"].AccRating,


                                            FSPassRating = (float)response["FS"].Ratings.PassRating,
                                            FSTechRating = (float)response["FS"].Ratings.TechRating,
                                            FSPredictedAcc = (float)response["FS"].PredictedAcc,
                                            FSAccRating = (float)response["FS"].AccRating,


                                            SFPassRating = (float)response["SFS"].Ratings.PassRating,
                                            SFTechRating = (float)response["SFS"].Ratings.TechRating,
                                            SFPredictedAcc = (float)response["SFS"].PredictedAcc,
                                            SFAccRating = (float)response["SFS"].AccRating,


                                            BFSPassRating = (float)response["BFS"].Ratings.PassRating,
                                            BFSTechRating = (float)response["BFS"].Ratings.TechRating,
                                            BFSPredictedAcc = (float)response["BFS"].PredictedAcc,
                                            BFSAccRating = (float)response["BFS"].AccRating,


                                            BSFPassRating = (float)response["BSF"].Ratings.PassRating,
                                            BSFTechRating = (float)response["BSF"].Ratings.TechRating,
                                            BSFPredictedAcc = (float)response["BSF"].PredictedAcc,
                                            BSFAccRating = (float)response["BSF"].AccRating,

                                        };
                                    }

                                    if (modrating != null) {
                                        modrating.SSPeakSustainedEBPM = (float)response["SS"].Ratings.PeakSustainedEBPM;
                                        modrating.FSPeakSustainedEBPM = (float)response["FS"].Ratings.PeakSustainedEBPM;
                                        modrating.SFPeakSustainedEBPM = (float)response["SFS"].Ratings.PeakSustainedEBPM;
                                        modrating.BFSPeakSustainedEBPM = (float)response["BFS"].Ratings.PeakSustainedEBPM;
                                        modrating.BSFPeakSustainedEBPM = (float)response["BSF"].Ratings.PeakSustainedEBPM;

                                        modrating.SFStars = ReplayUtils.ToStars(modrating.SFAccRating, modrating.SFPassRating, modrating.SFTechRating);
                                        modrating.FSStars = ReplayUtils.ToStars(modrating.FSAccRating, modrating.FSPassRating, modrating.FSTechRating);
                                        modrating.BSFStars = ReplayUtils.ToStars(modrating.BSFAccRating, modrating.BSFPassRating, modrating.BSFTechRating);
                                        modrating.BFSStars = ReplayUtils.ToStars(modrating.BFSAccRating, modrating.BFSPassRating, modrating.BFSTechRating);
                                        modrating.SSStars = ReplayUtils.ToStars(modrating.SSAccRating, modrating.SSPassRating, modrating.SSTechRating);
                                    }

                                    if (songDiff.AccRating != null && songDiff.PassRating != null && songDiff.TechRating != null) {
                                        songDiff.Stars = ReplayUtils.ToStars(songDiff.AccRating ?? 0, songDiff.PassRating ?? 0, songDiff.TechRating ?? 0);
                                    }
                                    songDiff.MultiRating = (float)response["none"].Ratings.MultiPercentage;
                                    songDiff.LinearPercentage = (float)response["none"].Ratings.LinearPercentage;
                                    songDiff.PeakSustainedEBPM = (float)response["none"].Ratings.PeakSustainedEBPM;

                                    var diffStatistic = response["none"].Ratings.Statistics;
                                    var swingData = response["none"].Ratings.SwingData;

                                    songDiff.DifficultyStatistics = new DifficultyStatistics {
                                        Stacks = diffStatistic.Stacks,
                                        Towers = diffStatistic.Towers,
                                        Sliders = diffStatistic.Sliders,
                                        CurvedSliders = diffStatistic.CurvedSliders,
                                        Windows = diffStatistic.Windows,
                                        SlantedWindows = diffStatistic.SlantedWindows,
                                        DodgeWalls = diffStatistic.DodgeWalls,
                                        CrouchWalls = diffStatistic.CrouchWalls,
                                        ParityErrors = diffStatistic.ParityErrors,
                                        BombAvoidances = diffStatistic.BombAvoidances,
                                        LinearSwings = diffStatistic.LinearSwings,
                                        SwingData = swingData.Select(sd => new MapSwingData {
                                            BpmTime = sd.BpmTime,
                                            Direction = sd.Direction,
                                            Forehand = sd.Forehand,
                                            ParityErrors = sd.ParityErrors,
                                            BombAvoidance = sd.BombAvoidance,
                                            IsLinear = sd.IsLinear,
                                            AngleStrain = sd.AngleStrain,
                                            RepositioningDistance = sd.RepositioningDistance,
                                            RotationAmount = sd.RotationAmount,
                                            SwingFrequency = sd.SwingFrequency,
                                            DistanceDiff = sd.DistanceDiff,
                                            SwingSpeed = sd.SwingSpeed,
                                            HitDistance = sd.HitDistance,
                                            Stress = sd.Stress,
                                            LowSpeedFalloff = sd.LowSpeedFalloff,
                                            StressMultiplier = sd.StressMultiplier,
                                            NjsBuff = sd.NjsBuff,
                                            WallBuff = sd.WallBuff,
                                            IsStream = sd.IsStream,
                                            SwingDiff = sd.SwingDiff,
                                            SwingTech = sd.SwingTech
                                        }).ToList()
                                    };

                                    songDiff.Type |= response["none"].MapType;

                                    songDiff.TypeFitbeat = songDiff.Type.HasFlag(MapTypes.Fitbeat);
                                    songDiff.TypeLinear = songDiff.Type.HasFlag(MapTypes.Linear);
                                    songDiff.TypeBombReset = songDiff.Type.HasFlag(MapTypes.BombReset);
                                }

                                songDiff.MaxScoreGraph = new MaxScoreGraph();
                                songDiff.MaxScoreGraph.SaveList(set.MaxScoreGraph());
                                var newMaxScore = set.MaxScore();
                                if (newMaxScore != 0) {
                                    songDiff.MaxScore = newMaxScore;

                                    await _context.SaveChangesAsync();
                                    await ScoreRefreshControllerHelper.BulkRefreshScores(_context, $"{song.Id}{songDiff.Value}{songDiff.Mode}");
                                }
                            }
                        }
                    } 
                    catch (Exception e) { 
                        Console.WriteLine($"MapRefresh EXPETION: {e}");
                    }

                    song.Checked = true;
                }
                await _context.SaveChangesAsync();
            }
        }
    }
}
