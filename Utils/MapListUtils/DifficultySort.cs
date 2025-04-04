using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Utils;

public static class DifficultySort
{
    public static void SortDifficulties(this MapInfoResponse map, MapSortBy sortBy, Order order)
    {
        if (map.Difficulties == null) return;

        var difficulties = map.Difficulties.OrderBy(d => d.Applicable ? 0 : 1);

        switch (sortBy)
        {
            case MapSortBy.Stars:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.Stars ?? 0)
                    : difficulties.ThenBy(d => d.Stars ?? 0);
                break;

            case MapSortBy.PassRating:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.PassRating ?? 0)
                    : difficulties.ThenBy(d => d.PassRating ?? 0);
                break;

            case MapSortBy.AccRating:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.AccRating ?? 0)
                    : difficulties.ThenBy(d => d.AccRating ?? 0);
                break;

            case MapSortBy.TechRating:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.TechRating ?? 0)
                    : difficulties.ThenBy(d => d.TechRating ?? 0);
                break;

            case MapSortBy.PlayCount:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.Plays)
                    : difficulties.ThenBy(d => d.Plays);
                break;

            case MapSortBy.Attempts:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.Attempts)
                    : difficulties.ThenBy(d => d.Attempts);
                break;

            case MapSortBy.NJS:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.Njs)
                    : difficulties.ThenBy(d => d.Njs);
                break;

            case MapSortBy.NPS:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.Nps)
                    : difficulties.ThenBy(d => d.Nps);
                break;

            case MapSortBy.Voting:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.PositiveVotes - d.NegativeVotes)
                    : difficulties.ThenBy(d => d.PositiveVotes - d.NegativeVotes);
                break;

            case MapSortBy.VoteCount:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.PositiveVotes + d.NegativeVotes)
                    : difficulties.ThenBy(d => d.PositiveVotes + d.NegativeVotes);
                break;

            case MapSortBy.VoteRatio:
                difficulties = order == Order.Desc
                    ? difficulties.ThenByDescending(d => d.PositiveVotes + d.NegativeVotes == 0 ? 0 : (double)d.PositiveVotes / (d.PositiveVotes + d.NegativeVotes))
                        .ThenByDescending(d => d.PositiveVotes + d.NegativeVotes)
                    : difficulties.ThenBy(d => d.PositiveVotes + d.NegativeVotes == 0 ? 0 : (double)d.PositiveVotes / (d.PositiveVotes + d.NegativeVotes))
                        .ThenBy(d => d.PositiveVotes + d.NegativeVotes);
                break;

            default:
                // Default sorting by mode and value
                difficulties = difficulties.ThenBy(d => d.Mode > 0 ? d.Mode : 2000)
                    .ThenBy(d => d.Value);
                break;
        }

        map.Difficulties = difficulties.ToList();
    }
} 