using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using FuzzySharp;

namespace BeatLeader_Server.Services;

public static class PlayerSearchService
{
    private static readonly List<PlayerMetadata> Players = new();

    public static void AddPlayers(AppContext context)
    {
        foreach (Player contextPlayer in context.Players)
        {
            AddNewPlayer(contextPlayer);
        }
    }

    public static void AddNewPlayer(Player player) => Players.Add((PlayerMetadata)player);

    public static void PlayerChangedName(Player player) => Players.FirstOrDefault(playerMetadata => playerMetadata.Id == player.Id)?.Names.Add(player.Name);

    public static List<PlayerMatch> SearchPlayers(string query)
    {
        List<PlayerMatch> result = new();

        query = query.ToLower();

        foreach (PlayerMetadata s in Players)
        {
            string? match = s.Names.FirstOrDefault(x => (x.Length < 4 && x == query)
                                                     || x.Contains(query)
                                                     || (x.Length >= 4 && Fuzz.WeightedRatio(x, query) > 70));

            if (match != null)
            {
                result.Add(new PlayerMatch
                {
                    Id = s.Id,
                    Score = SearchService.ComparisonScore(match, query),
                });
            }
        }

        return result;
    }

    public static List<ResponseUtils.PlayerResponseWithStats> SortPlayers(IEnumerable<ResponseUtils.PlayerResponseWithStats> query, string searchQuery)
    {
        searchQuery = searchQuery.ToLower();

        return query.OrderByDescending(playerResponseWithStats => SearchService.ComparisonScore(playerResponseWithStats.Name.ToLower(), searchQuery)).ToList();
    }
}