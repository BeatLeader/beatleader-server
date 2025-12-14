using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils {
    public static class ScoreListUtilsAll {
        private static IOrderedQueryable<IScore> ScoresThenSort(
            this IQueryable<IScore> unorderedSequence, 
            IOrderedQueryable<IScore>? sequence,
            ScoresSortBy sortBy, 
            Order order, 
            bool showAllRatings,
            int? searchId) {

            if (sequence == null) {
                if (searchId != null) {
                    sequence = unorderedSequence.OrderByDescending(s => s.Leaderboard.Song.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score);
                } else {
                    switch (sortBy) {
                        case ScoresSortBy.Date: return unorderedSequence.Order(order, t => t.Timepost);
                        case ScoresSortBy.Pp: return unorderedSequence.Order(order, t => t.Pp);
                        case ScoresSortBy.AccPP: return unorderedSequence.Order(order, t => t.AccPP);
                        case ScoresSortBy.PassPP: return unorderedSequence.Order(order, t => t.PassPP);
                        case ScoresSortBy.TechPP: return unorderedSequence.Order(order, t => t.TechPP);
                        case ScoresSortBy.Acc: return unorderedSequence.Order(order, t => t.Accuracy);
                        case ScoresSortBy.Pauses:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.Pauses);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.Pauses);
                            }
                        case ScoresSortBy.PlayCount:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.PlayCount);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.PlayCount);
                            }
                        case ScoresSortBy.LastTryTime:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.LastTryTime);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.LastTryTime);
                            }
                        case ScoresSortBy.Rank:
                            return order == Order.Desc ? unorderedSequence.Order(Order.Asc, t => t.Rank) : unorderedSequence.Order(Order.Desc, t => t.Rank);
                        case ScoresSortBy.MaxStreak:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.MaxStreak);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.MaxStreak);
                            }
                        case ScoresSortBy.Timing:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => (t.LeftTiming + t.RightTiming) / 2);
                            } else {
                                return unorderedSequence.Order(order, t => (t.ScoreInstance.LeftTiming + t.ScoreInstance.RightTiming) / 2);
                            }
                        case ScoresSortBy.SotwNominations:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.SotwNominations);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.SotwNominations);
                            }
                        case ScoresSortBy.Stars:
                            return sequence.ThenOrder(order, s => s.ModifiedStars);
                        case ScoresSortBy.Mistakes:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.Mistakes);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.Mistakes);
                            }
                        case ScoresSortBy.ReplaysWatched:
                            if (unorderedSequence is IQueryable<Score>) {
                                return unorderedSequence.Order(order, t => t.ReplayWatchedTotal);
                            } else {
                                return unorderedSequence.Order(order, t => t.ScoreInstance.ReplayWatchedTotal);
                            }
                        default:
                            break;
                    }
                }
            }
            switch (sortBy) {
                case ScoresSortBy.Date: return sequence.ThenOrder(order, t => t.Timepost);
                case ScoresSortBy.Pp: return sequence.ThenOrder(order, t => t.Pp);
                case ScoresSortBy.AccPP: return sequence.ThenOrder(order, t => t.AccPP);
                case ScoresSortBy.PassPP: return sequence.ThenOrder(order, t => t.PassPP);
                case ScoresSortBy.TechPP: return sequence.ThenOrder(order, t => t.TechPP);
                case ScoresSortBy.Acc: return sequence.ThenOrder(order, t => t.Accuracy);
                case ScoresSortBy.Pauses:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.Pauses);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.Pauses);
                    }
                case ScoresSortBy.PlayCount:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.PlayCount);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.PlayCount);
                    }
                case ScoresSortBy.LastTryTime:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.LastTryTime);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.LastTryTime);
                    }
                case ScoresSortBy.Rank:
                    return order == Order.Desc ? sequence.ThenOrder(Order.Asc, t => t.Rank) : sequence.ThenOrder(Order.Desc, t => t.Rank);
                case ScoresSortBy.MaxStreak:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.MaxStreak);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.MaxStreak);
                    }
                case ScoresSortBy.Timing:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => (t.LeftTiming + t.RightTiming) / 2);
                    } else {
                        return sequence.ThenOrder(order, t => (t.ScoreInstance.LeftTiming + t.ScoreInstance.RightTiming) / 2);
                    }
                case ScoresSortBy.SotwNominations:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.SotwNominations);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.SotwNominations);
                    }
                case ScoresSortBy.Stars:
                    return sequence.ThenOrder(order, s => s.ModifiedStars);
                case ScoresSortBy.Mistakes:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.Mistakes);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.Mistakes);
                    }
                case ScoresSortBy.ReplaysWatched:
                    if (unorderedSequence is IQueryable<Score>) {
                        return sequence.ThenOrder(order, t => t.ReplayWatchedTotal);
                    } else {
                        return sequence.ThenOrder(order, t => t.ScoreInstance.ReplayWatchedTotal);
                    }
                default:
                    break;
            }
            return sequence;
        }

        public static async Task<(IQueryable<IScore>, int?, int?)> FilterAll(
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
            Operation allRequirements = Operation.Any,
            ScoreFilterStatus scoreStatus = ScoreFilterStatus.None,
            DifficultyStatus? type = null,
            MapTypes mapType = MapTypes.None,
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
            string? players = null,
            List<PlaylistResponse>? playlists = null) {

            int? searchId = null;
            int? scoreCount = null;

            if (sortBy == ScoresSortBy.Pp || sortBy == ScoresSortBy.AccPP || sortBy == ScoresSortBy.PassPP || sortBy == ScoresSortBy.TechPP) {
                sequence = sequence.Where(t => t.Pp > 0);
                scoreCount = MinuteRefresh.PpScoresCount;
            } else {
                scoreCount = MinuteRefresh.ScoresCount;
            }

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
                scoreCount = null;
            }

            var orderedSequence = sequence
                .ScoresThenSort(null, sortBy, order, showAllRatings, searchId);

            sequence = sequence.ScoresThenSort(orderedSequence, thenSortBy, thenOrder, showAllRatings, searchId);

            if (eventId != null) {
                var leaderboardIds = await context.EventRankings.Where(e => e.Id == eventId).Include(e => e.Leaderboards).Select(e => e.Leaderboards.Select(lb => lb.Id)).FirstOrDefaultAsync();
                if (leaderboardIds?.Count() != 0) {
                    sequence = sequence.Where(s => leaderboardIds.Contains(s.LeaderboardId));
                }
                scoreCount = null;
            }
            if (diff != null) {
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.DifficultyName == diff);
                scoreCount = null;
            }
            if (mode != null) {
                sequence = sequence.Where(s => s.Leaderboard.Difficulty.ModeName == mode);
                scoreCount = null;
            }
            if (requirements != Requirements.None) {
                var score = Expression.Parameter(typeof(IScore), "s");
                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(allRequirements == Operation.Any ? 2 : 1));

                foreach (Requirements term in Enum.GetValues(typeof(Requirements))) {
                    if (term != Requirements.Ignore && term != Requirements.None && requirements.HasFlag(term)) {
                        var subexpression = Expression.Equal(Expression.Property(Expression.Property(Expression.Property(score, "Leaderboard"), "Difficulty"), $"Requires{term.ToString()}"), Expression.Constant(true));
                        exp = Expression.OrElse(exp, subexpression);

                        exp = allRequirements switch {
                            Operation.Any => Expression.OrElse(exp, subexpression),
                            Operation.All => Expression.And(exp, subexpression),
                            Operation.Not => Expression.And(exp, Expression.Not(subexpression)),
                            _ => exp,
                        };
                    }
                }
                sequence = sequence.Where((Expression<Func<IScore, bool>>)Expression.Lambda(exp, score));
                scoreCount = null;
            }
            if (type != null) {
                if (type != DifficultyStatus.OST) {
                    sequence = sequence.Where(s => s.Leaderboard.Difficulty.Status == type);
                }
                scoreCount = null;
            }
            if (mapType != MapTypes.None) {
                var score = Expression.Parameter(typeof(IScore), "s");
                // 1 != 2 is here to trigger `OrElse` further the line.
                var exp = Expression.Equal(Expression.Constant(1), Expression.Constant(allTypes == Operation.Any ? 2 : 1));

                foreach (MapTypes term in Enum.GetValues(typeof(MapTypes))) {
                    if (term != MapTypes.None && mapType.HasFlag(term)) {
                        var subexpression = Expression.Equal(Expression.Property(Expression.Property(Expression.Property(score, "Leaderboard"), "Difficulty"), $"Type{term.ToString()}"), Expression.Constant(true));
                        exp = Expression.OrElse(exp, subexpression);

                        exp = allTypes switch {
                            Operation.Any => Expression.OrElse(exp, subexpression),
                            Operation.All => Expression.And(exp, subexpression),
                            Operation.Not => Expression.And(exp, Expression.Not(subexpression)),
                            _ => exp,
                        };
                    }
                }
                sequence = sequence.Where((Expression<Func<IScore, bool>>)Expression.Lambda(exp, score));
                scoreCount = null;
            }
            if (hmd != null) {
                sequence = sequence.Where(s => s.Hmd == hmd);
                scoreCount = null;
            }
            if (stars_from != null) {
                sequence = sequence.Where(s => (
                        showAllRatings ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        s.ModifiedStars >= stars_from);
                scoreCount = null;
            }
            if (stars_to != null) {
                sequence = sequence.Where(s => (
                        showAllRatings ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.nominated ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                        s.Leaderboard.Difficulty.Status == DifficultyStatus.ranked) && 
                        s.ModifiedStars <= stars_to);
                scoreCount = null;
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
                scoreCount = null;
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
                scoreCount = null;
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
                scoreCount = null;
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
                scoreCount = null;
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
                scoreCount = null;
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
                scoreCount = null;
            }
            if (time_from != null) {
                sequence = sequence.Where(s => s.Timepost >= time_from);
                scoreCount = null;
            }
            if (time_to != null) {
                sequence = sequence.Where(s => s.Timepost <= time_to);
                scoreCount = null;
            }
            if (excludeBanned) {
                sequence = sequence.Where(s => !s.Banned);
            }
            if (mappers != null) {
                var ids = mappers.Split(",").Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0).ToArray();
                if (ids.Length > 0 && ids.Length < 100) {
                    sequence = sequence.Where(s => s.Leaderboard.Song.Mappers.Any(m => ids.Contains(m.Id)));
                    scoreCount = null;
                }
            }
            if (players != null) {
                var ids = players.Split(",").ToArray();
                if (ids.Length > 0 && ids.Length < 100) {
                    sequence = sequence.Where(s => ids.Contains(s.PlayerId));
                    scoreCount = null;
                }
            }

            if (playlists != null) {
                var hashes = playlists.SelectMany(p => p.songs.Where(s => s.hash != null).Select(s => (string)s.hash!.ToLower())).ToList();
                var keys = playlists.SelectMany(p => p.songs.Where(s => s.hash == null && s.key != null).Select(s => (string)s.key!.ToLower())).ToList();

                if (hashes.Count > 0 || keys.Count > 0) {
                    sequence = sequence.Where(s => hashes.Contains(s.Leaderboard.Song.Hash.ToLower()) || keys.Contains(s.Leaderboard.SongId));
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
                    scoreCount = null;
                } else {
                    sequence = sequence.Where(s => s.Modifiers.Length == 0);
                    scoreCount = null;
                }
            }

            return (sequence, searchId, scoreCount);
        }
    }
}
