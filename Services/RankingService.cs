using BeatLeader_Server.Bot;
using BeatLeader_Server.Controllers;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Discord;
using Discord.Webhook;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services
{
    public class RankingService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public RankingService(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                DateTime today = DateTime.Today;
                int daysUntilFriday = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;

                int hoursUntil10 = (10 - (int)DateTime.Now.Hour + 24) % 24;

                if (daysUntilFriday == 0 && hoursUntil10 == 0)
                {
                    await RankMaps();
                    daysUntilFriday = 7;
                    hoursUntil10 = 0;
                }

                DateTime nextRanking = today.AddDays(daysUntilFriday).AddHours(DateTime.Now.Hour).AddHours(hoursUntil10);

                await Task.Delay(TimeSpan.FromSeconds(nextRanking.Subtract(DateTime.UtcNow).TotalSeconds), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task<(float, int)> RefreshPlayer(
                Player player,
                Score? score,
                AppContext _context)
        {

            var oldPp = player.Pp;
            var oldRank = player.Rank;

            _context.RecalculatePPAndRankFastGeneral(player);

            if (score != null && score.ScoreImprovement != null)
            {
                score.ScoreImprovement.TotalRank = player.Rank - oldRank;
                score.ScoreImprovement.TotalPp = player.Pp - oldPp;
            }

            await _context.SaveChangesAsync();

            return (player.Pp - oldPp, player.Rank - oldRank);
        }

        private async Task<(float, int)> RefreshLeaderboardPlayers(string id, AppContext _context)
        {
            Leaderboard? leaderboard = _context.Leaderboards
                .Where(p => p.Id == id)
                .Include(l => l.Scores)
                .ThenInclude(s => s.Player)
                .Include(l => l.Scores)
                .ThenInclude(s => s.ScoreImprovement)
                .FirstOrDefault();

            float pp = 0;
            int ranks = 0;

            foreach (var score in leaderboard.Scores.OrderBy(s => s.Timepost))
            {
                (float playerPP, int playerRank) = await RefreshPlayer(score.Player, score, _context);
                pp += playerPP;
                ranks += playerRank;
            }

            return (pp, ranks);
        }

        private async Task RankMaps()
        {
            DiscordWebhookClient? dsClient;
            var link = _configuration.GetValue<string?>("RankedDSHook");
            dsClient = link == null ? null : new DiscordWebhookClient(link);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _scoreController = scope.ServiceProvider.GetRequiredService<ScoreRefreshController>();

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 7;

                var leaderboards = await _context
                    .Leaderboards
                    .Where(lb =>
                        lb.Difficulty.Status == DifficultyStatus.qualified
                        && lb.Qualification.ApprovalTimeset < timeset)
                    .Include(l => l.Difficulty)
                    .ThenInclude(d => d.ModifierValues)
                    .Include(l => l.Song)
                    .Include(l => l.Qualification)
                    .Include(l => l.Changes)
                    .Include(l => l.ClanRanking)
                    .OrderBy(l => l.Difficulty.Stars)
                    .ToListAsync();

                foreach (var leaderboard in leaderboards)
                {
                    DifficultyDescription? difficulty = leaderboard.Difficulty;
                    if (leaderboard.Changes == null)
                    {
                        leaderboard.Changes = new List<LeaderboardChange>();
                    }
                    LeaderboardChange rankChange = new LeaderboardChange
                    {
                        Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds,
                        PlayerId = AdminController.RankingBotID,
                        OldRankability = difficulty.Status == DifficultyStatus.ranked ? 1 : 0,
                        OldStars = difficulty.Stars ?? 0,
                        OldType = difficulty.Type,
                        NewRankability = 1.0f,
                        NewStars = difficulty.Stars ?? 0,
                        NewType = difficulty.Type
                    };
                    leaderboard.Changes.Add(rankChange);

                    if (difficulty.Status != DifficultyStatus.ranked)
                    {
                        difficulty.RankedTime = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                    }

                    difficulty.Status = DifficultyStatus.ranked;
                    await _context.SaveChangesAsync();

                    await _scoreController.RefreshScores(leaderboard.Id);
                    await _scoreController.BulkRefreshScoresAllContexts(leaderboard.Id);
                    (float totalpp, int totalRanks) = await RefreshLeaderboardPlayers(leaderboard.Id, _context);
                    await _context.SaveChangesAsync();

                    if (dsClient != null)
                    {
                        string message = "The **" + difficulty.DifficultyName + "** diff of **" + leaderboard.Song.Name + "** was ranked! \n";
                        message += $"**{difficulty.Stars:0.00}★**\n";
                        message += "Mapped by: **" + leaderboard.Song.Mapper
                              + "** Nominated: **" + ((await _context.Players.FindAsync(leaderboard.Qualification.RTMember))?.Name ?? "None")
                              + "** Criteria: **" + ((await _context.Players.FindAsync(leaderboard.Qualification.CriteriaChecker))?.Name ?? "None") + "**\n";
                        message += $"{totalpp:0.00} total pp and {totalRanks * -1} total ranks were acquired \n";

                        await dsClient.SendMessageAsync(message, embeds: new List<Embed> {
                            new EmbedBuilder()
                              .WithThumbnailUrl(leaderboard.Song.CoverImage)
                              .WithTitle("Leaderboard")
                              .WithUrl("https://beatleader.xyz/leaderboard/global/" + leaderboard.Id)
                              .Build()
                        });

                        if (leaderboard.Qualification.DiscordRTChannelId.Length > 0)
                        {
                            try
                            {
                                var forum = scope.ServiceProvider.GetRequiredService<RTNominationsForum>();
                                await forum.NominationRanked(leaderboard.Qualification.DiscordRTChannelId);
                            }
                            catch { }
                        }
                    }
                }
                ConstantsService.RefreshRankedMapCount(_context);

                await _context.BulkSaveChangesAsync();

                foreach (var leaderboard in leaderboards)
                {
                    ClanTaskService.AddJob(new ClanRankingChangesDescription {
                        Changes = _context.CalculateClanRankingSlow(leaderboard),
                        GlobalMapEvent = GlobalMapEvent.ranked
                    });
                }

                await _context.BulkSaveChangesAsync();

                var clans = _context
                    .Clans
                    .Select(c => new { Clan = c, CaptureLeaderboardsCount = c.CapturedLeaderboards.Count() })
                    .OrderByDescending(c => c.CaptureLeaderboardsCount)
                    .ToList();
                var rank = 1;
                foreach (var c in clans)
                {
                    var clan = c.Clan;
                    clan.CaptureLeaderboardsCount = c.CaptureLeaderboardsCount;
                    clan.RankedPoolPercentCaptured = ((float)clan.CaptureLeaderboardsCount) / (float)ConstantsService.RankedMapCount;

                    clan.Rank = rank;
                    rank++;
                }
                await _context.BulkSaveChangesAsync();

                var _playlistController = scope.ServiceProvider.GetRequiredService<PlaylistController>();
                await _playlistController.RefreshNominatedPlaylist();
                await _playlistController.RefreshQualifiedPlaylist();
                await _playlistController.RefreshRankedPlaylist();

                var _playerController = scope.ServiceProvider.GetRequiredService<PlayerRefreshController>();
                await _playerController.RefreshRanks();
                await _playerController.RefreshPlayers();
                await _playerController.RefreshPlayersStats();

                var _playerContextController = scope.ServiceProvider.GetRequiredService<PlayerContextRefreshController>();
                await _playerContextController.RefreshPlayersAllContext();
                await _playerContextController.RefreshPlayersStatsAllContexts();
                
                await _context.BulkSaveChangesAsync();
            }
        }
    }
}
