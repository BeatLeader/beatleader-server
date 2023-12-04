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
        public static async Task<Song?> GetSongFromBeatSaver(string hash)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.beatsaver.com/maps/hash/" + hash);
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

        public static async Task<List<Song>> GetMapOfTheWeekSongs(DateTime afterTime)
        {
            var songs = new List<Song>();
            int pageNumber = 1;
            bool morePages = true;

            while (morePages)
            {
                var url = $"https://bsaber.com/songs/page/{pageNumber}/?genre=beastmap";
                var httpClient = new HttpClient();
                var html = await httpClient.GetStringAsync(url);

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                var articles = htmlDoc.DocumentNode.SelectNodes("//article[contains(@class, 'post')]");
                if (articles == null || articles.Count == 0)
                {
                    morePages = false;
                    break;
                }

                foreach (var article in articles)
                {
                    var href = article.SelectSingleNode(".//a").Attributes["href"].Value;
                    if (!string.IsNullOrEmpty(href) && href.EndsWith("/"))
                    {
                        href = href.Remove(href.Length - 1);
                    }
                    var songId = href?.Split('/').LastOrDefault();

                    if (songId == null) {
                        continue;
                    }

                    var timeNode = article.SelectSingleNode(".//time[@class='date published time']");
                    if (timeNode == null || !DateTime.TryParse(timeNode.Attributes["datetime"]?.Value, out DateTime motwTime))
                    {
                        continue;
                    }

                    if (motwTime < afterTime) {
                        break;
                    }

                    var songInfo = new Song
                    {
                        Id = songId,
                        ExternalStatuses = new List<ExternalStatus> {
                            new ExternalStatus {
                                Status = SongStatus.MapOfTheWeek,
                                Timeset = (int)motwTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                                Link = href
                            }
                        }
                    };

                    songs.Add(songInfo);
                }

                pageNumber++;
            }

            return songs;
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
