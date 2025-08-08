using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class LeaderboardListUtils
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

    private static IQueryable<Leaderboard> WhereTypes(this IQueryable<Leaderboard> sequence, Type type, string? types) {
        if (types != null) {
            if (types == "all") {
                return sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.outdated);
            } else {
                var statuses = types.Split(",").Select(type => (DifficultyStatus)Enum.Parse(typeof(DifficultyStatus), type)).ToList();
                return sequence.Where(leaderboard => statuses.Contains(leaderboard.Difficulty.Status) && leaderboard.Difficulty.Status != DifficultyStatus.outdated);
            }
        } else {
            return sequence.WhereType(type);
        }
    }

    private static IQueryable<Leaderboard> WhereMapType(this IQueryable<Leaderboard> sequence, MapTypes mapType, Operation allTypes)
    {
        if (mapType == null)
        {
            return sequence;
        }

        var score = Expression.Parameter(typeof(Leaderboard), "s");
        // 1 != 2 is here to trigger `OrElse` further the line.
        var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(allTypes == Operation.Any ? 2 : 1));

        foreach (MapTypes term in Enum.GetValues(typeof(MapTypes))) {
            if (term != MapTypes.None && mapType.HasFlag(term)) {
                var subexpression = Expression.Equal(Expression.Property(Expression.Property(score, "Difficulty"), $"Type{term.ToString()}"), Expression.Constant(true));
                exp = Expression.OrElse(exp, subexpression);

                exp = allTypes switch {
                    Operation.Any => Expression.OrElse(exp, subexpression),
                    Operation.All => Expression.And(exp, subexpression),
                    Operation.Not => Expression.And(exp, Expression.Not(subexpression)),
                    _ => exp,
                };
            }
        }
        return sequence.Where((Expression<Func<Leaderboard, bool>>)Expression.Lambda(exp, score));
    }

    private static IQueryable<Leaderboard> WhereSongStatus(this IQueryable<Leaderboard> sequence, SongStatus status)
    {
        if (status == SongStatus.None)
        {
            return sequence;
        }

        return sequence.Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.outdated && (leaderboard.Song.Status & status) != 0);
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

    private static IQueryable<Leaderboard> WherePlaylists(this IQueryable<Leaderboard> sequence, List<PlaylistResponse>? playlists)
    {
        if (playlists == null)
        {
            return sequence;
        }

        var hashes = playlists.SelectMany(p => p.songs.Where(s => s.hash != null).Select(s => (string)s.hash!)).ToList();
        var keys = playlists.SelectMany(p => p.songs.Where(s => s.hash == null && s.key != null).Select(s => (string)s.key!)).ToList();

        if (hashes.Count == 0 && keys.Count == 0) {
            return sequence;
        }
        return sequence.Where(lb => (hashes.Count > 0 && hashes.Contains(lb.Song.Hash)) || (keys.Count > 0 && keys.Contains(lb.SongId)));
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

        var score = Expression.Parameter(typeof(Leaderboard), "s");
        // 1 != 2 is here to trigger `OrElse` further the line.
        var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(allRequirements == Operation.Any ? 2 : 1));

        foreach (Requirements term in Enum.GetValues(typeof(Requirements))) {
            if (term != Requirements.Ignore && term != Requirements.None && mapRequirements.HasFlag(term)) {
                var subexpression = Expression.Equal(Expression.Property(Expression.Property(score, "Difficulty"), $"Requires{term.ToString()}"), Expression.Constant(true));
                exp = Expression.OrElse(exp, subexpression);

                exp = allRequirements switch {
                    Operation.Any => Expression.OrElse(exp, subexpression),
                    Operation.All => Expression.And(exp, subexpression),
                    Operation.Not => Expression.And(exp, Expression.Not(subexpression)),
                    _ => exp,
                };
            }
        }
        return sequence.Where((Expression<Func<Leaderboard, bool>>)Expression.Lambda(exp, score));
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

    private static IQueryable<Leaderboard> WhereDateFrom(this IQueryable<Leaderboard> sequence, DateRangeType rangeType, Type type, int? dateFrom, int? dateTo)
    {
        if (dateFrom == null && dateTo == null)
        {
            return sequence;
        }

        switch (rangeType) {
            case DateRangeType.Upload:
                sequence = sequence.Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo));
                break;
            case DateRangeType.Ranked:
                switch (type) {
                    case Type.Ranked:
                        sequence = sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.RankedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.RankedTime <= dateTo));
                        break;
                    case Type.Ranking:
                        sequence = sequence.Where(leaderboard => (dateFrom == null
                                    || leaderboard.Difficulty.RankedTime >= dateFrom
                                    || leaderboard.Difficulty.NominatedTime >= dateFrom
                                    || leaderboard.Difficulty.QualifiedTime >= dateFrom
                                    || leaderboard.Changes!.OrderByDescending(leaderboardChange => leaderboardChange.Timeset).FirstOrDefault()!.Timeset >= dateFrom)
                                   && (dateTo == null
                                    || leaderboard.Difficulty.RankedTime <= dateTo
                                    || leaderboard.Difficulty.NominatedTime <= dateTo
                                    || leaderboard.Difficulty.QualifiedTime <= dateTo
                                    || leaderboard.Changes!.OrderByDescending(leaderboardChange => leaderboardChange.Timeset).FirstOrDefault()!.Timeset <= dateTo));
                        break;
                    case Type.Nominated:
                        sequence = sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.NominatedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.NominatedTime <= dateTo));
                        break;
                    case Type.Qualified:
                        sequence = sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.QualifiedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.QualifiedTime <= dateTo));
                        break;
                    default:
                        break;
                }
                break;
            case DateRangeType.Score:
                sequence = sequence.Where(leaderboard => leaderboard.Scores.Any(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)));
                break;
            default:
                break;
        }

        return sequence;
    }

    public static async Task<(IQueryable<Leaderboard>, int)> WherePage(this IQueryable<Leaderboard> sequence, int page, int count)
    {
        if (page <= 0) {
            page = 1;
        }

        return (sequence.Skip((page - 1) * count).Take(count), await sequence.TagWithCallerS().CountAsync());
    }
}