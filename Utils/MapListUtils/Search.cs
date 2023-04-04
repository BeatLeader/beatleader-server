using BeatLeader_Server.Models;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence, string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return sequence;
        }

        List<string> matches = SearchService.SearchMaps(search);

        return sequence.Where(leaderboard => matches.Contains(leaderboard.SongId));
    }
}