using Amazon.S3;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using ReplayDecoder;

namespace BeatLeader_Server.ControllerHelpers {
    public class ScoreControllerHelper {
        
        public static async Task<(ScoreStatistic?, string?)> CalculateStatisticScore(
            AppContext? dbContext, 
            IAmazonS3 _s3Client, 
            Score score)
        {
            string fileName = score.Replay.Split("/").Last();
            Replay? replay;

            using (var replayStream = await _s3Client.DownloadReplay(fileName))
            {
                if (replayStream == null) return (null, "Couldn't find replay");

                using (var ms = new MemoryStream(5))
                {
                    await replayStream.CopyToAsync(ms);
                    long length = ms.Length;
                    try
                    {
                        (replay, _) = ReplayDecoder.ReplayDecoder.Decode(ms.ToArray());
                    }
                    catch (Exception)
                    {
                        return (null, "Error decoding replay");
                    }
                }
            }

            (ScoreStatistic? statistic, string? error) = await CalculateAndSaveStatistic(_s3Client, replay, score);
            if (statistic == null) {
                return (null, error);
            }

            if (dbContext != null) {
                score.AccLeft = statistic.accuracyTracker.accLeft;
                score.AccRight = statistic.accuracyTracker.accRight;
                score.MaxCombo = statistic.hitTracker.maxCombo;
                score.FcAccuracy = statistic.accuracyTracker.fcAcc;
                score.MaxStreak = statistic.hitTracker.maxStreak;
                score.LeftTiming = statistic.hitTracker.leftTiming;
                score.RightTiming = statistic.hitTracker.rightTiming;

                await dbContext.SaveChangesAsync();
            }

            return (statistic, null);
        }

        public static (ScoreStatistic?, string?) CalculateStatisticFromReplay(Replay? replay, Leaderboard leaderboard, bool allow = false)
        {
            ScoreStatistic? statistic;

            if (replay == null)
            {
                return (null, "Could not calculate statistics");
            }

            try
            {   string? error = null;
                if (replay.info.mode == ReBeatUtils.MODE_IDENTIFIER) {
                    (statistic, error) = ReBeatUtils.ProcessReplay(replay);
                } else {
                    (statistic, error) = ReplayStatisticUtils.ProcessReplay(replay, leaderboard, allow);
                }
                if (statistic == null && error != null) {
                    return (null, error);
                }
            } catch (Exception e) {
                return (null, e.ToString());
            }

            if (statistic == null)
            {
                return (null, "Could not calculate statistics");
            }

            return (statistic, null);
        }

        public static async Task<(ScoreStatistic?, string?)> CalculateAndSaveStatistic(IAmazonS3 _s3Client, Replay? replay, Score score)
        {
            (ScoreStatistic? statistic, string? error) = CalculateStatisticFromReplay(replay, score.Leaderboard);

            if (statistic == null)
            {
                return (null, error);
            }

            await _s3Client.UploadScoreStats(score.Id + ".json", statistic);

            return (statistic, null);
        }
    }
}
