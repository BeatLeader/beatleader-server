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

            return await query.AsNoTracking()
                    .Include(c => c.FeaturedPlaylists)
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
                        FeaturedPlaylists = c.FeaturedPlaylists.Select(fp => new FeaturedPlaylistResponse {
                            Id = fp.Id,
                            PlaylistLink = fp.PlaylistLink,
                            Cover = fp.Cover,
                            Title = fp.Title,
                            Description = fp.Description,

                            Owner = fp.Owner,
                            OwnerCover = fp.OwnerCover,
                            OwnerLink = fp.OwnerLink,
                        }).ToList(),
                        ClanRankingDiscordHook = currentID == c.LeaderID ? c.ClanRankingDiscordHook : null,
                        PlayerChangesCallback = currentID == c.LeaderID ? c.PlayerChangesCallback : null
                    })
                    .FirstOrDefaultAsync();
        }

        public static async Task<ActionResult<ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>>> PopulateClanWithMaps(
            AppContext _context,
            ClanResponseFull clan,
            string? currentID,
            [FromQuery] int page = 1,
            [FromQuery] int count = 10,
            [FromQuery] ClanMapsSortBy sortBy = ClanMapsSortBy.Pp,
            [FromQuery] LeaderboardContexts leaderboardContext = LeaderboardContexts.General,
            [FromQuery] Order order = Order.Desc)
        {
            var rankings = _context
                .ClanRanking
                .AsNoTracking()
                .Include(p => p.Leaderboard)
                .ThenInclude(l => l.Difficulty)
                .Include(p => p.Leaderboard)
                .ThenInclude(l => l.Song)
                .ThenInclude(s => s.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .Where(p => p.Leaderboard.Difficulty.Status == DifficultyStatus.ranked && p.ClanId == clan.Id);

            switch (sortBy)
            {
                case ClanMapsSortBy.Pp:
                    rankings = rankings.Order(order, t => t.Pp);
                    break;
                case ClanMapsSortBy.Acc:
                    rankings = rankings.Order(order, t => t.AverageAccuracy);
                    break;
                case ClanMapsSortBy.Rank:
                    rankings = rankings.Order(order, t => t.Rank);
                    break;
                case ClanMapsSortBy.Date:
                    rankings = rankings.Order(order, t => t.LastUpdateTime);
                    break;
                case ClanMapsSortBy.Tohold:
                    rankings = rankings
                        .Where(cr => cr.Rank == 1 && cr.Leaderboard.ClanRanking.Count > 1)
                        .Order(
                            order.Reverse(), 
                            t => t.Pp - t
                                    .Leaderboard
                                    .ClanRanking
                                    .Where(cr => cr.ClanId != clan.Id && cr.Rank == 2)
                                    .Select(cr => cr.Pp)
                                    .First());
                    break;
                case ClanMapsSortBy.Toconquer:
                    rankings = rankings
                        .Where(cr => cr.Rank != 1 || cr.Leaderboard.ClanRankingContested)
                        .Order(
                            order, 
                            t => t.Pp - t
                                    .Leaderboard
                                    .ClanRanking
                                    .Where(cr => cr.ClanId != clan.Id && cr.Rank == 1)
                                    .Select(cr => cr.Pp)
                                    .First());
                    break;
                default:
                    break;
            }

            var rankingList = await rankings
            
            .Skip((page - 1) * count)
            .Take(count)
            .Select(cr => new ClanRankingResponse {
                Id = cr.Id,
                Clan = cr.Clan == null ? null : new ClanResponseFull {
                    Id = cr.Clan.Id,
                    Name = cr.Clan.Name,
                    Color = cr.Clan.Color,
                    Icon = cr.Clan.Icon,
                    Tag = cr.Clan.Tag,
                    LeaderID = cr.Clan.LeaderID,
                    Description = cr.Clan.Description,
                    Pp = cr.Clan.Pp,
                    Rank = cr.Clan.Rank
                },
                LastUpdateTime = cr.LastUpdateTime,
                AverageRank = cr.AverageRank,
                Pp = cr.Pp,
                AverageAccuracy = cr.AverageAccuracy,
                TotalScore = cr.TotalScore,
                LeaderboardId = cr.LeaderboardId,
                Leaderboard = new LeaderboardResponse {
                    Id = cr.Leaderboard.Id,
                    Song = new SongResponse {
                        Id = cr.Leaderboard.Song.Id,
                        Hash = cr.Leaderboard.Song.Hash,
                        Name = cr.Leaderboard.Song.Name,
                        SubName = cr.Leaderboard.Song.SubName,
                        Author = cr.Leaderboard.Song.Author,
                        Mapper = cr.Leaderboard.Song.Mapper,
                        CoverImage  = cr.Leaderboard.Song.CoverImage,
                        FullCoverImage = cr.Leaderboard.Song.FullCoverImage,
                    },
                    Difficulty = new DifficultyResponse {
                        Id = cr.Leaderboard.Difficulty.Id,
                        Value = cr.Leaderboard.Difficulty.Value,
                        Mode = cr.Leaderboard.Difficulty.Mode,
                        DifficultyName = cr.Leaderboard.Difficulty.DifficultyName,
                        ModeName = cr.Leaderboard.Difficulty.ModeName,
                        Status = cr.Leaderboard.Difficulty.Status,
                        ModifierValues = cr.Leaderboard.Difficulty.ModifierValues,
                        ModifiersRating = cr.Leaderboard.Difficulty.ModifiersRating,

                        Stars  = cr.Leaderboard.Difficulty.Stars,
                        PredictedAcc  = cr.Leaderboard.Difficulty.PredictedAcc,
                        PassRating  = cr.Leaderboard.Difficulty.PassRating,
                        AccRating  = cr.Leaderboard.Difficulty.AccRating,
                        TechRating  = cr.Leaderboard.Difficulty.TechRating,
                        Type  = cr.Leaderboard.Difficulty.Type,
                        MaxScore = cr.Leaderboard.Difficulty.MaxScore,
                    },
                    Plays = cr.Leaderboard.Plays,
                },
                Rank = cr.Rank,
                MyScore = currentID == null ? null : cr.Leaderboard.Scores.Where(s => s.PlayerId == currentID && s.ValidContexts.HasFlag(leaderboardContext) && !s.Banned).Select(s => new ScoreResponseWithAcc {
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
                    ReplaysWatched = s.AuthorizedReplayWatched + s.AnonimusReplayWatched,
                    LeaderboardId = s.LeaderboardId,
                    Platform = s.Platform,
                    Weight = s.Weight,
                    AccLeft = s.AccLeft,
                    AccRight = s.AccRight,
                    MaxStreak = s.MaxStreak,
                }).FirstOrDefault(),
            })
            .TagWithCallSite()
            .AsSplitQuery()
            .ToListAsync();

            if (sortBy == ClanMapsSortBy.Tohold || sortBy == ClanMapsSortBy.Toconquer) {
                var pps = await rankings
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(t => new { t.LeaderboardId, Pp = t.Pp, SecondPp = t
                        .Leaderboard
                        .ClanRanking
                        .Where(cr => cr.ClanId != clan.Id && (cr.Rank == (sortBy == ClanMapsSortBy.Tohold ? 2 : 1)))
                        .Select(cr => cr.Pp)
                        .FirstOrDefault()
                    })
                    .TagWithCallSite()
                    .AsSplitQuery()
                    .ToListAsync();

                foreach (var item in pps)
                {
                    rankingList.First(cr => cr.LeaderboardId == item.LeaderboardId).Pp = item.Pp - item.SecondPp;
                }
            }

            return new ResponseWithMetadataAndContainer<ClanRankingResponse, ClanResponseFull>
            {
                Container = clan,
                Data = rankingList,
                Metadata = new Metadata
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await rankings.CountAsync()
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
                players = players
                        .Where(p => !p.Banned && 
                        p.Clans.OrderBy(c => p.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id)
                            .Take(1)
                            .Any(c => c.Id == clan.Id));
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
                    .TagWithCallSite()
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
