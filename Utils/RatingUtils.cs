using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils.RebalancedPp;
using static BeatLeader_Server.Utils.SongUtils;

namespace BeatLeader_Server.Utils
{
    public class RatingUtils
    {
        private static float toPass(float original)
        {
            if (original < 24.4)
            {
                return original;
            } else
            {
                return 16 + MathF.Sqrt(original) * 1.7f;
            }
        }

        public static async Task UpdateFromExMachina(Leaderboard leaderboard, LeaderboardChange? rankChange)
        {

            await UpdateFromExMachina(leaderboard.Difficulty, leaderboard.Song, rankChange);
        }

        private static float ResultStars(RatingResult result) =>
            result.Stars > 0 ? result.Stars : ReplayUtils.ToStars(result.AccRating, result.LackMapCalculation.PassRating, result.LackMapCalculation.TechRating, ResultCurve(result));

        private static CurveInputs ResultCurve(RatingResult result) =>
            new CurveInputs(result.PredictedAcc, result.LackMapCalculation.SliderPercentage, result.LackMapCalculation.NoteCount);

        public static void UpdateFromResponse(DifficultyDescription diff, ExmachinaResponse? response, LeaderboardChange? rankChange)
        {
            if (response != null && response.none != null)
            {
                diff.PassRating = response.none.LackMapCalculation.PassRating;
                diff.TechRating = response.none.LackMapCalculation.TechRating;
                diff.PredictedAcc = response.none.PredictedAcc;
                diff.AccRating = response.none.AccRating;
                diff.SliderPercentage = response.none.LackMapCalculation.SliderPercentage;
                diff.Stars = ResultStars(response.none);

                if (rankChange != null)
                {
                    rankChange.NewAccRating = diff.AccRating ?? 0;
                    rankChange.NewPassRating = diff.PassRating ?? 0;
                    rankChange.NewTechRating = diff.TechRating ?? 0;
                    rankChange.NewStars = diff.Stars ?? 0;
                }

                var modrating = diff.ModifiersRating = new ModifiersRating();

                if (response.SS != null) {
                    modrating.SSPassRating = response.SS.LackMapCalculation.PassRating;
                    modrating.SSTechRating = response.SS.LackMapCalculation.TechRating;
                    modrating.SSPredictedAcc = response.SS.PredictedAcc;
                    modrating.SSAccRating = response.SS.AccRating;
                    modrating.SSSliderPercentage = response.SS.LackMapCalculation.SliderPercentage;
                    modrating.SSStars = ResultStars(response.SS);
                }

                if (response.FS != null) {
                    modrating.FSPassRating = response.FS.LackMapCalculation.PassRating;
                    modrating.FSTechRating = response.FS.LackMapCalculation.TechRating;
                    modrating.FSPredictedAcc = response.FS.PredictedAcc;
                    modrating.FSAccRating = response.FS.AccRating;
                    modrating.FSSliderPercentage = response.FS.LackMapCalculation.SliderPercentage;
                    modrating.FSStars = ResultStars(response.FS);
                }

                if (response.SFS != null) {
                    modrating.SFPassRating = response.SFS.LackMapCalculation.PassRating;
                    modrating.SFTechRating = response.SFS.LackMapCalculation.TechRating;
                    modrating.SFPredictedAcc = response.SFS.PredictedAcc;
                    modrating.SFAccRating = response.SFS.AccRating;
                    modrating.SFSliderPercentage = response.SFS.LackMapCalculation.SliderPercentage;
                    modrating.SFStars = ResultStars(response.SFS);
                }

                if (response.BFS != null) {
                    modrating.BFSPassRating = response.BFS.LackMapCalculation.PassRating;
                    modrating.BFSTechRating = response.BFS.LackMapCalculation.TechRating;
                    modrating.BFSPredictedAcc = response.BFS.PredictedAcc;
                    modrating.BFSAccRating = response.BFS.AccRating;
                    modrating.BFSStars = ResultStars(response.BFS);
                }

                if (response.BSF != null) {
                    modrating.BSFPassRating = response.BSF.LackMapCalculation.PassRating;
                    modrating.BSFTechRating = response.BSF.LackMapCalculation.TechRating;
                    modrating.BSFPredictedAcc = response.BSF.PredictedAcc;
                    modrating.BSFAccRating = response.BSF.AccRating;
                    modrating.BSFStars = ResultStars(response.BSF);
                }
            } else
            {
                diff.PassRating = 0.0f;
                diff.PredictedAcc = 1.0f;
                diff.TechRating = 0.0f;
            }

        }

        public static async Task UpdateFromExMachina(DifficultyDescription diff, Song song, LeaderboardChange? rankChange)
        {
            try
            {
                var response = await ExmachinaStars(song.LowerHash, diff.Value, diff.ModeName);
                UpdateFromResponse(diff, response, rankChange);

            } catch {}
        }

        public static async Task UpdateFromExMachina(DifficultyDescription diff, string link, LeaderboardChange? rankChange)
        {
            try
            {
                var response = await ExmachinaStarsLink(link, diff.Value, diff.ModeName);
                UpdateFromResponse(diff, response, rankChange);

            } catch {}
        }

        public static async Task UpdateTags(Leaderboard leaderboard, LeaderboardChange? rankChange)
        {
            try
            {
                var diff = leaderboard.Difficulty;
                var response = await ApiTags(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);
                switch (response) {
                    case "acc":
                        diff.Type = MapTypes.Acc;
                        diff.TypeAcc = true;
                        break;
                    case "tech":
                        diff.Type = MapTypes.Tech;
                        diff.TypeTech = true;
                        break;
                    case "midspeed":
                        diff.Type = MapTypes.Midspeed;
                        diff.TypeMidspeed = true;
                        break;
                    case "speed":
                        diff.Type = MapTypes.Speed;
                        diff.TypeSpeed = true;
                        break;
                    default:
                        break;
                }
                if (rankChange != null) {
                    rankChange.NewType = diff.Type;
                }

            } catch {}
        }
    }
}
