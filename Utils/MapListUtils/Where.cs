using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> WhereType(this IQueryable<Leaderboard> sequence, string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return sequence;
        }

        return type switch
        {
            "ranked"      => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.ranked),
            "ranking"     => sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.unranked && leaderboard.Difficulty.Status != DifficultyStatus.outdated),
            "nominated"   => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.nominated),
            "qualified"   => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified),
            "staff"       => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified || leaderboard.Difficulty.Status == DifficultyStatus.nominated),
            "reweighting" => sequence.Where(leaderboard => leaderboard.Reweight != null && !leaderboard.Reweight.Finished),
            "reweighted"  => sequence.Where(leaderboard => leaderboard.Reweight != null && leaderboard.Reweight.Finished),
            "unranked"    => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.unranked),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WhereMapType(this IQueryable<Leaderboard> sequence, int? mapType, Operation allTypes)
    {
        if (mapType == null)
        {
            return sequence;
        }

        return allTypes switch
        {
            Operation.any => sequence.Where(leaderboard => (leaderboard.Difficulty.Type & mapType) != 0),
            Operation.all => sequence.Where(leaderboard => leaderboard.Difficulty.Type == mapType),
            Operation.not => sequence.Where(leaderboard => (leaderboard.Difficulty.Type & mapType) == 0),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WhereMyType(this IQueryable<Leaderboard> sequence, ReadAppContext context, string? mytype, string? currentId)
    {
        if (string.IsNullOrEmpty(mytype))
        {
            return sequence;
        }

        var mapperId = context.Players.Find(currentId)?.MapperId ?? 0;

        return mytype switch
        {
            "played"          => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId) != null),
            "unplayed"        => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId) == null),
            "mynominated"     => sequence.Where(leaderboard => leaderboard.Qualification != null && leaderboard.Qualification.RTMember == currentId),
            "othersnominated" => sequence.Where(leaderboard => leaderboard.Qualification != null && leaderboard.Qualification.RTMember != currentId),
            "mymaps"          => sequence.Where(leaderboard => leaderboard.Song.MapperId == mapperId),
            _                 => sequence,
        };
    }

    private static IQueryable<Leaderboard> WhereMode(this IQueryable<Leaderboard> sequence, string? mode)
    {
        if (mode == null)
        {
            return sequence;
        }

        return sequence.Where(leaderboard => leaderboard.Difficulty.ModeName == mode);
    }

    private static IQueryable<Leaderboard> WhereMapRequirements(this IQueryable<Leaderboard> sequence, Requirements? mapRequirements, Operation allRequirements)
    {
        if (mapRequirements == null)
        {
            return sequence;
        }

        return allRequirements switch
        {
            Operation.any => sequence.Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) != 0),
            Operation.all => sequence.Where(leaderboard => leaderboard.Difficulty.Requirements == mapRequirements),
            Operation.not => sequence.Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) == 0),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WhereStarsFrom(this IQueryable<Leaderboard> sequence, float? starsFrom)
    {
        if (starsFrom == null)
        {
            return sequence;
        }

        return sequence.Where(leaderboard => leaderboard.Difficulty.Stars >= starsFrom);
    }

    private static IQueryable<Leaderboard> WhereStarsTo(this IQueryable<Leaderboard> sequence, float? starsTo)
    {
        if (starsTo == null)
        {
            return sequence;
        }

        return sequence.Where(leaderboard => leaderboard.Difficulty.Stars <= starsTo);
    }
}