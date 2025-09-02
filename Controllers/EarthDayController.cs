using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Dasync.Collections;
using Discord;
using Discord.Webhook;
using Lib.ServerTiming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Primitives;
using Prometheus.Client;
using System.Buffers;
using ReplayDecoder;
using static BeatLeader_Server.Utils.ResponseUtils;
using System.Text;
using BeatLeader_Server.ControllerHelpers;
using System.Net;
using beatleader_parser;
using Parser.Utils;
using Parser.Map;
using Beatmap = Parser.Map.BeatmapV3; // Alias to avoid ambiguity
using System.Collections.Generic; // Added for List
using System.Linq; // Added for LINQ methods
using System; // Added for Random
using Parser.Map.Difficulty.V3.Base; // Added for DifficultyV3 and BpmEvent etc.
using Parser.Map.Difficulty.V3.Event; // For Light, RotationEvent etc.
using Parser.Map.Difficulty.V3.Grid;
using OggVorbisEncoder;
using System.Security.Cryptography; // For Note, Bomb, Wall etc.

namespace BeatLeader_Server.Controllers
{
    public class EarthDayController : Controller
    {
        private readonly AppContext _context;
        private readonly IAmazonS3 _s3Client;
        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly IServerTiming _serverTiming;
        private readonly IConfiguration _configuration;
        private readonly MapDownloader _downloader; // Add downloader field
        private readonly Parse _parser; // Add parser field

        public EarthDayController(
            AppContext context,
            IWebHostEnvironment env,
            IServerTiming serverTiming,
            IConfiguration configuration)
        {
            _context = context;
            _serverTiming = serverTiming;
            _s3Client = configuration.GetS3Client();
            _webHostEnvironment = env;
            _configuration = configuration;
            _downloader = new MapDownloader(_configuration.GetValue<string>("MapsPath") ?? "/home/maps"); // Initialize downloader
            _parser = new Parse(); // Initialize parser
        }

        private class ParsedMapData
        {
            public string Hash { get; set; }
            public Beatmap Beatmap { get; set; }
            public DifficultyDescription Difficulty { get; set; }
            public double Duration { get; set; }
            public int Timepost { get; set; }
            public float InitialBpm { get; set; }
            public Info MapInfo { get; set; } // Store the whole Info object
            public string SongPath { get; set; }
        }

