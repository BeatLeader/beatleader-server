using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

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
        public DbSet<CronTimestamps> cronTimestamps { get; set; }
        public DbSet<Badge> Badges { get; set; }
        public DbSet<CountryChange> CountryChanges { get; set; }
        public DbSet<LoginChange> LoginChanges { get; set; }
        public DbSet<ScoreRemovalLog> ScoreRemovalLogs { get; set; }
        public DbSet<Clan> Clans { get; set; }
        public DbSet<ClanRanking> ClanRanking { get; set; }
        public DbSet<PlayerFriends> Friends { get; set; }
        public DbSet<CustomMode> CustomModes { get; set; }
        public DbSet<AccountLinkRequest> AccountLinkRequests { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<PatreonLink> PatreonLinks { get; set; }
        public DbSet<TwitchLink> TwitchLinks { get; set; }
        public DbSet<TwitterLink> TwitterLinks { get; set; }
        public DbSet<DiscordLink> DiscordLinks { get; set; }
        public DbSet<YouTubeLink> YouTubeLinks { get; set; }
        public DbSet<BeatSaverLink> BeatSaverLinks { get; set; }
        public DbSet<PlayerLeaderboardStats> PlayerLeaderboardStats { get; set; }

        public DbSet<ReservedClanTag> ReservedTags { get; set; }
        public DbSet<Ban> Bans { get; set; }
        public DbSet<RankVoting> RankVotings { get; set; }
        public DbSet<ScoreRedirect> ScoreRedirects { get; set; }
        public DbSet<ModifiersMap> Modifiers { get; set; }
        public DbSet<ReplayWatchingSession> WatchingSessions { get; set; }
        public DbSet<Headset> Headsets { get; set; }
        public DbSet<VRController> VRControllers { get; set; }
        public DbSet<PlayerScoreStatsHistory> PlayerScoreStatsHistory { get; set; }

        public DbSet<EventRanking> EventRankings { get; set; }
        public DbSet<CountryChangeBan> CountryChangeBans { get; set; }
        public DbSet<RankQualification> RankQualification { get; set; }
        public DbSet<QualificationCommentary> QualificationCommentary { get; set; }
        public DbSet<CriteriaCommentary> CriteriaCommentary { get; set; }
        public DbSet<QualificationVote> QualificationVote { get; set; }
        public DbSet<PlayerSocial> PlayerSocial { get; set; }
        public DbSet<AchievementDescription> AchievementDescriptions { get; set; }
        public DbSet<Achievement> Achievements { get; set; }
        public DbSet<AchievementLevel> AchievementLevels { get; set; }
        public DbSet<SurveyPassed> SurveyResponses { get; set; }

        public DbSet<VoterFeedback> VoterFeedback { get; set; }
        public DbSet<SongSearch> SongSearches { get; set; }
        public DbSet<ScoreContextExtension> ScoreContextExtensions { get; set; }
        public DbSet<PlayerContextExtension> PlayerContextExtensions { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreApplication> OpenIddictApplications { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreAuthorization> OpenIddictAuthorizations { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreScope> OpenIddictScopes { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreToken> OpenIddictTokens { get; set; }


        public DbSet<SongSuggestRefresh> SongSuggestRefreshes { get; set; }

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
        public DbSet<CronTimestamps> cronTimestamps { get; set; }
        public DbSet<Badge> Badges { get; set; }
        public DbSet<CountryChange> CountryChanges { get; set; }
        public DbSet<LoginChange> LoginChanges { get; set; }
        public DbSet<ScoreRemovalLog> ScoreRemovalLogs { get; set; }
        public DbSet<Clan> Clans { get; set; }
        public DbSet<ClanRanking> ClanRanking { get; set; }
        public DbSet<PlayerFriends> Friends { get; set; }
        public DbSet<CustomMode> CustomModes { get; set; }
        public DbSet<AccountLinkRequest> AccountLinkRequests { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<PatreonLink> PatreonLinks { get; set; }
        public DbSet<TwitchLink> TwitchLinks { get; set; }
        public DbSet<TwitterLink> TwitterLinks { get; set; }
        public DbSet<YouTubeLink> YouTubeLinks { get; set; }
        public DbSet<BeatSaverLink> BeatSaverLinks { get; set; }
        public DbSet<PlayerLeaderboardStats> PlayerLeaderboardStats { get; set; }

        public DbSet<ReservedClanTag> ReservedTags { get; set; }
        public DbSet<Ban> Bans { get; set; }
        public DbSet<RankVoting> RankVotings { get; set; }
        public DbSet<ScoreRedirect> ScoreRedirects { get; set; }
        public DbSet<ModifiersMap> Modifiers { get; set; }
        public DbSet<ReplayWatchingSession> WatchingSessions { get; set; }
        public DbSet<Headset> Headsets { get; set; }
        public DbSet<VRController> VRControllers { get; set; }
        public DbSet<PlayerScoreStatsHistory> PlayerScoreStatsHistory { get; set; }

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
