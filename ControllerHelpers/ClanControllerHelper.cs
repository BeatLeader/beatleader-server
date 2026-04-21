using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using SixLabors.ImageSharp;
using Swashbuckle.AspNetCore.Annotations;

namespace BeatLeader_Server.ControllerHelpers {
    public class ClanControllerHelper {
        public static async Task<ClanResponseFull?> CurrentClan(AppContext _context, int? id, string? tag, string? currentID) {
            IQueryable<Clan> query;
            if (id != null) {
                query = _context
                    .Clans
                    .Where(c => c.Id == id);
            } else if (tag == "my") {
                query = _context
                    .Clans
                    .Where(c => c.LeaderID == currentID);
            } else {
                query = _context
                    .Clans
                    .Where(c => c.Tag == tag);
            }

            return await query
                    .TagWithCaller()
                    .AsNoTracking()
                    //.Include(c => c.FeaturedPlaylists)
                    .Select(c => new ClanResponseFull {
                        Id = c.Id,
                        Name = c.Name,
                        Color = c.Color,
                        Icon = c.Icon,
                        Tag = c.Tag,
                        LeaderID = c.LeaderID,
                        Description = c.Description,
                        Bio = c.Bio,
                        RichBioTimeset = c.RichBioTimeset,
                        DiscordInvite = c.DiscordInvite,
                        PlayersCount = c.PlayersCount,
                        Pp = c.Pp,
                        Rank = c.Rank,
                        AverageRank = c.AverageRank,
                        AverageAccuracy = c.AverageAccuracy,
                        RankedPoolPercentCaptured = c.RankedPoolPercentCaptured,
                        CaptureLeaderboardsCount = c.CaptureLeaderboardsCount,
                        //FeaturedPlaylists = c.FeaturedPlaylists.Select(fp => new FeaturedPlaylistResponse {
                        //    Id = fp.Id,
                        //    PlaylistLink = fp.PlaylistLink,
                        //    Cover = fp.Cover,
                        //    Title = fp.Title,
                        //    Description = fp.Description,

                        //    Owner = fp.Owner,
                        //    OwnerCover = fp.OwnerCover,
                        //    OwnerLink = fp.OwnerLink,
                        //}).ToList(),
                        ClanRankingDiscordHook = currentID == c.LeaderID ? c.ClanRankingDiscordHook : null,
                        PlayerChangesCallback = currentID == c.LeaderID ? c.PlayerChangesCallback : null
                    })
                    .FirstOrDefaultAsync();
        }

        private sealed class ClanMapPageRow
        {
            public int Id { get; set; }
            public string LeaderboardId { get; set; }
            public float DisplayPp { get; set; }
        }

