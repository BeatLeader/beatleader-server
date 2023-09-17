using Newtonsoft.Json;

namespace BeatLeader_Server.Models {
    public class GraphResponse {
        public string LeaderboardId { get; set; }
        public string Diff { get; set; }
        public string Mode { get; set; }
        public string Modifiers { get; set; }
        public string SongName { get; set; }
        public string Hash { get; set; }
        public string Mapper { get; set; }
        public float Acc { get; set; }
        public string Timeset { get; set; }
        public float? Stars { get; set; }

        [JsonIgnore]
        public ModifiersRating? ModifiersRating { get; set; }
        [JsonIgnore]
        public ModifiersMap? ModifierValues { get; set; }
        [JsonIgnore]
        public float? PassRating { get; set; }
        [JsonIgnore]
        public float? AccRating { get; set; }
        [JsonIgnore]
        public float? TechRating { get; set; }
    }
}
