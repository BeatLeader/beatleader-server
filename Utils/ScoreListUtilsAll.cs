using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils {
    public static class ScoreListUtilsAll {
        private static IOrderedQueryable<IScore> ThenSort(IOrderedQueryable<IScore> sequence, ScoresSortBy sortBy, Order order, bool showAllRatings) {
            switch (sortBy) {
                case ScoresSortBy.Date: return sequence.ThenOrder(order, t => t.Timepost);
                case ScoresSortBy.Pp: return sequence.ThenOrder(order, t => t.Pp);
                case ScoresSortBy.AccPP: return sequence.ThenOrder(order, t => t.AccPP);
                case ScoresSortBy.PassPP: return sequence.ThenOrder(order, t => t.PassPP);
                case ScoresSortBy.TechPP: return sequence.ThenOrder(order, t => t.TechPP);
                case ScoresSortBy.Acc: return sequence.ThenOrder(order, t => t.Accuracy);
                case ScoresSortBy.Pauses:
                    if (sequence is IOrderedQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.Pauses);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.Pauses);
                    }
                case ScoresSortBy.PlayCount:
                    if (sequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.PlayCount);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.PlayCount);
                    }
                case ScoresSortBy.LastTryTime:
                    if (sequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.LastTryTime);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.LastTryTime);
                    }
                case ScoresSortBy.Rank:
                    return sequence.ThenOrder(order, t => t.Rank);
                case ScoresSortBy.MaxStreak:
                    if (sequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.MaxStreak);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.MaxStreak);
                    }
                case ScoresSortBy.Timing:
                    if (sequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => (t.LeftTiming + t.RightTiming) / 2);
                    } else {
                        return sequence.ThenOrder(order, t => (t.ScoreInstance.LeftTiming + t.ScoreInstance.RightTiming) / 2);
                    }
                case ScoresSortBy.Stars:
                    return sequence.ThenOrder(order, s => 
                            showAllRatings || 
                            s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                            s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                            s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked ? 
                            (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFStars :
                            (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSStars :
                            (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSStars :
                            s.Leaderboard.Difficulty.Stars)))
                            : 0);
                case ScoresSortBy.Mistakes:
                    if (sequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.BadCuts + t.BombCuts + t.MissedNotes + t.WallsHit);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.BadCuts + t.ScoreInstance.BombCuts + t.ScoreInstance.MissedNotes + t.ScoreInstance.WallsHit);
                    }
                case ScoresSortBy.ReplaysWatched:
                    if (sequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.AnonimusReplayWatched + t.AuthorizedReplayWatched);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.AnonimusReplayWatched + t.ScoreInstance.AuthorizedReplayWatched);
                    }
                default:
                    break;
            }
            return sequence;
        }

        public static async Task<(IQueryable<IScore>, int?)> FilterAll(
            this IQueryable<IScore> sequence,
            AppContext context,
            bool excludeBanned,
            bool showAllRatings,

            ScoresSortBy sortBy = ScoresSortBy.Date,
            Order order = Order.Desc,
            ScoresSortBy thenSortBy = ScoresSortBy.Date,
            Order thenOrder = Order.Desc,

            string? search = null,
            string? diff = null,
            string? mode = null,
            Requirements requirements = Requirements.None,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            DifficultyStatus? type = null,
            int? mapType = null,
            Operation allTypes = Operation.Any,
            HMD? hmd = null,
            string? modifiers = null,
            float? stars_from = null,
            float? stars_to = null,
            float? accrating_from = null,
            float? accrating_to = null,
            float? passrating_from = null,
            float? passrating_to = null,
            float? techrating_from = null,
            float? techrating_to = null,
            int? time_from = null,
            int? time_to = null,
            int? eventId = null,
            string? mappers = null,
            string? players = null) {
            int? searchId = null;
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

                if (sequence is IQueryable<Score>) {
                    sequence = (IQueryable<Score>)(((IQueryable<Score>)sequence).Where(s => ids.Contains(s.Leaderboard.SongId)));
                } else {
                    sequence = sequence.Where(s => ids.Contains(s.Leaderboard.SongId));
                }
            }

            if (sortBy == ScoresSortBy.Pp || sortBy == ScoresSortBy.AccPP || sortBy == ScoresSortBy.PassPP || sortBy == ScoresSortBy.TechPP) {
                sequence = sequence.Where(t => t.Pp > 0);
            }

            if (sortBy == ScoresSortBy.MaxStreak) {
                if (sequence is IQueryable<Score>) {
                    sequence = sequence
                        .Where(s => !s.IgnoreForStats && s.MaxStreak != null);
                } else {
                    sequence = sequence
                        .Where(s => !s.ScoreInstance.IgnoreForStats && s.ScoreInstance.MaxStreak != null);
                }
            }

            sequence = ThenSort(ThenSort(sequence
                        .OrderByDescending(s => searchId != null ? s.Leaderboard.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0),
                        sortBy,
                        order,
                        showAllRatings),
                           thenSortBy,
                           thenOrder,
                           showAllRatings);

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
            if (mapType != null) {
                sequence = allTypes switch
                {
                    Operation.Any => sequence.Where(s => (s.Leaderboard.Difficulty.Type & mapType) != 0),
                    Operation.All => sequence.Where(s => s.Leaderboard.Difficulty.Type == mapType),
                    Operation.Not => sequence.Where(s => (s.Leaderboard.Difficulty.Type & mapType) == 0),
                    _             => sequence,
                };
            }
            if (hmd != null) {
                sequence = sequence.Where(s => s.Hmd == hmd);
            }
            if (stars_from != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        s.ModifiedStars >= stars_from);
            }
            if (stars_to != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        s.ModifiedStars <= stars_to);
            }
            if (accrating_from != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFAccRating :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSAccRating :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSAccRating :
                        s.Leaderboard.Difficulty.AccRating))) >= accrating_from);
            }
            if (accrating_to != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFAccRating :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSAccRating :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSAccRating :
                        s.Leaderboard.Difficulty.AccRating))) <= accrating_to);
            }
            if (passrating_from != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFPassRating :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSPassRating :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSPassRating :
                        s.Leaderboard.Difficulty.PassRating))) >= passrating_from);
            }
            if (passrating_to != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFPassRating :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSPassRating :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSPassRating :
                        s.Leaderboard.Difficulty.PassRating))) <= passrating_to);
            }
            if (techrating_from != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFTechRating :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSTechRating :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSTechRating :
                        s.Leaderboard.Difficulty.TechRating))) >= techrating_from);
            }
            if (techrating_to != null) {
                sequence = sequence.Where(s => (
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        (s.Modifiers.Contains("SF") ? s.Leaderboard.Difficulty.ModifiersRating.SFTechRating :
                        (s.Modifiers.Contains("SS") ? s.Leaderboard.Difficulty.ModifiersRating.SSTechRating :
                        (s.Modifiers.Contains("FS") ? s.Leaderboard.Difficulty.ModifiersRating.FSTechRating :
                        s.Leaderboard.Difficulty.TechRating))) <= techrating_to);
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
            if (mappers != null) {
                var ids = mappers.Split(",").Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0).ToArray();
                if (ids.Length > 0 && ids.Length < 100) {
                    sequence = sequence.Where(s => s.Leaderboard.Song.Mappers.Any(m => ids.Contains(m.Id)));
                }
            }
            if (players != null) {
                var ids = players.Split(",").ToArray();
                if (ids.Length > 0 && ids.Length < 100) {
                    sequence = sequence.Where(s => ids.Contains(s.PlayerId));
                }
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

            return (sequence, searchId);
        }
    }
}
