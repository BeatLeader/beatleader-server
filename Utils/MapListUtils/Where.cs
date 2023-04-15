using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> WhereType(this IQueryable<Leaderboard> sequence, Type type) =>
        type switch
        {
            Type.Ranked      => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.ranked),
            Type.Ranking     => sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.unranked && leaderboard.Difficulty.Status != DifficultyStatus.outdated),
            Type.Nominated   => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.nominated),
            Type.Qualified   => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified),
            Type.Staff       => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified || leaderboard.Difficulty.Status == DifficultyStatus.nominated),
            Type.Reweighting => sequence.Where(leaderboard => leaderboard.Reweight != null && !leaderboard.Reweight.Finished),
            Type.Reweighted  => sequence.Where(leaderboard => leaderboard.Reweight != null && leaderboard.Reweight.Finished),
            Type.Unranked    => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.unranked),
            _                => sequence,
        };

    private static IQueryable<Leaderboard> WhereMapType(this IQueryable<Leaderboard> sequence, int? mapType, Operation allTypes)
    {
        if (mapType == null)
        {
            return sequence;
        }

        return allTypes switch
        {
            Operation.Any => sequence.Where(leaderboard => (leaderboard.Difficulty.Type & mapType) != 0),
            Operation.All => sequence.Where(leaderboard => leaderboard.Difficulty.Type == mapType),
            Operation.Not => sequence.Where(leaderboard => (leaderboard.Difficulty.Type & mapType) == 0),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WhereMyType(this IQueryable<Leaderboard> sequence, ReadAppContext context, MyType mytype, string? currentId)
    {
        int mapperId = 0;

        if (mytype != MyType.None)
        {
            Player? currentPlayer = context.Players.Find(currentId);

            if (currentPlayer != null)
            {
                mapperId = currentPlayer.MapperId;
            }
        }

        return mytype switch
        {
            MyType.Played          => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId) != null),
            MyType.Unplayed        => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId) == null),
            MyType.MyNominated     => sequence.Where(leaderboard => leaderboard.Qualification != null && leaderboard.Qualification.RTMember == currentId),
            MyType.OthersNominated => sequence.Where(leaderboard => leaderboard.Qualification != null && leaderboard.Qualification.RTMember != currentId),
            MyType.MyMaps          => sequence.Where(leaderboard => leaderboard.Song.MapperId == mapperId),
            _                      => sequence,
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

    private static IQueryable<Leaderboard> WhereMapRequirements(this IQueryable<Leaderboard> sequence, Requirements mapRequirements, Operation allRequirements) =>
        allRequirements switch
        {
            Operation.Any => sequence.Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) != 0),
            Operation.All => sequence.Where(leaderboard => leaderboard.Difficulty.Requirements == mapRequirements),
            Operation.Not => sequence.Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) == 0),
            _             => sequence,
        };

    private static IQueryable<Leaderboard> WhereRatingFrom(this IQueryable<Leaderboard> sequence, RatingType rating, float? from)
    {
        if (from == null)
        {
            return sequence;
        }

        return rating switch {
            RatingType.Stars => sequence.Where(leaderboard => leaderboard.Difficulty.Stars >= from),
            RatingType.Acc => sequence.Where(leaderboard => leaderboard.Difficulty.AccRating >= from),
            RatingType.Pass => sequence.Where(leaderboard => leaderboard.Difficulty.PassRating >= from),
            RatingType.Tech => sequence.Where(leaderboard => leaderboard.Difficulty.TechRating >= from),
            _ => sequence,
        };
    }

    private static IQueryable<Leaderboard> WhereRatingTo(this IQueryable<Leaderboard> sequence, RatingType rating, float? to)
    {
        if (to == null)
        {
            return sequence;
        }

        return rating switch {
            RatingType.Stars => sequence.Where(leaderboard => leaderboard.Difficulty.Stars <= to),
            RatingType.Acc => sequence.Where(leaderboard => leaderboard.Difficulty.AccRating <= to),
            RatingType.Pass => sequence.Where(leaderboard => leaderboard.Difficulty.PassRating <= to),
            RatingType.Tech => sequence.Where(leaderboard => leaderboard.Difficulty.TechRating <= to),
            _ => sequence,
        };
    }

    private static IQueryable<Leaderboard> WherePage(this IQueryable<Leaderboard> sequence, int? page, int count, IReadOnlyCollection<SongMetadata> matches)
    {
        if (matches.Count > 0)
        {
            IEnumerable<string> ids = matches.Select(songMetadata => songMetadata.Id);

            if (page.HasValue)
            {
                ids = ids.Skip((page.Value - 1) * count);
            }

            ids = ids.Take(count);

            return sequence.Where(leaderboard => ids.Contains(leaderboard.SongId));
        }

        return page.HasValue
            ? sequence.Skip((page.Value - 1) * count).Take(count)
            : sequence.Take(count);
    }
}