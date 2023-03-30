using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence, string? search)
    {
        if (search == null)
        {
            return sequence;
        }

        string lowSearch = search.ToLower();

        return sequence.Where(leaderboard => leaderboard.Song.Id == lowSearch
                                          || leaderboard.Song.Hash == lowSearch
                                          || leaderboard.Song.Author.Contains(lowSearch)
                                          || leaderboard.Song.Mapper.Contains(lowSearch)
                                          || leaderboard.Song.Name.Contains(lowSearch));
    }
}