using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;

namespace BeatLeader_Server.Services;

public static class SongSearchService
{
    private static readonly List<SongMetadata> Songs = new();

    public static void AddSongs(AppContext context)
    {
        foreach (Song contextSong in context.Songs)
        {
            AddNewSong(contextSong);
        }
    }

    public static void AddNewSong(Song song) => Songs.Add((SongMetadata)song);

    public static List<SongMatch> SearchSongs(string query)
    {
        List<SongMatch> result = new();

        query = query.ToLower();

        foreach (SongMetadata s in Songs)
        {
            int score = SearchService.MapComparisonScore(s.Id, s.Hash, s.Name, s.Author, s.Mapper, query);

            if (score > 70)
            {
                result.Add(new SongMatch
                {
                    Id = s.Id,
                    Score = score,
                });
            }
        }

        return result;
    }

    public static List<ResponseUtils.LeaderboardInfoResponse> SortSongs(IEnumerable<ResponseUtils.LeaderboardInfoResponse> query, string searchQuery)
    {
        searchQuery = searchQuery.ToLower();

        return query.OrderByDescending(leaderboardInfoResponse => SearchService.MapComparisonScore(leaderboardInfoResponse.Song.Id.ToLower(),
                                                                                                   leaderboardInfoResponse.Song.Hash.ToLower(),
                                                                                                   leaderboardInfoResponse.Song.Name.ToLower(),
                                                                                                   leaderboardInfoResponse.Song.Author.ToLower(),
                                                                                                   leaderboardInfoResponse.Song.Mapper.ToLower(),
                                                                                                   searchQuery)).ToList();
    }
}