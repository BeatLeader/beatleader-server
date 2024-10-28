using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class LeaderboardListUtils
{
    private static IQueryable<Leaderboard> Sort(this IQueryable<Leaderboard> sequence,
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

        return result.ThenByDescending(l => l.Difficulty.Status <= DifficultyStatus.ranked ? (int)l.Difficulty.Status : -1).ThenBy(l => l.Timestamp);
    }

    private static IOrderedQueryable<Leaderboard> SortByTimestamp(this IQueryable<Leaderboard> sequence, Order order, Type type, int? dateFrom, int? dateTo, int? searchId) =>
        type switch
        {
            Type.Nominated => sequence.SortByNominated(order, dateFrom, dateTo, searchId),
            Type.Qualified => sequence.SortByQualified(order, dateFrom, dateTo, searchId),
            Type.Ranking   => sequence.SortByRanking(order, dateFrom, dateTo, searchId),
            Type.Ranked    => sequence.SortByRanked(order, dateFrom, dateTo, searchId),
            _ => sequence.SortByDate(order, dateFrom, dateTo, searchId),
        };

    private static IOrderedQueryable<Leaderboard> SortByNominated(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.NominatedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.NominatedTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.NominatedTime);

    private static IOrderedQueryable<Leaderboard> SortByQualified(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.QualifiedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.QualifiedTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.QualifiedTime);

    private static IOrderedQueryable<Leaderboard> SortByRanked(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.RankedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.RankedTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.RankedTime);

    private static IOrderedQueryable<Leaderboard> SortByRanking(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(leaderboard => (dateFrom == null
                                    || leaderboard.Difficulty.RankedTime >= dateFrom
                                    || leaderboard.Difficulty.NominatedTime >= dateFrom
                                    || leaderboard.Difficulty.QualifiedTime >= dateFrom
                                    || leaderboard.Changes!.OrderByDescending(leaderboardChange => leaderboardChange.Timeset).FirstOrDefault()!.Timeset >= dateFrom)
                                   && (dateTo == null
                                    || leaderboard.Difficulty.RankedTime <= dateTo
                                    || leaderboard.Difficulty.NominatedTime <= dateTo
                                    || leaderboard.Difficulty.QualifiedTime <= dateTo
                                    || leaderboard.Changes!.OrderByDescending(leaderboardChange => leaderboardChange.Timeset).FirstOrDefault()!.Timeset <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.RankedTime);

    private static IOrderedQueryable<Leaderboard> SortByDate(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo))
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Song.UploadTime);

    private static IOrderedQueryable<Leaderboard> SortByName(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo))
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Song.Name);

    private static IOrderedQueryable<Leaderboard> SortByStars(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.Stars : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByPassRating(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.PassRating : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByAccRating(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.AccRating : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByTechRating(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence.FilterRated(dateFrom, dateTo, searchId)
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.TechRating : 0);
    }

    private static IQueryable<Leaderboard> FilterRated(this IQueryable<Leaderboard> sequence, int? dateFrom, int? dateTo, int? searchId) => sequence
        .Where(leaderboard => (dateFrom == null
                            || (leaderboard.Difficulty.Status == DifficultyStatus.nominated && leaderboard.Difficulty.NominatedTime >= dateFrom)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.qualified && leaderboard.Difficulty.QualifiedTime >= dateFrom)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.ranked && leaderboard.Difficulty.RankedTime >= dateFrom))
                           && (dateTo == null
                            || (leaderboard.Difficulty.Status == DifficultyStatus.nominated && leaderboard.Difficulty.NominatedTime <= dateTo)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.qualified && leaderboard.Difficulty.QualifiedTime <= dateTo)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.ranked && leaderboard.Difficulty.RankedTime <= dateTo)))
        .Include(leaderboard => leaderboard.Difficulty);

    private static IOrderedQueryable<Leaderboard> SortByScoreTime(this IQueryable<Leaderboard> sequence, Order order, MyType mytype, int? dateFrom, int? dateTo, int? searchId, string? currentId)
    {
        if (mytype == MyType.Played)
        {
            return sequence
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Scores
                                                                   .Where(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo) && score.PlayerId == currentId)
                                                                   .Max(score => score.Timepost));
        }

        return sequence
            .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
            .ThenOrder(order, leaderboard => leaderboard.Scores
                                                               .Where(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))
                                                               .Max(score => score.Timepost));
    }

    private static IOrderedQueryable<Leaderboard> SortByPlayCount(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId, LeaderboardContexts leaderboardContext = LeaderboardContexts.General) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)));

    private static IOrderedQueryable<Leaderboard> SortByVoting(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo))
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes - leaderboard.NegativeVotes);

    private static IOrderedQueryable<Leaderboard> SortByVoteCount(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo))
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes + leaderboard.NegativeVotes);

    private static IOrderedQueryable<Leaderboard> SortByVoteRatio(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(leaderboard => 
            (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && 
            (dateTo == null || leaderboard.Song.UploadTime <= dateTo) && 
            (leaderboard.PositiveVotes > 0 || leaderboard.NegativeVotes > 0))
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => (int)(leaderboard.PositiveVotes / (leaderboard.PositiveVotes + leaderboard.NegativeVotes) * 100.0))
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes + leaderboard.NegativeVotes);

    private static IOrderedQueryable<Leaderboard> SortByDuration(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, int? searchId) => 
        sequence
        .Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo))
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Song.Duration);
}