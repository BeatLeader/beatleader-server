using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> WhereType(this IQueryable<Leaderboard> sequence, Type type) =>
        type switch
        {
            Type.Ranked      => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.ranked),
            Type.Ranking     => sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.unranked && leaderboard.Difficulty.Status != DifficultyStatus.outdated && leaderboard.Difficulty.Status != DifficultyStatus.inevent),
            Type.Nominated   => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.nominated),
            Type.Qualified   => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified),
            Type.Staff       => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified || leaderboard.Difficulty.Status == DifficultyStatus.nominated),
            Type.Reweighting => sequence.Where(leaderboard => leaderboard.Reweight != null && !leaderboard.Reweight.Finished),
            Type.Reweighted  => sequence.Where(leaderboard => leaderboard.Reweight != null && leaderboard.Reweight.Finished),
            Type.Unranked    => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.unranked),
            Type.Ost         => sequence.Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.OST),
            _                => sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.outdated),
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

    private static IQueryable<Leaderboard> WhereSongStatus(this IQueryable<Leaderboard> sequence, SongStatus status)
    {
        if (status == SongStatus.None)
        {
            return sequence;
        }

        return sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.outdated && leaderboard.Song.ExternalStatuses.FirstOrDefault(s => status.HasFlag(s.Status)) != null);
    }

    private static IQueryable<Leaderboard> WhereMapper(this IQueryable<Leaderboard> sequence, string? mapper)
    {
        if (mapper == null)
        {
            return sequence;
        }

        var ids = mapper.Split(",").Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0).ToArray();
        if (ids.Length == 0) {
            return sequence;
        }
        return sequence.Where(lb => lb.Song.Mappers.Any(m => ids.Contains(m.Id)));
    }

    private static IQueryable<Leaderboard> WhereMyType(this IQueryable<Leaderboard> sequence, MyType mytype, Player? currentPlayer, LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
    {
        int mapperId = 0;
        string? currentId = currentPlayer?.Id;

        if (currentId == null) { 
            return sequence;
        }

        if (mytype != MyType.None)
        {
            if (currentPlayer != null && currentPlayer.MapperId != null)
            {
                mapperId = currentPlayer.MapperId ?? 0;
            }
        }

        return mytype switch
        {
            MyType.Played          => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId && score.ValidContexts.HasFlag(leaderboardContext)) != null),
            MyType.Unplayed        => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId && score.ValidContexts.HasFlag(leaderboardContext)) == null),
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

    private static IQueryable<Leaderboard> WhereDifficulty(this IQueryable<Leaderboard> sequence, string? difficulty)
    {
        if (difficulty == null)
        {
            return sequence;
        }

        return sequence.Where(leaderboard => leaderboard.Difficulty.DifficultyName == difficulty);
    }

    private static IQueryable<Leaderboard> WhereMapRequirements(this IQueryable<Leaderboard> sequence, Requirements mapRequirements, Operation allRequirements)
    {
        if (mapRequirements == Requirements.Ignore)
        {
            return sequence;
        }

        return allRequirements switch
        {
            Operation.Any => sequence.Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) != 0),
            Operation.All => sequence.Where(leaderboard => leaderboard.Difficulty.Requirements == mapRequirements),
            Operation.Not => sequence.Where(leaderboard => (leaderboard.Difficulty.Requirements & mapRequirements) == 0),
            _             => sequence,
        };
    }

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

    public static async Task<(IQueryable<Leaderboard>, int)> WherePage(this IQueryable<Leaderboard> sequence, int page, int count)
    {
        if (page <= 0) {
            page = 1;
        }

        return (sequence.Skip((page - 1) * count).Take(count), await sequence.CountAsync());
    }
}