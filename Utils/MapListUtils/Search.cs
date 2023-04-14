using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterBySearch(this IQueryable<Leaderboard> sequence,
                                                          string? search,
                                                          out List<SongMetadata> matches,
                                                          ref Type type,
                                                          ref string? mode,
                                                          ref int? mapType,
                                                          ref Operation allTypes,
                                                          ref Requirements mapRequirements,
                                                          ref Operation allRequirements,
                                                          ref MyType mytype,
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
            matches = new List<SongMetadata>(0);

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

        matches = SongSearchService.Search(search);

        IEnumerable<string> ids = matches.Select(songMetadata => songMetadata.Id);

        return sequence.Where(leaderboard => ids.Contains(leaderboard.SongId));
    }

    public static string GetSearchFilters(this string search,
                                          ref Type type,
                                          ref string? mode,
                                          ref int? mapType,
                                          ref Operation allTypes,
                                          ref Requirements mapRequirements,
                                          ref Operation allRequirements,
                                          ref MyType mytype,
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
            if (!(filter.Contains('=') || filter.Contains('<') || filter.Contains('>')))
            {
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    stringBuilder.Append(filter);
                    stringBuilder.Append(' ');
                }

                continue;
            }

            if (EnumFilter(filter, nameof(type), ref type))
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

            if (EnumFilter(filter, nameof(allTypes), ref allTypes))
            {
                continue;
            }

            if (EnumFilter(filter, nameof(mapRequirements), ref mapRequirements))
            {
                continue;
            }

            if (EnumFilter(filter, nameof(allRequirements), ref allRequirements))
            {
                continue;
            }

            if (EnumFilter(filter, nameof(mytype), ref mytype))
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

        return stringBuilder.ToString().Trim();
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

    private static bool EnumFilter<T>(string filter, string name, ref T enumValue)
        where T : struct, Enum
    {
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