        private static IQueryable<ClanRankingResponse> BuildClanRankingDetailsQuery(
            AppContext context,
            List<int> rankingIds)
        {
            return context.ClanRanking
                .AsNoTracking()
                .TagWithCaller()
                .Where(cr => rankingIds.Contains(cr.Id))
                .Select(cr => new ClanRankingResponse
                {
                    Id = cr.Id,
                    Clan = cr.Leaderboard.Clan == null ? null : new ClanResponseFull
                    {
                        Id = cr.Leaderboard.Clan.Id,
                        Name = cr.Leaderboard.Clan.Name,
                        Color = cr.Leaderboard.Clan.Color,
                        Icon = cr.Leaderboard.Clan.Icon,
                        Tag = cr.Leaderboard.Clan.Tag,
                        LeaderID = cr.Leaderboard.Clan.LeaderID,
                        Description = cr.Leaderboard.Clan.Description,
                        Pp = cr.Leaderboard.Clan.Pp,
                        Rank = cr.Leaderboard.Clan.Rank
                    },
                    LastUpdateTime = cr.LastUpdateTime,
                    AverageRank = cr.AverageRank,
                    Pp = cr.Pp,
                    AverageAccuracy = cr.AverageAccuracy,
                    TotalScore = cr.TotalScore,
                    LeaderboardId = cr.LeaderboardId,
                    Leaderboard = new LeaderboardResponse
                    {
                        Id = cr.Leaderboard.Id,
                        Song = new SongResponse
                        {
                            Id = cr.Leaderboard.Song.Id,
                            Hash = cr.Leaderboard.Song.LowerHash,
                            Name = cr.Leaderboard.Song.Name,
                            SubName = cr.Leaderboard.Song.SubName,
                            Author = cr.Leaderboard.Song.Author,
                            Mapper = cr.Leaderboard.Song.Mapper,
                            CoverImage = cr.Leaderboard.Song.CoverImage,
                            FullCoverImage = cr.Leaderboard.Song.FullCoverImage,
                            DownloadUrl = cr.Leaderboard.Song.DownloadUrl
                        },
                        Difficulty = new DifficultyResponse
                        {
                            Id = cr.Leaderboard.Difficulty.Id,
                            Value = cr.Leaderboard.Difficulty.Value,
                            Mode = cr.Leaderboard.Difficulty.Mode,
                            DifficultyName = cr.Leaderboard.Difficulty.DifficultyName,
                            ModeName = cr.Leaderboard.Difficulty.ModeName,
                            Status = cr.Leaderboard.Difficulty.Status,
                            ModifierValues = cr.Leaderboard.Difficulty.ModifierValues,
                            ModifiersRating = cr.Leaderboard.Difficulty.ModifiersRating,
                            Stars = cr.Leaderboard.Difficulty.Stars,
                            PredictedAcc = cr.Leaderboard.Difficulty.PredictedAcc,
                            PassRating = cr.Leaderboard.Difficulty.PassRating,
                            AccRating = cr.Leaderboard.Difficulty.AccRating,
                            TechRating = cr.Leaderboard.Difficulty.TechRating,
                            Type = cr.Leaderboard.Difficulty.Type,
                            MaxScore = cr.Leaderboard.Difficulty.MaxScore,
                        },
                        Plays = cr.Leaderboard.Plays,
                    },
                    Rank = cr.Rank,
                    MyScore = null
                });
        }

        private static IQueryable<ScoreResponseWithAcc> BuildMyScoresQuery(
            AppContext context,
            string currentID,
            List<string> leaderboardIds,
            LeaderboardContexts leaderboardContext)
        {
            return context.Scores
                .AsNoTracking()
                .TagWithCaller()
                .Where(s =>
                    s.PlayerId == currentID &&
                    leaderboardIds.Contains(s.LeaderboardId) &&
                    !s.Banned &&
                    (s.ValidContexts & leaderboardContext) == leaderboardContext)
                .Select(s => new ScoreResponseWithAcc
                {
                    Id = s.Id,
                    BaseScore = s.BaseScore,
                    ModifiedScore = s.ModifiedScore,
                    PlayerId = s.PlayerId,
                    Accuracy = s.Accuracy,
                    Pp = s.Pp,
                    FcAccuracy = s.FcAccuracy,
                    FcPp = s.FcPp,
                    BonusPp = s.BonusPp,
                    Rank = s.Rank,
                    Replay = s.Replay,
                    Offsets = s.ReplayOffsets,
                    Modifiers = s.Modifiers,
                    BadCuts = s.BadCuts,
                    MissedNotes = s.MissedNotes,
                    BombCuts = s.BombCuts,
                    WallsHit = s.WallsHit,
                    Pauses = s.Pauses,
                    FullCombo = s.FullCombo,
                    Hmd = s.Hmd,
                    Timeset = s.Timeset,
                    Timepost = s.Timepost,
                    ReplaysWatched = s.ReplayWatchedTotal,
                    LeaderboardId = s.LeaderboardId,
                    Platform = s.Platform,
                    Weight = s.Weight,
                    AccLeft = s.AccLeft,
                    AccRight = s.AccRight,
                    MaxStreak = s.MaxStreak,
                });
        }

