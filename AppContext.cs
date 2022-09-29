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
        public DbSet<PlayerStatsHistory> StatsHistory { get; set; }
        public DbSet<CronTimestamps> cronTimestamps { get; set; }
        public DbSet<Badge> Badges { get; set; }
        public DbSet<CountryChange> CountryChanges { get; set; }
        public DbSet<LoginChange> LoginChanges { get; set; }
        public DbSet<ScoreRemovalLog> ScoreRemovalLogs { get; set; }
        public DbSet<Clan> Clans { get; set; }
        public DbSet<PlayerFriends> Friends { get; set; }
        public DbSet<CustomMode> CustomModes { get; set; }
        public DbSet<AccountLinkRequest> AccountLinkRequests { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<PatreonLink> PatreonLinks { get; set; }
        public DbSet<TwitchLink> TwitchLinks { get; set; }
        public DbSet<TwitterLink> TwitterLinks { get; set; }
        public DbSet<YouTubeLink> YouTubeLinks { get; set; }
        public DbSet<BeatSaverLink> BeatSaverLinks { get; set; }

        public DbSet<ReservedClanTag> ReservedTags { get; set; }
        public DbSet<Ban> Bans { get; set; }
        public DbSet<RankVoting> RankVotings { get; set; }
        public DbSet<ScoreRedirect> ScoreRedirects { get; set; }
        public DbSet<ModifiersMap> Modifiers { get; set; }
        public DbSet<ReplayWatchingSession> WatchingSessions { get; set; }

        public DbSet<EventRanking> EventRankings { get; set; }

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

    public class ReadAppContext : DbContext
    {
        public ReadAppContext(DbContextOptions<ReadAppContext> options)
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
        public DbSet<PlayerStatsHistory> StatsHistory { get; set; }
        public DbSet<CronTimestamps> cronTimestamps { get; set; }
        public DbSet<Badge> Badges { get; set; }
        public DbSet<CountryChange> CountryChanges { get; set; }
        public DbSet<LoginChange> LoginChanges { get; set; }
        public DbSet<ScoreRemovalLog> ScoreRemovalLogs { get; set; }
        public DbSet<Clan> Clans { get; set; }
        public DbSet<PlayerFriends> Friends { get; set; }
        public DbSet<CustomMode> CustomModes { get; set; }
        public DbSet<AccountLinkRequest> AccountLinkRequests { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<PatreonLink> PatreonLinks { get; set; }
        public DbSet<TwitchLink> TwitchLinks { get; set; }
        public DbSet<TwitterLink> TwitterLinks { get; set; }
        public DbSet<YouTubeLink> YouTubeLinks { get; set; }
        public DbSet<BeatSaverLink> BeatSaverLinks { get; set; }

        public DbSet<ReservedClanTag> ReservedTags { get; set; }
        public DbSet<Ban> Bans { get; set; }
        public DbSet<RankVoting> RankVotings { get; set; }
        public DbSet<ScoreRedirect> ScoreRedirects { get; set; }
        public DbSet<ModifiersMap> Modifiers { get; set; }
        public DbSet<ReplayWatchingSession> WatchingSessions { get; set; }

        public DbSet<EventRanking> EventRankings { get; set; }

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
