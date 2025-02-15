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
                                                DateRangeType rangeType,
                                                int? searchId,
                                                Player? currentPlayer,
                                                LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
        var result = sortBy switch
        {
            MapSortBy.Timestamp  => sequence.SortByTimestamp(order, type, searchId),
            MapSortBy.Name       => sequence.SortById(order, searchId),
            MapSortBy.Stars      => sequence.SortByStars(order, searchId, currentPlayer),
            MapSortBy.PassRating => sequence.SortByPassRating(order, searchId, currentPlayer),
            MapSortBy.AccRating  => sequence.SortByAccRating(order, searchId, currentPlayer),
            MapSortBy.TechRating => sequence.SortByTechRating(order, searchId, currentPlayer),
            MapSortBy.ScoreTime  => sequence.SortByScoreTime(order, mytype, dateFrom, dateTo, rangeType, searchId, currentPlayer?.Id),
            MapSortBy.PlayCount  => sequence.SortByPlayCount(order, dateFrom, dateTo, rangeType, searchId, leaderboardContext),
            MapSortBy.Voting     => sequence.SortByVoting(order, searchId),
            MapSortBy.VoteCount  => sequence.SortByVoteCount(order, searchId),
            MapSortBy.VoteRatio  => sequence.SortByVoteRatio(order, searchId),
            MapSortBy.Duration   => sequence.SortByDuration(order, searchId),
            MapSortBy.Attempts   => sequence.SortByAttempts(order, searchId),
            MapSortBy.NJS   => sequence.SortByNJS(order, searchId),
            MapSortBy.NPS   => sequence.SortByNPS(order, searchId),
            MapSortBy.BPM   => sequence.SortByBPM(order, searchId),
            _                 => sequence.SortById(order, searchId),
        };

        return result.ThenByDescending(l => l.Difficulty.Status <= DifficultyStatus.ranked ? (int)l.Difficulty.Status : -1).ThenBy(l => l.Timestamp);
    }

    private static IOrderedQueryable<Leaderboard> SortByTimestamp(this IQueryable<Leaderboard> sequence, Order order, Type type, int? searchId) =>
        type switch
        {
            Type.Nominated => sequence.SortByNominated(order, searchId),
            Type.Qualified => sequence.SortByQualified(order, searchId),
            Type.Ranking   => sequence.SortByRanking(order, searchId),
            Type.Ranked    => sequence.SortByRanked(order, searchId),
            _ => sequence.SortByDate(order, searchId),
        };

    private static IOrderedQueryable<Leaderboard> SortByNominated(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.Order(order, leaderboard => leaderboard.Difficulty.NominatedTime);

    private static IOrderedQueryable<Leaderboard> SortByQualified(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.Order(order, leaderboard => leaderboard.Difficulty.QualifiedTime);

    private static IOrderedQueryable<Leaderboard> SortByRanked(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.Order(order, leaderboard => leaderboard.Difficulty.RankedTime);

    private static IOrderedQueryable<Leaderboard> SortByRanking(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.Order(order, leaderboard => leaderboard.Difficulty.RankedTime);

    private static IOrderedQueryable<Leaderboard> SortByDate(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Song.UploadTime);

    private static IOrderedQueryable<Leaderboard> SortByName(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Song.Name);

    private static IOrderedQueryable<Leaderboard> SortById(this IQueryable<Leaderboard> sequence, Order order, int? searchId) =>
        sequence.OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Id);

    private static IOrderedQueryable<Leaderboard> SortByStars(this IQueryable<Leaderboard> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.Stars : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByPassRating(this IQueryable<Leaderboard> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.PassRating : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByAccRating(this IQueryable<Leaderboard> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.AccRating : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByTechRating(this IQueryable<Leaderboard> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        return sequence
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => (
                    showRatings || 
                    leaderboard.Difficulty.Status == DifficultyStatus.nominated || 
                    leaderboard.Difficulty.Status == DifficultyStatus.qualified || 
                    leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                    leaderboard.Difficulty.Status == DifficultyStatus.OST) ? leaderboard.Difficulty.TechRating : 0);
    }

    private static IOrderedQueryable<Leaderboard> SortByScoreTime(this IQueryable<Leaderboard> sequence, Order order, MyType mytype, int? dateFrom, int? dateTo, DateRangeType rangeType, int? searchId, string? currentId)
    {
        if (mytype == MyType.Played)
        {
            return sequence
                .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, leaderboard => leaderboard.Scores
                                                                   .Where(score => (rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))) && score.PlayerId == currentId)
                                                                   .Max(score => score.Timepost));
        }

        return sequence
            .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
            .ThenOrder(order, leaderboard => leaderboard.Scores
                                                               .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)))
                                                               .Max(score => score.Timepost));
    }

    private static IOrderedQueryable<Leaderboard> SortByPlayCount(this IQueryable<Leaderboard> sequence, Order order, int? dateFrom, int? dateTo, DateRangeType rangeType, int? searchId, LeaderboardContexts leaderboardContext = LeaderboardContexts.General) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))));

    private static IOrderedQueryable<Leaderboard> SortByVoting(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes - leaderboard.NegativeVotes);

    private static IOrderedQueryable<Leaderboard> SortByVoteCount(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes + leaderboard.NegativeVotes);

    private static IOrderedQueryable<Leaderboard> SortByVoteRatio(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .Where(leaderboard => leaderboard.PositiveVotes > 0 || leaderboard.NegativeVotes > 0)
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => (int)(leaderboard.PositiveVotes / (leaderboard.PositiveVotes + leaderboard.NegativeVotes) * 100.0))
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes + leaderboard.NegativeVotes);

    private static IOrderedQueryable<Leaderboard> SortByDuration(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Song.Duration);

    private static IOrderedQueryable<Leaderboard> SortByAttempts(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.PlayCount);

    private static IOrderedQueryable<Leaderboard> SortByNJS(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Difficulty.Njs);

    private static IOrderedQueryable<Leaderboard> SortByNPS(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Difficulty.Nps);

    private static IOrderedQueryable<Leaderboard> SortByBPM(this IQueryable<Leaderboard> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(l => searchId != null ? l.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, leaderboard => leaderboard.Song.Bpm);
}