        public static async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> PopulateClanWithMaps(
            AppContext context,
            ClanResponseFull clan,
            string? currentID,
            int page = 1,
            int count = 10,
            ClanMapsSortBy sortBy = ClanMapsSortBy.Pp,
            LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            Order order = Order.Desc,
            PlayedStatus playedStatus = PlayedStatus.Any)
        {
            page = Math.Max(page, 1);
            count = Math.Max(count, 1);

            var filtered = context.ClanRanking
                .AsNoTracking()
                .TagWithCaller()
                .Where(cr =>
                    cr.ClanId == clan.Id &&
                    cr.Leaderboard.Difficulty.Status == DifficultyStatus.ranked);

            if (currentID != null && playedStatus != PlayedStatus.Any)
            {
                filtered = playedStatus == PlayedStatus.Played
                    ? filtered.Where(cr => cr.Leaderboard.Scores.Any(s => s.PlayerId == currentID))
                    : filtered.Where(cr => !cr.Leaderboard.Scores.Any(s => s.PlayerId == currentID));
            }

            IQueryable<ClanMapPageRow> pageBaseQuery;

            switch (sortBy)
            {
                case ClanMapsSortBy.Pp:
                    pageBaseQuery = (order == Order.Desc
                            ? filtered.OrderByDescending(cr => cr.Pp).ThenBy(cr => cr.Id)
                            : filtered.OrderBy(cr => cr.Pp).ThenBy(cr => cr.Id))
                        .Select(cr => new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp
                        });
                    break;

                case ClanMapsSortBy.Acc:
                    pageBaseQuery = (order == Order.Desc
                            ? filtered.OrderByDescending(cr => cr.AverageAccuracy).ThenBy(cr => cr.Id)
                            : filtered.OrderBy(cr => cr.AverageAccuracy).ThenBy(cr => cr.Id))
                        .Select(cr => new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp
                        });
                    break;

                case ClanMapsSortBy.Rank:
                    pageBaseQuery = (order == Order.Desc
                            ? filtered.OrderByDescending(cr => cr.Rank).ThenBy(cr => cr.Id)
                            : filtered.OrderBy(cr => cr.Rank).ThenBy(cr => cr.Id))
                        .Select(cr => new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp
                        });
                    break;

