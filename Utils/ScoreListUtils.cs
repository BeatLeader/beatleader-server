using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Linq.Expressions;

namespace BeatLeader_Server.Utils
{
    public static class ScoreListUtils
    {
        public static IQueryable<Score> Filter(
            this IQueryable<Score> sequence, 
            ReadAppContext context,
            string sortBy = "date",
            string order = "desc",
            string? search = null,
            string? diff = null,
            string? type = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null)
        {
            switch (sortBy)
            {
                case "date":
                    sequence = sequence.Order(order, t => t.Timeset);
                    break;
                case "pp":
                    sequence = sequence.Order(order, t => t.Pp);
                    break;
                case "acc":
                    sequence = sequence.Order(order, t => t.Accuracy);
                    break;
                case "pauses":
                    sequence = sequence.Order(order, t => t.Pauses);
                    break;
                case "rank":
                    sequence = sequence.Order(order, t => t.Rank);
                    break;
                case "maxStreak":
                    sequence = sequence.Where(s => !s.IgnoreForStats).Order(order, t => t.MaxStreak);
                    break;
                case "timing":
                    sequence = sequence.Order(order, t => (t.LeftTiming + t.RightTiming) / 2);
                    break;
                case "stars":
                    sequence = sequence
                                .Include(lb => lb.Leaderboard)
                                .ThenInclude(lb => lb.Difficulty)
                                .Order(order, s => s.Leaderboard.Difficulty.Stars)
                                .Where(s => s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Include(lb => lb.Leaderboard)
                    .ThenInclude(lb => lb.Song)
                    .Where(p => p.Leaderboard.Song.Author.ToLower().Contains(lowSearch) ||
                                p.Leaderboard.Song.Mapper.ToLower().Contains(lowSearch) ||
                                p.Leaderboard.Song.Name.ToLower().Contains(lowSearch));
            }
            if (eventId != null) {
                var leaderboardIds = context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefault();
                if (leaderboardIds?.Count() != 0) {
                    sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                }
            }
            if (diff != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.DifficultyName.ToLower().Contains(diff.ToLower()));
            }
            if (type != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => type == "ranked" ? p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked : p.Leaderboard.Difficulty.Status != DifficultyStatus.ranked);
            }
            if (stars_from != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null)
            {
                sequence = sequence.Include(lb => lb.Leaderboard).ThenInclude(lb => lb.Difficulty).Where(p => p.Leaderboard.Difficulty.Stars <= stars_to);
            }
            if (time_from != null)
            {
                sequence = sequence.Where(s => s.Timepost >= time_from);
            }
            if (time_to != null)
            {
                sequence = sequence.Where(s => s.Timepost <= time_to);
            }

            if (modifiers != null) {
                if (!modifiers.Contains("none")) {
                    var score = Expression.Parameter(typeof(Score), "s");
                
                    var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                    var any = modifiers.Contains("any");
                    // 1 != 2 is here to trigger `OrElse` further the line.
                    var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(any ? 2 : 1));
                    var modifiersList = modifiers.Split(",").Where(m => m != "any" && m != "none");

                    foreach (var term in modifiersList)
                    {
                        var subexpression = Expression.Call(Expression.Property(score, "Modifiers"), contains, Expression.Constant(term));
                        if (any) {
                            exp = Expression.OrElse(exp, subexpression);
                        } else {
                            exp = Expression.And(exp, subexpression);
                        }
                    }
                    sequence = sequence.Where((Expression<Func<Score, bool>>)Expression.Lambda(exp, score));
                } else {
                    sequence = sequence.Where(s => s.Modifiers.Length == 0);
                }
            }

            return sequence;
        }
    }
}
