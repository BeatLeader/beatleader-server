using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Utils {
    public class SpeedrunUtils {
        public static void FinishSpeedrun(AppContext dbContext, string playerId) {
            var speedrunner = dbContext
                .PlayerContextExtensions
                .Where(ce => ce.PlayerId == playerId && ce.Context == Models.LeaderboardContexts.Speedrun)
                .FirstOrDefault();
            var speedrunnerBackup = dbContext
                .PlayerContextExtensions
                .Where(ce => ce.PlayerId == playerId && ce.Context == Models.LeaderboardContexts.SpeedrunBackup)
                .FirstOrDefault();

            if (speedrunner == null) return;
            bool record = false;
            var scoresBackup = dbContext
                .Scores
                .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.SpeedrunBackup))
                .Include(s => s.ContextExtensions)
                .ToList();
            var playerExtensionBackup = dbContext
                .PlayerContextExtensions
                .Where(ce => ce.PlayerId == playerId && ce.Context == LeaderboardContexts.SpeedrunBackup)
                .Include(p => p.ScoreStats)
                .FirstOrDefault();
            if (speedrunnerBackup == null || speedrunner.Pp > speedrunnerBackup.Pp) {
                record = true;
                foreach (var score in scoresBackup) {
                    var ce = score.ContextExtensions.Where(c => c.Context == LeaderboardContexts.SpeedrunBackup).FirstOrDefault();
                    if (ce != null) {
                        dbContext.ScoreContextExtensions.Remove(ce);
                    }

                    score.ValidContexts &= ~LeaderboardContexts.SpeedrunBackup;
                    if (score.ValidContexts == LeaderboardContexts.None) {
                        dbContext.Scores.Remove(score);
                    }
                }
                if (playerExtensionBackup != null) {
                    dbContext.PlayerContextExtensions.Remove(playerExtensionBackup);
                }
            } else {
                foreach (var score in scoresBackup) {
                    var ce = score.ContextExtensions.Where(c => c.Context == LeaderboardContexts.SpeedrunBackup).FirstOrDefault();
                    if (ce != null) {
                        ce.Context = LeaderboardContexts.Speedrun;
                    }

                    score.ValidContexts &= ~LeaderboardContexts.SpeedrunBackup;
                    score.ValidContexts |= LeaderboardContexts.Speedrun;
                }
                if (playerExtensionBackup != null) {
                    playerExtensionBackup.Context = LeaderboardContexts.Speedrun;
                }

                var scores = dbContext
                    .Scores
                    .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.Speedrun))
                    .Include(s => s.ContextExtensions)
                    .ToList();
                var playerExtension = dbContext
                    .PlayerContextExtensions
                    .Where(ce => ce.PlayerId == playerId && ce.Context == LeaderboardContexts.Speedrun)
                    .Include(p => p.ScoreStats)
                    .FirstOrDefault();

                foreach (var score in scores) {
                    var ce = score.ContextExtensions.Where(c => c.Context == LeaderboardContexts.Speedrun).FirstOrDefault();
                    if (ce != null) {
                        dbContext.ScoreContextExtensions.Remove(ce);
                    }

                    score.ValidContexts &= ~LeaderboardContexts.Speedrun;
                    if (score.ValidContexts == LeaderboardContexts.None) {
                        dbContext.Scores.Remove(score);
                    }
                }
                if (playerExtension != null) {
                    dbContext.PlayerContextExtensions.Remove(playerExtension);
                }
            }

            dbContext.Speedruns.Add(new Speedrun {
                PlayerId = playerId,
                FinishTimeset = speedrunner.Player.SpeedrunStart + 60 * 60,
                Pp = speedrunner.Pp,
                Record = record
            });
            dbContext.SaveChanges();
        }
    }
}
