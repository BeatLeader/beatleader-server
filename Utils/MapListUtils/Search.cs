using System.Globalization;
using System.Text;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Song> FilterBySearch(this IQueryable<Song> sequence,
                                                          string? search,
                                                          AppContext _context,
                                                          out int? searchId,
                                                          ref Type type,
                                                          ref string? mode,
                                                          ref int? mapType,
                                                          ref Operation allTypes,
                                                          ref Requirements mapRequirements,
                                                          ref Operation allRequirements,
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
        if (string.IsNullOrEmpty(search)) // returns before search filters if truely empty
        {
            searchId = null;

            return sequence;
        }

        List<SongMetadata> matches = SongSearchService.Search(search);
        Random rnd = new Random();

        int searchIdentifier = rnd.Next(1, 10000);
        searchId = searchIdentifier;

        foreach (var item in matches) {
            _context.SongSearches.Add(new SongSearch {
                SongId = item.Id,
                Score = item.Score,
                SearchId = searchIdentifier
            });
        }
        _context.BulkSaveChanges();

        IEnumerable<string> ids = matches.Select(songMetadata => songMetadata.Id);

        return sequence.Where(s => ids.Contains(s.Id));
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
            if (filter.Contains('=')
             || filter.Contains('<')
             || filter.Contains('>'))
            {
                if (EnumFilter(filter, nameof(type), ref type)
                 || BasicFilter(filter, nameof(mode), ref mode)
                 || BasicParseFilter(filter, nameof(mapType), ref mapType)
                 || EnumFilter(filter, nameof(allTypes), ref allTypes)
                 || EnumFilter(filter, nameof(mapRequirements), ref mapRequirements)
                 || EnumFilter(filter, nameof(allRequirements), ref allRequirements)
                 || EnumFilter(filter, nameof(mytype), ref mytype)
                 || ClampFilter(filter, RatingType.Stars.ToString(), ref starsFrom, ref starsTo)
                 || ClampFilter(filter, RatingType.Acc.ToString(), ref accRatingFrom, ref accRatingTo)
                 || ClampFilter(filter, RatingType.Pass.ToString(), ref passRatingFrom, ref passRatingTo)
                 || ClampFilter(filter, RatingType.Tech.ToString(), ref techRatingFrom, ref techRatingTo)
                 || ClampFilter(filter, "date", ref dateFrom, ref dateTo))
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(filter))
            {
                stringBuilder.Append(filter);
                stringBuilder.Append(' ');
            }
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