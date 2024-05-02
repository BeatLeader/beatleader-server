using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class LeaderboardControllerHelper {
        public static async Task<Leaderboard?> GetByHash(AppContext dbContext, string hash, string diff, string mode, bool recursive = true) {
            Leaderboard? leaderboard;

            leaderboard = await dbContext
                .Leaderboards
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.MaxScoreGraph)
                .TagWithCallSite()
                .AsSplitQuery()
                .FirstOrDefaultAsync(lb => lb.Song.Hash == hash && lb.Difficulty.ModeName == mode && lb.Difficulty.DifficultyName == diff);

            if (leaderboard == null) {
                Song? song = await SongControllerHelper.GetOrAddSong(dbContext, hash);
                if (song == null) {
                    return null;
                }
                // Song migrated leaderboards
                if (recursive) {
                    return await GetByHash(dbContext, hash, diff, mode, false);
                } else {
                    leaderboard = await SongControllerHelper.NewLeaderboard(dbContext, song, null, diff, mode);
                }

                if (leaderboard == null) {
                    return null;
                }
            }

            return leaderboard;
        }
    }
}
