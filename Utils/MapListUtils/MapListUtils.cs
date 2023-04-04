using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils;

public enum Operation {
    any = 0,
    all = 1,
    not = 2,
}

public static partial class MapListUtils {
    public static IQueryable<Leaderboard> Filter(this IQueryable<Leaderboard> source,
                                                 ReadAppContext context,
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
                                                 float? stars_from = null,
                                                 float? stars_to = null,
                                                 int? date_from = null,
                                                 int? date_to = null,
                                                 string? currentID = null) {

        var filtered = source
               .FilterBySearch(search)
               .WhereType(type)
               .WhereMapType(mapType, allTypes)
               .WhereMode(mode)
               .WhereMapRequirements(mapRequirements, allRequirements)
               .WhereMyType(context, mytype, currentID)
               .WhereStarsFrom(stars_from)
               .WhereStarsTo(stars_to);

        if (search == null || search.Length == 0) {
            return filtered.Sort(sortBy, order!, type, mytype, date_from, date_to, currentID);
        } else {
            return filtered;
        }
    }
}