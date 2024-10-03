using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;

namespace BeatLeader_Server.Utils {
    public static class AttemptsListUtils {
        public static async Task<IQueryable<PlayerLeaderboardStats>> Filter(
            this IQueryable<PlayerLeaderboardStats> sequence,
            AppContext context,
            bool showAllRatings,
            ScoresSortBy sortBy = ScoresSortBy.Date,
            Order order = Order.Desc,
            string? search = null,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            EndType? endType = null,
            DifficultyStatus? type = null,
            HMD? hmd = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null) {
            
            
            if (hmd != null) {
                sequence = sequence.Where(s => s.Hmd == hmd);
            }
            if (endType != null) {
                sequence = sequence.Where(s => s.Type == endType);
            }
            if (time_from != null) {
                sequence = sequence.Where(s => s.Timepost >= time_from);
            }
            if (time_to != null) {
                sequence = sequence.Where(s => s.Timepost <= time_to);
            }
           
            if (eventId != null) {
                var leaderboardIds = await context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefaultAsync();
                if (leaderboardIds?.Count() != 0) {
                    sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                }
            }

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(PlayerLeaderboardStats), "s");

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
                    sequence = sequence.Where((Expression<Func<PlayerLeaderboardStats, bool>>)Expression.Lambda(exp, score));
                } else {
                    sequence = sequence.Where(s => s.Modifiers.Length == 0);
                }
            }

            if (diff != null || search != null || mode != null || requirements != Requirements.None || type != null || stars_from != null || stars_to != null) {
                var leaderboarIds = await sequence.Select(s => s.LeaderboardId).ToListAsync();
                var leaderboards = context.Leaderboards.Where(l => leaderboarIds.Contains(l.Id));

                if (diff != null) {
                    leaderboards = leaderboards.Where(l => l.Difficulty.DifficultyName == diff);
                }

                if (search != null) {
                    string lowSearch = search.ToLower();
                    leaderboards = leaderboards
                        .Where(l => l.Song.Id == lowSearch ||
                                    l.Song.Hash == lowSearch ||
                                    l.Song.Author.ToLower().Contains(lowSearch) ||
                                    l.Song.Mapper.ToLower().Contains(lowSearch) ||
                                    l.Song.Name.ToLower().Contains(lowSearch));
                }
                if (mode != null) {
                    leaderboards = leaderboards.Where(s => s.Difficulty.ModeName == mode);
                }
                if (requirements != Requirements.None) {
                    leaderboards = leaderboards.Where(s => s.Difficulty.Requirements.HasFlag(requirements));
                }
                if (type != null) {
                    leaderboards = leaderboards.Where(s => s.Difficulty.Status == type);
                }

                if (stars_from != null) {
                    leaderboards = leaderboards.Where(s => (
                            s.Difficulty.Status == DifficultyStatus.nominated ||
                            s.Difficulty.Status == DifficultyStatus.qualified ||
                            s.Difficulty.Status == DifficultyStatus.ranked) &&
                            s.Difficulty.Stars >= stars_from);
                }
                if (stars_to != null) {
                    leaderboards = leaderboards.Where(s => (
                            s.Difficulty.Status == DifficultyStatus.nominated ||
                            s.Difficulty.Status == DifficultyStatus.qualified ||
                            s.Difficulty.Status == DifficultyStatus.ranked) &&
                            s.Difficulty.Stars <= stars_to);
                }
                leaderboarIds = await leaderboards.Select(s => s.Id).ToListAsync();
                sequence = sequence.Where(s => leaderboarIds.Contains(s.LeaderboardId));
            }

            IOrderedQueryable<PlayerLeaderboardStats>? orderedSequence = null;
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
                case ScoresSortBy.Rank:
                    orderedSequence = sequence.Order(order, t => t.Rank);
                    break;
                case ScoresSortBy.MaxStreak:
                    orderedSequence = sequence.Where(s => s.MaxStreak != null).Order(order, t => t.MaxStreak);
                    break;
                case ScoresSortBy.Timing:
                    orderedSequence = sequence.Order(order, t => (t.LeftTiming + t.RightTiming) / 2);
                    break;
                //case ScoresSortBy.Stars:
                //    orderedSequence = sequence.Order(order, s => 
                //        showAllRatings || 
                //        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                //        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                //        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? 
                //        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFStars :
                //        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSStars :
                //        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSStars :
                //        s.Leaderboard.Difficulty.Stars)))
                //        : 0);
                //    break;
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

            return sequence;
        }
    }
}
