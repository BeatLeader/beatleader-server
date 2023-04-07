using BeatLeader_Server.Models;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence, int page, int count, ref int matchCount, string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return sequence;
        }

        var matches = SearchService.SearchMaps(search);

        matchCount = matches.Count;

        var ids = matches.OrderByDescending(m => m.Score).Skip((page - 1) * count).Take(count).Select(m => m.Id).ToArray();

        return sequence.Where(leaderboard => ids.Contains(leaderboard.SongId));
    }
}