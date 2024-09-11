using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Song> Sort(this IQueryable<Song> sequence,
                                                MapSortBy sortBy,
                                                Order order,
                                                Type type,
                                                MyType mytype,
                                                int? dateFrom,
                                                int? dateTo,
                                                int? searchId,
                                                Player? currentPlayer,
                                                LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
        var result = sortBy switch
        {
            MapSortBy.Timestamp  => sequence.SortByTimestamp(order, type, dateFrom, dateTo, searchId),
            MapSortBy.Name       => sequence.SortByName(order, dateFrom, dateTo, searchId),
            MapSortBy.Stars      => sequence.SortByStars(order, dateFrom, dateTo, searchId, currentPlayer),
            MapSortBy.PassRating => sequence.SortByPassRating(order, dateFrom, dateTo, searchId, currentPlayer),
            MapSortBy.AccRating  => sequence.SortByAccRating(order, dateFrom, dateTo, searchId, currentPlayer),
            MapSortBy.TechRating => sequence.SortByTechRating(order, dateFrom, dateTo, searchId, currentPlayer),
            MapSortBy.ScoreTime  => sequence.SortByScoreTime(order, mytype, dateFrom, searchId, dateTo, currentPlayer?.Id),
            MapSortBy.PlayCount  => sequence.SortByPlayCount(order, dateFrom, dateTo, searchId, leaderboardContext),
            MapSortBy.Voting     => sequence.SortByVoting(order, dateFrom, dateTo, searchId),
            MapSortBy.VoteCount  => sequence.SortByVoteCount(order, dateFrom, dateTo, searchId),
            MapSortBy.VoteRatio  => sequence.SortByVoteRatio(order, dateFrom, dateTo, searchId),
            MapSortBy.Duration   => sequence.SortByDuration(order, dateFrom, dateTo, searchId),
            _                 => sequence.SortByName(order, dateFrom, dateTo, searchId),
        };

        return result.ThenByDescending(s => s.Difficulties.OrderBy(d => d.Status <= DifficultyStatus.ranked ? (int)d.Status : -1).First().Status).ThenByDescending(l => l.Id);
    }

    private static IOrderedQueryable<Song> SortByTimestamp(this IQueryable<Song> sequence, Order order, Type type, int? dateFrom, int? dateTo, int? searchId) =>
        type switch
        {
            Type.Nominated => sequence.SortByNominated(order, dateFrom, dateTo, searchId),
            Type.Qualified => sequence.SortByQualified(order, dateFrom, dateTo, searchId),
            Type.Ranked    => sequence.SortByRanked(order, dateFrom, dateTo, searchId),
            _ => sequence.SortByDate(order, dateFrom, dateTo, searchId),
        };

    private static IOrderedQueryable<Song> SortByNominated(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(s => s.Difficulties.Any(d => (dateFrom == null || d.NominatedTime >= dateFrom) && (dateTo == null || d.NominatedTime <= dateTo)))
                .Order(order, s => s.Difficulties.First(d => d.NominatedTime != 0).NominatedTime);

    private static IOrderedQueryable<Song> SortByQualified(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(s => s.Difficulties.Any(d => (dateFrom == null || d.QualifiedTime >= dateFrom) && (dateTo == null || d.QualifiedTime <= dateTo)))
                .Order(order, s => s.Difficulties.First(d => d.QualifiedTime != 0).QualifiedTime);

    private static IOrderedQueryable<Song> SortByRanked(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(s => s.Difficulties.Any(d => (dateFrom == null || d.RankedTime >= dateFrom) && (dateTo == null || d.RankedTime <= dateTo)))
                .Order(order, s => s.Difficulties.First(d => d.RankedTime != 0).RankedTime);

    private static IOrderedQueryable<Song> SortByDate(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(s => (dateFrom == null || s.UploadTime >= dateFrom) && (dateTo == null || s.UploadTime <= dateTo))
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.UploadTime);

    private static IOrderedQueryable<Song> SortByName(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(s => (dateFrom == null || s.UploadTime >= dateFrom) && (dateTo == null || s.UploadTime <= dateTo))
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Name);

    private static IOrderedQueryable<Song> SortByStars(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.Stars : 0)
                .OrderByDescending(d => d)
                .First());
    }

    private static IOrderedQueryable<Song> SortByPassRating(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.PassRating : 0)
                .OrderByDescending(d => d)
                .First());
    }

    private static IOrderedQueryable<Song> SortByAccRating(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.AccRating : 0)
                .OrderByDescending(d => d)
                .First());
    }

    private static IOrderedQueryable<Song> SortByTechRating(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.TechRating : 0)
                .OrderByDescending(d => d)
                .First());
    }

    private static IQueryable<Song> FilterRated(this IQueryable<Song> sequence, int? dateFrom, int? dateTo, int? searchId) => sequence
        .Where(s => s.Difficulties.Any(d => (dateFrom == null
                            || (d.Status == DifficultyStatus.nominated && d.NominatedTime >= dateFrom)
                            || (d.Status == DifficultyStatus.qualified && d.QualifiedTime >= dateFrom)
                            || (d.Status == DifficultyStatus.ranked && d.RankedTime >= dateFrom))
                           && (dateTo == null
                            || (d.Status == DifficultyStatus.nominated && d.NominatedTime <= dateTo)
                            || (d.Status == DifficultyStatus.qualified && d.QualifiedTime <= dateTo)
                            || (d.Status == DifficultyStatus.ranked && d.RankedTime <= dateTo))));

    private static IOrderedQueryable<Song> SortByScoreTime(this IQueryable<Song> sequence, Order order, MyType mytype, int? dateFrom, int? dateTo, int? searchId, string? currentId)
    {
        if (mytype == MyType.Played)
        {
            return sequence
                .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Leaderboards.Max(l => l.Scores
                                                                .Where(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo) && score.PlayerId == currentId)
                                                                .Max(score => score.Timepost)));
        }

        return sequence
            .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
            .ThenOrder(order, s => s.Leaderboards.Max(l => l.Scores
                                                               .Where(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))
                                                               .Max(score => score.Timepost)));
    }

    private static IOrderedQueryable<Song> SortByPlayCount(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, LeaderboardContexts leaderboardContext = LeaderboardContexts.General) => 
        sequence
        .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Leaderboards.Sum(l => l.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))));

    private static IOrderedQueryable<Song> SortByVoting(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(s => (dateFrom == null || s.UploadTime >= dateFrom) && (dateTo == null || s.UploadTime <= dateTo))
        .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Leaderboards.Sum(l => l.PositiveVotes - l.NegativeVotes));

    private static IOrderedQueryable<Song> SortByVoteCount(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(s => (dateFrom == null || s.UploadTime >= dateFrom) && (dateTo == null || s.UploadTime <= dateTo))
        .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Leaderboards.Sum(l => l.PositiveVotes + l.NegativeVotes));

    private static IOrderedQueryable<Song> SortByVoteRatio(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(s => 
            (dateFrom == null || s.UploadTime >= dateFrom) && 
            (dateTo == null || s.UploadTime <= dateTo) && 
            s.Leaderboards.Any(l => l.PositiveVotes > 0 || l.NegativeVotes > 0))
        .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Leaderboards.Sum(l => (int)(l.PositiveVotes / (l.PositiveVotes + l.NegativeVotes) * 100.0)))
        .ThenOrder(order, s => s.Leaderboards.Sum(l => l.PositiveVotes + l.NegativeVotes));

    private static IOrderedQueryable<Song> SortByDuration(this IQueryable<Song> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(s => (dateFrom == null || s.UploadTime >= dateFrom) && (dateTo == null || s.UploadTime <= dateTo))
        .OrderByDescending(s => searchId != null ? s.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Duration);
}