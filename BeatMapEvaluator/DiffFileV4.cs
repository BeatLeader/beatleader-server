using System;
using Newtonsoft.Json;

namespace BeatLeader_Server.BeatMapEvaluator
{
    public class NoteV4
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("i")]
        public int? Index { get; set; }
    }

    public class BombNoteV4
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("i")]
        public int? Index { get; set; }
    }

    public class ObstacleV4
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("i")]
        public int? Index { get; set; }
    }

    public class ChainV4
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("i")]
        public int? Index { get; set; }
    }

    public class ArcV4
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("i")]
        public int? Index { get; set; }
    }

    public class NJSEventV4
    {
        [JsonProperty("b")]
        public float Time { get; set; }
        [JsonProperty("i")]
        public int? Index { get; set; }
    }

    public class NJSEventDataV4
    {
        [JsonProperty("p")]
        public int? Priority { get; set; }
        [JsonProperty("e")]
        public int? Event { get; set; }
        [JsonProperty("d")]
        public float? Data { get; set; }
    }

    public class ColorNoteDataV4
    {
        [JsonProperty("x")]
        public int? X { get; set; }
        [JsonProperty("y")]
        public int? Y { get; set; }
        [JsonProperty("c")]
        public int? Color { get; set; }
        [JsonProperty("d")]
        public int? Direction { get; set; }
        [JsonProperty("a")]
        public int? AngleOffset { get; set; }
    }

    public class BombNoteDataV4
    {
        [JsonProperty("x")]
        public int? X { get; set; }
        [JsonProperty("y")]
        public int? Y { get; set; }
    }

    public class ObstacleDataV4
    {
        [JsonProperty("x")]
        public int? X { get; set; }
        [JsonProperty("y")]
        public int? Y { get; set; }
        [JsonProperty("d")]
        public float? Duration { get; set; }
        [JsonProperty("w")]
        public int? Width { get; set; }
        [JsonProperty("h")]
        public int? Height { get; set; }
    }

    public class ChainDataV4 
    {
        [JsonProperty("tx")]
        public int? TailX { get; set; }
        [JsonProperty("ty")] 
        public int? TailY { get; set; }
        [JsonProperty("c")]
        public int? SliceCount { get; set; }
        [JsonProperty("s")]
        public float? SquishFactor { get; set; }
    }

    public class ArcDataV4
    {
        [JsonProperty("m")]
        public float? HeadControlPointLengthMultiplier { get; set; }
        [JsonProperty("tm")]
        public float? TailControlPointLengthMultiplier { get; set; }
        [JsonProperty("a")]
        public int? MidAnchorMode { get; set; }
    }

    public class DiffFileV4
    {
        public string? version { get; set; }
        public NoteV4[]? colorNotes { get; set; }
        public BombNoteV4[]? bombNotes { get; set; }
        public ObstacleV4[]? obstacles { get; set; }
        public ChainV4[]? chains { get; set; }
        public ArcV4[]? arcs { get; set; }
        public NJSEventV4[]? njsEvents { get; set; }
        public ColorNoteDataV4[]? colorNotesData { get; set; }
        public BombNoteDataV4[]? bombNotesData { get; set; }
        public ObstacleDataV4[]? obstaclesData { get; set; }
        public ChainDataV4[]? chainsData { get; set; }
        public ArcDataV4[]? arcsData { get; set; }
        public NJSEventDataV4[]? njsEventData { get; set; }
    }
}

