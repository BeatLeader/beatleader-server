using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;

namespace BeatLeader_Server.Utils {
    public enum ScoreFilterStatus {
        None = 0,
        Suspicious = 1
    }
    public static class ScoreListUtils {
        public static async Task<IQueryable<IScore>> Filter(
            this IQueryable<IScore> sequence,
            AppContext context,
            bool excludeBanned,
            bool showAllRatings,
            ScoresSortBy sortBy = ScoresSortBy.Date,
            Order order = Order.Desc,
            string? search = null,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            DifficultyStatus? type = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null) {
            IOrderedQueryable<IScore>? orderedSequence = null;
            switch (sortBy) {
                case ScoresSortBy.Date:
                    orderedSequence = sequence.Order(order, t => t.Timepost);
                    break;
                case ScoresSortBy.Pp:
                    orderedSequence = sequence.Where(t => t.Pp > 0).Order(order, t => t.Pp);
                    break;
                case ScoresSortBy.AccPP:
                    orderedSequence = sequence.Where(t => t.Pp > 0).Order(order, t => t.AccPP);
                    break;
                case ScoresSortBy.PassPP:
                    orderedSequence = sequence.Where(t => t.Pp > 0).Order(order, t => t.PassPP);
                    break;
                case ScoresSortBy.TechPP:
                    orderedSequence = sequence.Where(t => t.Pp > 0).Order(order, t => t.TechPP);
                    break;
                case ScoresSortBy.Acc:
                    orderedSequence = sequence.Order(order, t => t.Accuracy);
                    break;
                case ScoresSortBy.Pauses:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Order(order, t => t.Pauses);
                    } else {
                        orderedSequence = sequence.Order(order, t => t.ScoreInstance.Pauses);
                    }
                    break;
                case ScoresSortBy.PlayCount:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Order(order, t => t.PlayCount);
                    } else {
                        orderedSequence = sequence.Order(order, t => t.ScoreInstance.PlayCount);
                    }
                    break;
                case ScoresSortBy.LastTryTime:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Order(order, t => t.LastTryTime);
                    } else {
                        orderedSequence = sequence.Order(order, t => t.ScoreInstance.LastTryTime);
                    }
                    break;
                case ScoresSortBy.Rank:
                    orderedSequence = sequence.Order(order, t => t.Rank);
                    break;
                case ScoresSortBy.MaxStreak:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Where(s => !s.IgnoreForStats && s.MaxStreak != null).Order(order, t => t.MaxStreak);
                    } else {
                        orderedSequence = sequence.Where(s => !s.ScoreInstance.IgnoreForStats && s.ScoreInstance.MaxStreak != null).Order(order, t => t.ScoreInstance.MaxStreak);
                    }
                    break;
                case ScoresSortBy.Timing:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Order(order, t => (t.LeftTiming + t.RightTiming) / 2);
                    } else {
                        orderedSequence = sequence.Order(order, t => (t.ScoreInstance.LeftTiming + t.ScoreInstance.RightTiming) / 2);
                    }
                    break;
                case ScoresSortBy.Stars:
                    orderedSequence = sequence.Order(order, s => 
                        showAllRatings || 
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFStars :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSStars :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSStars :
                        s.Leaderboard.Difficulty.Stars)))
                        : 0);
                    break;
                case ScoresSortBy.Mistakes:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Order(order, t => t.BadCuts + t.BombCuts + t.MissedNotes + t.WallsHit);
                    } else {
                        orderedSequence = sequence.Order(order, t => t.ScoreInstance.BadCuts + t.ScoreInstance.BombCuts + t.ScoreInstance.MissedNotes + t.ScoreInstance.WallsHit);
                    }
                    break;
                case ScoresSortBy.ReplaysWatched:
                    if (sequence is IQueryable<Score>) {
                        orderedSequence = sequence.Order(order, t => t.AnonimusReplayWatched + t.AuthorizedReplayWatched);
                    } else {
                        orderedSequence = sequence.Order(order, t => t.ScoreInstance.AnonimusReplayWatched + t.ScoreInstance.AuthorizedReplayWatched);
                    }
                    break;
                default:
                    break;
            }
            if (orderedSequence != null) {
                sequence = orderedSequence.ThenBy(s => s.Timepost);
            }
            if (search != null) {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Where(p => p.Leaderboard.Song.Id == lowSearch ||
                                p.Leaderboard.Song.Hash == lowSearch ||
                                p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
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
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.Requirements.HasFlag(requirements));
            }
            if (type != null) {
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.Status == type);
            }
            if (stars_from != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFStars :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSStars :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSStars :
                        s.Leaderboard.Difficulty.Stars))) >= stars_from);
            }
            if (stars_to != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFStars :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSStars :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSStars :
                        s.Leaderboard.Difficulty.Stars))) <= stars_to);
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

            return sequence;
        }
    }
}
