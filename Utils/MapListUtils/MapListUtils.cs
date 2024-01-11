using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    public static IQueryable<Leaderboard> FilterRanking(this IQueryable<Leaderboard> source,
                                                 AppContext _context,
                                                 int page,
                                                 int count,
                                                 out int totalMatches,
                                                 SortBy sortBy = SortBy.None,
                                                 Order order = Order.Desc,
                                                 int? dateFrom = null,
                                                 int? dateTo = null) =>
        source
              .WhereType(Type.Ranking)
              .Sort(sortBy, order, Type.Ranking, MyType.None, dateFrom, dateTo, null, null)
              .WherePage(page, count, out totalMatches);

    public static IQueryable<Leaderboard> Filter(this IQueryable<Leaderboard> source,
                                                 AppContext _context,
                                                 out int? searchId,
                                                 SortBy sortBy = SortBy.None,
                                                 Order order = Order.Desc,
                                                 string? search = null,
                                                 Type type = Type.All,
                                                 string? mode = null,
                                                 string? difficulty = null,
                                                 int? mapType = null,
                                                 Operation allTypes = Operation.Any,
                                                 Requirements mapRequirements = Requirements.Ignore,
                                                 Operation allRequirements = Operation.Any,
                                                 SongStatus songStatus = SongStatus.None,
                                                 MyType mytype = MyType.None,
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
                                                 Player? currentPlayer = null) =>
        source.FilterBySearch(search,
                              _context,
                              out searchId,
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
                              ref dateTo)
              .WhereType(type)
              .WhereDifficulty(difficulty)
              .WhereMapType(mapType, allTypes)
              .WhereMode(mode)
              .WhereMapRequirements(mapRequirements, allRequirements)
              .WhereSongStatus(songStatus)
              .WhereMyType(mytype, currentPlayer)
              .WhereRatingFrom(RatingType.Stars, starsFrom)
              .WhereRatingFrom(RatingType.Acc, accRatingFrom)
              .WhereRatingFrom(RatingType.Pass, passRatingFrom)
              .WhereRatingFrom(RatingType.Tech, techRatingFrom)
              .WhereRatingTo(RatingType.Stars, starsTo)
              .WhereRatingTo(RatingType.Acc, accRatingTo)
              .WhereRatingTo(RatingType.Pass, passRatingTo)
              .WhereRatingTo(RatingType.Tech, techRatingTo)
              .Sort(sortBy, order, type, mytype, dateFrom, dateTo, searchId, currentPlayer);
}