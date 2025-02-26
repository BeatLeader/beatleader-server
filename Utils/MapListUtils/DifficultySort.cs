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

        var difficulties = map.Difficulties.ToList();

        switch (sortBy)
        {
            case MapSortBy.Stars:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.Stars ?? 0).ToList()
                    : difficulties.OrderBy(d => d.Stars ?? 0).ToList();
                break;

            case MapSortBy.PassRating:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.PassRating ?? 0).ToList()
                    : difficulties.OrderBy(d => d.PassRating ?? 0).ToList();
                break;

            case MapSortBy.AccRating:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.AccRating ?? 0).ToList()
                    : difficulties.OrderBy(d => d.AccRating ?? 0).ToList();
                break;

            case MapSortBy.TechRating:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.TechRating ?? 0).ToList()
                    : difficulties.OrderBy(d => d.TechRating ?? 0).ToList();
                break;

            case MapSortBy.PlayCount:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.Plays).ToList()
                    : difficulties.OrderBy(d => d.Plays).ToList();
                break;

            case MapSortBy.Attempts:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.Attempts).ToList()
                    : difficulties.OrderBy(d => d.Attempts).ToList();
                break;

            case MapSortBy.NJS:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.Njs).ToList()
                    : difficulties.OrderBy(d => d.Njs).ToList();
                break;

            case MapSortBy.NPS:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.Nps).ToList()
                    : difficulties.OrderBy(d => d.Nps).ToList();
                break;

            case MapSortBy.Voting:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.PositiveVotes - d.NegativeVotes).ToList()
                    : difficulties.OrderBy(d => d.PositiveVotes - d.NegativeVotes).ToList();
                break;

            case MapSortBy.VoteCount:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.PositiveVotes + d.NegativeVotes).ToList()
                    : difficulties.OrderBy(d => d.PositiveVotes + d.NegativeVotes).ToList();
                break;

            case MapSortBy.VoteRatio:
                difficulties = order == Order.Desc
                    ? difficulties.OrderByDescending(d => d.PositiveVotes + d.NegativeVotes == 0 ? 0 : (double)d.PositiveVotes / (d.PositiveVotes + d.NegativeVotes))
                        .ThenByDescending(d => d.PositiveVotes + d.NegativeVotes)
                        .ToList()
                    : difficulties.OrderBy(d => d.PositiveVotes + d.NegativeVotes == 0 ? 0 : (double)d.PositiveVotes / (d.PositiveVotes + d.NegativeVotes))
                        .ThenBy(d => d.PositiveVotes + d.NegativeVotes)
                        .ToList();
                break;

            default:
                // Default sorting by mode and value
                difficulties = difficulties.OrderBy(d => d.Mode > 0 ? d.Mode : 2000)
                    .ThenBy(d => d.Value)
                    .ToList();
                break;
        }

        map.Difficulties = difficulties;
    }
} 