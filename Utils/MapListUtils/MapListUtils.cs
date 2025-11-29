using BeatLeader_Server.Enums;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    public static IQueryable<SongHelper> Filter(this IQueryable<Song> source,
                                                 AppContext _context,
                                                 out int? searchId,
                                                 MapSortBy sortBy = MapSortBy.None,
                                                 Order order = Order.Desc,
                                                 string? search = null,
                                                 Type type = Type.All,
                                                 string? types = null,
                                                 string? mode = null,
                                                 string? difficulty = null,
                                                 MapTypes mapType = MapTypes.None,
                                                 Operation allTypes = Operation.Any,
                                                 Requirements mapRequirements = Requirements.Ignore,
                                                 Operation allRequirements = Operation.Any,
                                                 SongStatus songStatus = SongStatus.None,
                                                 LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
                                                 MyTypeMaps mytype = MyTypeMaps.None,
                                                 float? starsFrom = null,
                                                 float? starsTo = null,
                                                 float? accRatingFrom = null,
                                                 float? accRatingTo = null,
                                                 float? passRatingFrom = null,
                                                 float? passRatingTo = null,
                                                 float? techRatingFrom = null,
                                                 float? techRatingTo = null,
                                                 float? duration_from = null,
                                                 float? duration_to = null,
                                                 int? dateFrom = null,
                                                 int? dateTo = null,
                                                 DateRangeType date_range = DateRangeType.Upload,
                                                 string? mapper = null,
                                                 List<PlaylistResponse>? playlists = null,
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
              .WhereTypes(type, types)
              .WhereDifficulty(difficulty)
              .WhereMapType(mapType, allTypes)
              .WhereMode(mode)
              .WhereMapRequirements(mapRequirements, allRequirements)
              .WhereSongStatus(songStatus)
              .WhereMapper(mapper)
              .WherePlaylists(playlists)
              .WhereMyType(mytype, currentPlayer, leaderboardContext)
              .WhereRatingFrom(RatingType.Stars, starsFrom)
              .WhereRatingFrom(RatingType.Acc, accRatingFrom)
              .WhereRatingFrom(RatingType.Pass, passRatingFrom)
              .WhereRatingFrom(RatingType.Tech, techRatingFrom)
              .WhereRatingTo(RatingType.Stars, starsTo)
              .WhereRatingTo(RatingType.Acc, accRatingTo)
              .WhereRatingTo(RatingType.Pass, passRatingTo)
              .WhereRatingTo(RatingType.Tech, techRatingTo)
              .WhereDurationFrom(duration_from)
              .WhereDurationTo(duration_to)
              .WhereDateFrom(date_range, type, dateFrom, dateTo)
              .Sort(sortBy, order, type, mytype, dateFrom, dateTo, date_range, searchId, currentPlayer, leaderboardContext);
}