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
                                                          ref float? starsFrom,
                                                          ref float? starsTo,
                                                          ref string? mytype,
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
                                         ref starsFrom,
                                         ref starsTo,
                                         ref mytype,
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
                                           ref float? starsFrom,
                                           ref float? starsTo,
                                           ref string? mytype,
                                           ref int? dateFrom,
                                           ref int? dateTo)
    {
        StringBuilder stringBuilder = new(search.Length);

        foreach (string filter in search.Split(' '))
        {
            if (!filter.Contains('='))
            {
                stringBuilder.Append(filter);
                stringBuilder.Append(' ');

                continue;
            }

            string[] nameValue = filter.ToLower().Split('=');
            string name = nameValue[0];
            string value = nameValue[1];

            switch (name)
            {
                case "type":            type = value;

                    break;
                case "mode":            mode = value;

                    break;
                case "maptype":         mapType = int.Parse(value);

                    break;
                case "allTypes":        allTypes = Enum.Parse<Operation>(value, true);

                    break;
                case "maprequirements": mapRequirements = Enum.Parse<Requirements>(value, true);

                    break;
                case "allRequirements": allRequirements = Enum.Parse<Operation>(value, true);

                    break;
                case "starsfrom":       starsFrom = float.Parse(name);

                    break;
                case "starsto":         starsTo = float.Parse(name);

                    break;
                case "mytype":          mytype = value;

                    break;
                case "datefrom":        dateFrom = int.Parse(value);

                    break;
                case "dateto":          dateTo = int.Parse(value);

                    break;
            }
        }

        return stringBuilder.ToString();
    }
}