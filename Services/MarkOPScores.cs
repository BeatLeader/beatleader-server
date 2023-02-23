using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Dasync.Collections;

namespace BeatLeader_Server.Services
{
    public class MarkOPScores : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private static int lastScoreCount = 0;

        public MarkOPScores(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                    int newCount = _context.Scores.Where(s => !s.Migrated).Count();
                    if (newCount == lastScoreCount) {
                        await Task.Delay(TimeSpan.FromHours(1000), stoppingToken);
                    }
                    lastScoreCount = newCount;
                }
                await Migrate();
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task MigrateScore(
            Score score, 
            AppContext _context,
            IAmazonS3 _s3Client
            )
        {
            using (var stream = await _s3Client.DownloadStats(score.Id + ".json")) {
                if (stream == null)
                {
                    return;
                }

                var stats = stream.ObjectFromStream<ScoreStatistic>();
                if (stats == null) return;
                score.Pauses = stats.winTracker.nbOfPause;
                if (stats.winTracker.averageHeadPosition.z < 0.8) return;
            }


            Replay? replay = null;
            string fileName = score.Replay.Split("/").Last();
            if (score.Replay.Contains("cdn.replay")) {
                using (var replayStream = await _s3Client.DownloadReplay(fileName))
                {
                    if (replayStream == null) return;

                    using (var ms = new MemoryStream(5))
                    {
                        await replayStream.CopyToAsync(ms);
                        long length = ms.Length;
                        try
                        {
                            (replay, _) = ReplayDecoder.Decode(ms.ToArray());
                        }
                        catch (Exception)
                        {
                            return;
                        }
                    }
                } 
            }

            if (replay == null) {
                return;
            }

            if (!ReplayUtils.IsPlayerCuttingNotesOnPlatform(replay)) {
                score.Modifiers += score.Modifiers.Length > 0 ? ",OP" : "OP";
                score.IgnoreForStats = true;
            }
        }

        public async Task Migrate()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var _s3Client = configuration.GetS3Client();

                _context.ChangeTracker.AutoDetectChangesEnabled = false;

                var count = _context.Scores.Where(s => !s.Migrated).Count();

                var list = new List<int>();
                for (int i = 0; i < count; i += 5000)
                {
                    var scores =_context
                        .Scores
                        .Where(s => !s.Migrated)
                        .OrderByDescending(s => s.Pp)
                        .ThenByDescending(s => s.ModifiedScore)
                        .Skip(i)
                        .Take(5000)
                        .Select(s => new { s.Id, s.Replay, s.Modifiers, s.Pauses, s.IgnoreForStats, Requirements = s.Leaderboard.Difficulty.Requirements })
                        .ToList();
                    var toUpdate = new List<Score>();

                    await scores.ParallelForEachAsync(async s =>
                    {
                        
                        var score = new Score { Id = s.Id, Modifiers = s.Modifiers, Replay = s.Replay, Pauses = s.Pauses, IgnoreForStats = s.IgnoreForStats };
                        if (!s.Requirements.HasFlag(Requirements.Noodles)) {
                                await MigrateScore(
                                    score, 
                                    _context,
                                    _s3Client);
                        }
                        score.Migrated = true;
                        toUpdate.Add(score);
                    }, maxDegreeOfParallelism: 50);

                    foreach (var score in toUpdate)
                    {
                        _context.Scores.Attach(score);
                        _context.Entry(score).Property(x => x.Migrated).IsModified = true;
                        _context.Entry(score).Property(x => x.Modifiers).IsModified = true;
                        _context.Entry(score).Property(x => x.IgnoreForStats).IsModified = true;
                        _context.Entry(score).Property(x => x.Pauses).IsModified = true;
                    }

                    await _context.BulkSaveChangesAsync();
                }
            }
        }
    }
}