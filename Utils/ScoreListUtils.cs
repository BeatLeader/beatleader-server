using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils {
    public enum ScoreFilterStatus {
        None = 0,
        Suspicious = 1
    }
    public static class ScoreListUtils {
        public static IOrderedQueryable<IScore> OrderBySearch(
            this IQueryable<IScore> sequence,
            int searchId) {
            return sequence.OrderByDescending(s => s.Leaderboard.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score);
        }

        public static IOrderedQueryable<IScore> ThenOrderScores(
            this IOrderedQueryable<IScore> sequence,
            ScoresSortBy sortBy,
            Order order,
            bool isScoreQuery,
            bool showAllRatings) {
            switch (sortBy) {
                case ScoresSortBy.Date:
                    return sequence.ThenOrder(order, t => t.Timepost);
                case ScoresSortBy.Pp:
                    return sequence.ThenOrder(order, t => t.Pp);
                case ScoresSortBy.AccPP:
                    return sequence.ThenOrder(order, t => t.AccPP);
                case ScoresSortBy.PassPP:
                    return sequence.ThenOrder(order, t => t.PassPP);
                case ScoresSortBy.TechPP:
                    return sequence.ThenOrder(order, t => t.TechPP);
                case ScoresSortBy.Acc:
                    return sequence.ThenOrder(order, t => t.Accuracy);
                case ScoresSortBy.Pauses:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.Pauses);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.Pauses);
                    }
                case ScoresSortBy.PlayCount:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.PlayCount);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.PlayCount);
                    }
                case ScoresSortBy.LastTryTime:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.LastTryTime);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.LastTryTime);
                    }
                case ScoresSortBy.Rank:
                    return sequence.ThenOrder(order, t => t.Rank);
                case ScoresSortBy.ScoreValue:
                    return sequence.ThenOrder(order, t => t.ModifiedScore);
                case ScoresSortBy.MaxStreak:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.MaxStreak);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.MaxStreak);
                    }
                case ScoresSortBy.Timing:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => (t.LeftTiming + t.RightTiming) / 2);
                    } else {
                        return sequence.ThenOrder(order, t => (t.ScoreInstance.LeftTiming + t.ScoreInstance.RightTiming) / 2);
                    }
                case ScoresSortBy.Stars:
                    return sequence.ThenOrder(order, s => 
                        showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked 
                        ? s.ModifiedStars
                        : 0);
                case ScoresSortBy.Mistakes:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.Mistakes);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.Mistakes);
                    }
                case ScoresSortBy.ReplaysWatched:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.ReplayWatchedTotal);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.ReplayWatchedTotal);
                    }
                case ScoresSortBy.SotwNominations:
                    if (isScoreQuery) {
                        return sequence.ThenOrder(order, t => t.SotwNominations);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.SotwNominations);
                    }
                default:
                    return sequence;
            }
        }

        public static async Task<(IQueryable<IScore>, int?)> Filter(
            this IQueryable<IScore> sequence,
            AppContext context,
            bool excludeBanned,
            bool showAllRatings,
            ScoresSortBy sortBy = ScoresSortBy.Date,
            Order order = Order.Desc,
            ScoresSortBy thenSortBy = ScoresSortBy.Date,
            Order thenOrder = Order.Desc,
            string? search = null,
            bool noSearchSort = false,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            DifficultyStatus? type = null,
            HMD? hmd = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            float? acc_from = null,
            float? acc_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null,
            List<PlaylistResponse>? playlists = null) {
            
            int? searchId = null;
            bool isScoreQuery = sequence is IQueryable<Score>;

            if (search != null) {
                List<SongMetadata> matches = SongSearchService.Search(search);
                Random rnd = new Random();

                int searchIdentifier = rnd.Next(1, 10000);
                searchId = searchIdentifier;

                foreach (var item in matches) {
                    context.SongSearches.Add(new SongSearch {
                        SongId = item.Id,
                        Score = item.Score,
                        SearchId = searchIdentifier
                    });
                }
                await context.BulkSaveChangesAsync();

                IEnumerable<string> ids = matches.Select(songMetadata => songMetadata.Id);

                if (isScoreQuery) {
                    sequence = (IQueryable<Score>)(((IQueryable<Score>)sequence).Where(s => ids.Contains(s.Leaderboard.SongId)));
                } else {
                    sequence = sequence.Where(s => ids.Contains(s.Leaderboard.SongId));
                }
            }

            if (sortBy == ScoresSortBy.Pp || sortBy == ScoresSortBy.AccPP || sortBy == ScoresSortBy.PassPP || sortBy == ScoresSortBy.TechPP) {
                sequence = sequence.Where(t => t.Pp > 0);
            }

            if (sortBy == ScoresSortBy.MaxStreak) {
                if (isScoreQuery) {
                    sequence = sequence.Where(s => !s.IgnoreForStats && s.MaxStreak != null);
                } else {
                    sequence = sequence.Where(s => !s.ScoreInstance.IgnoreForStats && s.ScoreInstance.MaxStreak != null);
                }
            }
            
            if (eventId != null) {
                var leaderboardIds = await context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefaultAsync();
                if (leaderboardIds?.Count() != 0) {
                    sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                }
            }
            if (diff != null) {
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.DifficultyName == diff);
            }
            if (mode != null) {
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.ModeName == mode);
            }

            if (requirements != Requirements.None) {
                var score = Expression.Parameter(typeof(IScore), "s");
                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(2));

                foreach (Requirements term in Enum.GetValues(typeof(Requirements))) {
                    if (term != Requirements.Ignore && term != Requirements.None && requirements.HasFlag(term)) {
                        var subexpression = Expression.Equal(Expression.Property(Expression.Property(Expression.Property(score, "Leaderboard"), "Difficulty"), $"Requires{term.ToString()}"), Expression.Constant(true));
                        exp = Expression.OrElse(exp, subexpression);
                    }
                }
                sequence = sequence.Where((Expression<Func<IScore, bool>>)Expression.Lambda(exp, score));
            }
            if (type != null) {
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.Status == type);
            }
            if (hmd != null) {
                sequence = sequence.Where(s => s.Hmd == hmd);
            }
            if (stars_from != null) {
                sequence = sequence.Where(s => (
                        showAllRatings ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        s.ModifiedStars >= stars_from);
            }
            if (stars_to != null) {
                sequence = sequence.Where(s => (
                        showAllRatings ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        s.ModifiedStars <= stars_to);
            }
            if (acc_from != null) {
                sequence = sequence.Where(s => s.Accuracy >= acc_from);
            }
            if (acc_to != null) {
                sequence = sequence.Where(s => s.Accuracy <= acc_to);
            }
            if (time_from != null) {
                sequence = sequence.Where(s => s.Timepost >= time_from);
            }
            if (time_to != null) {
                sequence = sequence.Where(s => s.Timepost <= time_to);
            }
            if (excludeBanned) {
                sequence = sequence.Where(s => !s.Banned);
            }
            switch (scoreStatus) {
                case ScoreFilterStatus.None:
                    break;
                case ScoreFilterStatus.Suspicious:
                    sequence = sequence.Where(s => s.Suspicious);
                    break;
                default:
                    break;
            }

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(IScore), "s");

                    var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                    var any = modifiers.Contains("any");
                    var not = modifiers.Contains("not");
                    // 1 != 2 is here to trigger `OrElse` further the line.
                    var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(any ? 2 : 1));
                    var modifiersList = modifiers.Split(",").Where(m => m != "any" && m != "none" && m != "not");

                    foreach (var term in modifiersList) {
                        var subexpression = Expression.Call(Expression.Property(score, "Modifiers"), contains, Expression.Constant(term));
                        if (not) {
                            exp = Expression.And(exp, Expression.Not(subexpression));
                        } else {
                            if (any) {
                                exp = Expression.OrElse(exp, subexpression);
                            } else {
                                exp = Expression.And(exp, subexpression);
                            }
                        }
                    }
                    sequence = sequence.Where((Expression<Func<IScore, bool>>)Expression.Lambda(exp, score));
                } else {
                    sequence = sequence.Where(s => s.Modifiers.Length == 0);
                }
            }

            if (playlists != null) {
              sequence = sequence.WherePlaylists(playlists);
            }

            IOrderedQueryable<IScore> orderedSequence = searchId != null && !noSearchSort ? OrderBySearch(sequence, searchId ?? 0) : sequence.OrderBy(s => 0);
            orderedSequence = orderedSequence.ThenOrderScores(sortBy, order, isScoreQuery, showAllRatings).ThenOrderScores(thenSortBy, thenOrder, isScoreQuery, showAllRatings);

            return (orderedSequence, searchId);
        }


        
    private static IQueryable<IScore> WherePlaylists(this IQueryable<IScore> sequence, List<PlaylistResponse>? playlists)
    {
        if (playlists == null)
        {
            return sequence;
        }

        var hashes = playlists.SelectMany(p => p.songs.Where(s => s.hash != null).Select(s => (string)s.hash!.ToLower())).ToList();
        var keys = playlists.SelectMany(p => p.songs.Where(s => s.hash == null && s.key != null).Select(s => (string)s.key!.ToLower())).ToList();

        if (hashes.Count == 0 && keys.Count == 0) {
            return sequence;
        }
        return sequence.Where(s => hashes.Contains(s.Leaderboard.Song.LowerHash) || keys.Contains(s.Leaderboard.Song.Id));
    }
    }
}
