﻿using Amazon.S3;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Services {
    public class PlayerStatsJob {
        public byte[]? replayData { get; set; }
        public bool saveReplay { get; set; }
        public string fileName { get; set; }
        public string playerId { get; set; }
        public string leaderboardId { get; set; }
        public Score score { get; set; }
        public float time { get; set; }
        public float startTime { get; set; }
        public float speed { get; set; }
        public int? timeset { get; set; }
        public EndType type { get; set; }
    }

    public class LeaderboardPlayerStatsService {
        public static async Task AddJob(PlayerStatsJob job, AppContext _context, StorageContext _storageContext, IAmazonS3 _s3Client) {
            var score = job.score;

            var leaderboard = await _context.Leaderboards.Where(l => l.Id == job.leaderboardId).Include(lb => lb.Song).FirstOrDefaultAsync();
            if (leaderboard == null) return;

            var playerRole = await _context.Players.Where(p => p.Id == job.playerId).Select(p => p.Role).FirstOrDefaultAsync();
            var anySupporter = Player.RoleIsAnySupporter(playerRole ?? "");

            leaderboard.PlayCount++;

            int timeset = job.timeset ?? (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            string? replayLink = null;
            if (job.replayData == null) {
                replayLink = job.fileName;
            } else if (anySupporter && job.saveReplay) {
                try {
                    string fileName = job.fileName;
                    await _s3Client.UploadOtherReplay(fileName, job.replayData);
                    replayLink = $"https://api.beatleader.com/otherreplays/{fileName}";
                } catch {
                }
            }

            var stats = new PlayerLeaderboardStats {
                Timeset = timeset,
                Time = (float)Math.Min(job.time, leaderboard.Song.Duration),
                Speed = job.speed,
                StartTime = job.startTime,
                Score = score.BaseScore,
                Type = job.type,
                PlayerId = job.playerId,
                Replay = replayLink,
                LeaderboardId = leaderboard.Id
            };

            if (job.leaderboardId != null) {
                stats.LeaderboardId = job.leaderboardId;
            }
            stats.FromScore(score);

            var currentScore = await _context
                    .Scores
                    .Where(s =>
                        s.LeaderboardId == job.leaderboardId &&
                        s.PlayerId == job.playerId)
                    .FirstOrDefaultAsync();
            if (currentScore != null) {
                currentScore.PlayCount++;
                currentScore.LastTryTime = score.Timepost;
            }

            if (float.IsNaN(stats.Accuracy) || float.IsNegativeInfinity(stats.Accuracy) || float.IsPositiveInfinity(stats.Accuracy)) {
                stats.Accuracy = 0;
            }
            stats.AttemptsCount = await _storageContext.PlayerLeaderboardStats.Where(s => s.LeaderboardId == stats.LeaderboardId && s.PlayerId == stats.PlayerId).CountAsync();
            stats.AttemptsCount++;
            _storageContext.PlayerLeaderboardStats.Add(stats);

            try {
                await _storageContext.SaveChangesAsync();
                await _context.SaveChangesAsync();
            } catch (Exception e) {
                Console.WriteLine($"LeaderboardPlayerStatsService EXCEPTION: {e}");
            }
        }
    }
}

