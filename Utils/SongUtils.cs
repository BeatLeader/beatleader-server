using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;

namespace BeatLeader_Server.Utils
{
    public class SongUtils
    {
        public static int ModeForModeName(string modeName) {
            switch (modeName) {
                case "Standard":
                    return 1;
                case "OneSaber":
                    return 2;
                case "NoArrows":
                    return 3;
                case "90Degree":
                    return 4;
                case "360Degree":
                    return 5;
                case "Lightshow":
                    return 6;
                case "Lawless":
                    return 7;
            }

            return 0;
        }

        public static int DiffForDiffName(string diffName) {
            switch (diffName) {
                case "Easy":
                case "easy":
                    return 1;
                case "Normal":
                case "normal":
                    return 3;
                case "Hard":
                case "hard":
                    return 5;
                case "Expert":
                case "expert":
                    return 7;
                case "ExpertPlus":
                case "expertPlus":
                    return 9;
            }

            return 0;
        }

        public static string DiffNameForDiff(int diff)
        {
            switch (diff)
            {
                case 1:
                    return "Easy";
                case 3:
                    return "Normal";
                case 5:
                    return "Hard";
                case 7:
                    return "Expert";
                case 9:
                    return "ExpertPlus";
            }

            return "";
        }

        public static Task<Song?> GetSongFromBeatSaver(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Proxy = null;

            WebResponse? response = null;
            Song? song = null;
            var stream = 
            Task<(WebResponse?, Song?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }
            
                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadSongFromResponse(t.Result));
        }
        private static Song? ReadSongFromResponse((WebResponse?, Song?) response)
        {
            if (response.Item1 != null) {
                using (Stream responseStream = response.Item1.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string results = reader.ReadToEnd();
                    if (string.IsNullOrEmpty(results))
                    {
                        return null;
                    }

                    dynamic? info = JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    if (info == null) return null;
                    Song result = new Song();
                    result.Author = info.metadata.songAuthorName;
                    result.Mapper = info.metadata.levelAuthorName;
                    result.Name = info.metadata.songName;
                    result.SubName = info.metadata.songSubName;
                    result.Duration = info.metadata.duration;
                    result.Bpm = info.metadata.bpm;
                    result.MapperId = (int)info.uploader.id;
                    result.UploadTime = (int)info.uploaded.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    if (ExpandantoObject.HasProperty(info, "tags")) {
                        result.Tags = string.Join(",", info.tags);
                    }

                    dynamic currentVersion = info.versions[0];
                    result.CoverImage = currentVersion.coverURL;
                    result.DownloadUrl = currentVersion.downloadURL;
                    result.Hash = currentVersion.hash;
                    if (ExpandantoObject.HasProperty(info, "id"))
                    {
                        result.Id = info.id;
                    } else
                    {
                        result.Id = currentVersion.key;
                    }

                    List<DifficultyDescription> difficulties = new List<DifficultyDescription>();
                    dynamic diffs = currentVersion.diffs;
                    foreach (dynamic diff in diffs) {
                        DifficultyDescription difficulty = new DifficultyDescription();
                        difficulty.ModeName = diff.characteristic;
                        difficulty.Mode = SongUtils.ModeForModeName(diff.characteristic);
                        difficulty.DifficultyName = diff.difficulty;
                        difficulty.Value = SongUtils.DiffForDiffName(diff.difficulty);
                        
                        difficulty.Njs = (float)diff.njs;
                        difficulty.Notes = (int)diff.notes;
                        difficulty.Bombs = (int)diff.bombs;
                        difficulty.Nps = (float)diff.nps;
                        difficulty.Walls = (int)diff.obstacles;
                        difficulty.MaxScore = (int)diff.maxScore;
                        difficulty.Duration = result.Duration;
                        if (diff.chroma) {
                            difficulty.Requirements |= Requirements.Chroma;
                        }
                        if (diff.me) {
                            difficulty.Requirements |= Requirements.MappingExtensions;
                        }
                        if (diff.ne) {
                            difficulty.Requirements |= Requirements.Noodles;
                        }
                        if (diff.cinema) {
                            difficulty.Requirements |= Requirements.Cinema;
                        }

                        difficulties.Add(difficulty);
                    }
                    result.Difficulties = difficulties;

                    return result;
                }
            } else {
                return response.Item2;
            }   
        }

        public class LackMapCalculation
        {
            [JsonProperty("avg_pattern_rating")]
            public float PatternRating { get; set; } = 0;
            [JsonProperty("balanced_pass_diff")]
            public float PassRating { get; set; } = 0;
            [JsonProperty("linear_rating")]
            public float LinearRating { get; set; } = 0;

            [JsonProperty("balanced_tech")]
            public float TechRating { get; set; } = 0;
            [JsonProperty("low_note_nerf")]
            public float LowNoteNerf { get; set; } = 0;
        }

        public class CurvePoint
        {
            public double x { get; set; } = 0;
            public double y { get; set; } = 0;
        }

        public class RatingResult
        {
            [JsonProperty("predicted_acc")]
            public float PredictedAcc { get; set; } = 0;
            [JsonProperty("acc_rating")]
            public float AccRating { get; set; } = 0;
            [JsonProperty("lack_map_calculation")]
            public LackMapCalculation LackMapCalculation { get; set; } = new();
            [JsonProperty("pointlist")]
            public List<CurvePoint> PointList { get; set; } = new();
        }

        public class ExmachinaResponse {
            public RatingResult FS { get; set; }
            public RatingResult SFS { get; set; }
            public RatingResult SS { get; set; }
            public RatingResult none { get; set; }
        }

        public static async Task<ExmachinaResponse?> ExmachinaStars(string hash, int diff, string mode) {

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"https://stage.api.beatleader.net/ppai2/{hash}/{mode}/{diff}");
            request.Method = "GET";
            request.Proxy = null;

            try {
                return await request.DynamicResponse<ExmachinaResponse>();
            } catch (Exception e) { 
                return null; 
            }
        }
    }
}
