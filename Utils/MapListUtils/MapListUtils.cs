using BeatLeader_Server.Models;
using BeatLeader_Server.Services;

namespace BeatLeader_Server.Utils;

public enum Operation
{
    any = 0,
    all = 1,
    not = 2,
}

public static partial class MapListUtils
{
    public static IQueryable<Leaderboard> Filter(this IQueryable<Leaderboard> source,
                                                 ReadAppContext context,
                                                 int page,
                                                 int count,
                                                 ref int searchCount,
                                                 string? sortBy = null,
                                                 string? order = null,
                                                 string? search = null,
                                                 string? type = null,
                                                 string? mode = null,
                                                 int? mapType = null,
                                                 Operation allTypes = 0,
                                                 Requirements? mapRequirements = null,
                                                 Operation allRequirements = 0,
                                                 string? mytype = null,
                                                 float? starsFrom = null,
                                                 float? starsTo = null,
                                                 float? accRatingFrom = null,
                                                 float? accRatingTo = null,
                                                 float? passRatingFrom = null,
                                                 float? passRatingTo = null,
                                                 float? techRatingFrom = null,
                                                 float? techRatingTo = null,
                                                 int? dateFrom = null,
                                                 int? dateTo = null,
                                                 string? currentID = null)
    {
        IQueryable<Leaderboard> filtered = source
                                           .FilterBySearch(search,
                                                           ref type,
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
                                                           ref dateTo,
                                                           out List<SearchService.SongMatch> matches)
                                           .WhereType(type)
                                           .WhereMapType(mapType, allTypes)
                                           .WhereMode(mode)
                                           .WhereMapRequirements(mapRequirements, allRequirements)
                                           .WhereMyType(context, mytype, currentID)
                                           .WhereRatingFrom(RatingType.Stars, starsFrom)
                                           .WhereRatingFrom(RatingType.Acc, accRatingFrom)
                                           .WhereRatingFrom(RatingType.Pass, passRatingFrom)
                                           .WhereRatingFrom(RatingType.Tech, techRatingFrom)
                                           .WhereRatingTo(RatingType.Stars, starsTo)
                                           .WhereRatingTo(RatingType.Acc, accRatingTo)
                                           .WhereRatingTo(RatingType.Pass, passRatingTo)
                                           .WhereRatingTo(RatingType.Tech, techRatingTo);

        if (matches.Count != 0)
        {
            List<string> matchedAndFiltered = filtered.Select(leaderboard => leaderboard.Id).ToList();
            searchCount = matchedAndFiltered.Count;

            IEnumerable<string> sorted = matchedAndFiltered
                                         .OrderByDescending(s => matches.FirstOrDefault(songMatch => songMatch.Id == s)?.Score ?? 0)
                                         .Skip((page - 1) * count)
                                         .Take(count);

            return filtered.Where(leaderboard => sorted.Contains(leaderboard.Id));
        }

        return filtered.Sort(sortBy, order!, type, mytype, dateFrom, dateTo, currentID);
    }
}