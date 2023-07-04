using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils {
    public class RatingUtils {
        private static float toPass(float original) {
            if (original < 24.4) {
                return original;
            } else {
                return 16 + MathF.Sqrt(original) * 1.7f;
            }
        }
        public static async Task SetRating(DifficultyDescription diff, Song song) {
            if (!diff.Status.WithRating() && !diff.Requirements.HasFlag(Requirements.Noodles) && !diff.Requirements.HasFlag(Requirements.MappingExtensions)) {
                var response = await SongUtils.ExmachinaStars(song.Hash, diff.Value, diff.ModeName);
                if (response != null) {
                    diff.PassRating = toPass(response.none.lack_map_calculation.balanced_pass_diff);
                    diff.TechRating = response.none.lack_map_calculation.balanced_tech * 10;
                    diff.PredictedAcc = response.none.AIacc;
                    diff.AccRating = ReplayUtils.AccRating(diff.PredictedAcc, diff.PassRating, diff.TechRating);

                    diff.ModifiersRating = new ModifiersRating {
                        SSPassRating = toPass(response.SS.lack_map_calculation.balanced_pass_diff),
                        SSTechRating = response.SS.lack_map_calculation.balanced_tech * 10,
                        SSPredictedAcc = response.SS.AIacc,
                        FSPassRating = toPass(response.FS.lack_map_calculation.balanced_pass_diff),
                        FSTechRating = response.FS.lack_map_calculation.balanced_tech * 10,
                        FSPredictedAcc = response.FS.AIacc,
                        SFPassRating = toPass(response.SFS.lack_map_calculation.balanced_pass_diff),
                        SFTechRating = response.SFS.lack_map_calculation.balanced_tech * 10,
                        SFPredictedAcc = response.SFS.AIacc,
                    };

                    diff.Stars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);

                    var rating = diff.ModifiersRating;
                    rating.SSAccRating = ReplayUtils.AccRating(
                            rating.SSPredictedAcc,
                            rating.SSPassRating,
                            rating.SSTechRating);
                    rating.SSStars = ReplayUtils.ToStars(rating.SSPredictedAcc, rating.SSPassRating, rating.SSTechRating);
                    rating.FSAccRating = ReplayUtils.AccRating(
                            rating.FSPredictedAcc,
                            rating.FSPassRating,
                            rating.FSTechRating);
                    rating.FSStars = ReplayUtils.ToStars(rating.FSPredictedAcc, rating.FSPassRating, rating.FSTechRating);
                    rating.SFAccRating = ReplayUtils.AccRating(
                            rating.SFPredictedAcc,
                            rating.SFPassRating,
                            rating.SFTechRating);
                    rating.SFStars = ReplayUtils.ToStars(rating.SFPredictedAcc, rating.SFPassRating, rating.SFTechRating);
                } else {
                    diff.PassRating = null;
                    diff.PredictedAcc = null;
                    diff.TechRating = null;
                    diff.AccRating = null;
                    diff.ModifiersRating = null;
                }
            }

        }
    }
}
