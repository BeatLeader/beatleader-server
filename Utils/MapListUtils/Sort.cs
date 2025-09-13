using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<SongHelper> Sort(this IQueryable<SongHelper> sequence,
                                                MapSortBy sortBy,
                                                Order order,
                                                Type type,
                                                MyTypeMaps mytype,
                                                int? dateFrom,
                                                int? dateTo,
                                                DateRangeType rangeType,
                                                int? searchId,
                                                Player? currentPlayer,
                                                LeaderboardContexts leaderboardContext = LeaderboardContexts.General) {
        var result = sortBy switch
        {
            MapSortBy.Timestamp  => sequence.SortByTimestamp(order, type, searchId),
            MapSortBy.Name       => sequence.SortByName(order, searchId),
            MapSortBy.Stars      => sequence.SortByStars(order, searchId, currentPlayer),
            MapSortBy.PassRating => sequence.SortByPassRating(order, searchId, currentPlayer),
            MapSortBy.AccRating  => sequence.SortByAccRating(order, searchId, currentPlayer),
            MapSortBy.TechRating => sequence.SortByTechRating(order, searchId, currentPlayer),
            MapSortBy.ScoreTime  => sequence.SortByScoreTime(order, mytype, dateFrom, dateTo, rangeType, searchId, currentPlayer),
            MapSortBy.PlayCount  => sequence.SortByPlayCount(order, dateFrom, dateTo, rangeType, searchId, leaderboardContext),
            MapSortBy.Voting     => sequence.SortByVoting(order, searchId),
            MapSortBy.VoteCount  => sequence.SortByVoteCount(order, searchId),
            MapSortBy.VoteRatio  => sequence.SortByVoteRatio(order, searchId),
            MapSortBy.Duration   => sequence.SortByDuration(order, searchId),
            MapSortBy.Attempts   => sequence.SortByAttempts(order, searchId),
            MapSortBy.NJS        => sequence.SortByNJS(order, searchId),
            MapSortBy.NPS        => sequence.SortByNPS(order, searchId),
            MapSortBy.BPM        => sequence.SortByBPM(order, searchId),
            _                    => sequence.SortByName(order, searchId),
        };

        return result.ThenByDescending(s => s.Song.Id);
    }

    private static IOrderedQueryable<SongHelper> SortByTimestamp(this IQueryable<SongHelper> sequence, Order order, Type type, int? searchId) =>
        type switch
        {
            Type.Nominated => sequence.SortByNominated(order, searchId),
            Type.Qualified => sequence.SortByQualified(order, searchId),
            Type.Ranked    => sequence.SortByRanked(order, searchId),
            _             => sequence.SortByDate(order, searchId),
        };

    private static IOrderedQueryable<SongHelper> SortByNominated(this IQueryable<SongHelper> sequence, Order order, int? searchId) =>
        sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.nominated))
                .Order(order, s => s.Difficulties.First(d => d.Status == DifficultyStatus.nominated).NominatedTime);

    private static IOrderedQueryable<SongHelper> SortByQualified(this IQueryable<SongHelper> sequence, Order order, int? searchId) =>
        sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.qualified))
                .Order(order, s => s.Difficulties.First(d => d.Status == DifficultyStatus.qualified).QualifiedTime);

    private static IOrderedQueryable<SongHelper> SortByRanked(this IQueryable<SongHelper> sequence, Order order, int? searchId) =>
        sequence.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.ranked))
                .Order(order, s => s.Difficulties.First(d => d.Status == DifficultyStatus.ranked).RankedTime);

    private static IOrderedQueryable<SongHelper> SortByDate(this IQueryable<SongHelper> sequence, Order order, int? searchId) =>
        sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Song.UploadTime);

    private static IOrderedQueryable<SongHelper> SortByName(this IQueryable<SongHelper> sequence, Order order, int? searchId) =>
        sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Song.Name);

    private static IOrderedQueryable<SongHelper> SortByStars(this IQueryable<SongHelper> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.Stars : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.Stars : 0)
                .OrderBy(d => d)
                .First());
        }  
    }

    private static IOrderedQueryable<SongHelper> SortByPassRating(this IQueryable<SongHelper> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.PassRating : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.PassRating : 0)
                .OrderBy(d => d)
                .First());
        }
    }

    private static IOrderedQueryable<SongHelper> SortByAccRating(this IQueryable<SongHelper> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.AccRating : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.AccRating : 0)
                .OrderBy(d => d)
                .First());
        }
    }

    private static IOrderedQueryable<SongHelper> SortByTechRating(this IQueryable<SongHelper> sequence, Order order, int? searchId, Player? player) {
        bool showRatings = player?.ProfileSettings?.ShowAllRatings ?? false;
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.TechRating : 0)
                .OrderByDescending(d => d)
                .First());
        } else {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Select(d => 
                    showRatings || 
                    d.Status == DifficultyStatus.nominated || 
                    d.Status == DifficultyStatus.qualified || 
                    d.Status == DifficultyStatus.ranked || 
                    d.Status == DifficultyStatus.OST ? d.TechRating : 0)
                .OrderBy(d => d)
                .First());
        }
    }

    private static IOrderedQueryable<SongHelper> SortByScoreTime(this IQueryable<SongHelper> sequence, Order order, MyTypeMaps mytype, int? dateFrom, int? dateTo, DateRangeType rangeType, int? searchId, Player? currentPlayer)
    {
        if (mytype == MyTypeMaps.Played || mytype == MyTypeMaps.FriendsPlayed)
        {
            string? currentId = currentPlayer?.Id;
            if (currentId == null) {
                return sequence
                .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
            }

            var friendIds = new List<string> { currentId };
            if (mytype == MyTypeMaps.FriendsPlayed) {
                if (currentPlayer?.Friends != null) {
                    friendIds.AddRange(currentPlayer.Friends.First().Friends.Select(f => f.Id).ToList());
                }

                var tempSeq = sequence
                    .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
                if (order == Order.Desc) {
                    return tempSeq.ThenOrder(order, s => s.Song.Leaderboards.Max(l => l.Scores
                                                                    .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && friendIds.Contains(score.PlayerId))
                                                                    .Max(score => score.Timepost)));
                } else {
                    return tempSeq.ThenOrder(order, s => s.Song.Leaderboards.Min(l => l.Scores
                                                                    .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && friendIds.Contains(score.PlayerId))
                                                                    .Min(score => score.Timepost)));
                }
            }

            var tempSeq2 = sequence
                .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
            if (order == Order.Desc) {
                return tempSeq2.ThenOrder(order, s => s.Song.Leaderboards.Max(l => l.Scores
                                                                .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && score.PlayerId == currentId)
                                                                .Max(score => score.Timepost)));
            } else {
                return tempSeq2.ThenOrder(order, s => s.Song.Leaderboards.Min(l => l.Scores
                                                                .Where(score => rangeType != DateRangeType.Score || ((dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo)) && score.PlayerId == currentId)
                                                                .Min(score => score.Timepost)));
            }
        }

        var tempSeq3 = sequence
            .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq3.ThenOrder(order, s => s.Song.Leaderboards
                .Where(lb => rangeType != DateRangeType.Score || ((dateFrom == null || lb.LastScoreTime >= dateFrom) && (dateTo == null || lb.LastScoreTime <= dateTo)))
                .Max(lb => lb.LastScoreTime));
        } else {
            return tempSeq3.ThenOrder(order, s => s.Song.Leaderboards
                .Where(lb => rangeType != DateRangeType.Score || ((dateFrom == null || lb.LastScoreTime >= dateFrom) && (dateTo == null || lb.LastScoreTime <= dateTo)))
                .Min(lb => lb.LastScoreTime));
        }
    }

    private static IOrderedQueryable<SongHelper> SortByPlayCount(this IQueryable<SongHelper> sequence, Order order, int? dateFrom, int? dateTo, DateRangeType rangeType, int? searchId, LeaderboardContexts leaderboardContext = LeaderboardContexts.General) { 
        
        if ((dateTo == null && dateFrom == null) || rangeType != DateRangeType.Score) {
            return sequence
            .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
            .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.Plays));
        }

        if (dateTo == null && dateFrom != null) {
            var currentTime = Time.UnixNow();
            if (Math.Abs(currentTime - (int)dateFrom - 60 * 60 * 24) < 60 * 30) {
                return sequence
                .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.TodayPlays));
            }
            if (Math.Abs(currentTime - (int)dateFrom - 60 * 60 * 24 * 7) < 60 * 30) {
                return sequence
                .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.ThisWeekPlays));
            }
        }
        return sequence
        .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.Scores.Where(s => s.ValidContexts.HasFlag(leaderboardContext)).Count(score => (dateFrom == null || score.Timepost >= dateFrom) && (dateTo == null || score.Timepost <= dateTo))));
    }

    private static IOrderedQueryable<SongHelper> SortByVoting(this IQueryable<SongHelper> sequence, Order order, int? searchId) => 
        sequence
        .Where(s => s.Song.Leaderboards.Any(l => l.PositiveVotes > 0 || l.NegativeVotes > 0))
        .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PositiveVotes - l.NegativeVotes));

    private static IOrderedQueryable<SongHelper> SortByVoteCount(this IQueryable<SongHelper> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PositiveVotes + l.NegativeVotes));

    private static IOrderedQueryable<SongHelper> SortByVoteRatio(this IQueryable<SongHelper> sequence, Order order, int? searchId) => 
        sequence
        .Where(s => 
            s.Song.Leaderboards.Any(l => l.PositiveVotes > 0 || l.NegativeVotes > 0))
        .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => 
            (int)(s.Song.Leaderboards.Where(l => l.PositiveVotes > 0 || l.NegativeVotes > 0).Sum(l => l.PositiveVotes) / 
            s.Song.Leaderboards.Where(l => l.PositiveVotes > 0 || l.NegativeVotes > 0).Sum(l => l.PositiveVotes + l.NegativeVotes) * 100.0))
        .ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PositiveVotes + l.NegativeVotes));

    private static IOrderedQueryable<SongHelper> SortByDuration(this IQueryable<SongHelper> sequence, Order order, int? searchId) => 
        sequence
        .OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
        .ThenOrder(order, s => s.Song.Duration);

    private static IOrderedQueryable<SongHelper> SortByAttempts(this IQueryable<SongHelper> sequence, Order order, int? searchId) {
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        return tempSeq.ThenOrder(order, s => s.Song.Leaderboards.Sum(l => l.PlayCount));
    }

    private static IOrderedQueryable<SongHelper> SortByNJS(this IQueryable<SongHelper> sequence, Order order, int? searchId) {
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Max(d => d.Njs));
        } else {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Min(d => d.Njs));
        }
    }

    private static IOrderedQueryable<SongHelper> SortByNPS(this IQueryable<SongHelper> sequence, Order order, int? searchId) {
        var tempSeq = sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);
        if (order == Order.Desc) {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Max(d => d.Nps));
        } else {
            return tempSeq.ThenOrder(order, s => s.Difficulties.Min(d => d.Nps));
        }
    }

    private static IOrderedQueryable<SongHelper> SortByBPM(this IQueryable<SongHelper> sequence, Order order, int? searchId) =>
        sequence.OrderByDescending(s => searchId != null ? s.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                .ThenOrder(order, s => s.Song.Bpm);
}