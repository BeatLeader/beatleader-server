using System.Globalization;
using System.Text;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence,
                                                          string? search,
                                                          ref string? type,
                                                          ref string? mode,
                                                          ref int? mapType,
                                                          ref Operation allTypes,
                                                          ref Requirements? mapRequirements,
                                                          ref Operation allRequirements,
                                                          ref string? mytype,
                                                          ref float? starsFrom,
                                                          ref float? starsTo,
                                                          ref float? accRatingFrom,
                                                          ref float? accRatingTo,
                                                          ref float? passRatingFrom,
                                                          ref float? passRatingTo,
                                                          ref float? techRatingFrom,
                                                          ref float? techRatingTo,
                                                          ref int? dateFrom,
                                                          ref int? dateTo)
    {
        if (string.IsNullOrEmpty(search))
        {
            return sequence;
        }

        search = search.GetSearchFilters(ref type,
                                         ref mode,
                                         ref mapType,
                                         ref allTypes,
                                         ref mapRequirements,
                                         ref allRequirements,
                                         ref mytype,
                                         ref starsFrom,
                                         ref starsTo,
                                         ref accRatingFrom,
                                         ref accRatingTo,
                                         ref passRatingFrom,
                                         ref passRatingTo,
                                         ref techRatingFrom,
                                         ref techRatingTo,
                                         ref dateFrom,
                                         ref dateTo);

        List<string> matches = SearchService.SearchMaps(search);

        return sequence.Where(leaderboard => matches.Contains(leaderboard.SongId));
    }

    private static string GetSearchFilters(this string search,
                                           ref string? type,
                                           ref string? mode,
                                           ref int? mapType,
                                           ref Operation allTypes,
                                           ref Requirements? mapRequirements,
                                           ref Operation allRequirements,
                                           ref string? mytype,
                                           ref float? starsFrom,
                                           ref float? starsTo,
                                           ref float? accRatingFrom,
                                           ref float? accRatingTo,
                                           ref float? passRatingFrom,
                                           ref float? passRatingTo,
                                           ref float? techRatingFrom,
                                           ref float? techRatingTo,
                                           ref int? dateFrom,
                                           ref int? dateTo)
    {
        StringBuilder stringBuilder = new(search.Length);

        foreach (string filter in search.Split(' '))
        {
            if (!filter.Contains('=') || !filter.Contains('<') || !filter.Contains('>'))
            {
                stringBuilder.Append(filter);
                stringBuilder.Append(' ');

                continue;
            }

            if (BasicFilter(filter, nameof(type), ref type))
            {
                continue;
            }

            if (BasicFilter(filter, nameof(mode), ref mode))
            {
                continue;
            }

            if (BasicParseFilter(filter, nameof(mapType), ref mapType))
            {
                continue;
            }

            if (EnumFilter(filter, ref allTypes))
            {
                continue;
            }

            // bypass because null
            Requirements requirements = Requirements.None;

            if (EnumFilter(filter, ref requirements))
            {
                mapRequirements = requirements;

                continue;
            }

            if (EnumFilter(filter, ref allRequirements))
            {
                continue;
            }

            if (BasicFilter(filter, nameof(mytype), ref mytype))
            {
                continue;
            }

            if (ClampFilter(filter, RatingType.Stars.ToString(), ref starsFrom, ref starsTo))
            {
                continue;
            }

            if (ClampFilter(filter, RatingType.Acc.ToString(), ref accRatingFrom, ref accRatingTo))
            {
                continue;
            }

            if (ClampFilter(filter, RatingType.Pass.ToString(), ref passRatingFrom, ref passRatingTo))
            {
                continue;
            }

            if (ClampFilter(filter, RatingType.Tech.ToString(), ref techRatingFrom, ref techRatingTo))
            {
                continue;
            }

            if (ClampFilter(filter, "date", ref dateFrom, ref dateTo))
            {
                continue;
            }

            // adds string back to search if parse fails
            stringBuilder.Append(filter);
            stringBuilder.Append(' ');
        }

        return stringBuilder.ToString();
    }

    private static bool BasicFilter(string filter, string name, ref string? stringValue)
    {
        if (!filter.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        stringValue = filter[(name.Length + 1)..];

        return true;
    }

    private static bool BasicParseFilter<T>(string filter, string name, ref T? parsableValue)
        where T : struct, IParsable<T>
    {
        if (!filter.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (T.TryParse(filter[(name.Length + 1)..], CultureInfo.InvariantCulture, out T value))
        {
            parsableValue = value;
        }

        return true;
    }

    private static bool EnumFilter<T>(string filter, ref T enumValue)
        where T : struct, Enum
    {
        const string name = nameof(T);

        if (!filter.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Enum.TryParse(filter[(name.Length + 1)..], true, out T value))
        {
            enumValue = value;
        }

        return true;
    }

    private static bool ClampFilter<T>(string filter, string name, ref T? from, ref T? to)
        where T : struct, IParsable<T>

    {
        if (!filter.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int valueOffset = filter[name.Length + 1] == '='
            ? name.Length + 2
            : name.Length + 1;

        if (T.TryParse(filter[valueOffset..], CultureInfo.InvariantCulture, out T value))
        {
            if (filter[name.Length] == '=')
            {
                to = value;
                from = value;
            }
            else if (filter[name.Length] == '<')
            {
                to = value;
            }
            else if (filter[name.Length] == '>')
            {
                from = value;
            }
        }

        return true;
    }
}