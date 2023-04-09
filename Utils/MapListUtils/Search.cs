using BeatLeader_Server.Models;
using static BeatLeader_Server.Services.SearchService;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence, ref List<SongMatch>? matches, string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return sequence;
        }

        matches = SearchMaps(search);

        var ids = matches.Select(m => m.Id).ToArray();

        return sequence.Where(leaderboard => ids.Contains(leaderboard.SongId));
    }
}