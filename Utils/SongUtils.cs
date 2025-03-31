using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net;
using System.Web;

namespace BeatLeader_Server.Utils
{
    public class SongUtils
    {
        public static async Task<(MapDetail?, WebException?)> GetSongFromBeatSaver(string hash)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.beatsaver.com/maps/hash/" + hash);
            return await request.DynamicResponseWithError<MapDetail>();
        }

        public static async Task<Song?> GetSongFromBeatSaverId(string id)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.beatsaver.com/maps/id/" + id);
            var info = await request.DynamicResponse<MapDetail>();
            if (info == null) return null;

            Song result = new Song();
            result.FromMapDetails(info);

            return result;
        }

        public static async Task<List<Song>> GetCuratedSongsFromBeatSaver(DateTime afterTime)
        {
            var songs = new List<Song>();
            DateTime? curatedAfter = afterTime;
            SearchResponse? searchResponse;

            do
            {
                string url = "https://api.beatsaver.com/maps/latest?sort=CURATED";
                if (curatedAfter.HasValue)
                {
                    url += "&after=" + HttpUtility.UrlEncode(curatedAfter.Value.ToString("yyyy-MM-ddTHH:mm:ss+00:00"));
                }

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                searchResponse = await request.DynamicResponse<SearchResponse>();

                if (searchResponse == null || searchResponse.Docs == null || !searchResponse.Docs.Any())
                {
                    break;
                }

                foreach (var mapDetail in searchResponse.Docs)
                {
                    var song = new Song();
                    song.FromMapDetails(mapDetail);
                    songs.Add(song);
                }

                curatedAfter = searchResponse.Docs.OrderByDescending(s => s.CuratedAt).FirstOrDefault()?.CuratedAt;

            } while (searchResponse.Docs.Count == 20);

            return songs;
        }

        public static async Task<List<MapDetail>?> GetTrendingSongsFromBeatSaver(DateTime afterTime)
        {
            DateTime? curatedAfter = afterTime;
            SearchResponse? searchResponse;
            string url = "https://beatsaver.com/api/search/text/0?order=Rating";
            if (curatedAfter.HasValue)
            {
                url += "&from=" + curatedAfter.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            searchResponse = await request.DynamicResponse<SearchResponse>();

            return searchResponse?.Docs;
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

        public class ExmachinaResponse
        {
            public RatingResult FS { get; set; }
            public RatingResult SFS { get; set; }
            public RatingResult BFS { get; set; }
            public RatingResult BSF { get; set; }
            public RatingResult SS { get; set; }
            public RatingResult none { get; set; }
        }

        public static async Task<ExmachinaResponse?> ExmachinaStars(string hash, int diff, string mode)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"https://stage.api.beatleader.net/ppai2/{hash}/{mode}/{diff}");
            request.Method = "GET";
            request.Proxy = null;

            try
            {
                return await request.DynamicResponse<ExmachinaResponse>();
            } catch (Exception e)
            {
                return null;
            }
        }

        public static async Task<ExmachinaResponse?> LocalExmachinaStars(string hash, int diff, string mode)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"http://localhost:5168/ppai2/{hash}/{mode}/{diff}");
            request.Method = "GET";
            request.Proxy = null;

            try
            {
                return await request.DynamicResponse<ExmachinaResponse>();
            } catch (Exception e)
            {
                return null;
            }
        }

        public static async Task<string?> ApiTags(float acc, float pass, float tech)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"http://localhost:5168/ppai2/tag?acc={acc}&pass={pass}&tech={tech}");
            request.Method = "GET";
            request.Proxy = null;

            try
            {
                return await request.DynamicResponse<string>();
            } catch (Exception e)
            {
                return null;
            }
        }

        public static async Task<ExmachinaResponse?> ExmachinaStarsLink(string link, int diff, string mode)
        {

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"https://stage.api.beatleader.net/ppai2/link/{mode}/{diff}?link={link}");
            request.Method = "GET";
            request.Proxy = null;

            try
            {
                return await request.DynamicResponse<ExmachinaResponse>();
            } catch (Exception e)
            {
                return null;
            }
        }

        public class PredictedNotes {
            public List<List<double>> Rows { get; set; }
        }

        public class PredictedAcc {
            public PredictedNotes Notes { get; set; }
        }

        public static async Task<PredictedAcc?> ExmachinaAcc(string hash, int diff, string mode, float speed)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"https://stage.api.beatleader.net/json/{hash}/{mode}/{diff}/full/time-scale/{speed}");
            request.Method = "GET";
            request.Proxy = null;

            try
            {
                return await request.DynamicResponse<PredictedAcc>();
            } catch (Exception e)
            {
                return null;
            }
        }
    }
}
