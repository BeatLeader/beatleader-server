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
                Int64 userId = long.Parse(id);
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

        public static async Task<Player?> GetPlayerFromBL(AppContext dbContext, string playerID)
        {
            AuthInfo? authInfo = await dbContext.Auths.FirstOrDefaultAsync(el => el.Id.ToString() == playerID);

            if (authInfo == null) return null;

            Player result = new Player();
            result.Id = playerID;
            result.Name = authInfo.Login;
            result.Platform = "oculus";
            result.SetDefaultAvatar();

            return result;
        }
    }
}
