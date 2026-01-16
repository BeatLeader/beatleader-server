using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace BeatLeader_Server.ControllerHelpers {
    public class PlayerControllerHelper {
        public static async Task<Player?> GetLazy(
            AppContext dbContext, 
            IConfiguration _configuration,
            string id, 
            bool addToBase = true)
        {
            Player? player = await dbContext
                .Players
                .Include(p => p.ScoreStats)
                .Include(p => p.ProfileSettings)
                .Include(p => p.Clans)
                .Include(p => p.ContextExtensions)
                .ThenInclude(ce => ce.ScoreStats)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (player == null) {
                Int64 userId = 0;
                try {
                    userId = long.Parse(id);
                } catch { 
                    return null;
                }
                if (userId > 70000000000000000) {
                    player = await PlayerUtils.GetPlayerFromSteam(_configuration.GetValue<string>("SteamApi"), id, _configuration.GetValue<string>("SteamKey"));
                    if (player == null) {
                        return null;
                    }
                } else if (userId > 1000000000000000) {
                    player = await PlayerUtils.GetPlayerFromOculus(id, _configuration.GetValue<string>("OculusToken"));
                    if (player == null)
                    {
                        return null;
                    }
                    if (addToBase) {
                        var net = new System.Net.WebClient();
                        var data = net.DownloadData(player.Avatar);
                        var readStream = new MemoryStream(data);
                        string fileName = player.Id;

                        (string extension, MemoryStream stream) = ImageUtils.GetFormatAndResize(readStream);
                        fileName += extension;
                        player.Avatar = await _configuration.GetS3Client().UploadAsset(fileName, stream);
                        readStream.Position = 0;
                        MemoryStream webpstream = ImageUtils.ResizeToWebp(readStream, 40);
                        player.WebAvatar = await _configuration.GetS3Client().UploadAsset(fileName.Replace(extension, ".webp"), webpstream);
                    }
                } else {
                    player = await GetPlayerFromBL(dbContext, id);
                    if (player == null)
                    {
                        return null;
                    }
                }

                player.Id = id;
                player.ScoreStats = new PlayerScoreStats();
                player.ContextExtensions = new List<PlayerContextExtension>();
                foreach (var context in ContextExtensions.NonGeneral) {
                    if (player.ContextExtensions.FirstOrDefault(ce => ce.Context == context) == null) {
                        player.ContextExtensions.Add(new PlayerContextExtension {
                            Context = context,
                            ScoreStats = new PlayerScoreStats(),
                            PlayerId = player.Id,
                            Country = player.Country
                        });
                    }
                }

                if (addToBase) {
                    dbContext.Players.Add(player);
                    
                    await dbContext.SaveChangesAsync();

                    PlayerSearchService.AddNewPlayer(player);
                }
            }

            return player;
        }

        public static async Task DeletePlayer(
            AppContext dbContext, 
            StorageContext storageContext,
            IAmazonS3 _s3Client,
            string playerID) {
            var bslink = await dbContext.BeatSaverLinks.FirstOrDefaultAsync(link => link.Id == playerID);
            if (bslink != null) {
                dbContext.BeatSaverLinks.Remove(bslink);
            }

            var plink = await dbContext.PatreonLinks.FirstOrDefaultAsync(l => l.Id == playerID);
            if (plink != null) {
                dbContext.PatreonLinks.Remove(plink);
            }

            var searches = await dbContext.PlayerSearches.Where(l => l.PlayerId == playerID).ToListAsync();
            foreach (var item in searches) {
                dbContext.PlayerSearches.Remove(item);
            }

            var intId = long.Parse(playerID);
            var auth = await dbContext.Auths.FirstOrDefaultAsync(l => l.Id == intId);
            if (auth != null) {
                dbContext.Auths.Remove(auth);
            }

            var scores = await dbContext
                .Scores
                .Include(s => s.ContextExtensions)
                .Include(s => s.RankVoting)
                .ThenInclude(rv => rv.Feedbacks)
                .Where(s => s.PlayerId == playerID)
                .ToListAsync();
            foreach (var score in scores) {
                string? name = score.Replay?.Split("/").LastOrDefault();
                if (name != null) {
                    await _s3Client.DeleteReplay(name);
                }

                foreach (var item in score.ContextExtensions)
                {
                    dbContext.ScoreContextExtensions.Remove(item);
                }

                if (score.RankVoting != null) {
                    if (score.RankVoting.Feedbacks != null) {
                        foreach (var item in score.RankVoting.Feedbacks)
                        {
                            score.RankVoting.Feedbacks.Remove(item);
                        }
                    }
                    dbContext.RankVotings.Remove(score.RankVoting);
                }

                dbContext.Scores.Remove(score);
            }

            var attempts = await storageContext
                .PlayerLeaderboardStats
                .Where(s => s.PlayerId == playerID)
                .ToListAsync();

            foreach (var attempt in attempts) {
                string? name = attempt.Replay?.Split("/").LastOrDefault();
                if (!string.IsNullOrEmpty(name)) {
                    if (attempt.Replay.Contains("otherreplay")) {
                        await _s3Client.DeleteOtherReplay(name);
                    } else {
                        await _s3Client.DeleteReplay(name);
                    }
                }

                storageContext.PlayerLeaderboardStats.Remove(attempt);
            }

            await storageContext.BulkSaveChangesAsync();

            var clanUpdates = await dbContext.ClanUpdates.Where(cu => cu.Player.Id == playerID).ToListAsync();
            foreach (var item in clanUpdates) {
                dbContext.ClanUpdates.Remove(item);
            }

            await dbContext.SaveChangesAsync();

            Player? player = await dbContext.Players.Where(p => p.Id == playerID)
                .Include(p => p.Socials)
                .Include(p => p.ProfileSettings)
                .Include(p => p.Changes)
                .Include(p => p.Badges).FirstOrDefaultAsync();

            if (player != null)
            {
                player.Socials = null;
                player.ProfileSettings = null;
                player.Badges = null;
                dbContext.Players.Remove(player);
                PlayerSearchService.RemovePlayer(player);
                await dbContext.SaveChangesAsync();
            }
        }

        public static IQueryable<T> Sorted<T>(
            LeaderboardContexts contexts,
            IQueryable<T> request, 
            PlayerSortBy sortBy, 
            PpType ppType,
            Order order, 
            MapsType mapsType,
            int? searchId = null) where T : IPlayer {

            var ganeralContext = contexts == LeaderboardContexts.General || contexts == LeaderboardContexts.None;
            var preSorted = ganeralContext
                ? request.OrderByDescending(p => searchId != null ? p.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                : request.OrderByDescending(p => searchId != null ? p.PlayerInstance.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0);

            if (sortBy == PlayerSortBy.DailyImprovements) {
                return preSorted.ThenOrder(order, p => p.ScoreStats.DailyImprovements);
            }

            if (sortBy == PlayerSortBy.AllContextsPp) {
                return preSorted.ThenOrder(order, p => p.AllContextsPp);
            }

            if (sortBy == PlayerSortBy.Prestige) {
                return preSorted.ThenOrder(order, p => p.Prestige);
            }

            if (sortBy == PlayerSortBy.Experience) {
                return preSorted.ThenOrder(order, p => p.Experience);
            }

            if (sortBy == PlayerSortBy.Level) {
                return preSorted.ThenOrder(order, p => p.Level);
            }

            if (sortBy == PlayerSortBy.ScorePlaytime) {
                return preSorted.ThenOrder(order, p => p.ScoreStats.ScorePlaytime);
            }

            if (sortBy == PlayerSortBy.SteamPlaytime) {
                return preSorted.ThenOrder(order, p => p.ScoreStats.SteamPlaytimeForever);
            }

            if (sortBy == PlayerSortBy.SteamPlaytime2Weeks) {
                return preSorted.ThenOrder(order, p => p.ScoreStats.SteamPlaytime2Weeks);
            }

            if (sortBy == PlayerSortBy.Pp) {
                switch (ppType)
                {
                    case PpType.Acc:
                        request = preSorted.ThenOrder(order, p => p.AccPp);
                        break;
                    case PpType.Tech:
                        request = preSorted.ThenOrder(order, p => p.TechPp);
                        break;
                    case PpType.Pass:
                        request = preSorted.ThenOrder(order, p => p.PassPp);
                        break;
                    default:
                        request = preSorted.ThenOrder(order, p => p.Pp);
                        break;
                }
            } else if (sortBy == PlayerSortBy.TopPp) {
                switch (ppType)
                {
                    case PpType.Acc:
                        request = preSorted.ThenOrder(order, p => p.ScoreStats.TopAccPP);
                        break;
                    case PpType.Tech:
                        request = preSorted.ThenOrder(order, p => p.ScoreStats.TopTechPP);
                        break;
                    case PpType.Pass:
                        request = preSorted.ThenOrder(order, p => p.ScoreStats.TopPassPP);
                        break;
                    default:
                        request = preSorted.ThenOrder(order, p => p.ScoreStats.TopPp);
                        break;
                }
            }

            switch (mapsType)
            {
                case MapsType.Ranked:
                    switch (sortBy)
                    {
                        case PlayerSortBy.Name:
                            if (ganeralContext) {
                                request = preSorted.ThenOrder(order, p => p.Name);
                            } else {
                                request = preSorted.ThenOrder(order, p => p.PlayerInstance.Name);
                            }
                            break;
                        case PlayerSortBy.Rank:
                            request = request
                                .Where(p => p.ScoreStats.AverageRankedRank != 0)
                                .OrderByDescending(p => searchId != null ? p.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                                .ThenOrder(order.Reverse(), p => Math.Round(p.ScoreStats.AverageRankedRank))
                                .ThenOrder(order, p => p.ScoreStats.RankedPlayCount); 
                            break;
                        case PlayerSortBy.Acc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AverageRankedAccuracy);
                            break;
                        case PlayerSortBy.WeightedAcc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AverageWeightedRankedAccuracy);
                            break;
                        case PlayerSortBy.Top1Count:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.RankedTop1Count);
                            break;
                        case PlayerSortBy.Top1Score:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.RankedTop1Score);
                            break;
                        case PlayerSortBy.WeightedRank:
                            request = request
                                .Where(p => p.ScoreStats != null && p.ScoreStats.AverageWeightedRankedRank != 0)
                                .OrderByDescending(p => searchId != null ? p.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                                .ThenOrder(order.Reverse(), p => p.ScoreStats.AverageWeightedRankedRank);
                            break;
                        case PlayerSortBy.TopAcc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TopRankedAccuracy);
                            break;
                        case PlayerSortBy.Hmd:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TopHMD);
                            break;
                        case PlayerSortBy.PlayCount:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.RankedPlayCount);
                            break;
                        case PlayerSortBy.Score:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TotalRankedScore);
                            break;
                        case PlayerSortBy.Lastplay:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.LastRankedScoreTime);
                            break;
                        case PlayerSortBy.MaxStreak:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.RankedMaxStreak);
                            break;
                        case PlayerSortBy.ReplaysWatched:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched);
                            break;
                        default:
                            break;
                    }
                    break;
                case MapsType.Unranked:
                    switch (sortBy)
                    {
                        case PlayerSortBy.Name:
                            if (ganeralContext) {
                                request = preSorted.ThenOrder(order, p => p.Name);
                            } else {
                                request = preSorted.ThenOrder(order, p => p.PlayerInstance.Name);
                            }
                            break;
                        case PlayerSortBy.Rank:
                            request = request
                                .Where(p => p.ScoreStats.AverageUnrankedRank != 0)
                                .OrderByDescending(p => searchId != null ? p.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                                .ThenOrder(order.Reverse(), p => Math.Round(p.ScoreStats.AverageUnrankedRank))
                                .ThenOrder(order, p => p.ScoreStats.UnrankedPlayCount);
                            break;
                        case PlayerSortBy.Acc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AverageUnrankedAccuracy);
                            break;
                        case PlayerSortBy.WeightedAcc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AverageUnrankedAccuracy);
                            break;
                        case PlayerSortBy.Top1Count:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.UnrankedTop1Count);
                            break;
                        case PlayerSortBy.Top1Score:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.UnrankedTop1Score);
                            break;
                        
                        case PlayerSortBy.TopAcc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TopUnrankedAccuracy);
                            break;
                        case PlayerSortBy.Hmd:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TopHMD);
                            break;
                        case PlayerSortBy.PlayCount:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.UnrankedPlayCount);
                            break;
                        case PlayerSortBy.Score:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TotalUnrankedScore);
                            break;
                        case PlayerSortBy.Lastplay:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.LastUnrankedScoreTime);
                            break;
                        case PlayerSortBy.MaxStreak:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.UnrankedMaxStreak);
                            break;
                        case PlayerSortBy.ReplaysWatched:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched);
                            break;
                        default:
                            break;
                    }
                    break;
                case MapsType.All:
                    switch (sortBy)
                    {
                        case PlayerSortBy.Name:
                            if (ganeralContext) {
                                request = preSorted.ThenOrder(order, p => p.Name);
                            } else {
                                request = preSorted.ThenOrder(order, p => p.PlayerInstance.Name);
                            }
                            break;
                        case PlayerSortBy.Rank:
                            request = request
                                .Where(p => p.ScoreStats.AverageRank != 0)
                                .OrderByDescending(p => searchId != null ? p.Searches.FirstOrDefault(s => s.SearchId == searchId)!.Score : 0)
                                .ThenOrder(order.Reverse(), p => Math.Round(p.ScoreStats.AverageRank))
                                .ThenOrder(order, p => p.ScoreStats.TotalPlayCount);
                            break;
                        case PlayerSortBy.Top1Count:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.Top1Count);
                            break;
                        case PlayerSortBy.Top1Score:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.Top1Score);
                            break;
                        case PlayerSortBy.Acc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AverageAccuracy);
                            break;
                        case PlayerSortBy.WeightedAcc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AverageWeightedRankedAccuracy);
                            break;
                        case PlayerSortBy.TopAcc:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TopAccuracy);
                            break;
                        case PlayerSortBy.Hmd:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TopHMD);
                            break;
                        case PlayerSortBy.PlayCount:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TotalPlayCount);
                            break;
                        case PlayerSortBy.Score:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.TotalScore);
                            break;
                        case PlayerSortBy.Lastplay:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.LastScoreTime);
                            break;
                        case PlayerSortBy.MaxStreak:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.MaxStreak);
                            break;
                        case PlayerSortBy.Timing:
                            request = preSorted.ThenOrder(order, p => (p.ScoreStats.AverageLeftTiming + p.ScoreStats.AverageRightTiming) / 2);
                            break;
                        case PlayerSortBy.ReplaysWatched:
                            request = preSorted.ThenOrder(order, p => p.ScoreStats.AnonimusReplayWatched + p.ScoreStats.AuthorizedReplayWatched);
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    break;
            }

            return request;
        }

        public static async Task<Player?> GetPlayerFromBL(AppContext dbContext, string playerID)
        {
            AuthInfo? authInfo = await dbContext.Auths.FirstOrDefaultAsync(el => el.Id.ToString() == playerID);

            if (authInfo == null) return null;

            Player result = new Player();
            result.Id = playerID;
            result.Name = authInfo.Login;
            result.Platform = "oculus";
            result.CreatedAt = Time.UnixNow();
            result.SetDefaultAvatar();

            return result;
        }

        public class FollowerCounter {
            public string Id { get; set; }
            public int Count { get; set; }
            public bool Mutual { get; set; }
        }

        public static async Task<(List<string>, List<FollowerCounter>)> GetPlayerFollowers(AppContext dbContext, string id, int page, int count) {
            // Step 1: Get all follower IDs
            var allFollowersIds = await dbContext
                .Friends
                .Where(f => !f.HideFriends && f.Friends.Any(p => !p.Banned && p.Id == id))
                .Select(f => f.Id)
                .ToListAsync();

            var following = await dbContext
                .Friends
                .Where(f => !f.HideFriends && f.Id == id)
                .Select(f => f.Friends.Where(f => !f.Banned).Select(ff => ff.Id).ToList())
                .FirstOrDefaultAsync() ?? new List<string> { };

            if (!allFollowersIds.Any()) {
                return (new List<string>(), new List<FollowerCounter>());
            }

            // Step 2: Get following information for all followers
            var followersWithFollowings = await dbContext
                .Friends
                .Where(f => !f.HideFriends && allFollowersIds.Contains(f.Id))
                .Select(f => new {
                    FollowerId = f.Id,
                    FollowingIds = f.Friends.Where(f => !f.Banned).Select(ff => ff.Id).ToList()
                })
                .ToListAsync();

            var bannedIds = await dbContext.Players.Where(p => p.Banned).Select(p => p.Id).ToListAsync();

            // Step 3: Calculate the count of common followings
            var followersCounts = followersWithFollowings.Select(f => new FollowerCounter {
                Id = f.FollowerId,
                Count = f.FollowingIds.Intersect(allFollowersIds).Count(),
                Mutual = following.Contains(f.FollowerId)
            }).Where(f => !bannedIds.Contains(f.Id)).ToList();

            // Step 4: Order and paginate the results
            var pagedFollowersCounts = followersCounts
                .OrderByDescending(f => f.Count)
                .Skip((page - 1) * count)
                .Take(count)
                .ToList();

            return (followersCounts.Select(f => f.Id).ToList(), pagedFollowersCounts);
        }

        public static async Task<(List<string>, List<FollowerCounter>)> GetPlayerFollowing(AppContext dbContext, string id, string currentID, int page, int count) {
            // Step 1: Get all following IDs
            List<string> allFollowingIds = await dbContext
                .Friends
                .Where(f => (!f.HideFriends || currentID == id) && f.Id == id)
                .SelectMany(f => f.Friends.Where(f => !f.Banned).Select(f => f.Id))
                .ToListAsync() ?? new List<string>();

            if (!allFollowingIds.Any()) {
                return (new List<string>(), new List<FollowerCounter>());
            }

            // Step 2: Get followers count for each following ID
            var followersCounts = await dbContext
                .Friends
                .Where(f => !f.HideFriends)
                .SelectMany(f => f.Friends.Where(f => !f.Banned))
                .Where(f => allFollowingIds.Contains(f.Id))
                .GroupBy(f => f.Id)
                .Select(g => new FollowerCounter {
                    Id = g.Key,
                    Count = g.Count(),
                    Mutual = dbContext.Friends.FirstOrDefault(f => !f.HideFriends && f.Id == g.Key && f.Friends.FirstOrDefault(ff => !ff.Banned && ff.Id == id) != null) != null
                })
                .ToListAsync();

            // Step 3: Order and paginate the results
            var pagedFollowersCounts = followersCounts
                .OrderByDescending(f => f.Count)
                .Skip((page - 1) * count)
                .Take(count)
                .ToList();

            return (allFollowingIds, pagedFollowersCounts);
        }

        public static async Task<MapperStatus> GetMapperStatus(AppContext _context, string role, UserDetail mapper) {
            var result = MapperStatus.None;
            if (mapper.VerifiedMapper) {
                result |= MapperStatus.Verified;
            }
            if (mapper.Curator == true) {
                result |= MapperStatus.Curator;
            }
            if (await _context.Leaderboards.AnyAsync(lb => lb.Song.MapperId == mapper.Id && lb.Difficulty.Status == DifficultyStatus.ranked)) {
                result |= MapperStatus.Ranked;
            }
            if (Player.RoleIsAnyTeam(role)) {
                result |= MapperStatus.Team;
            }

            return result;
        }
    }
}
