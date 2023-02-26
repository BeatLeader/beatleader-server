using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Net;
using BeatLeader_Server.Extensions;
using BeatMapEvaluator;
using BeatLeader_Server.Utils;
using System.Dynamic;

namespace BeatLeader_Server.Controllers
{
    public class DiffCheckResult
    {
        public DiffCriteriaReport CriteriaReport { get; set; }
        public string Diff { get; set; }
        public string Characteristic { get; set; }
    }
    public class ProfanityCheckResult
    {
        public string Type { get; set; }
        public string Intensity { get; set; }
        public string Value { get; set; }
        public string Line { get; set; }
    }

    public class MapCheckResult
    {
        public List<DiffCheckResult> Diffs { get; set; }
        public List<ProfanityCheckResult>? ProfanityCheck { get; set; }
    }

    public class MapEvaluationController : Controller
    {
        private readonly AppContext _context;
        private readonly ReadAppContext _readContext;

        private readonly IConfiguration _configuration;
        IWebHostEnvironment _environment;

        public MapEvaluationController(
            AppContext context,
            ReadAppContext readContext,
            IConfiguration configuration, 
            IWebHostEnvironment env)
        {
            _context = context;
            _readContext = readContext;

            _configuration = configuration;
            _environment = env;
        }

        [HttpGet("~/criteria/check/{id}")]
        public async Task<ActionResult<MapCheckResult>> Get(string id)
        {
            var song = _context.Songs.Where(s => s.Id == id).FirstOrDefault();
            if (song == null) {
                return NotFound();
            }

            var processingResults = await DownloadAndProcessMap(song.DownloadUrl, (float)song.Duration);
            if (processingResults == null) {
                return BadRequest("There was an error with processing map");
            }

            return new MapCheckResult {
                Diffs = processingResults.Select(s => new DiffCheckResult {
                    Diff = s.mapDiff._difficulty,
                    Characteristic = s.mapDiff._beatmapFilename,
                    CriteriaReport = s.report
                }).ToList(),
                ProfanityCheck = await CheckForProfanity(processingResults)
            };
        }

        [NonAction]
        public static async Task<List<MapStorageLayout>?> DownloadAndProcessMap(string downloadLink, float duration) {
            HttpWebResponse res = (HttpWebResponse) await WebRequest.Create(downloadLink).GetResponseAsync();
            if (res.StatusCode != HttpStatusCode.OK) return null;

            var archive = new ZipArchive(res.GetResponseStream());

            var infoFile = archive.Entries.FirstOrDefault(e => e.Name.ToLower() == "info.dat");
            if (infoFile == null) return null;

            var info = infoFile.Open().ObjectFromStream<json_MapInfo>();
            if (info == null) return null;

            var result = new List<MapStorageLayout>();
            foreach (var set in info.beatmapSets)
            {
                foreach (var beatmap in set._diffMaps)
                {
                    var diffFile = archive.Entries.FirstOrDefault(e => e.Name == beatmap._beatmapFilename);
                    if (diffFile == null) continue;

                    var diff = diffFile.Open().ObjectFromStream<DiffFileV2>();
                    diff.noteCount = diff._notes.Length;
                    diff.obstacleCount = diff._walls.Length;

                    var map = new MapStorageLayout(info, diff, beatmap, duration);
                    await map.ProcessDiffRegistery();
                    result.Add(map);
                }
            }

            return result;
        }

        [NonAction]
        public static async Task<List<ProfanityCheckResult>?> CheckForProfanity(List<MapStorageLayout> maps) {
            string valueToCheck = "";
            var info = maps[0].info;
            var lines = new Dictionary<int, string>();

            valueToCheck += $"| Name: {info._songName} |";
            lines[valueToCheck.Length] = valueToCheck;

            valueToCheck += $"| Subname: {info._songSubName} |";
            lines[valueToCheck.Length] = $"| Subname: {info._songSubName} |";

            foreach (var map in maps) { 
                if (map.mapDiff._customData != null && map.mapDiff._customData._difficultyLabel != null) {
                    var line = $"| Diffname: {map.mapDiff._customData._difficultyLabel} |";
                    
                    valueToCheck += line;
                    lines[valueToCheck.Length] = line;
                }
            }

            Dictionary<string, string> postData = new Dictionary<string, string>
            {
                { "text", valueToCheck },
                { "lang", "en" },
                { "mode", "standard" },
                { "api_user", "1640681755" },
                { "api_secret", "YYrPu8vUf4RQZwtM5pQA" }
            };
            dynamic response;

            try
            {
                response = await WebUtils.PostHTTPRequestAsync("https://api.sightengine.com/1.0/text/check.json", postData);
            }
            catch (Exception ex)
            {
                return null;
            }

            var result = new List<ProfanityCheckResult>();
            foreach (var item in response.profanity.matches)
            {
                result.Add(new ProfanityCheckResult {
                    Value = item.match,
                    Intensity = item.intensity,
                    Type = item.type,
                    Line = lines[lines.Keys.First(k => k > item.start)]
                });
            }

            return result;

        }
    }
}
