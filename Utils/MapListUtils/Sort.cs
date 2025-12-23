using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IOrderedQueryable<SongHelper> ThenSort(this IOrderedQueryable<SongHelper> sequence,
                                                MapSortBy sortBy,
                                                Order order,
                                                Type type,
                                                MyTypeMaps mytype,
                                                int? dateFrom,
                                                int? dateTo,
                                                DateRangeType rangeType,
                                                Player? currentPlayer,
                                                LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
        var result = sortBy switch
        {
            MapSortBy.Timestamp  => sequence.SortByTimestamp(order, type),
            MapSortBy.Name       => sequence.SortByName(order),
            MapSortBy.Stars      => sequence.SortByStars(order, currentPlayer),
            MapSortBy.PassRating => sequence.SortByPassRating(order, currentPlayer),
            MapSortBy.AccRating  => sequence.SortByAccRating(order, currentPlayer),
            MapSortBy.TechRating => sequence.SortByTechRating(order, currentPlayer),
            MapSortBy.ScoreTime  => sequence.SortByScoreTime(order, mytype, dateFrom, dateTo, rangeType, currentPlayer),
            MapSortBy.PlayCount  => sequence.SortByPlayCount(order, dateFrom, dateTo, rangeType, leaderboardContext),
            MapSortBy.Voting     => sequence.SortByVoting(order),
            MapSortBy.VoteCount  => sequence.SortByVoteCount(order),
            MapSortBy.VoteRatio  => sequence.SortByVoteRatio(order),
            MapSortBy.Duration   => sequence.SortByDuration(order),
            MapSortBy.Attempts   => sequence.SortByAttempts(order),
            MapSortBy.NJS        => sequence.SortByNJS(order),
            MapSortBy.NPS        => sequence.SortByNPS(order),
            MapSortBy.BPM        => sequence.SortByBPM(order),
            _                    => sequence.SortByName(order),
        };

        return result;
    }

    private static IQueryable<SongHelper> Sort(this IQueryable<SongHelper> sequence,
                                                MapSortBy sortBy,
                                                Order order,
                                                MapSortBy thenSortBy,
                                                Order thenOrder,
                                                Type type,
                                                MyTypeMaps mytype,
                                                int? dateFrom,
                                                int? dateTo,
                                                DateRangeType rangeType,
                                                int? searchId,
                                                Player? currentPlayer,
                                                LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
        var orderedQuery = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);

        return orderedQuery
            .ThenSort(sortBy, order, type, mytype, dateFrom, dateTo, rangeType, currentPlayer, leaderboardContext)
            .ThenSort(thenSortBy, thenOrder, type, mytype, dateFrom, dateTo, rangeType, currentPlayer, leaderboardContext);
    }

    private static IOrderedQueryable<SongHelper> SortByTimestamp(this IOrderedQueryable<SongHelper> sequence, Order order, Type type) =>
        type switch
        {
            Type.Nominated => sequence.ThenOrder(order, s => s.Difficulties.First(d => d.Status == DifficultyStatus.nominated).NominatedTime),
            Type.Qualified => sequence.ThenOrder(order, s => s.Difficulties.First(d => d.Status == DifficultyStatus.qualified).QualifiedTime),
            Type.Ranked    => sequence.ThenOrder(order, s => s.Difficulties.First(d => d.Status == DifficultyStatus.ranked).RankedTime),
            _             => sequence.ThenOrder(order, s => s.Song.UploadTime),
        };

    private static IOrderedQueryable<SongHelper> SortByName(this IOrderedQueryable<SongHelper> sequence, Order order) =>
        sequence.ThenOrder(order, s => s.Song.Name);

    private static IOrderedQueryable<SongHelper> SortByStars(this IOrderedQueryable<SongHelper> sequence, Order order, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.Stars : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.Stars : 0)
                .OrderBy(d => d)
                .First());
        }  
    }

    private static IOrderedQueryable<SongHelper> SortByPassRating(this IOrderedQueryable<SongHelper> sequence, Order order, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.PassRating : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.PassRating : 0)
                .OrderBy(d => d)
                .First());
        }
    }

    private static IOrderedQueryable<SongHelper> SortByAccRating(this IOrderedQueryable<SongHelper> sequence, Order order, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.AccRating : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.AccRating : 0)
                .OrderBy(d => d)
                .First());
        }
    }

    private static IOrderedQueryable<SongHelper> SortByTechRating(this IOrderedQueryable<SongHelper> sequence, Order order, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.TechRating : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return sequence.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.TechRating : 0)
                .OrderBy(d => d)
                .First());
        }
    }

    private static IOrderedQueryable<SongHelper> SortByScoreTime(this IOrderedQueryable<SongHelper> sequence, Order order, MyTypeMaps mytype, int? dateFrom, int? dateTo, DateRangeType rangeType, Player? currentPlayer)
    {
        if (mytype == MyTypeMaps.Played || mytype == MyTypeMaps.FriendsPlayed)
        {
            string? currentId = currentPlayer?.Id;
            if (currentId == null) {
                return sequence;
            }

            var friendIds = new List<string> { currentId };
            if (mytype == MyTypeMaps.FriendsPlayed) {
                if (currentPlayer?.Friends != null) {
                    friendIds.AddRange(currentPlayer.Friends.First().Friends.Select(f => f.Id).ToList());
                }

                if (order == Order.Desc) {
                    return sequence.ThenOrder(order, s => s.Song.Leaderboards.Max(l => l.Scores
                                                                    .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && friendIds.Contains(score.PlayerId))
                                                                    .Max(score => score.Timepost)));
                } else {
                    return sequence.ThenOrder(order, s => s.Song.Leaderboards.Min(l => l.Scores
                                                                    .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && friendIds.Contains(score.PlayerId))
                                                                    .Min(score => score.Timepost)));
                }
            }

            if (order == Order.Desc) {
                return sequence.ThenOrder(order, s => s.Song.Leaderboards.Max(l => l.Scores
                                                                .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && score.PlayerId == currentId)
                                                                .Max(score => score.Timepost)));
            } else {
                return sequence.ThenOrder(order, s => s.Song.Leaderboards.Min(l => l.Scores
                                                                .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && score.PlayerId == currentId)
                                                                .Min(score => score.Timepost)));
            }
        }

        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Song.Leaderboards
                .Where(lb => rangeType != DateRangeType.Score || ((dateFrom == null || lb.LastScoreTime >= dateFrom) && (dateTo == null || lb.LastScoreTime <= dateTo)))
                .Max(lb => lb.LastScoreTime));
        } else {
            return sequence.ThenOrder(order, s => s.Song.Leaderboards
                .Where(lb => rangeType != DateRangeType.Score || ((dateFrom == null || lb.LastScoreTime >= dateFrom) && (dateTo == null || lb.LastScoreTime <= dateTo)))
                .Min(lb => lb.LastScoreTime));
        }
    }

    private static IOrderedQueryable<SongHelper> SortByPlayCount(this IOrderedQueryable<SongHelper> sequence, Order order, int? dateFrom, int? dateTo, DateRangeType rangeType, LeaderboardContexts leaderboardContext = LeaderboardContexts.General) { 
        
        if ((dateTo == null && dateFrom == null) || rangeType != DateRangeType.Score) {
            return sequence.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.Plays));
        }

        if (dateTo == null && dateFrom != null) {
            var currentTime = Time.UnixNow();
            if (Math.Abs(currentTime - (int)dateFrom - 60 * 60 * 24) < 60 * 30) {
                return sequence.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.TodayPlays));
            }
            if (Math.Abs(currentTime - (int)dateFrom - 60 * 60 * 24 * 7) < 60 * 30) {
                return sequence.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.ThisWeekPlays));
            }
        }
        return sequence
        .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))));
    }

    private static IOrderedQueryable<SongHelper> SortByVoting(this IOrderedQueryable<SongHelper> sequence, Order order) => 
        sequence.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PositiveVotes - l.NegativeVotes));

    private static IOrderedQueryable<SongHelper> SortByVoteCount(this IOrderedQueryable<SongHelper> sequence, Order order) => 
        sequence.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PositiveVotes + l.NegativeVotes));

    private static IOrderedQueryable<SongHelper> SortByVoteRatio(this IOrderedQueryable<SongHelper> sequence, Order order) => 
        sequence
        .ThenOrder(order, s => 
            (int)(s.Song.Leaderboards.Where(l => l.PositiveVotes > 0 || l.NegativeVotes > 0).Sum(l => l.PositiveVotes) / 
            s.Song.Leaderboards.Where(l => l.PositiveVotes > 0 || l.NegativeVotes > 0).Sum(l => l.PositiveVotes + l.NegativeVotes) * 100.0))
        .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PositiveVotes + l.NegativeVotes));

    private static IOrderedQueryable<SongHelper> SortByDuration(this IOrderedQueryable<SongHelper> sequence, Order order) => 
        sequence.ThenOrder(order, s => s.Song.Duration);

    private static IOrderedQueryable<SongHelper> SortByAttempts(this IOrderedQueryable<SongHelper> sequence, Order order) {
        return sequence.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PlayCount));
    }

    private static IOrderedQueryable<SongHelper> SortByNJS(this IOrderedQueryable<SongHelper> sequence, Order order) {
        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Difficulties.Max(d => d.Njs));
        } else {
            return sequence.ThenOrder(order, s => s.Difficulties.Min(d => d.Njs));
        }
    }

    private static IOrderedQueryable<SongHelper> SortByNPS(this IOrderedQueryable<SongHelper> sequence, Order order) {
        if (order == Order.Desc) {
            return sequence.ThenOrder(order, s => s.Difficulties.Max(d => d.Nps));
        } else {
            return sequence.ThenOrder(order, s => s.Difficulties.Min(d => d.Nps));
        }
    }

    private static IOrderedQueryable<SongHelper> SortByBPM(this IOrderedQueryable<SongHelper> sequence, Order order) =>
        sequence.ThenOrder(order, s => s.Song.Bpm);
}