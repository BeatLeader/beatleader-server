using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Type = BeatLeader_Server.Enums.Type;

namespace BeatLeader_Server.Utils;

public class SongHelper {
    public Song Song { get; set; }
    public IEnumerable<DifficultyDescription> Difficulties { get; set; }
}

public static partial class MapListUtils
{
    private static IQueryable<SongHelper> WhereType(this IQueryable<Song> sequence, Type type) =>
        type switch
        {
            Type.Ranked      => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status == DifficultyStatus.ranked),
            }),
            Type.Ranking     => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => 
                    d.Status != DifficultyStatus.unranked && 
                    d.Status != DifficultyStatus.outdated && 
                    d.Status != DifficultyStatus.inevent
                )
            }),
            Type.Nominated   => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status == DifficultyStatus.nominated)
            }),
            Type.Qualified   => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status == DifficultyStatus.qualified)
            }),
            Type.Staff       => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status == DifficultyStatus.qualified || d.Status == DifficultyStatus.nominated)
            }),
            Type.Unranked    => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status == DifficultyStatus.unranked)
            }),
            Type.Ost         => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status == DifficultyStatus.OST)
            }),
            _                => sequence.Select(s => new SongHelper {
                Song = s,
                Difficulties = s.Difficulties.Where(d => d.Status != DifficultyStatus.outdated)
            }),
        };

    private static IQueryable<SongHelper> WhereTypes(this IQueryable<Song> sequence, Type type, string? types) {
        if (types != null) {
            if (types == "all") {
                return sequence.Select(s => new SongHelper {
                    Song = s,
                    Difficulties = s.Difficulties.Where(d => d.Status != DifficultyStatus.outdated)
                });
            } else {
                var statuses = types.Split(",").Select(type => (DifficultyStatus)Enum.Parse(typeof(DifficultyStatus), type)).ToList();
                return sequence.Select(s => new SongHelper {
                    Song = s,
                    Difficulties = s.Difficulties.Where(d => statuses.Contains(d.Status) && d.Status != DifficultyStatus.outdated)
                });
            }
        } else {
            return sequence.WhereType(type);
        }
    }

    private static IQueryable<SongHelper> WhereMapType(this IQueryable<SongHelper> sequence, MapTypes mapType, Operation allTypes)
    {
        if (mapType == MapTypes.None)
        {
            return sequence;
        }

        return allTypes switch
        {
            Operation.Any => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => (d.Type & mapType) != 0)
            }),
            Operation.All => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.Type == mapType)
            }),
            Operation.Not => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => (d.Type & mapType) == 0)
            }),
            _             => sequence,
        };
    }

    private static IQueryable<SongHelper> WhereSongStatus(this IQueryable<SongHelper> sequence, SongStatus status)
    {
        if (status == SongStatus.None)
        {
            return sequence;
        }

        return sequence.Where(s => (s.Song.Status & status) != 0);
    }

    private static IQueryable<SongHelper> WhereMapper(this IQueryable<SongHelper> sequence, string? mapper)
    {
        if (mapper == null)
        {
            return sequence;
        }

        var ids = mapper.Split(",").Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0).ToArray();
        if (ids.Length == 0) {
            return sequence;
        }
        return sequence.Where(s => s.Song.Mappers.Any(m => ids.Contains(m.Id)));
    }

    private static IQueryable<SongHelper> WherePlaylists(this IQueryable<SongHelper> sequence, List<PlaylistResponse>? playlists)
    {
        if (playlists == null)
        {
            return sequence;
        }

        var hashes = playlists.SelectMany(p => p.songs.Where(s => s.hash != null).Select(s => (string)s.hash!.ToLower())).ToList();
        var keys = playlists.SelectMany(p => p.songs.Where(s => s.hash == null && s.key != null).Select(s => (string)s.key!.ToLower())).ToList();

        if (hashes.Count == 0 && keys.Count == 0) {
            return sequence;
        }
        return sequence.Where(s => hashes.Contains(s.Song.LowerHash) || keys.Contains(s.Song.Id));
    }

    private static IQueryable<SongHelper> WhereMyType(this IQueryable<SongHelper> sequence, MyTypeMaps mytype, Player? currentPlayer, LeaderboardContexts leaderboardContext = LeaderboardContexts.General)
    {
        string? currentId = currentPlayer?.Id;
        
        if (currentId == null) { 
            return sequence;
        }
        var friendIds = new List<string> { currentId };
        if (mytype == MyTypeMaps.FriendsPlayed && currentPlayer?.Friends != null) {
            friendIds.AddRange(currentPlayer.Friends.Select(f => f.Id).ToList());
        }

        return mytype switch
        {
            MyTypeMaps.Played          => sequence.Where(s => s
                .Song
                .Leaderboards
                .Where(l => l.Difficulty.Value < 10)
                .Any(l => l.Scores.Any(sc => sc.PlayerId == currentId && sc.ValidContexts.HasFlag(leaderboardContext)))),
            MyTypeMaps.Unplayed        => sequence.Where(s => s
                .Song
                .Leaderboards
                .Where(l => l.Difficulty.Value < 10)
                .All(l => !l.Scores.Any(sc => sc.PlayerId == currentId && sc.ValidContexts.HasFlag(leaderboardContext)))),
            MyTypeMaps.FriendsPlayed   => sequence.Where(s => s
                .Song
                .Leaderboards
                .Where(l => l.Difficulty.Value < 10)
                .Any(l => l.Scores.Any(sc => friendIds.Contains(sc.PlayerId) && sc.ValidContexts.HasFlag(leaderboardContext)))),
            _                      => sequence,
        };
    }

    private static IQueryable<SongHelper> WhereMode(this IQueryable<SongHelper> sequence, string? mode)
    {
        if (mode == null)
        {
            return sequence;
        }

        return sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.ModeName == mode)
        });
    }

    private static IQueryable<SongHelper> WhereDifficulty(this IQueryable<SongHelper> sequence, string? difficulty)
    {
        if (difficulty == null)
        {
            return sequence;
        }

        if (difficulty == "fullspread") {
            return sequence
                .Where(s => s.Difficulties.Where(d => d.Mode == 1).Sum(d => d.Value) == (1 + 3 + 5 + 7 + 9))
                .Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.Mode == 1)
            });
        } else {
            return sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.DifficultyName == difficulty)
            });
        }
    }

    private static IQueryable<SongHelper> WhereMapRequirements(this IQueryable<SongHelper> sequence, Requirements mapRequirements, Operation allRequirements)
    {
        if (mapRequirements == Requirements.Ignore)
        {
            return sequence;
        }

        return allRequirements switch
        {
            Operation.Any => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => (d.Requirements & mapRequirements) != 0)
            }),
            Operation.All => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.Requirements == mapRequirements)
            }),
            Operation.Not => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => (d.Requirements & mapRequirements) == 0)
            }),
            _             => sequence,
        };
    }

    private static IQueryable<SongHelper> WhereRatingFrom(this IQueryable<SongHelper> sequence, RatingType rating, float? from)
    {
        if (from == null)
        {
            return sequence;
        }

        return rating switch {
            RatingType.Stars => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.Stars >= from)
            }),
            RatingType.Acc => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.AccRating >= from)
            }),
            RatingType.Pass => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.PassRating >= from)
            }),
            RatingType.Tech => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.TechRating >= from)
            }),
            _ => sequence,
        };
    }

    private static IQueryable<SongHelper> WhereRatingTo(this IQueryable<SongHelper> sequence, RatingType rating, float? to)
    {
        if (to == null)
        {
            return sequence;
        }

        return rating switch {
            RatingType.Stars => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.Stars <= to)
            }),
            RatingType.Acc => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.AccRating <= to)
            }),
            RatingType.Pass => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.PassRating <= to)
            }),
            RatingType.Tech => sequence.Select(s => new SongHelper {
                Song = s.Song,
                Difficulties = s.Difficulties.Where(d => d.TechRating <= to)
            }),
            _ => sequence,
        };
    }

    private static IQueryable<SongHelper> WhereDurationFrom(this IQueryable<SongHelper> sequence, float? from)
    {
        if (from == null)
        {
            return sequence;
        }

        return sequence.Where(s => s.Song.Duration >= from);
    }

    private static IQueryable<SongHelper> WhereDurationTo(this IQueryable<SongHelper> sequence, float? to)
    {
        if (to == null)
        {
            return sequence;
        }

        return sequence.Where(s => s.Song.Duration <= to);
    }

    private static IQueryable<SongHelper> WhereDateFrom(this IQueryable<SongHelper> sequence, DateRangeType rangeType, Type type, int? dateFrom, int? dateTo)
    {
        if (dateFrom == null && dateTo == null)
        {
            return sequence;
        }

        switch (rangeType) {
            case DateRangeType.Upload:
                sequence = sequence.Where(s => (dateFrom == null || s.Song.UploadTime >= dateFrom) && (dateTo == null || s.Song.UploadTime <= dateTo));
                break;
            case DateRangeType.Ranked:
                switch (type) {
                    case Type.Ranked:
                        sequence = sequence.Select(s => new SongHelper {
                            Song = s.Song,
                            Difficulties = s.Difficulties.Where(d => (dateFrom == null || d.RankedTime >= dateFrom) && (dateTo == null || d.RankedTime <= dateTo))
                        });
                        break;
                    case Type.Ranking:
                        sequence = sequence.Select(s => new SongHelper {
                            Song = s.Song,
                            Difficulties = s.Difficulties.Where(d => (dateFrom == null
                                    || d.RankedTime >= dateFrom
                                    || d.NominatedTime >= dateFrom
                                    || d.QualifiedTime >= dateFrom)
                                   && (dateTo == null
                                    || d.RankedTime <= dateTo
                                    || d.NominatedTime <= dateTo
                                    || d.QualifiedTime <= dateTo))
                        });
                        break;
                    case Type.Nominated:
                        sequence = sequence.Select(s => new SongHelper {
                            Song = s.Song,
                            Difficulties = s.Difficulties.Where(d => (dateFrom == null || d.NominatedTime >= dateFrom) && (dateTo == null || d.NominatedTime <= dateTo))
                        });
                        break;
                    case Type.Qualified:
                        sequence = sequence.Select(s => new SongHelper {
                            Song = s.Song,
                            Difficulties = s.Difficulties.Where(d => (dateFrom == null || d.QualifiedTime >= dateFrom) && (dateTo == null || d.QualifiedTime <= dateTo))
                        });
                        break;
                    default:
                        break;
                }
                break;
            case DateRangeType.Score:
                sequence = sequence.Where(s => s.Song.Leaderboards.Any(l => (dateFrom == null || l.LastScoreTime >= dateFrom) && (dateTo == null || l.LastScoreTime <= dateTo)));
                break;
            default:
                break;
        }

        return sequence;
    }

    public static async Task<(IQueryable<Song>, int)> WherePage(this IQueryable<Song> sequence, int page, int count)
    {
        if (page <= 0) {
            page = 1;
        }

        return (sequence.Skip((page - 1) * count).Take(count), await sequence.CountAsync());
    }
}