        private string? GetIpAddress()
        {
            if (!string.IsNullOrEmpty(HttpContext.Request.Headers["cf-connecting-ip"]))
                return HttpContext.Request.Headers["cf-connecting-ip"];

            var ipAddress = HttpContext.GetServerVariable("HTTP_X_FORWARDED_FOR");

            if (!string.IsNullOrEmpty(ipAddress))
            {
                var addresses = ipAddress.Split(',');
                if (addresses.Length != 0)
                    return addresses.Last();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        [HttpGet("~/earthday/map/{playerId}")]
        public async Task<ActionResult> GetPlayerMap(string playerId)
        {
            var result = await _context.EarthDayMaps.Where(m => m.PlayerId == playerId).FirstOrDefaultAsync();
            if (result != null) { 
                return Ok(result);
            } else {
                return NotFound();
            }
        }

        [HttpGet("~/earthday/generate")]
        public async Task<ActionResult> Generate([FromQuery] string? overridePlayerId = null)
        {
            string? currentID = HttpContext.CurrentUserID(_context);

            if (overridePlayerId != null) {
                if (GetIpAddress() == "172.104.4.248" || GetIpAddress() == "188.37.156.70") {
                    currentID = overridePlayerId;
                } else {
                    var currentPlayer = await _context.Players.FindAsync(currentID);

                    if (currentPlayer == null || !currentPlayer.Role.Contains("admin"))
                    {
                        return Unauthorized();
                    }
                    currentID = overridePlayerId;
                }
            }

            if (currentID == null)
            {
                return Unauthorized();
            }

            var timestamp = Time.UnixNow();

            if (_configuration.GetValue<string>("ServicesHost") == "YES") {
                var client = new HttpClient();
                using (var request = new HttpRequestMessage(HttpMethod.Get, $"http://5.161.236.104/earthday/generate?overridePlayerId={currentID}"))
                {
                    Stream contentStream = await (await client.SendAsync(request)).Content.ReadAsStreamAsync();
                    return File(contentStream, "application/zip", $"EarthDayMap_{currentID}_{timestamp}.zip");
                }
            }

            var existingMap = _context.EarthDayMaps.Where(ed => ed.PlayerId == currentID).FirstOrDefault();
            if (existingMap != null) {
                _context.EarthDayMaps.Remove(existingMap);
                var score = await _context.Scores.Where(s => s.Leaderboard.Song.Hash == "EarthDay2025" && s.PlayerId == currentID).Include(s => s.ContextExtensions).FirstOrDefaultAsync();
                if (score != null) {
                    foreach (var extension in score.ContextExtensions) {
                        _context.ScoreContextExtensions.Remove(extension);
                    }
                    _context.Scores.Remove(score);

                    await SocketController.ScoreWasRejected(score, _context);
                    await _context.BulkSaveChangesAsync();
                }
            }

            Random random = new Random();

            var scores = (await _context.Scores
                .Where(s => s.PlayerId == currentID && s.ValidForGeneral && s.Timepost < 1735693261) // Assuming 1735693261 is a valid timestamp cutoff
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(s => s.Leaderboard)
                .ThenInclude(l => l.Song)
                .Select(s => new {
                    s.Leaderboard.Difficulty.Requirements,
                    s.Timepost,
                    s.Leaderboard.Song.Hash,
                    s.Leaderboard.Song.Duration,
                    s.Leaderboard.Difficulty,
                    LeaderboardId = s.LeaderboardId // Needed for filtering later? Maybe just difficulty.
                }).ToListAsync())
                .Where(s =>
                    !s.Requirements.HasFlag(Requirements.Noodles) &&
                    !s.Requirements.HasFlag(Requirements.MappingExtensions) &&
                    !s.Requirements.HasFlag(Requirements.Vivify))
                .ToList()
                .OrderBy(s => random.Next(2000))
                .Take(200)
                .ToList(); // Convert to List for potential modification/removal

            if (!scores.Any()) {
                 return BadRequest("No suitable scores found for generation.");
            }

            List<ParsedMapData> parsedMaps = new List<ParsedMapData>();

            foreach (var score in scores)
            {
                string? mapPath = await _downloader.Map(score.Hash);
                if (mapPath == null) continue; // Skip if download fails

                Beatmap? mapset = null; // Use alias
                try
                {
                    mapset = _parser.TryLoadPath(mapPath);
                    if (mapset == null || mapset.Info == null) continue; // Skip if parsing fails or Info is missing
                    if (!(mapset.Info._songFilename.Contains(".ogg") || mapset.Info._songFilename.Contains(".egg"))) continue;

                    var songPath = Path.Combine(mapPath, mapset.Info._songFilename);
                    if (!System.IO.File.Exists(songPath)) continue;

                    var difficultyBeatmap = mapset.Difficulties.FirstOrDefault(d =>
                        d.Characteristic == score.Difficulty.ModeName &&
                        d.Difficulty == score.Difficulty.DifficultyName);

                    if (difficultyBeatmap != null && difficultyBeatmap.Data != null) {
                         // Construct the full Beatmap object for this difficulty
                         var singleDiffMap = new Beatmap {
                             Info = mapset.Info,
                             SongLength = mapset.SongLength,
                             Difficulties = new List<DifficultySet> { difficultyBeatmap } // Assign the found DifficultySet
                         };

                         parsedMaps.Add(new ParsedMapData {
                             Hash = score.Hash,
                             Beatmap = singleDiffMap, // Store the single difficulty Beatmap
                             Difficulty = score.Difficulty, // Keep original DifficultyDescription
                             Duration = score.Duration,
                             Timepost = score.Timepost,
                             InitialBpm = mapset.Info._beatsPerMinute, // Get initial BPM from Info
                             MapInfo = mapset.Info, // Store Info
                             SongPath = songPath
                         });
                    }
                }
                catch (FileNotFoundException)
                {
                    // Optional: Log error or retry download/parse
                    // Directory.Delete(mapPath, true); // Consider consequences before enabling auto-delete
                    continue;
                }
                 catch (Exception ex) {
                    // Log general parsing errors
                    Console.WriteLine($"Error parsing map {score.Hash}: {ex.Message}");
                    continue;
                 }
            }

            if (!parsedMaps.Any()) {
                return BadRequest("Could not download or parse any maps from the selected scores.");
            }

            // --- Map Generation Logic Starts Here ---
            List<ParsedMapData> availableMaps = new List<ParsedMapData>(parsedMaps);
            Beatmap generatedMap; // Use alias, will be initialized from seed
            float generatedDuration = 0;
            float targetDuration = 3600; // 1 hour
            

            // 1. Select and cut initial seed map
            if (!availableMaps.Any()) {
                 return BadRequest("No maps available to start generation.");
            }

            int seedIndex = random.Next(availableMaps.Count);
            ParsedMapData seedMapData = availableMaps[seedIndex];
            availableMaps.RemoveAt(seedIndex); // Remove used map

            float initialCutTime = (float)(random.NextDouble() * (40 - 30) + 30); // Random time between 30 and 40 seconds

            // Use the selected map as the seed and cut it
            generatedMap = CutMap(seedMapData.Beatmap, initialCutTime);
            generatedDuration = initialCutTime; // Set initial duration

            using var reader0 = new NVorbis.VorbisReader(seedMapData.SongPath);
            var _audioSampleRate = 44100;
            var _audioChannels = 2;
            long samplesToRead0 = (long)(initialCutTime * reader0.SampleRate * reader0.Channels);
            float[] buffer0 = new float[4096]; // Read in chunks
            long samplesRead0 = 0;
            List<float> _generatedAudioSamples = new List<float>();

            while (samplesRead0 < samplesToRead0)
            {
                int count = (int)Math.Min(buffer0.Length, samplesToRead0 - samplesRead0);
                int read = reader0.ReadSamples(buffer0, 0, count);
                if (read <= 0) break; // End of stream reached prematurely
                _generatedAudioSamples.AddRange(buffer0.Take(read));
                samplesRead0 += read;
            }

            byte[] songOutput = ConvertRawPCMFile(_audioSampleRate, _audioChannels, _generatedAudioSamples.ToArray(), reader0.SampleRate, reader0.Channels, initialCutTime);
            _generatedAudioSamples = new List<float>();

            //2.Loop to splice maps
            while (generatedDuration < targetDuration && availableMaps.Any()) {
                // Get pattern from end of generatedMap
                var endPattern = GetEndPattern(generatedMap);
                Console.WriteLine($"Generated map length: {generatedMap.SongLength:F2}s. Looking for pattern with {endPattern.Count} notes.");

                // Find a map with a matching start pattern
                var matchResult = FindPatternMatch(availableMaps, endPattern, random);

                if (matchResult.HasValue) {
                    var (matchedMapData, matchTime) = matchResult.Value;

                    Console.WriteLine($"Found match in {matchedMapData.Hash}, removing. Placeholder duration: {generatedDuration:F2}s");

                    float spliceDuration = availableMaps.Count == 1 ? (float)matchedMapData.Duration - matchTime : (float)(random.NextDouble() * (50 - 10) + 10); // 10-50 seconds

                    bool shouldAppend = true;

                    if (availableMaps.Count != 1) {
                        for (int i = 0; i < 3; i++) {
                            var subMapPattern = GetEndPattern(CutMap(matchedMapData.Beatmap, matchTime + spliceDuration));
                            var subMatchResult = FindPatternMatch(availableMaps.Where(m => m != matchedMapData).ToList(), subMapPattern, random);

                            if (subMatchResult.HasValue) {
                                break;
                            } else if (i < 2) {
                                spliceDuration = (float)(random.NextDouble() * (70 - 10) + 10);
                            } else {
                                Console.WriteLine($"Warning: Can't find continuation for {matchedMapData.Hash}, skipping");
                                shouldAppend = false;
                            }
                        }
                    }

                    if (!shouldAppend) {
                        availableMaps.Remove(matchedMapData);
                        continue;
                    }

                    try {
                        using var reader = new NVorbis.VorbisReader(matchedMapData.SongPath);

                        long startSample = (long)(matchTime * reader.SampleRate); // SamplePosition is per channel
                        long samplesToRead = (long)(spliceDuration * reader.SampleRate * reader.Channels);

                        if (samplesToRead > 0 && startSample < reader.TotalSamples) {
                            reader.SamplePosition = startSample;
                            float[] buffer = new float[4096];
                            long samplesRead = 0;
                            int readCount = 0;
                            Console.WriteLine($"Reading {samplesToRead} audio samples from {matchedMapData.Hash} starting at sample {startSample * reader.Channels}");

                            while (samplesRead < samplesToRead) {
                                int count = (int)Math.Min(buffer.Length, samplesToRead - samplesRead);
                                readCount = reader.ReadSamples(buffer, 0, count);
                                if (readCount <= 0) {
                                    Console.WriteLine($"Warning: End of audio stream reached prematurely while reading from {matchedMapData.Hash}");
                                } else {
                                    _generatedAudioSamples.AddRange(buffer.Take(readCount));
                                    samplesRead += readCount;
                                }
                            }

                            songOutput = songOutput
                                .Concat(ConvertRawPCMFile(_audioSampleRate, _audioChannels, _generatedAudioSamples.ToArray(), reader.SampleRate, reader.Channels, spliceDuration))
                                .ToArray();
                            _generatedAudioSamples = new List<float>();

                            Console.WriteLine($"Appended {samplesRead} audio samples ({_generatedAudioSamples.Count} total).");
                        } else {
                            Console.WriteLine($"Warning: Calculated samples to read is zero or start sample is out of bounds for {matchedMapData.Hash}. Skipping audio append.");
                            throw new Exception();
                        }
                    } catch (Exception ex) {
                        Console.WriteLine($"Error appending audio segment from {matchedMapData.SongPath}: {ex.Message}");
                        availableMaps.Remove(matchedMapData);
                        continue;
                    }

                    generatedMap = AppendMapSegment(generatedMap, matchedMapData, matchTime, generatedDuration, spliceDuration);
                    generatedDuration += spliceDuration;
                    availableMaps.Remove(matchedMapData);
                } else {
                    // No match found, break the loop
                    Console.WriteLine("No matching pattern found in any available maps. Stopping generation.");
                    break;
                }
            }

            generatedMap.Info._beatsPerMinute = 160;
            generatedMap.Difficulties[0].Data.bpmEvents = new List<BpmEvent>();

            ToBeats(generatedMap.Difficulties[0].Data);

            // --- Save and return the generated map ---
            try
            {
                // Create a temporary directory for the map files
                string tempDir = Path.Combine(Path.GetTempPath(), "EarthDayMap_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                // Create Info.dat
                var infoData = generatedMap.Info;
                // Update info with EarthDay specific data
                infoData._songName = "Earth Day 2025";
                infoData._songSubName = "RECYCLED";
                infoData._songAuthorName = "BeatLeader";
                infoData._levelAuthorName = "BeatLeader";
                infoData._songFilename = "Song.egg";
                infoData._coverImageFilename = "Cover.jpg";
                infoData._environmentName = "BigMirrorEnvironment";
                infoData._allDirectionsEnvironmentName = "BigMirrorEnvironment";
                infoData._environmentNames = new object[] { };
                infoData._colorSchemes = new object[] { };

                infoData._difficultyBeatmapSets = new List<_Difficultybeatmapsets> {
                    new _Difficultybeatmapsets {
                        _beatmapCharacteristicName = "Standard",
                        _difficultyBeatmaps = new List<_Difficultybeatmaps> {
                            new _Difficultybeatmaps {
                                _difficulty = "ExpertPlus",
                                _difficultyRank = 9,
                                _beatmapFilename = "ExpertPlus.dat",
                                _lightshowDataFilename = "",
                                _noteJumpMovementSpeed = 19,
                                _noteJumpStartBeatOffset = 0.1f,
                                _beatmapColorSchemeIdx = 0,
                                _environmentNameIdx = 0,
                                _customData = new _Customdata1 {
                                    _difficultyLabel = "Shuffle Jam",
                                    _requirements = new List<string> { },
                                    _suggestions = new List<string> { }
                                }
                            }
                        }
                    }
                };

                // Serialize and save Info.dat
                string infoJson = Newtonsoft.Json.JsonConvert.SerializeObject(infoData, Newtonsoft.Json.Formatting.Indented);
                string infoPath = Path.Combine(tempDir, "Info.dat");
                await System.IO.File.WriteAllTextAsync(infoPath, infoJson);

                // Serialize and save ExpertPlus.dat
                var difficultyData = generatedMap.Difficulties.First().Data;
                string difficultyJson = Newtonsoft.Json.JsonConvert.SerializeObject(difficultyData, Newtonsoft.Json.Formatting.Indented);
                string difficultyPath = Path.Combine(tempDir, "ExpertPlus.dat");
                await System.IO.File.WriteAllTextAsync(difficultyPath, difficultyJson);

                // Copy the cover image
                string sourceImagePath = Path.Combine(_webHostEnvironment.WebRootPath, "EarthDayMapCover.jpg");
                string destImagePath = Path.Combine(tempDir, "Cover.jpg");
                System.IO.File.Copy(sourceImagePath, destImagePath);

                // Copy the song file (assuming we have audio data from the generated map)
                // For testing, we'll use the first song file we have
                string songFilePath = Path.Combine(tempDir, "Song.egg");

                await System.IO.File.WriteAllBytesAsync(songFilePath, songOutput);

                
                // Create the zip file
                string zipPath = Path.Combine(Path.GetTempPath(), $"EarthDayMap_{currentID}_{timestamp}.zip");
                if (System.IO.File.Exists(zipPath))
                {
                    System.IO.File.Delete(zipPath);
                }

                var prependBytes = System.Text.Encoding.UTF8.GetBytes(infoJson);

                string hash = CreateSha1FromFilesWithPrependBytes(prependBytes, new string[] { difficultyPath });

                _context.EarthDayMaps.Add(new EarthDayMap {
                    Timeset = timestamp,
                    PlayerId = currentID,
                    Hash = hash
                });
                await _context.SaveChangesAsync();

                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, zipPath);

                // Clean up the temporary directory
                Directory.Delete(tempDir, true);

                // Return the zip file
                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
                HttpContext.Response.OnCompleted(async () => {
                    await _s3Client.UploadSong($"EarthDayMap_{currentID}_{timestamp}.zip", System.IO.File.OpenRead(zipPath));
                    System.IO.File.Delete(zipPath);
                });
                return File(fileBytes, "application/zip", $"EarthDayMap_{currentID}_{timestamp}.zip");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error creating map package: {ex.Message}");
            }
        }


         // Cuts a map at a specific time, keeping elements before the cut
        private Beatmap? CutMap(Beatmap sourceMap, float cutTimeInSeconds)
        {
            if (sourceMap == null || sourceMap.Difficulties == null || sourceMap.Difficulties.First().Data == null) {
                 return null;
            }
            if (sourceMap.Info == null) {
                return null;
            }

            var sourceData = sourceMap.Difficulties.First().Data;

             // Create a new DifficultyV3 to store the cut data
             // Deep copy non-list properties if they are reference types and mutable
            var cutDifficultyData = new DifficultyV3
            {
                Version = sourceData.Version,
                // Filter lists based on cutBeat
                bpmEvents = sourceData.bpmEvents?.Where(e => e.Seconds < cutTimeInSeconds).ToList() ?? new List<BpmEvent>(),
                njsEvents = sourceData.njsEvents?.Where(e => e.Seconds < cutTimeInSeconds).ToList() ?? new List<NjsEvent>(), // Assuming NjsEvent has 'Beats'
                Rotations = sourceData.Rotations?.Where(e => e.Seconds < cutTimeInSeconds).ToList() ?? new List<RotationEvent>(),
                Notes = sourceData.Notes?.Where(n => n.Seconds < cutTimeInSeconds).ToList() ?? new List<Note>(),
                Bombs = sourceData.Bombs?.Where(b => b.Seconds < cutTimeInSeconds).ToList() ?? new List<Bomb>(),
                Walls = sourceData.Walls?.Where(w => w.Seconds < cutTimeInSeconds).ToList() ?? new List<Wall>(),
                Arcs = sourceData.Arcs?.Where(a => a.Seconds < cutTimeInSeconds).ToList() ?? new List<Arc>(),
                Chains = sourceData.Chains?.Where(c => c.Seconds < cutTimeInSeconds).ToList() ?? new List<Chain>(),
                Lights = sourceData.Lights?.Where(l => l.Seconds < cutTimeInSeconds).ToList() ?? new List<Light>(),
                colorBoostBeatmapEvents = sourceData.colorBoostBeatmapEvents?.Where(e => e.Seconds < cutTimeInSeconds).ToList() ?? new List<Colorboostbeatmapevent>(),
                lightColorEventBoxGroups = sourceData.lightColorEventBoxGroups?.Where(e => e.Seconds < cutTimeInSeconds).ToList(),
                lightRotationEventBoxGroups = sourceData.lightRotationEventBoxGroups?.Where(e => e.Seconds < cutTimeInSeconds).ToList(),
                lightTranslationEventBoxGroups = sourceData.lightTranslationEventBoxGroups?.Where(e => e.Seconds < cutTimeInSeconds).ToList(),
                vfxEventBoxGroups = new object[] { },
                // Copy simple properties
                useNormalEventsAsCompatibleEvents = sourceData.useNormalEventsAsCompatibleEvents,
                basicEventTypesWithKeywords = sourceData.basicEventTypesWithKeywords, // Assuming this is safe to copy shallowly
                // Waypoints might need filtering if they have timing associated? Assuming not for now.
                Waypoints = new object[] { },
                // customData might need deep cloning or selective copying if complex
                customData = sourceData.customData // Shallow copy for now, might need adjustment
            };

            // Reconstruct the Beatmap structure
            var cutMap = new Beatmap
            {
                 // Deep copy Info to avoid modifying the original
                 Info = sourceMap.Info, // Assuming Info is okay to share or clone if necessary later
                 SongLength = cutTimeInSeconds, // Set new duration
                 Difficulties = new List<DifficultySet> { new DifficultySet(
                     sourceMap.Difficulties.First().Difficulty,
                     sourceMap.Difficulties.First().Characteristic,
                     cutDifficultyData,
                     sourceMap.Difficulties.First().BeatMap // Reusing this might be fine if it only contains filenames/metadata
                 ) }
            };

            return cutMap;
        }

        private float BeatsFromSecond(float seconds, float bpm) {
            return (seconds * bpm) / 60;
        }

        private void ToBeats(DifficultyV3 diff)
        {
            var _bpm = 160;
            foreach (var item in diff.Notes) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
            }
            foreach (var item in diff.Bombs) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
            }
            foreach (var item in diff.Walls) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
                item.DurationInBeats = BeatsFromSecond(item.DurationInSeconds, _bpm);
            }
            foreach (var item in diff.Arcs) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
                item.TailInBeats = BeatsFromSecond(item.TailInSeconds, _bpm);
            }

