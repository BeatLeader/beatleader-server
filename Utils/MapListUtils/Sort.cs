using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> Sort(this IQueryable<Leaderboard> sequence,
                                                string? sortBy,
                                                string order,
                                                string? type,
                                                string? mytype,
                                                int? dateFrom,
                                                int? dateTo,
                                                string? currentId) =>
        sortBy switch
        {
            "timestamp"  => sequence.SortByTimestamp(order, type, dateFrom, dateTo),
            "name"       => sequence.SortByName(order, dateFrom, dateTo),
            "stars"      => sequence.SortByStars(order, dateFrom, dateTo),
            "passRating" => sequence.SortByPassRating(order, dateFrom, dateTo),
            "accRating"  => sequence.SortByAccRating(order, dateFrom, dateTo),
            "techRating" => sequence.SortByTechRating(order, dateFrom, dateTo),
            "scoreTime"  => sequence.SortByScoreTime(order, mytype, dateFrom, dateTo, currentId),
            "playcount"  => sequence.SortByPlayCount(order, dateFrom, dateTo),
            "voting"     => sequence.SortByVoting(order),
            "votecount"  => sequence.SortByVoteCount(order),
            "voteratio"  => sequence.SortByVoteRatio(order),
            _            => sequence,
        };

    private static IQueryable<Leaderboard> SortByTimestamp(this IQueryable<Leaderboard> sequence, string order, string? type, int? dateFrom, int? dateTo) =>
        type switch
        {
            "nominated" => sequence.SortByNominated(order, dateFrom, dateTo),
            "qualified" => sequence.SortByQualified(order, dateFrom, dateTo),
            "ranking" => sequence.SortByRanking(dateFrom, dateTo),
            _ => sequence.SortByDate(order, dateFrom, dateTo),
        };

    private static IOrderedQueryable<Leaderboard> SortByNominated(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.NominatedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.NominatedTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.NominatedTime);

    private static IOrderedQueryable<Leaderboard> SortByQualified(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.QualifiedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.QualifiedTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.QualifiedTime);

    private static IQueryable<Leaderboard> SortByRanking(this IQueryable<Leaderboard> sequence, int? dateFrom, int? dateTo) =>
        sequence.Where(leaderboard => (dateFrom == null
                                    || leaderboard.Difficulty.RankedTime >= dateFrom
                                    || leaderboard.Difficulty.NominatedTime >= dateFrom
                                    || leaderboard.Difficulty.QualifiedTime >= dateFrom
                                    || leaderboard.Changes!.OrderByDescending(leaderboardChange => leaderboardChange.Timeset).FirstOrDefault()!.Timeset >= dateFrom)
                                   && (dateTo == null
                                    || leaderboard.Difficulty.RankedTime <= dateTo
                                    || leaderboard.Difficulty.NominatedTime <= dateTo
                                    || leaderboard.Difficulty.QualifiedTime <= dateTo
                                    || leaderboard.Changes!.OrderByDescending(leaderboardChange => leaderboardChange.Timeset).FirstOrDefault()!.Timeset <= dateTo));

    private static IOrderedQueryable<Leaderboard> SortByDate(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Difficulty.RankedTime >= dateFrom) && (dateTo == null || leaderboard.Difficulty.RankedTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Difficulty.RankedTime);

    private static IQueryable<Leaderboard> SortByName(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.Where(leaderboard => (dateFrom == null || leaderboard.Song.UploadTime >= dateFrom) && (dateTo == null || leaderboard.Song.UploadTime <= dateTo))
                .Order(order, leaderboard => leaderboard.Song.Name);

    private static IQueryable<Leaderboard> SortByStars(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.FilterRated(dateFrom, dateTo)
                .Order(order, leaderboard => leaderboard.Difficulty.Stars);

    private static IQueryable<Leaderboard> SortByPassRating(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.FilterRated(dateFrom, dateTo)
                .Order(order, leaderboard => leaderboard.Difficulty.PassRating);

    private static IQueryable<Leaderboard> SortByAccRating(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.FilterRated(dateFrom, dateTo)
                .Order(order, leaderboard => leaderboard.Difficulty.AccRating);

    private static IQueryable<Leaderboard> SortByTechRating(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) =>
        sequence.FilterRated(dateFrom, dateTo)
                .Order(order, leaderboard => leaderboard.Difficulty.TechRating);

    private static IQueryable<Leaderboard> FilterRated(this IQueryable<Leaderboard> sequence, int? dateFrom, int? dateTo) => sequence
        .Where(leaderboard => (dateFrom == null
                            || (leaderboard.Difficulty.Status == DifficultyStatus.nominated && leaderboard.Difficulty.NominatedTime >= dateFrom)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.qualified && leaderboard.Difficulty.QualifiedTime >= dateFrom)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.ranked && leaderboard.Difficulty.RankedTime >= dateFrom))
                           && (dateTo == null
                            || (leaderboard.Difficulty.Status == DifficultyStatus.nominated && leaderboard.Difficulty.NominatedTime <= dateTo)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.qualified && leaderboard.Difficulty.QualifiedTime <= dateTo)
                            || (leaderboard.Difficulty.Status == DifficultyStatus.ranked && leaderboard.Difficulty.RankedTime <= dateTo)))
        .Include(leaderboard => leaderboard.Difficulty);

    private static IQueryable<Leaderboard> SortByScoreTime(this IQueryable<Leaderboard> sequence, string order, string? mytype, int? dateFrom, int? dateTo, string? currentId)
    {
        if (mytype == "played")
        {
            return sequence.Order(order, leaderboard => leaderboard.Scores
                                                                   .Where(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo) && score.PlayerId == currentId)
                                                                   .Max(score => score.Timepost));
        }

        return sequence.Order(order, leaderboard => leaderboard.Scores
                                                               .Where(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))
                                                               .Max(score => score.Timepost));
    }

    private static IQueryable<Leaderboard> SortByPlayCount(this IQueryable<Leaderboard> sequence, string order, int? dateFrom, int? dateTo) => sequence
        .Order(order, leaderboard => leaderboard.Scores.Count(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)));

    private static IQueryable<Leaderboard> SortByVoting(this IQueryable<Leaderboard> sequence, string order) => sequence
        .Order(order, leaderboard => leaderboard.PositiveVotes - leaderboard.NegativeVotes);

    private static IQueryable<Leaderboard> SortByVoteCount(this IQueryable<Leaderboard> sequence, string order) => sequence
        .Order(order, leaderboard => leaderboard.PositiveVotes + leaderboard.NegativeVotes);

    private static IQueryable<Leaderboard> SortByVoteRatio(this IQueryable<Leaderboard> sequence, string order) => sequence
        .Where(leaderboard => leaderboard.PositiveVotes > 0 || leaderboard.NegativeVotes > 0)
        .Order(order, leaderboard => (int)(leaderboard.PositiveVotes / (leaderboard.PositiveVotes + leaderboard.NegativeVotes) * 100.0))
        .ThenOrder(order, leaderboard => leaderboard.PositiveVotes + leaderboard.NegativeVotes);
}