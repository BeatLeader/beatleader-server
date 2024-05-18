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
        public static async Task<IQueryable<Score>> Filter(
            this IQueryable<Score> sequence,
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
            IOrderedQueryable<Score>? orderedSequence = null;
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
                    orderedSequence = sequence.Order(order, t => t.Pauses);
                    break;
                case ScoresSortBy.PlayCount:
                    orderedSequence = sequence.Order(order, t => t.PlayCount);
                    break;
                case ScoresSortBy.LastTryTime:
                    orderedSequence = sequence.Order(order, t => t.LastTryTime);
                    break;
                case ScoresSortBy.Rank:
                    orderedSequence = sequence.Order(order, t => t.Rank);
                    break;
                case ScoresSortBy.MaxStreak:
                    orderedSequence = sequence.Where(s => !s.IgnoreForStats && s.MaxStreak != null).Order(order, t => t.MaxStreak);
                    break;
                case ScoresSortBy.Timing:
                    orderedSequence = sequence.Order(order, t => (t.LeftTiming + t.RightTiming) / 2);
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
                    orderedSequence = sequence.Order(order, t => t.BadCuts + t.BombCuts + t.MissedNotes + t.WallsHit);
                    break;
                case ScoresSortBy.ReplaysWatched:
                    orderedSequence = sequence.Order(order, t => t.AnonimusReplayWatched + t.AuthorizedReplayWatched);
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
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.DifficultyName == diff);
            }
            if (mode != null) {
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.ModeName == mode);
            }
            if (requirements != null) {
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.Requirements.HasFlag(requirements));
            }
            if (type != null) {
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.Status == type);
            }
            if (stars_from != null) {
                sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null) {
                sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars <= stars_to);
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
                    var score = Expression.Parameter(typeof(Score), "s");

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
                    sequence = sequence.Where((Expression<Func<Score, bool>>)Expression.Lambda(exp, score));
                } else {
                    sequence = sequence.Where(s => s.Modifiers.Length == 0);
                }
            }

            return sequence;
        }

        public static async Task<IQueryable<ScoreContextExtension>> Filter(
            this IQueryable<ScoreContextExtension> sequence,
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
            IOrderedQueryable<ScoreContextExtension>? orderedSequence = null;
            switch (sortBy) {
                case ScoresSortBy.Date:
                    orderedSequence = sequence.Order(order, t => t.Timeset);
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
                    orderedSequence = sequence.Order(order, t => t.Score.Pauses);
                    break;
                case ScoresSortBy.PlayCount:
                    orderedSequence = sequence.Order(order, t => t.Score.PlayCount);
                    break;
                case ScoresSortBy.LastTryTime:
                    orderedSequence = sequence.Order(order, t => t.Score.LastTryTime);
                    break;
                case ScoresSortBy.Rank:
                    orderedSequence = sequence.Order(order, t => t.Rank);
                    break;
                case ScoresSortBy.MaxStreak:
                    orderedSequence = sequence.Where(s => !s.Score.IgnoreForStats && s.Score.MaxStreak != null).Order(order, t => t.Score.MaxStreak);
                    break;
                case ScoresSortBy.Timing:
                    orderedSequence = sequence.Order(order, t => (t.Score.LeftTiming + t.Score.RightTiming) / 2);
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
                    orderedSequence = sequence.Order(order, t => t.Score.BadCuts + t.Score.BombCuts + t.Score.MissedNotes + t.Score.WallsHit);
                    break;
                case ScoresSortBy.ReplaysWatched:
                    orderedSequence = sequence.Order(order, t => t.Score.AnonimusReplayWatched + t.Score.AuthorizedReplayWatched);
                    break;
                default:
                    break;
            }
            if (orderedSequence != null) {
                sequence = orderedSequence.ThenBy(s => s.Timeset);
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
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.DifficultyName == diff);
            }
            if (mode != null) {
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.ModeName == mode);
            }
            if (requirements != null) {
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.Requirements.HasFlag(requirements));
            }
            if (type != null) {
                sequence = sequence.Where(p => p.Leaderboard.Difficulty.Status == type);
            }
            if (stars_from != null) {
                sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null) {
                sequence = sequence.Where(p => (p.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && p.Leaderboard.Difficulty.Stars <= stars_to);
            }
            if (time_from != null) {
                sequence = sequence.Where(s => s.Timeset >= time_from);
            }
            if (time_to != null) {
                sequence = sequence.Where(s => s.Timeset <= time_to);
            }
            if (excludeBanned) {
                sequence = sequence.Where(s => !s.Score.Banned);
            }
            switch (scoreStatus) {
                case ScoreFilterStatus.None:
                    break;
                case ScoreFilterStatus.Suspicious:
                    sequence = sequence.Where(s => s.Score.Suspicious);
                    break;
                default:
                    break;
            }

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(ScoreContextExtension), "s");

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
                    sequence = sequence.Where((Expression<Func<ScoreContextExtension, bool>>)Expression.Lambda(exp, score));
                } else {
                    sequence = sequence.Where(s => s.Modifiers.Length == 0);
                }
            }

            return sequence;
        }
    }
}
