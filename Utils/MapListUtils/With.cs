using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> WithMapType(this IQueryable<Leaderboard> sequence, int? mapType, Operation allTypes)
    {
        if (mapType == null)
        {
            return sequence;
        }

        return allTypes switch
        {
            Operation.any => sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => (leaderboard.Difficulty.Type & mapType) != 0),
            Operation.all => sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => leaderboard.Difficulty.Type == mapType),
            Operation.not => sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => (leaderboard.Difficulty.Type & mapType) == 0),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WithMode(this IQueryable<Leaderboard> sequence, string? mode)
    {
        if (mode == null)
        {
            return sequence;
        }

        return sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => leaderboard.Difficulty.ModeName == mode);
    }

    private static IQueryable<Leaderboard> WithMapRequirements(this IQueryable<Leaderboard> sequence, Requirements? mapRequirements, Operation allRequirements)
    {
        if (mapRequirements == null)
        {
            return sequence;
        }

        return allRequirements switch
        {
            Operation.any => sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) != 0),
            Operation.all => sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => leaderboard.Difficulty.Requirements == mapRequirements),
            Operation.not => sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) == 0),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WithStarsFrom(this IQueryable<Leaderboard> sequence, float? starsFrom)
    {
        if (starsFrom == null)
        {
            return sequence;
        }

        return sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => leaderboard.Difficulty.Stars >= starsFrom);
    }

    private static IQueryable<Leaderboard> WithStarsTo(this IQueryable<Leaderboard> sequence, float? starsTo)
    {
        if (starsTo == null)
        {
            return sequence;
        }

        return sequence.Include(leaderboard => leaderboard.Difficulty).Where(leaderboard => leaderboard.Difficulty.Stars <= starsTo);
    }
}