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
                    return 1;
                case "Normal":
                    return 3;
                case "Hard":
                    return 5;
                case "Expert":
                    return 7;
                case "ExpertPlus":
                    return 9;
            }

            return 0;
        }
    }
}
