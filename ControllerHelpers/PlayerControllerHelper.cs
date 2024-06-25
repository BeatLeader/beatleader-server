using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

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
                        ;
                        player.Avatar = await _configuration.GetS3Client().UploadAsset(fileName, stream);
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

            Player? player = await dbContext.Players.Where(p => p.Id == playerID)
                .Include(p => p.Socials)
                .Include(p => p.ProfileSettings)
                .Include(p => p.History)
                .Include(p => p.Changes).FirstOrDefaultAsync();

            if (player != null)
            {
                player.Socials = null;
                player.ProfileSettings = null;
                player.History = null;
                dbContext.Players.Remove(player);
                await dbContext.SaveChangesAsync();
            }
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
        }

        public static async Task<(List<string>, List<FollowerCounter>)> GetPlayerFollowers(AppContext dbContext, string id, int page, int count) {
            var allFollowersIds = await dbContext
                .Friends
                .Where(f => !f.HideFriends && f.Friends.FirstOrDefault(p => p.Id == id) != null)
                .Select(f => f.Id)
                .ToListAsync();

            var followersCounts = new List<FollowerCounter>();
            foreach (var followerId in allFollowersIds) {
                var followingOfFollowers = await dbContext
                    .Friends
                    .Where(f => !f.HideFriends && f.Id == followerId)
                    .Select(f => f.Friends.Select(ff => ff.Id))
                    .FirstOrDefaultAsync();
                if (followingOfFollowers == null) continue;

                followersCounts.Add(new FollowerCounter {
                    Id = followerId,
                    Count = allFollowersIds.Intersect(followingOfFollowers).Count()
                });
            }

            return (allFollowersIds, followersCounts.OrderByDescending(f => f.Count).Skip((page - 1) * count).Take(count).ToList());
        }

        public static async Task<(List<string>, List<FollowerCounter>)> GetPlayerFollowing(AppContext dbContext, string id, string currentID, int page, int count) {
            List<string> allFollowingIds = await dbContext
                .Friends
                .Where(f => (!f.HideFriends || currentID == id) && f.Id == id)
                .Select(f => f.Friends.Select(f => f.Id).ToList())
                .FirstOrDefaultAsync() ?? new List<string>();

            var following = await dbContext
                .Friends
                .Where(f => allFollowingIds.Contains(f.Id))
                .OrderByDescending(f => f.Friends.Count)
                .Skip((page - 1) * count)
                .Take(count)
                .Select(f => new FollowerCounter {
                    Id = f.Id,
                    Count = f.Friends.Count
                })
                .ToListAsync();

            return (allFollowingIds, following);
        }
    }
}
