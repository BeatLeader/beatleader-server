using System.Text.Json.Serialization;

namespace BeatLeader_Server.Models
{
    public class CurvePoint
    {
        [JsonIgnore]
        public int Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }

        [JsonIgnore]
        public int? DifficultyId { get; set; }
        [JsonIgnore]
        public int? FSRatingId { get; set; }
        [JsonIgnore]
        public int? SSRatingId { get; set; }
        [JsonIgnore]
        public int? SFRatingId { get; set; }
    }
}