                case ClanMapsSortBy.Date:
                    pageBaseQuery = (order == Order.Desc
                            ? filtered.OrderByDescending(cr => cr.LastUpdateTime).ThenBy(cr => cr.Id)
                            : filtered.OrderBy(cr => cr.LastUpdateTime).ThenBy(cr => cr.Id))
                        .Select(cr => new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp
                        });
                    break;

                case ClanMapsSortBy.Tohold:
                {
                    var holdBase =
                        from cr in filtered
                        where cr.Rank == 1
                        join rival in context.ClanRanking.AsNoTracking()
                                .Where(x => x.Rank == 2 && x.ClanId != clan.Id)
                            on cr.LeaderboardId equals rival.LeaderboardId
                        select new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp - rival.Pp
                        };

                    pageBaseQuery = (order.Reverse() == Order.Desc
                            ? holdBase.OrderByDescending(x => x.DisplayPp).ThenBy(x => x.Id)
                            : holdBase.OrderBy(x => x.DisplayPp).ThenBy(x => x.Id));
                    break;
                }

                case ClanMapsSortBy.Toconquer:
                {
                    var conquerBase =
                        from cr in filtered
                        where cr.Rank != 1 || cr.Leaderboard.ClanRankingContested
                        join rival in context.ClanRanking.AsNoTracking()
                                .Where(x => x.Rank == 1 && x.ClanId != clan.Id)
                            on cr.LeaderboardId equals rival.LeaderboardId into rivalJoin
                        from rival in rivalJoin.DefaultIfEmpty()
                        select new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp - (rival == null ? 0 : rival.Pp)
                        };

                    pageBaseQuery = (order == Order.Desc
                            ? conquerBase.OrderByDescending(x => x.DisplayPp).ThenBy(x => x.Id)
                            : conquerBase.OrderBy(x => x.DisplayPp).ThenBy(x => x.Id));
                    break;
                }

                default:
                    pageBaseQuery = filtered
                        .OrderByDescending(cr => cr.Pp)
                        .ThenBy(cr => cr.Id)
                        .Select(cr => new ClanMapPageRow
                        {
                            Id = cr.Id,
                            LeaderboardId = cr.LeaderboardId,
                            DisplayPp = cr.Pp
                        });
                    break;
            }

            var total = await pageBaseQuery.CountAsync();

            var pageRows = await pageBaseQuery
                .Skip((page - 1) * count)
                .Take(count)
                .ToListAsync();

            if (pageRows.Count == 0)
            {
                return new ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>
                {
                    Container = clan,
                    Data = new List<ClanRankingResponse>(),
                    Metadata = new Metadata
                    {
                        Page = page,
                        ItemsPerPage = count,
                        Total = total
                    }
                };
            }

            var rankingIds = pageRows.Select(x => x.Id).ToList();
            var leaderboardIds = pageRows.Select(x => x.LeaderboardId).Distinct().ToList();

            var detailItems = await BuildClanRankingDetailsQuery(context, rankingIds).ToListAsync();
            var detailById = detailItems.ToDictionary(x => x.Id);

            if (!string.IsNullOrEmpty(currentID))
            {
                // If there can be multiple rows per player+leaderboard, add an explicit ordering rule here.
                var myScores = await BuildMyScoresQuery(context, currentID, leaderboardIds, leaderboardContext)
                    .ToListAsync();

                var myScoreByLeaderboardId = myScores
                    .GroupBy(s => s.LeaderboardId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var item in detailItems)
                {
                    if (myScoreByLeaderboardId.TryGetValue(item.LeaderboardId, out var myScore))
                    {
                        item.MyScore = myScore;
                    }
                }
            }

            var rankingList = pageRows
                .Select(row =>
                {
                    var item = detailById[row.Id];
                    item.Pp = row.DisplayPp;
                    return item;
                })
                .ToList();

            return new ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>
            {
                Container = clan,
                Data = rankingList,
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = total
                }
            };
        }

        public static async Task<ActionResult<ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>>> PopulateClan(
            AppContext _context,
            ClanResponseFull clan,
            int page = 1,
            int count = 10,
            PlayerSortBy sortBy = PlayerSortBy.Pp,
            Order order = Order.Desc,
            bool primary = false)
        {
            IQueryable<Player> players = _context
                .Players
                .AsNoTracking()
                .Include(p => p.ProfileSettings)
                .Include(p => p.Socials);
            if (primary) {
                players = players.Where(p => !p.Banned && p.TopClan != null && p.TopClan.Id == clan.Id);
            } else {
                players = players.Where(p => !p.Banned && p.Clans.Any(c => c.Id == clan.Id));
            }

            switch (sortBy)
            {
                case PlayerSortBy.Pp:
                    players = players.Order(order, t => t.Pp);
                    break;
                case PlayerSortBy.Acc:
                    players = players.Order(order, t => t.ScoreStats.AverageRankedAccuracy);
                    break;
                case PlayerSortBy.Rank:
                    players = players.Order(order, t => t.Rank);
                    break;
                
            }
            return new ResponseWithMetadataAndContainer<PlayerResponse, ClanResponseFull>
            {
                Container = clan,
                Data = (await players
                    .TagWithCallerS()
                    .AsNoTracking()
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(p => new PlayerResponse
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Alias = p.Alias,
                        Platform = p.Platform,
                        Avatar = p.Avatar,
                        Country = p.Country,

                        Bot = p.Bot,
                        Pp = p.Pp,
                        Rank = p.Rank,
                        CountryRank = p.CountryRank,
                        Role = p.Role,
                        ProfileSettings = p.ProfileSettings,
                        Socials = p.Socials,
                        ClanOrder = p.ClanOrder.Length > 0 ? p.ClanOrder : string.Join(",", p.Clans.OrderBy(c => c.Id).Select(c => c.Tag)) 
                    })
                    .ToListAsync())
                    .Select(p => PostProcessSettings(p, false)),
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await players.CountAsync()
                }
            };
        }
    }
}
