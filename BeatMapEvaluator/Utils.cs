using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace BeatMapEvaluator
{
    /// <summary>
    /// <remarks>
    /// <para name="Error">Error:
    /// <c>Something went wrong, dont fully trust this evaluation</c>
    /// </para>
    /// <para name="Failed">Failed: 
    /// <c>Map doesnt meet criteria somewhere, (logged to the reports folder)</c>
    /// </para>
    /// <para name="Passed">Passed: 
    /// <c>Map meets all criteria.. hopefully :)</c>
    /// </para>
    /// <para name="None">None: 
    /// <c>Init state but not zero because im stupid or something (I think its index casting)</c>
    /// </para>
    /// </remarks>
    /// </summary>
    public enum ReportStatus {Error=0, Failed=1, Passed=2, None};

    /// <summary>Boiler plate functions class... Utils!</summary>
    internal class Utils {
        /// <summary>Systems defined path separator "/" "\" etc.</summary>
        private readonly static char _ps = Path.DirectorySeparatorChar;

        /// <summary>
        /// Calculates Jump Distance (JD) like you would see on JDFixer.
        /// </summary>
        /// 
        /// <remarks>
        /// <para name="bpm">bpm: 
        /// <a href="https://bsmg.wiki/mapping/map-format.html#beatsperminute">"_beatsPerMinute"</a>
        /// </para>
        /// <para name="njs">njs: 
        /// <a href="https://bsmg.wiki/mapping/map-format.html#notejumpmovementspeed">"_noteJumpMovementSpeed"</a>
        /// </para>
        /// <para name="offset">offset: 
        /// <a href="https://bsmg.wiki/mapping/map-format.html#notejumpstartbeatoffset">"_noteJumpStartBeatOffset"</a>
        /// </para>
        /// </remarks>
        /// 
        /// <param name="bpm">Songs Beats/Minute</param>
        /// <param name="njs">Songs Note Jump Speed</param>
        /// <param name="offset">Songs Note Offset</param>
        /// 
        /// <returns>Jump Distance</returns>
        public static float CalculateJD(float bpm, float njs, float offset) {
            //How was this implemented? https://cdn.discordapp.com/emojis/356735628110594048.webp
            if(njs <= 0.01f) njs = 10.0f;
            float hj = 4.0f;
            float bps = 60f / bpm;
            float leadTime = njs * bps;

            float c = leadTime * hj;
            while(c > 17.999f) {
                hj /= 2.0f;
                c = leadTime * hj;
            }

            hj += offset;
            if(hj < 0.25f)
                hj = 0.25f;

            return leadTime * hj * 2.0f;
        }

        /// <summary>
        /// Calculates the Reation Time in milliseconds.
        /// </summary>
        /// <remarks>
        /// <para name="njs">njs: 
        /// <a href="https://bsmg.wiki/mapping/map-format.html#notejumpmovementspeed">"_noteJumpMovementSpeed"</a>
        /// </para>
        /// </remarks>
        /// 
        /// <param name="jd">Jump Distance</param>
        /// <param name="njs">Note Jump Speed</param>
        /// <returns>the maps reaction time (ms)</returns>
        public static float CalculateRT(float jd, float njs) { 
            if(njs > 0.002f)
                return (jd / (2.0f * njs) * 1000.0f);
            return 0.0f;
        }

        /// <summary>
        /// Checks if float absolute difference ± within small value epsilon.
        /// </summary>
        /// <param name="a">the first number</param>
        /// <param name="b">the second number</param>
        /// <param name="epsilon">the max difference</param>
        /// <returns>
        /// <c><see cref="bool">True</see></c> if numeric difference from <paramref name="a"/> to <paramref name="b"/> is within <paramref name="epsilon"/>, 
        /// <c><see cref="bool">False</see></c> otherwise.
        /// </returns>
        public static bool Approx(float a, float b, float epsilon) {
            return Math.Abs(a - b) <= epsilon;
        }

        /// <summary>
        /// Serializes a maps available difficulties to flag enum <see cref="BeatMapEvaluator.MapDiffs"/>.
        /// </summary>
        /// <param name="Sets">All "Standard" beatmap sets</param>
        /// <returns>All difficulties in the beatmap set</returns>
        public static MapDiffs GetMapDifficulties(json_beatMapDifficulty[]? Sets) {
            //Handle no standard maps
            if(Sets == null)
                return MapDiffs.NONE;

            //Loop through all difficulties and add them to the diffs flag
            //https://bsmg.wiki/mapping/map-format.html#difficultyrank
            MapDiffs diffs = MapDiffs.NONE;
            foreach(json_beatMapDifficulty set in Sets) {
                switch(set._difficultyRank) {
                    case 1: diffs |= MapDiffs.Easy; break;
                    case 3: diffs |= MapDiffs.Normal; break;
                    case 5: diffs |= MapDiffs.Hard; break;
                    case 7: diffs |= MapDiffs.Expert; break;
                    case 9: diffs |= MapDiffs.ExpertPlus; break;
                }
            }
            return diffs;
        }

        /// <summary>
        /// Format BSR from a directory path to a value
        /// </summary>
        /// <remarks>
        /// <example> Example input:
        /// <code>
        /// mapPath = "C:\..\1e6ff (Som..).zip"
        /// returns "1e6ff"
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="mapPath">Directory path</param>
        /// <returns>BSR code</returns>
        public static string ParseBSR(string mapPath) { 
            int cut = mapPath.LastIndexOf(_ps) + 1;
            string name = mapPath.Substring(cut, mapPath.Length-cut);
            int end = name.IndexOf(' ');
            if(end != -1)
                name = name.Substring(0, end);
            return name;
        }

        /// <summary>
        /// Gets the note directly beside a given note on the same time step.
        /// </summary>
        /// <param name="list">List of notes on a time frame</param>
        /// <param name="note">The current note to look from</param>
        /// <param name="lookDir">Which direction we are testing</param>
        /// <returns>
        /// <c><see cref="BeatMapEvaluator.json_MapNote"/></c> if note found,
        /// <c><see cref="Nullable">null</see></c> if note outside of range or no note found.
        /// </returns>
        public static json_MapNote? GetAdjacentNote(List<json_MapNote> list, json_MapNote note, NoteCutDirection lookDir) {
            int cell = note.cellIndex;

            bool u = note._lineLayer != 2;  //Not the top most layer
            bool d = note._lineLayer != 0;  //Not the bottom most layer
            bool l = note._lineIndex != 0;  //Not the left most column
            bool r = note._lineIndex != 3;  //Not the right most column

            switch(lookDir) {
                case NoteCutDirection.Up:   if(u) cell += 4; break;
                case NoteCutDirection.Down: if(d) cell -= 4; break;
                case NoteCutDirection.Left: if(l) cell -= 1; break;
                case NoteCutDirection.Right: if(r) cell += 1; break;
                case NoteCutDirection.UpLeft: if(u&&l) cell += 3; break;
                case NoteCutDirection.UpRight: if(u&&r) cell += 5; break;
                case NoteCutDirection.DownLeft: if(d&&l) cell -= 5; break;
                case NoteCutDirection.DownRight:if(d&&r) cell -= 3; break;
            }
            //if note space exists (switch above made a change)
            if(cell != note.cellIndex) { 
                foreach(var test in list) { 
                    if(test.cellIndex == cell)
                        return test;
                }   
            }
            //return null if nothing found
            return null;
        }

        /// <summary>
        /// Gets the high percentile of a swing list.
        /// </summary>
        /// <remarks>
        /// <para name="percent">percent 1.0-0.0 = 100%-0%</para>
        /// </remarks>
        /// <param name="swings">All swings per second</param>
        /// <param name="percent">Percentile inclusivity</param>
        /// <returns>the swings per second</returns>
        public static int GetSwingPercentile(int[] swings, float percent) {
            //Copy because array uses pointer
            int[] sorted = new int[swings.Length];
            Array.Copy(swings, sorted, sorted.Length);
            //Sort in descending order (High to low)
            Array.Sort(sorted);
            Array.Reverse(sorted);

            //Calculate the included sample amount and segregate
            int subdivCount = (int)Math.Round(sorted.Length * percent);
            if (subdivCount == 0) subdivCount = 1; 
            int[] div = new int[subdivCount];
            Array.Copy(sorted, div, subdivCount);
            //Return the average of the subset
            return (int)div.Average();
        }

        /// <summary>
        /// Evaluates if current block is part of a slider, if its the start of a slider this will return false
        /// </summary>
        /// <param name="current">The current note</param>
        /// <param name="last">The last note with the same colour</param>
        /// <param name="dotPrec">A dot notes precision</param>
        /// <param name="sliderPrec">A non-dot note precision</param>
        /// <returns><c><see cref="bool">True</see></c> if current is part of a slider</returns>
        public static bool IsSlider(json_MapNote current, json_MapNote last, float dotPrec, float sliderPrec) {
            int[] cutDir = {90, 270, 0, 180, 45, 135, 315, 225, 0};
            const float deltaEpsilon = 1.0f / 32.0f;
            const float angleEpsilon = 1.0f / 4.0f;

            //if either the last or current note was a dot note
            //use dot precision, otherwise use slider precision
            bool stepHasDot = (current._cutDirection | last._cutDirection).HasFlag(NoteCutDirection.DotNote);
            //Angular difference between the last cut direction and the current in degrees
            int cutDiff = Math.Abs(cutDir[(int)current._cutDirection] - cutDir[(int)last._cutDirection]);
            //if either the current or last note was a dot note, use dot precision otherwise use slider precision
            float precision = stepHasDot ? dotPrec : sliderPrec;
            //Get the time between the last note and the current note in beats
            float timeDelta = current._time - last._time;

            //If this or the last block is a dot note, use dotPrec to determine if it's a part of the same swing
            //else check if theyre facing the same direction and use sliderPrec
            //Or if the last block to the current is super close, it just assumes it was a part of the same swing.
            //Or check if the block angle from the last is <= to 90deg and also < angleEpsilon precision
            bool isSlider = (timeDelta <= precision && (stepHasDot || cutDiff == 0)) ||
                            (timeDelta <= deltaEpsilon || (stepHasDot || cutDiff <= 90) &&
                            (timeDelta <= angleEpsilon));
            return isSlider;
        }
    }
}
