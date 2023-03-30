using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private const double SearchLeniency = 0.75;

    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence, string? search)
    {
        if (search == null)
        {
            return sequence;
        }

        string lowSearch = search.ToLower();

        return sequence.Where(leaderboard => leaderboard.Song.Id == lowSearch
                                          || leaderboard.Song.Hash == lowSearch
                                          || StringSimilarity(leaderboard.Song.Author, lowSearch) > SearchLeniency
                                          || StringSimilarity(leaderboard.Song.Mapper, lowSearch) > SearchLeniency
                                          || StringSimilarity(leaderboard.Song.Name, lowSearch) > SearchLeniency);
    }

    private static double StringSimilarity(string value, string search)
    {
        int valueLength = value.Length;
        int searchLength = search.Length;
        int[,] matrix = new int[valueLength + 1, searchLength + 1];

        if (valueLength == 0)
        {
            return searchLength;
        }

        if (searchLength == 0)
        {
            return valueLength;
        }

        for (int i = 0; i <= valueLength; i++)
        {
            matrix[i, 0] = i;
        }

        for (int j = 0; j <= searchLength; j++)
        {
            matrix[0, j] = j;
        }

        for (int i = 1; i <= valueLength; i++)
        {
            for (int j = 1; j <= searchLength; j++)
            {
                int cost = search[j - 1] == value[i - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1), matrix[i - 1, j - 1] + cost);
            }
        }

        int maxLength = Math.Max(value.Length, search.Length);
        double similarityPercentage = (double)(maxLength - matrix[valueLength, searchLength]) / maxLength * 100;

        return similarityPercentage;
    }
}