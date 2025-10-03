using System.Dynamic;
using System.Net;
using BeatLeader_Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Microsoft.EntityFrameworkCore;
using BeatLeader_Server.Bot;
using Discord;
using static BeatLeader_Server.Utils.ResponseUtils;
using BeatLeader_Server.Extensions;

namespace BeatLeader_Server.Utils
{
    public static class PlayerUtils
    {
        public static async Task RecalculatePPAndRankFast(
            this AppContext dbcontext, 
            Player player,
            LeaderboardContexts leaderboardContexts)
        {
            if (leaderboardContexts.HasFlag(LeaderboardContexts.General)) {
                await dbcontext.RecalculatePPAndRankFastGeneral(player);
            }

            foreach (var context in ContextExtensions.NonGeneral) {
                if (leaderboardContexts.HasFlag(context)) {
                    await dbcontext.RecalculatePPAndRankFastContext(context, player);
                }
            }
        }

        public static async Task RecalculatePPAndRankFastGeneral(
            this AppContext context, 
            Player player)
        {
            float oldPp = player.Pp;

            var rankedScores = await context
                .Scores
                .Where(s => s.ValidContexts.HasFlag(LeaderboardContexts.General) && s.PlayerId == player.Id && s.Pp != 0 && !s.Banned && !s.Qualification)
                .AsNoTracking()
                .OrderByDescending(s => s.Pp)
                .Select(s => new Score { 
                    Id = s.Id, 
                    Accuracy = s.Accuracy, 
                    Rank = s.Rank, 
                    Pp = s.Pp, 
                    AccPP = s.AccPP, 
                    PassPP = s.PassPP, 
                    TechPP = s.TechPP, 
                    Weight = s.Weight 
                })
                .ToListAsync();

            float resultPP = 0f;
            float accPP = 0f;
            float techPP = 0f;
            float passPP = 0f;
            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                float weight = MathF.Pow(0.965f, i);
                s.Weight = weight;
                resultPP += s.Pp * weight;
                accPP += s.AccPP * weight;
                techPP += s.TechPP * weight;
                passPP += s.PassPP * weight;
            }

            await context.BulkUpdateAsync(rankedScores, options => options.ColumnInputExpression = c => new { c.Weight });

            player.Pp = resultPP;
            player.AccPp = accPP;
            player.TechPp = techPP;
            player.PassPp = passPP;

            int rankOffset = 0;

            if (oldPp > resultPP) {
                var temp = oldPp;
                oldPp = resultPP;
                resultPP = temp;
                rankOffset = -1;
            }

            var rankedPlayers = (await context
                .Players
                .Where(t => t.Pp > 0 && t.Pp >= oldPp && t.Pp != 0 && t.Pp <= resultPP && t.Id != player.Id && !t.Banned)
                .AsNoTracking()
                .Select(p => new Player {
                    Id = p.Id,
                    Rank = p.Rank,
                    Country = p.Country,
                    CountryRank = p.CountryRank,
                    Pp = p.Pp,
                })
                .ToListAsync())
                .Append(new Player {
                    Id = player.Id,
                    Rank = player.Rank,
                    Country = player.Country,
                    CountryRank = player.CountryRank,
                    Pp = player.Pp,
                })
                .OrderByDescending(t => t.Pp)
                .ToList();

            if (rankedPlayers.Count() > 1)
            {
                var country = player.Country;
                int topRank = rankedPlayers.Where(p => p.Id != player.Id).First().Rank + rankOffset; 
                int? topCountryRank = rankedPlayers.Where(p => p.Country == country && p.Id != player.Id).FirstOrDefault()?.CountryRank + rankOffset;

                foreach ((int i, var p) in rankedPlayers.Select((value, i) => (i, value)))
                {
                    p.Rank = i + topRank;

                    if (p.Country == country && topCountryRank != null)
                    {
                        p.CountryRank = (int)topCountryRank;
                        topCountryRank++;
                    }
                }
            }

            await context.BulkUpdateAsync(rankedPlayers, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });

            var updatedPlayer = rankedPlayers.FirstOrDefault(p => p.Id == player.Id);
            if (updatedPlayer != null) {
                player.Rank = updatedPlayer.Rank;
            }

