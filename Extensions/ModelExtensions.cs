using BeatLeader_Server.Models;

namespace BeatLeader_Server.Extensions
{
    public static class ModelExtensions
    {
        public static bool WithRating(this DifficultyStatus context)
        {
            return context == DifficultyStatus.ranked || context == DifficultyStatus.qualified || context == DifficultyStatus.nominated || context == DifficultyStatus.inevent || context == DifficultyStatus.OST;
        }

        public static bool WithPP(this DifficultyStatus context)
        {
            return context == DifficultyStatus.ranked || context == DifficultyStatus.qualified || context == DifficultyStatus.inevent;
        }
    }
}
