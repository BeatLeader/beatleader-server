using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    public class RatingUtils
    {
        public static async Task UpdateFromExMachina(Leaderboard leaderboard, LeaderboardChange? rankChange)
        {

            await UpdateFromExMachina(leaderboard.Difficulty, leaderboard.Song, rankChange);
        }

        public static async Task UpdateFromExMachina(DifficultyDescription diff, Song song, LeaderboardChange? rankChange)
        {
            try
            {
                var response = await SongUtils.ExmachinaStars(song.Hash, diff.Value, diff.ModeName);
                if (response != null)
                {
                    diff.PassRating = response.none.LackMapCalculation.PassRating;
                    diff.TechRating = response.none.LackMapCalculation.TechRating;
                    diff.LinearRating = response.none.LackMapCalculation.LinearRating;
                    diff.PatternRating = response.none.LackMapCalculation.PatternRating;
                    diff.PredictedAcc = response.none.PredictedAcc;
                    diff.AccRating = response.none.AccRating;
                    diff.Stars = ReplayUtils.ToStars(
                        diff.AccRating ?? 0,
                        diff.PassRating ?? 0,
                        diff.TechRating ?? 0,
                        diff.PatternRating ?? 0,
                        diff.LinearRating ?? 0,
                        diff.PredictedAcc ?? 0,
                        diff.ModeName,
                        "");

                    if (rankChange != null)
                    {
                        rankChange.NewAccRating = diff.AccRating ?? 0;
                        rankChange.NewPassRating = diff.PassRating ?? 0;
                        rankChange.NewTechRating = diff.TechRating ?? 0;
                        rankChange.NewStars = diff.Stars ?? 0;
                    }

                    var modrating = diff.ModifiersRating = new ModifiersRating
                    {
                        SSPassRating = response.SS.LackMapCalculation.PassRating,
                        SSTechRating = response.SS.LackMapCalculation.TechRating,
                        SSPredictedAcc = response.SS.PredictedAcc,
                        SSAccRating = response.SS.AccRating,

                        FSPassRating = response.FS.LackMapCalculation.PassRating,
                        FSTechRating = response.FS.LackMapCalculation.TechRating,
                        FSPredictedAcc = response.FS.PredictedAcc,
                        FSAccRating = response.FS.AccRating,

                        SFPassRating = response.SFS.LackMapCalculation.PassRating,
                        SFTechRating = response.SFS.LackMapCalculation.TechRating,
                        SFPredictedAcc = response.SFS.PredictedAcc,
                        SFAccRating = response.SFS.AccRating,
                    };

                    modrating.SFStars = ReplayUtils.ToStars(
                        modrating.SFAccRating,
                        modrating.SFPassRating,
                        modrating.SFTechRating,
                        diff.PatternRating ?? 0,
                        diff.LinearRating ?? 0,
                        diff.PredictedAcc ?? 0,
                        diff.ModeName,
                        "SF");

                    modrating.FSStars = ReplayUtils.ToStars(
                        modrating.FSAccRating,
                        modrating.FSPassRating,
                        modrating.FSTechRating,
                        diff.PatternRating ?? 0,
                        diff.LinearRating ?? 0,
                        diff.PredictedAcc ?? 0,
                        diff.ModeName,
                        "FS");

                    modrating.SSStars = ReplayUtils.ToStars(
                        modrating.SSAccRating,
                        modrating.SSPassRating,
                        modrating.SSTechRating,
                        diff.PatternRating ?? 0,
                        diff.LinearRating ?? 0,
                        diff.PredictedAcc ?? 0,
                        diff.ModeName,
                        "SS");
                } else
                {
                    diff.PassRating = 0.0f;
                    diff.PredictedAcc = 1.0f;
                    diff.TechRating = 0.0f;
                }
            } catch
            {
            }
        }
    }
}