            var scoresForWeightedAcc = rankedScores.OrderByDescending(s => s.Accuracy).Take(100).ToList();
            var sum = 0.0f;
            var weights = 0.0f;

            for (int i = 0; i < 100; i++)
            {
                float weight = MathF.Pow(0.95f, i);
                if (i < scoresForWeightedAcc.Count)
                {
                    sum += scoresForWeightedAcc[i].Accuracy * weight;
                }

                weights += weight;
            }

            if (player.ScoreStats != null) {
                player.ScoreStats.AverageWeightedRankedAccuracy = sum / weights;
            }

            var scoresForWeightedRank = rankedScores.OrderBy(s => s.Rank).Take(100).ToList();
            sum = 0.0f;
            weights = 0.0f;

            for (int i = 0; i < 100; i++)
            {
                float weight = MathF.Pow(1.05f, i);
                if (i < scoresForWeightedRank.Count)
                {
                    sum += scoresForWeightedRank[i].Rank * weight;
                }
                else {
                    sum += i * 10 * weight;
                }

                weights += weight;
            }

            if (player.ScoreStats != null) {
                player.ScoreStats.AverageWeightedRankedRank = sum / weights;
            }
        }

        public static async Task RecalculatePPAndRankFastContext(
            this AppContext dbContext, 
            LeaderboardContexts context,
            Player playerProfile)
        {
            var player = playerProfile.ContextExtensions?.FirstOrDefault(c => c.Context == context);
            if (player == null) return;

            float oldPp = player.Pp;

            var rankedScores = await dbContext
                .ScoreContextExtensions
                .Where(s => s.PlayerId == player.PlayerId && s.Pp != 0 && s.ScoreId != null && s.Context == context && !s.Banned && !s.Qualification)
                .AsNoTracking()
                .OrderByDescending(s => s.Pp)
                .Select(s => new ScoreContextExtension { 
                    Id = s.Id, 
                    Accuracy = s.Accuracy, 
                    Rank = s.Rank, 
                    Pp = s.Pp, 
                    AccPP = s.AccPP, 
                    PassPP = s.PassPP, 
                    TechPP = s.TechPP, 
                    Weight = s.Weight 
                })
                .ToListAsync();

            float resultPP = 0f;
            float accPP = 0f;
            float techPP = 0f;
            float passPP = 0f;
            foreach ((int i, var s) in rankedScores.Select((value, i) => (i, value)))
            {
                float weight = MathF.Pow(0.965f, i);
                s.Weight = weight;
                resultPP += s.Pp * weight;
                accPP += s.AccPP * weight;
                techPP += s.TechPP * weight;
                passPP += s.PassPP * weight;
            }
            await dbContext.BulkUpdateAsync(rankedScores, options => options.ColumnInputExpression = c => new { c.Weight });
            player.Pp = resultPP;
            player.AccPp = accPP;
            player.TechPp = techPP;
            player.PassPp = passPP;

            int rankOffset = 0;

            if (oldPp > resultPP) {
                var temp = oldPp;
                oldPp = resultPP;
                resultPP = temp;
                rankOffset = -1;
            }

            var rankedPlayers = (await dbContext
                .PlayerContextExtensions
                .Where(t => t.Context == context && t.Pp > 0 && t.Pp >= oldPp && t.Pp <= resultPP && t.Id != player.Id && !t.Banned)
                .AsNoTracking()
                .Select(p => new PlayerContextExtension { 
                    Id = p.Id, 
                    Rank = p.Rank, 
                    Country = p.Country, 
                    CountryRank = p.CountryRank, 
                    Pp = p.Pp 
                })
                .ToListAsync())
                .Append(new PlayerContextExtension { 
                    Id = player.Id, 
                    Rank = player.Rank, 
                    Country = player.Country, 
                    CountryRank = player.CountryRank, 
                    Pp = player.Pp 
                })
                .OrderByDescending(t => t.Pp)
                .ToList();

            if (rankedPlayers.Count() > 1)
            {
                var country = player.Country;
                int topRank = rankedPlayers.Where(p => p.Id != player.Id).First().Rank + rankOffset; 
                int? topCountryRank = rankedPlayers.Where(p => p.Country == country && p.Id != player.Id).FirstOrDefault()?.CountryRank + rankOffset;

                foreach ((int i, var p) in rankedPlayers.Select((value, i) => (i, value)))
                {
                    p.Rank = i + topRank;

                    if (p.Country == country && topCountryRank != null)
                    {
                        p.CountryRank = (int)topCountryRank;
                        topCountryRank++;
                    }
                }
            }
            await dbContext.BulkUpdateAsync(rankedPlayers, options => options.ColumnInputExpression = c => new { c.Rank, c.CountryRank });

            var scoresForWeightedAcc = rankedScores.OrderByDescending(s => s.Accuracy).Take(100).ToList();
            var sum = 0.0f;
            var weights = 0.0f;

            for (int i = 0; i < 100; i++)
            {
                float weight = MathF.Pow(0.95f, i);
                if (i < scoresForWeightedAcc.Count)
                {
                    sum += scoresForWeightedAcc[i].Accuracy * weight;
                }

                weights += weight;
            }
            if (player.ScoreStats != null) {
                player.ScoreStats.AverageWeightedRankedAccuracy = sum / weights;
            }

            var scoresForWeightedRank = rankedScores.OrderBy(s => s.Rank).Take(100).ToList();
            sum = 0.0f;
            weights = 0.0f;

            for (int i = 0; i < 100; i++)
            {
                float weight = MathF.Pow(1.05f, i);
                if (i < scoresForWeightedRank.Count)
                {
                    sum += scoresForWeightedRank[i].Rank * weight;
                }
                else {
                    sum += i * 10 * weight;
                }

                weights += weight;
            }
            if (player.ScoreStats != null) {
                player.ScoreStats.AverageWeightedRankedRank = sum / weights;
            }
        }

        public static async Task RecalculateEventsPP(this AppContext context, Player player, Leaderboard leaderboard)
        {
            int timestamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var events = await context.EventRankings
                .Where(ev => ev.EndDate >= timestamp && ev.Leaderboards.Contains(leaderboard))
                .Include(ev => ev.Players)
                .ToListAsync();
            if (events.Count() == 0) {
                return;
            }

            foreach (var eventRanking in events)
            {
                var ranked = await context.Leaderboards.Where(lb => lb.Events.FirstOrDefault(e => e.Id == eventRanking.Id) != null)
                    .Select(lb => new { Pp = lb.Scores.Where(s =>
                        s.PlayerId == player.Id &&
                        s.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                        s.Pp != 0 &&
                        !s.Banned).Count() > 0 ? lb.Scores.Where(s =>
                        s.PlayerId == player.Id &&
                        s.ValidContexts.HasFlag(LeaderboardContexts.General) &&
                        s.Pp != 0 &&
                        !s.Banned).First().Pp : 0 } )
                    .OrderByDescending(s => s.Pp).ToListAsync();
                if (eventRanking.Players == null)
                {
                    eventRanking.Players = new List<EventPlayer>();
                }

                var eventPlayer = eventRanking.Players.Where(p => p.PlayerId == player.Id).FirstOrDefault();

                if (eventPlayer == null)
                {
                    eventPlayer = new EventPlayer
                    {
                        PlayerId = player.Id,
                        Country = player.Country,
                        EventRankingId = eventRanking.Id,
                        EventName = eventRanking.Name,
                        PlayerName = player.Name
                    };
                    eventRanking.Players.Add(eventPlayer);
                    await context.SaveChangesAsync();
                }

                if (player.EventsParticipating == null || player.EventsParticipating.FirstOrDefault(e => e.EventRankingId == eventRanking.Id) == null)
                {
                    if (player.EventsParticipating == null)
                    {
                        player.EventsParticipating = new List<EventPlayer> { eventPlayer };
                    }
                    else
                    {
                        player.EventsParticipating.Add(eventPlayer);
                    }
                }

                if (eventRanking.EventType == EventRankingType.Playlist) {
                    float resultPP = 0f;
                    foreach ((int i, var s) in ranked.Select((value, i) => (i, value)))
                    {
                        resultPP += s.Pp * MathF.Pow(0.925f, i);
                    }

                    eventPlayer.Pp = resultPP;
                }

                var players = eventRanking.Players.OrderByDescending(t => t.Pp).ToList();
                Dictionary<string, int> countries = new Dictionary<string, int>();

                foreach ((int i, EventPlayer p) in players.Select((value, i) => (i, value)))
                {
                    p.Rank = i + 1;
                    if (!countries.ContainsKey(p.Country))
                    {
                        countries[p.Country] = 1;
                    }

                    p.CountryRank = countries[p.Country];
                    countries[p.Country]++;
                }
            }
        }

        public static string[] AllowedCountries() {
            return new string[] { "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM", "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW", "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI", "VN", "VU", "WF", "WS", "XK", "YE", "YT", "ZA", "ZM", "ZW" };
        }

        public static async Task<Player?> GetPlayerFromSteam(string steamUrl, string playerID, string steamKey)
        {
            dynamic? info = await GetPlayer($"{steamUrl}/ISteamUser/GetPlayerSummaries/v0002/?key={steamKey}&steamids={playerID}");

            if (info == null || info.response.players.Count == 0) return null;

            dynamic playerInfo = info.response.players[0];
            Player result = new Player();
            result.Name = playerInfo.personaname;
            result.Avatar = playerInfo.avatarfull;
            result.CreatedAt = Time.UnixNow();
            result.Platform = "steam";
            result.SanitizeName();
            if (ExpandantoObject.HasProperty(playerInfo, "loccountrycode"))
            {
                result.Country = playerInfo.loccountrycode;
            }
            else
            {
                result.Country = "not set";
            }
            result.ExternalProfileUrl = playerInfo.profileurl;

            return result;
        }

        public static async Task<(long, long)?> GetPlaytimeFromSteam(string steamUrl, string playerID, string steamKey)
        {
            dynamic? info = await GetPlayer($"{steamUrl}/IPlayerService/GetOwnedGames/v0001/?key={steamKey}&format=json&input_json={{\"steamid\": {playerID}, \"appids_filter\": [ 620980 ]}}");

            if (info == null || !ExpandantoObject.HasProperty(info.response, "game_count") || info.response.game_count == 0) return null;

            dynamic gameInfo = info.response.games[0];

            long playtime_2weeks = 0;
            if (ExpandantoObject.HasProperty(gameInfo, "playtime_2weeks")) {
                playtime_2weeks = gameInfo.playtime_2weeks;
            }

            long playtime_forever = 0;
            if (ExpandantoObject.HasProperty(gameInfo, "playtime_forever")) {
                playtime_forever = gameInfo.playtime_forever;
            }

            return (playtime_2weeks, playtime_forever);
        }

        public static async Task<Player?> GetPlayerFromOculus(string playerID, string token)
        {
            dynamic? info = await GetPlayer("https://graph.oculus.com/" + playerID + "?fields=id,alias,avatar_v2{avatar_image{uri}}", token);

            if (info == null) return null;

            Player result = new Player();
            result.Name = info.alias;
            result.Id = info.id;
            result.CreatedAt = Time.UnixNow();
            result.Platform = "oculuspc";
            result.Country = "not set";
            result.SanitizeName();
            if (ExpandantoObject.HasProperty(info, "avatar_v2") && ExpandantoObject.HasProperty(info.avatar_v2, "avatar_image"))
            {
                result.Avatar = info.avatar_v2.avatar_image.uri;
            }
            else
            {
                result.SetDefaultAvatar();
            }

            return result;
        }

        public static async Task<UserDetail?> GetMapperFromBeatSaver(string playerID)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.beatsaver.com/users/id/" + playerID);
            return await request.DynamicResponse<UserDetail>();
        }

        public static async Task<(Player?, UserDetail?)> GetPlayerFromBeatSaver(string playerID)
        {
            var user = await GetMapperFromBeatSaver(playerID);

            if (user == null) return (null, null);

            Player result = new Player();
            result.Name = user.Name;
            result.Id = (30000000 + user.Id) + "";
            result.MapperId = user.Id;
            result.CreatedAt = Time.UnixNow();
            result.Platform = "beatsaver";
            result.Country = "not set";
            result.Avatar = user.Avatar;
            result.ExternalProfileUrl = "https://beatsaver.com/profile/" + playerID;
            result.SanitizeName();

            return (result, user);
        }

        public static Task<dynamic?> GetPlayer(string url, string? token = null)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            if (token != null) {
                request.Headers.Add("Authorization", "Bearer " + token);
            }
            request.Proxy = null;

            WebResponse? response = null;
            dynamic? song = null;
            var stream =
            Task<(WebResponse?, dynamic?)>.Factory.FromAsync(request.BeginGetResponse, result =>
            {
                try
                {
                    response = request.EndGetResponse(result);
                }
                catch (Exception e)
                {
                    song = null;
                }

                return (response, song);
            }, request);

            return stream.ContinueWith(t => ReadPlayerFromResponse(t.Result));
        }

        private static dynamic? ReadPlayerFromResponse((WebResponse?, dynamic?) response)
        {
                if (response.Item1 != null)
                {
                    using (Stream responseStream = response.Item1.GetResponseStream())
                    using (StreamReader reader = new StreamReader(responseStream))
                    {
                        string results = reader.ReadToEnd();
                        if (string.IsNullOrEmpty(results))
                        {
                            return null;
                        }

                        return JsonConvert.DeserializeObject<ExpandoObject>(results, new ExpandoObjectConverter());
                    }
                }
                else
                {
                    return response.Item2;
                }
        }

        public static void UpdateBoosterRole(Player player, string? role)
        {
            player.Role = string.Join(",", player.Role.Split(",").Where(r => r != "booster"));
            if (role != null) {
                player.Role += "," + role;
            }
        }

        public static async Task RefreshBoosterRole(AppContext _context, Player? player, ulong userId) {
            var user = await ((IGuild)BotService.Client.GetGuild(BotService.BLServerID)).GetUserAsync(userId, CacheMode.AllowDownload);

            if (user != null && player != null) {
                if (user.RoleIds.Contains(BotService.BLBoosterRoleID)) {
                    UpdateBoosterRole(player, "booster");
                } else {
                    UpdateBoosterRole(player, null);
                }
                await _context.SaveChangesAsync();
            }
        }

        public static async Task UpdateBoosterRole(AppContext _context, ulong userId) {
            var discordLink = await _context.DiscordLinks.FirstOrDefaultAsync(d => d.DiscordId == userId.ToString());
            if (discordLink != null) {
                var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == discordLink.Id);
                await RefreshBoosterRole(_context, player, userId);
            }
        }

        public static void UpdateScoreStats(
            this PlayerScoreStats playerScoreStats, 
            IScore? currentScore, 
            IScore resultScore,
            IPlayer player,
            bool isRanked) {
            if (currentScore != null)
            {
                if (!currentScore.IgnoreForStats) {
                    playerScoreStats.TotalScore -= currentScore.ModifiedScore;

                    if (playerScoreStats.TotalPlayCount == 1)
                    {
                        playerScoreStats.AverageAccuracy = 0.0f;
                    }
                    else
                    {
                        playerScoreStats.AverageAccuracy = MathUtils.RemoveFromAverage(playerScoreStats.AverageAccuracy, playerScoreStats.TotalPlayCount, currentScore.Accuracy);
                    }

                        
                    if (isRanked)
                    {
                        float oldAverageAcc = playerScoreStats.AverageRankedAccuracy;
                        if (playerScoreStats.RankedPlayCount == 1)
                        {
                            playerScoreStats.AverageRankedAccuracy = 0.0f;
                        }
                        else
                        {
                            playerScoreStats.AverageRankedAccuracy = MathUtils.RemoveFromAverage(playerScoreStats.AverageRankedAccuracy, playerScoreStats.RankedPlayCount, currentScore.Accuracy);
                        }

                        switch (currentScore.Accuracy)
                        {
                            case > 0.95f:
                                playerScoreStats.SSPPlays--;
                                break;
                            case > 0.9f:
                                playerScoreStats.SSPlays--;
                                break;
                            case > 0.85f:
                                playerScoreStats.SPPlays--;
                                break;
                            case > 0.8f:
                                playerScoreStats.SPlays--;
                                break;
                            default:
                                playerScoreStats.APlays--;
                                break;
                        }
                    }
                }
            }
            else
            {
                if (isRanked)
                {
                    playerScoreStats.RankedPlayCount++;
                }
                else {
                    playerScoreStats.UnrankedPlayCount++;
                }
                playerScoreStats.TotalPlayCount++;
            }

            if (isRanked)
            {
                playerScoreStats.LastRankedScoreTime = resultScore.Timepost;
                if (playerScoreStats.FirstRankedScoreTime == 0) {
                    playerScoreStats.FirstRankedScoreTime = resultScore.Timepost;
                }
            }
            else
            {
                playerScoreStats.LastUnrankedScoreTime = resultScore.Timepost;
                if (playerScoreStats.FirstUnrankedScoreTime == 0) {
                    playerScoreStats.FirstUnrankedScoreTime = resultScore.Timepost;
                }
            }
            playerScoreStats.LastScoreTime = resultScore.Timepost;
            if (playerScoreStats.FirstScoreTime == 0) {
                playerScoreStats.FirstScoreTime = resultScore.Timepost;
            }

            if (!resultScore.IgnoreForStats) {
                playerScoreStats.TotalScore += resultScore.ModifiedScore;
                playerScoreStats.AverageAccuracy = MathUtils.AddToAverage(playerScoreStats.AverageAccuracy, playerScoreStats.TotalPlayCount, resultScore.Accuracy);
                if (resultScore.MaxStreak > playerScoreStats.MaxStreak) {
                    playerScoreStats.MaxStreak = resultScore.MaxStreak ?? 0;
                }

                if (player.Rank < playerScoreStats.PeakRank || playerScoreStats.PeakRank == 0)
                {
                    playerScoreStats.PeakRank = player.Rank;
                }
                if (isRanked)
                {
                    playerScoreStats.AverageRankedAccuracy = MathUtils.AddToAverage(playerScoreStats.AverageRankedAccuracy, playerScoreStats.RankedPlayCount, resultScore.Accuracy);
                    if (resultScore.Accuracy > playerScoreStats.TopAccuracy)
                    {
                        playerScoreStats.TopAccuracy = resultScore.Accuracy;
                    }
                    if (resultScore.Pp > playerScoreStats.TopPp)
                    {
                        playerScoreStats.TopPp = resultScore.Pp;
                    }

                    if (resultScore.BonusPp > playerScoreStats.TopBonusPP)
                    {
                        playerScoreStats.TopBonusPP = resultScore.BonusPp;
                    }

                    if (resultScore.PassPP > playerScoreStats.TopPassPP)
                    {
                        playerScoreStats.TopPassPP = resultScore.PassPP;
                    }
                    if (resultScore.AccPP > playerScoreStats.TopAccPP)
                    {
                        playerScoreStats.TopAccPP = resultScore.AccPP;
                    }
                    if (resultScore.TechPP > playerScoreStats.TopTechPP)
                    {
                        playerScoreStats.TopTechPP = resultScore.TechPP;
                    }

                    switch (resultScore.Accuracy)
                    {
                        case > 0.95f:
                            playerScoreStats.SSPPlays++;
                            break;
                        case > 0.9f:
                            playerScoreStats.SSPlays++;
                            break;
                        case > 0.85f:
                            playerScoreStats.SPPlays++;
                            break;
                        case > 0.8f:
                            playerScoreStats.SPlays++;
                            break;
                        default:
                            playerScoreStats.APlays++;
                            break;
                    }

                    if (currentScore != null) {
                        playerScoreStats.RankedImprovementsCount++;
                        playerScoreStats.TotalImprovementsCount++;
                    }
                } else {
                    if (currentScore != null) {
                        playerScoreStats.UnrankedImprovementsCount++;
                        playerScoreStats.TotalImprovementsCount++;
                    }
                }
            }
        }
    }
}

