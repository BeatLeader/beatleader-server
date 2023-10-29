using System;
using Newtonsoft.Json;

namespace BeatLeader_Server.BeatMapEvaluator
{
    public class Note
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("c")]
        public int? Color { get; set; }
        [JsonProperty("x")]
        public int? X { get; set; }
        [JsonProperty("y")]
        public int? Y { get; set; }
        [JsonProperty("d")]
        public int? Direction { get; set; }
        [JsonProperty("a")]
        public int? AngleOffset { get; set; }

        public bool Optional() {
            return Color == null || X == null || Y == null || Direction == null || AngleOffset == null;
        }
    }

    public class Chain
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("c")]
        public int Color { get; set; }
        [JsonProperty("x")]
        public int X { get; set; }
        [JsonProperty("y")]
        public int Y { get; set; }
        [JsonProperty("d")]
        public int Direction { get; set; }
        [JsonProperty("tb")]
        public float TailTime { get; set; }
        [JsonProperty("tx")]
        public int TailX { get; set; }
        [JsonProperty("ty")]
        public int TailY { get; set; }
        [JsonProperty("sc")]
        public int SliceCount { get; set; }
        [JsonProperty("s")]
        public float SquishAmount { get; set; }
    }

    public class Slider
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("c")]
        public int Color { get; set; }
        [JsonProperty("x")]
        public int X { get; set; }
        [JsonProperty("y")]
        public int Y { get; set; }
        [JsonProperty("d")]
        public int Direction { get; set; }
        [JsonProperty("tb")]
        public float TailTime { get; set; }
        [JsonProperty("tx")]
        public int TailX { get; set; }
        [JsonProperty("ty")]
        public int TailY { get; set; }
        [JsonProperty("mu")]
        public float HeadControlPointLengthMultiplier { get; set; }
        [JsonProperty("tmu")]
        public float TailControlPointLengthMultiplier { get; set; }
        [JsonProperty("tc")]
        public int TailCutDirection { get; set; }
        [JsonProperty("m")]
        public int ArcMidAnchorMode { get; set; }
    }

    public class DiffFileV3
    {
        public string? version { get; set; }
        public Chain[]? burstSliders { get; set; }
        public Slider[]? sliders { get; set; }
        public Note[]? colorNotes { get; set; }
    }
}
