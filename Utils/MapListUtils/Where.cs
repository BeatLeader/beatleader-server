using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Song> WhereType(this IQueryable<Song> sequence, Type type) =>
        type switch
        {
            Type.Ranked      => sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.ranked)),
            Type.Ranking     => sequence.Where(s => 
                s.Difficulties.Any(d => 
                    d.Status != DifficultyStatus.unranked && 
                    d.Status != DifficultyStatus.outdated && 
                    d.Status != DifficultyStatus.inevent
                )
            ),
            Type.Nominated   => sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.nominated)),
            Type.Qualified   => sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.qualified)),
            Type.Staff       => sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.qualified || d.Status == DifficultyStatus.nominated)),
            Type.Reweighting => sequence.Where(s => s.Leaderboards.Any(l => l.Reweight != null && !l.Reweight.Finished)),
            Type.Reweighted  => sequence.Where(s => s.Leaderboards.Any(l => l.Reweight != null && l.Reweight.Finished)),
            Type.Unranked    => sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.unranked)),
            Type.Ost         => sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.OST)),
            _                => sequence.Where(s => s.Difficulties.Any(d => d.Status != DifficultyStatus.outdated)),
        };

    private static IQueryable<Song> WhereMapType(this IQueryable<Song> sequence, int? mapType, Operation allTypes)
    {
        if (mapType == null)
        {
            return sequence;
        }

        return allTypes switch
        {
            Operation.Any => sequence.Where(s => s.Difficulties.Any(d => (d.Type & mapType) != 0)),
            Operation.All => sequence.Where(s => s.Difficulties.Any(d => d.Type == mapType)),
            Operation.Not => sequence.Where(s => s.Difficulties.Any(d => (d.Type & mapType) == 0)),
            _             => sequence,
        };
    }

    private static IQueryable<Song> WhereSongStatus(this IQueryable<Song> sequence, SongStatus status)
    {
        if (status == SongStatus.None)
        {
            return sequence;
        }

        return sequence.Where(s => s.Difficulties.Any(d => d.Status != DifficultyStatus.outdated) && status.HasFlag(s.Status));
    }

    private static IQueryable<Song> WhereMapper(this IQueryable<Song> sequence, string? mapper)
    {
        if (mapper == null)
        {
            return sequence;
        }

        var ids = mapper.Split(",").Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0).ToArray();
        if (ids.Length == 0) {
            return sequence;
        }
        return sequence.Where(s => s.Mappers.Any(m => ids.Contains(m.Id)));
    }

    private static IQueryable<Song> WhereMyType(this IQueryable<Song> sequence, MyType mytype, Player? currentPlayer, LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
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
            MyType.Played          => sequence.Where(s => s.Leaderboards.Any(l => l.Scores.Any(sc => sc.PlayerId == currentId && sc.ValidContexts.HasFlag(leaderboardContext)))),
            MyType.Unplayed        => sequence.Where(s => s.Leaderboards.Any(l => !l.Scores.Any(sc => sc.PlayerId == currentId && sc.ValidContexts.HasFlag(leaderboardContext)))),
            MyType.MyNominated     => sequence.Where(s => s.Leaderboards.Any(l => l.Qualification != null && l.Qualification.RTMember == currentId)),
            MyType.OthersNominated => sequence.Where(s => s.Leaderboards.Any(l => l.Qualification != null && l.Qualification.RTMember != currentId)),
            MyType.MyMaps          => sequence.Where(s => s.MapperId == mapperId),
            _                      => sequence,
        };
    }

    private static IQueryable<Song> WhereMode(this IQueryable<Song> sequence, string? mode)
    {
        if (mode == null)
        {
            return sequence;
        }

        return sequence.Where(s => s.Difficulties.Any(d => d.ModeName == mode));
    }

    private static IQueryable<Song> WhereDifficulty(this IQueryable<Song> sequence, string? difficulty)
    {
        if (difficulty == null)
        {
            return sequence;
        }

        return sequence.Where(s => s.Difficulties.Any(d => d.DifficultyName == difficulty));
    }

    private static IQueryable<Song> WhereMapRequirements(this IQueryable<Song> sequence, Requirements mapRequirements, Operation allRequirements)
    {
        if (mapRequirements == Requirements.Ignore)
        {
            return sequence;
        }

        return allRequirements switch
        {
            Operation.Any => sequence.Where(s => s.Difficulties.Any(d => (d.Requirements & mapRequirements) != 0)),
            Operation.All => sequence.Where(s => s.Difficulties.Any(d => d.Requirements == mapRequirements)),
            Operation.Not => sequence.Where(s => s.Difficulties.Any(d => (d.Requirements & mapRequirements) == 0)),
            _             => sequence,
        };
    }

    private static IQueryable<Song> WhereRatingFrom(this IQueryable<Song> sequence, RatingType rating, float? from)
    {
        if (from == null)
        {
            return sequence;
        }

        return rating switch {
            RatingType.Stars => sequence.Where(s => s.Difficulties.Any(d => d.Stars >= from)),
            RatingType.Acc => sequence.Where(s => s.Difficulties.Any(d => d.AccRating >= from)),
            RatingType.Pass => sequence.Where(s => s.Difficulties.Any(d => d.PassRating >= from)),
            RatingType.Tech => sequence.Where(s => s.Difficulties.Any(d => d.TechRating >= from)),
            _ => sequence,
        };
    }

    private static IQueryable<Song> WhereRatingTo(this IQueryable<Song> sequence, RatingType rating, float? to)
    {
        if (to == null)
        {
            return sequence;
        }

        return rating switch {
            RatingType.Stars => sequence.Where(s => s.Difficulties.Any(d => d.Stars <= to)),
            RatingType.Acc => sequence.Where(s => s.Difficulties.Any(d => d.AccRating <= to)),
            RatingType.Pass => sequence.Where(s => s.Difficulties.Any(d => d.PassRating <= to)),
            RatingType.Tech => sequence.Where(s => s.Difficulties.Any(d => d.TechRating <= to)),
            _ => sequence,
        };
    }

    public static async Task<(IQueryable<Song>, int)> WherePage(this IQueryable<Song> sequence, int page, int count)
    {
        if (page <= 0) {
            page = 1;
        }

        return (sequence.Skip((page - 1) * count).Take(count), await sequence.CountAsync());
    }
}