            foreach (var item in diff.Chains) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
                item.TailInBeats = BeatsFromSecond(item.TailInSeconds, _bpm);
            }
            foreach (var item in diff.Lights) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
            }
            foreach (var item in diff.colorBoostBeatmapEvents) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
            }
            foreach (var item in diff.Rotations) {
                item.Beats = BeatsFromSecond(item.Seconds, _bpm);
            }
        }

         // Gets notes from the last second of the map to serve as a pattern
         private List<Note> GetEndPattern(Beatmap map)
         {
             if (map?.Difficulties == null || !map.Difficulties.Any() || map.Difficulties.First().Data?.Notes == null)
             {
                 return new List<Note>(); // Return empty list if no notes data
             }
 
             var notes = map.Difficulties.First().Data.Notes;
             double mapEndTime = map.SongLength;
             double patternStartTime = Math.Max(0, mapEndTime - 1.0); // Start time for the 1-second pattern window
 
             // Filter notes that fall within the last second
             var patternNotes = notes.Where(n => n.Seconds >= patternStartTime && n.Seconds <= mapEndTime)
                                    .OrderBy(n => n.Seconds) // Order by time for potential relative comparisons later
                                    .ToList();
 
             return patternNotes;
         }
 
         // Finds a map in the available list that starts with a pattern similar to the endPattern
         private (ParsedMapData MapData, float MatchTime)? FindPatternMatch(
             List<ParsedMapData> availableMaps,
             List<Note> endPattern,
             Random random,
             float similarityThreshold = 0.5f, // Require 50% of notes to match
             float timeTolerance = 0.1f // Allow 100ms difference in relative timing
             )
         {
             if (endPattern == null || !endPattern.Any()) {
                 // If the previous segment ended with no notes in the last second,
                 // we can technically match anywhere. Let's pick a random map and start time (e.g., 0).
                 if (availableMaps.Any()) {
                      var randomMap = availableMaps[random.Next(availableMaps.Count)];
                      Console.WriteLine("End pattern is empty, picking random map to start next segment.");
                      return (randomMap, 0.0f); // Start splicing from the beginning
                 }
                 return null; // No maps available
             }
 
             // Create a shuffled list of maps to try matching against
             var mapsToSearch = availableMaps.OrderBy(x => random.Next()).ToList();
 
             // Calculate relative times for the endPattern (relative to the first note in the pattern)
             double patternStartTimeAbs = endPattern.First().Seconds;
             var patternRelative = endPattern.Select(p => new {
                 Note = p,
                 RelativeSeconds = p.Seconds - patternStartTimeAbs
             }).ToList();
 
             foreach (var mapData in mapsToSearch)
             {
                 var candidateNotes = mapData.Beatmap?.Difficulties?.FirstOrDefault()?.Data?.Notes;
                 if (candidateNotes == null || !candidateNotes.Any()) continue;
 
                 // Iterate through potential start notes in the candidate map
                 for (int i = 0; i < candidateNotes.Count; i++)
                 {
                     var potentialStartNote = candidateNotes[i];
                     double potentialMatchStartTime = potentialStartNote.Seconds;
 
                     // Optimization: If the first pattern note doesn't match the potentialStartNote, skip detailed check?
                     // Maybe too strict? Let's keep the window approach for now.
 
                     // Define the 1-second window in the candidate map to search for matches
                     double windowEndTime = potentialMatchStartTime + 1.0; // Match within a 1s window
                      var windowNotes = candidateNotes
                         .Skip(i) // Start searching from the potentialStartNote index
                         .TakeWhile(n => n.Seconds <= windowEndTime) // Look within the time window
                         .ToList();
 
                      if (!windowNotes.Any()) continue; // No notes in window
 
                     int matchesFound = 0;
                     HashSet<Note> usedWindowNotes = new HashSet<Note>(); // Ensure window notes are matched at most once
 
                     // Try to match each note in the endPattern
                     foreach (var patternNoteInfo in patternRelative)
                     {
                          Note? bestMatch = null;
                          double minDiff = timeTolerance;
 
                          // Find the best matching note in the window for this pattern note
                          foreach(var windowNote in windowNotes) {
                             if (usedWindowNotes.Contains(windowNote)) continue; // Already matched this window note
 
                             // Basic check: position and color must match
                             if (windowNote.x == patternNoteInfo.Note.x &&
                                 windowNote.y == patternNoteInfo.Note.y &&
                                 windowNote.Color == patternNoteInfo.Note.Color /*&&
                                 windowNote.CutDirection == patternNoteInfo.Note.CutDirection*/) // Optional: relax cut direction match
                             {
                                 // Check relative timing
                                 double expectedTimeInWindow = potentialMatchStartTime + patternNoteInfo.RelativeSeconds;
                                 double timeDiff = Math.Abs(windowNote.Seconds - expectedTimeInWindow);
 
                                 if (timeDiff < minDiff) {
                                      minDiff = timeDiff;
                                      bestMatch = windowNote;
                                 }
                             }
                          }
 
                          // If a suitable match was found within tolerance, count it
                          if (bestMatch != null) {
                              matchesFound++;
                              usedWindowNotes.Add(bestMatch); // Mark as used
                          }
                     }
 
                     // Check if similarity threshold is met
                     if ((float)matchesFound / endPattern.Count >= similarityThreshold)
                     {
                         // Match found!
                         Console.WriteLine($"Found pattern match in map {mapData.Hash} at time {potentialMatchStartTime:F2}s ({matchesFound}/{endPattern.Count} notes)");
                         return (mapData, (float)potentialMatchStartTime);
                     }
                 }
             }
 
             // No match found in any map
             Console.WriteLine("Could not find a suitable pattern match in any remaining maps.");
             return null;
         }

        private void AppendAdjusted<T>(
            List<T> baseList, 
            List<T> sourceList, 
            float startTimeInSource,
            float endTimeInBase,
            float spliceDuration) where T : BeatmapObject {
            if (sourceList == null || baseList == null) return;

            var itemsToAdd = sourceList
                .Where(obj => obj.Seconds >= startTimeInSource && obj.Seconds < startTimeInSource + spliceDuration)
                .Select(obj => {
                    // --- Proper Cloning Needed Here ---
                    // Example manual clone for Note (replace with robust cloning)
                    T? clone = null;
                    try {
                        if (obj is Note n) clone = (T)(object)new Note { 
                            Beats = n.Beats, 
                            Seconds = n.Seconds, 
                            x = n.x, 
                            y = n.y, 
                            Color = n.Color, 
                            CutDirection = n.CutDirection, 
                            AngleOffset = n.AngleOffset };
                        else if (obj is Bomb b) clone = (T)(object)new Bomb { Beats = b.Beats, Seconds = b.Seconds, x = b.x, y = b.y };
                        else if (obj is Wall w) clone = (T)(object)new Wall { 
                            Beats = w.Beats, 
                            Seconds = w.Seconds, 
                            x = w.x, 
                            y = w.y, 
                            DurationInBeats = w.DurationInBeats,
                            DurationInSeconds = w.DurationInSeconds,
                            Width = w.Width, 
                            Height = w.Height };
                        else if (obj is Arc a) clone = (T)(object)new Arc { 
                            Beats = a.Beats, 
                            Seconds = a.Seconds, 
                            x = a.x, 
                            y = a.y, 
                            Color = a.Color, 
                            CutDirection = a.CutDirection, 
                            TailInBeats = a.TailInBeats,
                            TailInSeconds = a.TailInSeconds - startTimeInSource + endTimeInBase,
                            TailCutDirection = a.TailCutDirection,
                            tx = a.tx, 
                            ty = a.ty };
                        else if (obj is Chain c) clone = (T)(object)new Chain { 
                            Beats = c.Beats, 
                            Seconds = c.Seconds, 
                            x = c.x, 
                            y = c.y, 
                            Color = c.Color, 
                            CutDirection = c.CutDirection, 
                            TailInBeats = c.TailInBeats, 
                            TailInSeconds = c.TailInSeconds - startTimeInSource + endTimeInBase,
                            tx = c.tx, ty = c.ty, SliceCount = c.SliceCount, Squish = c.Squish /* + other props */ };
                        else if (obj is Light l) clone = (T)(object)new Light { Beats = l.Beats, Seconds = l.Seconds, Type = l.Type, Value = l.Value, f = l.f };
                        else if (obj is RotationEvent r) clone = (T)(object)new RotationEvent { Beats = r.Beats, Seconds = r.Seconds, Event = r.Event, Rotation = r.Rotation };
                        else if (obj is NjsEvent njs) clone = (T)(object)new NjsEvent { Beats = njs.Beats, Seconds = njs.Seconds, Delta = njs.Delta, Easing = njs.Easing };
                        else if (obj is Colorboostbeatmapevent boost) clone = (T)(object)new Colorboostbeatmapevent { Beats = boost.Beats, Seconds = boost.Seconds, On = boost.On };
                        else if (obj is BpmEvent bpm) clone = (T)(object)new BpmEvent { Beats = bpm.Beats, Seconds = bpm.Seconds, Bpm = bpm.Bpm };
                    } catch (Exception ex) {
                        Console.WriteLine($"Error cloning object of type {obj.GetType().Name}: {ex.Message}");
                        return null; // Return default to filter out later
                    }

                    clone.Seconds -= startTimeInSource;
                    clone.Seconds += endTimeInBase;

                    return clone;
                })
                .Where(c => c != null) // Filter out failures
                .OrderBy(n => n.Seconds)
                .ToList();

            baseList.AddRange(itemsToAdd);
        }

        // Appends a segment from sourceMapData to baseMap
        private Beatmap AppendMapSegment(
            Beatmap baseMap,
            ParsedMapData sourceMapData,
            float startTimeInSource,
            float currentDuration,
            float spliceDuration)
        {
            var baseDiffData = baseMap.Difficulties.First().Data;
            var baseLastBeats = baseDiffData.Notes.OrderBy(n => n.Beats).Last().Beats;
            var sourceDiffData = sourceMapData.Beatmap.Difficulties.First().Data;

            // Append adjusted objects to base map lists
            AppendAdjusted(baseDiffData.Notes, sourceDiffData.Notes, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.Bombs, sourceDiffData.Bombs, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.Walls, sourceDiffData.Walls, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.Arcs, sourceDiffData.Arcs, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.Chains, sourceDiffData.Chains, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.Lights, sourceDiffData.Lights, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.Rotations, sourceDiffData.Rotations, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.njsEvents, sourceDiffData.njsEvents, startTimeInSource, currentDuration, spliceDuration);
            AppendAdjusted(baseDiffData.colorBoostBeatmapEvents, sourceDiffData.colorBoostBeatmapEvents, startTimeInSource, currentDuration, spliceDuration);

            // TODO: Handle Event Box Groups if necessary

            // --- 6. Update Base Map Length ---
            baseMap.SongLength += spliceDuration;

            return baseMap;
        }

        private static readonly int WriteBufferSize = 512;

        // Helper function to safely get a sample from the PCM buffer, handling boundaries
        private static float GetPcmSample(float[] pcmSamples, int index, int channel, int numChannels)
        {
            int sampleIndex = index * numChannels + channel;
            if (sampleIndex >= 0 && sampleIndex < pcmSamples.Length)
            {
                return pcmSamples[sampleIndex];
            }
            return 0.0f; // Return silence if index is out of bounds
        }

        private static byte[] ConvertRawPCMFile(
            int outputSampleRate,
            int outputChannels,
            float[] pcmSamples,
            int pcmSampleRate,
            int pcmChannels,
            float expectedDuration) // Added expected duration parameter
        {
            if (pcmSampleRate == 0 || pcmChannels == 0 || pcmSamples.Length == 0 || expectedDuration <= 0) {
                 // Handle invalid input
                 Console.WriteLine("Warning: Invalid PCM data or expected duration provided for conversion.");
                 return new byte[0]; // Return empty byte array or throw an exception
            }

            // Target number of samples *per channel* in the output, based on expected duration
            int numOutputSamplesPerChannel = (int)Math.Round(expectedDuration * outputSampleRate);
            if (numOutputSamplesPerChannel == 0) {
                Console.WriteLine("Warning: Calculated output samples is zero.");
                return new byte[0];
            }

            float[][] outSamples = new float[outputChannels][];
            for (int ch = 0; ch < outputChannels; ch++)
            {
                outSamples[ch] = new float[numOutputSamplesPerChannel];
            }

            double inputSampleRateDouble = pcmSampleRate;
            double outputSampleRateDouble = outputSampleRate;

            // Resample using linear interpolation to fill the exact number of output samples
            for (int i = 0; i < numOutputSamplesPerChannel; i++)
            {
                // Calculate the corresponding fractional position in the input signal
                // This position maps the output sample 'i' back to the input timeline
                double inputPosition = i * inputSampleRateDouble / outputSampleRateDouble;
                int inputIndex1 = (int)Math.Floor(inputPosition);
                int inputIndex2 = inputIndex1 + 1;
                double factor = inputPosition - inputIndex1; // Interpolation factor

                for (int ch = 0; ch < outputChannels; ch++)
                {
                    // Get the two nearest input samples for the current channel
                    int sourceChannel = Math.Min(ch, pcmChannels - 1); // Use the available source channel
                    float sample1 = GetPcmSample(pcmSamples, inputIndex1, sourceChannel, pcmChannels);
                    float sample2 = GetPcmSample(pcmSamples, inputIndex2, sourceChannel, pcmChannels);

                    // Linearly interpolate
                    outSamples[ch][i] = (float)(sample1 * (1.0 - factor) + sample2 * factor);
                }
            }

            // GenerateFile will encode exactly the samples provided in outSamples
            return GenerateFile(outSamples, outputSampleRate, outputChannels);
        }

        private static byte[] GenerateFile(float[][] floatSamples, int sampleRate, int channels)
        {
            using MemoryStream outputData = new MemoryStream();

            // Stores all the static vorbis bitstream settings
            var info = VorbisInfo.InitVariableBitRate(channels, sampleRate, 0.5f);

            // set up our packet->stream encoder
            var serial = new Random().Next();
            var oggStream = new OggStream(serial);

            // =========================================================
            // HEADER
            // =========================================================
            var comments = new Comments();
            comments.AddTag("ARTIST", "BeatLeader");
            comments.AddTag("TITLE", "Earth Day 2025 - RECYCLED");

            var infoPacket = HeaderPacketBuilder.BuildInfoPacket(info);
            var commentsPacket = HeaderPacketBuilder.BuildCommentsPacket(comments);
            var booksPacket = HeaderPacketBuilder.BuildBooksPacket(info);

            oggStream.PacketIn(infoPacket);
            oggStream.PacketIn(commentsPacket);
            oggStream.PacketIn(booksPacket);

            FlushPages(oggStream, outputData, true); // Flush headers

            // =========================================================
            // BODY (Audio Data)
            // =========================================================
            if (floatSamples == null || floatSamples.Length == 0 || floatSamples[0] == null || floatSamples[0].Length == 0)
            {
                 Console.WriteLine("Warning: No audio data to encode.");
                 return outputData.ToArray();
            }
            var processingState = ProcessingState.Create(info);
            long totalSamples = floatSamples[0].Length; // Samples per channel

            // Write data in chunks
            for (long readIndex = 0; readIndex < totalSamples; readIndex += WriteBufferSize)
            {
                long samplesToWrite = Math.Min(WriteBufferSize, totalSamples - readIndex);

                if (samplesToWrite > 0)
                {
                    // Provide the exact number of samples to write in this chunk
                    processingState.WriteData(floatSamples, (int)samplesToWrite, (int)readIndex);
                }

                // Flush intermediate pages as they become available
                while (!oggStream.Finished && processingState.PacketOut(out OggPacket packet))
                {
                    oggStream.PacketIn(packet);
                    FlushPages(oggStream, outputData, false);
                }
            }

            // Signal end of stream after all samples are written
            processingState.WriteEndOfStream();

            // Flush any remaining packets and pages
            while (!oggStream.Finished && processingState.PacketOut(out OggPacket packet))
            {
                oggStream.PacketIn(packet);
                FlushPages(oggStream, outputData, false); // Flush remaining intermediate pages
            }

            FlushPages(oggStream, outputData, true); // Final flush for the last page

            return outputData.ToArray();
        }

         private static void FlushPages(OggStream oggStream, Stream output, bool force)
         {
             while (oggStream.PageOut(out OggPage page, force))
             {
                 output.Write(page.Header, 0, page.Header.Length);
                 output.Write(page.Body, 0, page.Body.Length);
             }
         }

        private static float SineSample(int sample, float frequency, int sampleRate)
        {
            float sampleT = ((float)sample) / sampleRate;
            return MathF.Sin(sampleT * MathF.PI * 2.0f * frequency);
        }

        private static float ByteToSample(short pcmValue)
        {
            return pcmValue / 128f;
        }

        private static float ShortToSample(short pcmValue)
        {
            return pcmValue / 32768f;
        }

        /// <summary>
        /// We cheat on the WAV header; we just bypass the header and never
        /// verify that it matches 16bit/stereo/44.1kHz.This is just an
        /// example, after all.
        /// </summary>
        private static void StripWavHeader(BinaryReader stdin)
        {
            var tempBuffer = new byte[6];
            for (var i = 0; (i < 30) && (stdin.Read(tempBuffer, 0, 2) > 0); i++)
            {
                if ((tempBuffer[0] == 'd') && (tempBuffer[1] == 'a'))
                {
                    stdin.Read(tempBuffer, 0, 6);
                    break;
                }
            }
        }

        enum PcmSample : int
        {
            EightBit = 1,
            SixteenBit = 2
        }

        public static string CreateSha1FromFilesWithPrependBytes(IEnumerable<byte> prependBytes, IEnumerable<string> files)
        {
            using var sha1 = SHA1.Create();
            var buffer = new byte[4096];
            var bufferIndex = 0;

            foreach (var prependByte in prependBytes)
            {
                buffer[bufferIndex++] = prependByte;
                if (bufferIndex == buffer.Length)
                {
                    sha1.TransformBlock(buffer, 0, buffer.Length, null, 0);
                    bufferIndex = 0;
                }
            }

            foreach (var file in files)
            {
                using var fileStream = System.IO.File.Open(file, FileMode.Open);
                int bytesRead;
                while ((bytesRead = fileStream.Read(buffer, bufferIndex, buffer.Length - bufferIndex)) > 0)
                {
                    bufferIndex += bytesRead;
                    if (bufferIndex == buffer.Length)
                    {
                        sha1.TransformBlock(buffer, 0, buffer.Length, null, 0);
                        bufferIndex = 0;
                    }
                }
            }

            sha1.TransformFinalBlock(buffer, 0, bufferIndex);

            return ByteToHexBitFiddle(sha1.Hash);
        }

        static string ByteToHexBitFiddle(byte[] bytes)
        {
            char[] c = new char[bytes.Length * 2];
            int b;
            for (int i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                c[i * 2] = (char) (55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char) (55 + b + (((b - 10) >> 31) & -7));
            }
            return new string(c);
        }
    }
}
