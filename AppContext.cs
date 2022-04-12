using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server
{
    public class AppContext : DbContext
    {
        public AppContext(DbContextOptions<AppContext> options)
            : base(options)
        { }

        public DbSet<User> Users { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<Song> Songs { get; set; }
        public DbSet<Leaderboard> Leaderboards { get; set; }
        public DbSet<Player> Players { get; set; }
        public DbSet<Score> Scores { get; set; }
        public DbSet<FailedScore> FailedScores { get; set; }
        public DbSet<PlayerScoreStats> Stats { get; set; }
        public DbSet<AuthInfo> Auths { get; set; }
        public DbSet<AuthIP> AuthIPs { get; set; }
        public DbSet<AuthID> AuthIDs { get; set; }
        public DbSet<AccountLink> AccountLinks { get; set; }
        public DbSet<ScoreStatistic> ScoreStatistics { get; set; }
        public DbSet<PlayerStatsHistory> StatsHistory { get; set; }
        public DbSet<CronTimestamps> cronTimestamps { get; set; }
        public DbSet<Badge> Badges { get; set; }

        public void RejectChanges()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                switch (entry.State)
                {
                    case EntityState.Modified:
                    case EntityState.Deleted:
                        entry.State = EntityState.Modified; //Revert changes made to deleted entity.
                        entry.State = EntityState.Unchanged;
                        break;
                    case EntityState.Added:
                        entry.State = EntityState.Detached;
                        break;
                }
            }
        }
    }
}
