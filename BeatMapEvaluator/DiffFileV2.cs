using System;
using Newtonsoft.Json;

namespace BeatMapEvaluator
{
    #region Info.dat json objects

    [Flags] public enum MapDiffs {
        NONE = 0,
        Easy = 1<<0, Normal = 1<<1,
        Hard = 1<<2, Expert = 1<<3,
        ExpertPlus = 1<<4
    };

    //Based off: https://bsmg.wiki/mapping/map-format.html#info-dat
    public class json_MapInfo {
        public MapDiffs mapDifficulties;
        public string? mapContextDir;
        public string? songFilePath;

        public string? _version { get; set; }
        
        public string? _songName { get; set; }
        public string? _songSubName { get; set; }
        public string? _songAuthorName { get; set; }

        public string? _levelAuthorName { get; set; }
        [JsonProperty("_beatsPerMinute")]
        public float _bpm { get; set; }

        public string? _songFilename { get; set; }
        public string? _coverImageFilename { get; set; }
        public float _songTimeOffset { get; set; }

        [JsonProperty("_difficultyBeatmapSets")]
        public json_beatMapSet[] beatmapSets { get; set; }
    }

    //Based off: https://bsmg.wiki/mapping/map-format.html#difficulty-beatmap-sets
    public class json_beatMapSet {
        public string _beatmapCharacteristicName { get; set; }
        [JsonProperty("_difficultyBeatmaps")]
        public json_beatMapDifficulty[] _diffMaps { get; set; }
    }

    //Based off: https://bsmg.wiki/mapping/map-format.html#difficulty-beatmaps
    public class json_beatMapDifficulty {
        public string? _difficulty { get; set; }
        public int _difficultyRank { get; set; }
        public string? _beatmapFilename { get; set; }
        public float _noteJumpMovementSpeed { get; set; }
        public float _noteJumpStartBeatOffset { get; set; }
        public dynamic? _customData { get; set; }

        public float bpm;
    }
    #endregion //Info.dat

    #region Diff V2 objects

    public enum NoteType {
        Left=0,Right=1,Bomb=3
    };
    public enum NoteCutDirection {
        Up=0, Down=1, Left=2, Right=3,
        UpLeft=4, UpRight=5,
        DownLeft=6, DownRight=7,
        DotNote=8
    };

    //Based off: https://bsmg.wiki/mapping/map-format.html#difficulty-file-v2
    public class DiffFileV2 {
        public string? _version { get; set; }
        public MapNote[]? _notes { get; set; }
        [JsonProperty("_obstacles")]
        public MapObstacle[]? _walls { get; set; }

        public int noteCount;
        public int obstacleCount;
    }

    //Based off: https://bsmg.wiki/mapping/map-format.html#notes-1
    public class MapNote {
        /// <summary>Time in beats</summary>
        public float _time { get; set; }
        /// <summary>Position 0-3, left to right</summary>
        public int _lineIndex { get; set; }
        /// <summary>Position 0-2, bottom to top</summary>
        public int _lineLayer { get; set; }
        /// <summary>Handed ID or bomb specifier</summary>
        public NoteType _type { get; set; }
        /// <summary>Wack ass cardinal direction or "dot"</summary>
        public NoteCutDirection _cutDirection { get; set; }

        /// <summary>Grid index bottom-left to top-right</summary>
        public int cellIndex;
        /// <summary>Time in seconds</summary>
        public float realTime { get; set; }
    }

    public enum ObstacleType {
        FullWall=0,CrouchWall=1
    };
    //Based off: https://bsmg.wiki/mapping/map-format.html#obstacles-3
    public class MapObstacle {
        /// <summary>Starting time in beats</summary>
        public float _time { get; set; }
        /// <summary>Grid space index</summary>
        public int _lineIndex { get; set; }
        /// <summary>Full or partial wall</summary>
        public ObstacleType _type { get; set; }
        /// <summary>Length in beats</summary>
        public float _duration { get; set; }
        /// <summary>Wall width in grid spaces</summary>
        public int _width { get; set; }
        
        /// <summary>
        /// <c>True</c> if wall is in the middle two rows, otherwise <c>False</c>
        /// </summary>
        public bool isInteractive;
        /// <summary>
        /// <c>True</c> if less than 13.8ms long, otherwise <c>False</c>
        /// </summary>
        public bool isShort;
        /// <summary>Time in seconds</summary>
        public float realTime { get; set; }
        /// <summary>The end time in beats</summary>
        public float endTime;
    }

    #endregion
}
