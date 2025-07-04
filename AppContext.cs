﻿using BeatLeader_Server.Models;
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
        public DbSet<DifficultyDescription> DifficultyDescription { get; set; }
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
        public DbSet<GitHubLink> GitHubLinks { get; set; }

        public DbSet<ReservedClanTag> ReservedTags { get; set; }
        public DbSet<Ban> Bans { get; set; }
        public DbSet<IpBan> IpBans { get; set; }
        public DbSet<RankVoting> RankVotings { get; set; }
        public DbSet<ScoreRedirect> ScoreRedirects { get; set; }
        public DbSet<ModifiersMap> Modifiers { get; set; }
        public DbSet<ReplayWatchingSession> WatchingSessions { get; set; }
        public DbSet<Headset> Headsets { get; set; }
        public DbSet<VRController> VRControllers { get; set; }

        public DbSet<EventRanking> EventRankings { get; set; }
        public DbSet<EventPlayer> EventPlayer { get; set; }
        public DbSet<CountryChangeBan> CountryChangeBans { get; set; }
        public DbSet<UsernamePfpChangeBan> UsernamePfpChangeBans { get; set; }
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
        public DbSet<PlayerSearch> PlayerSearches { get; set; }
        public DbSet<ScoreContextExtension> ScoreContextExtensions { get; set; }
        public DbSet<PlayerContextExtension> PlayerContextExtensions { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreApplication> OpenIddictApplications { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreAuthorization> OpenIddictAuthorizations { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreScope> OpenIddictScopes { get; set; }
        public DbSet<OpenIddictEntityFrameworkCoreToken> OpenIddictTokens { get; set; }


        public DbSet<SongSuggestRefresh> SongSuggestRefreshes { get; set; }
        public DbSet<SongsLastUpdateTime> SongsLastUpdateTimes { get; set; }
        public DbSet<ReeSabersPreset> ReeSabersPresets { get; set; }
        public DbSet<ReePresetDownload> ReePresetDownloads { get; set; }

        public DbSet<ReeSabersComment> ReeSabersComment { get; set; }
        public DbSet<GlobalMapHistory> GlobalMapHistory { get; set; }
        public DbSet<GlobalMapChange> GlobalMapChanges { get; set; }
        public DbSet<FeaturedPlaylist> FeaturedPlaylist { get; set; }
        public DbSet<ClanOrderChange> ClanOrderChanges { get; set; }

        public DbSet<ClanManager> ClanManagers { get; set; }
        public DbSet<ClanUpdate> ClanUpdates { get; set; }
        public DbSet<ValentineMessage> ValentineMessages { get; set; }
        public DbSet<DeveloperProfile> DeveloperProfile { get; set; }
        public DbSet<ExternalStatus> ExternalStatus { get; set; }
        public DbSet<PredictedScore> PredictedScores { get; set; }
        public DbSet<MaxScoreGraph> MaxScoreGraph { get; set; }

        public DbSet<AliasRequest> AliasRequests { get; set; }
        public DbSet<WatermarkRequest> WatermarkRequests { get; set; }
        public DbSet<Speedrun> Speedruns { get; set; }
        public DbSet<SanitizerConfig> SanitizerConfigs { get; set; }
        public DbSet<ModVersion> ModVersions { get; set; }

        public DbSet<Week100Poll> Week100Polls { get; set; }
        public DbSet<IngameAvatar> IngameAvatars { get; set; }
        public DbSet<Mapper> Mappers { get; set; }
        public DbSet<ModNews> ModNews { get; set; }
        public DbSet<BeastiesNomination> BeastiesNominations { get; set; }

        public DbSet<PlayerChange> PlayerChange { get; set; }
        public DbSet<PromotionHit> PromotionHits { get; set; }
        public DbSet<ScoreImprovement> ScoreImprovement { get; set; }
        
        public DbSet<TreeMap> TreeMaps { get; set; }
        public DbSet<TreeOrnament> TreeOrnaments { get; set; }
        public DbSet<PlayerTreeOrnament> PlayerTreeOrnaments { get; set; }
        public DbSet<TreeChampion> TreeChampions { get; set; }
        public DbSet<ProfileSettings> ProfileSettings { get; set; }
        public DbSet<EarthDayMap> EarthDayMaps { get; set; }
        public DbSet<BlueSkyLink> BlueSkyLinks { get; set; }
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

    public class StorageContext : DbContext
    {
        public StorageContext(DbContextOptions<StorageContext> options)
            : base(options)
        { }

        public DbSet<PlayerScoreStatsHistory> PlayerScoreStatsHistory { get; set; }
        public DbSet<PlayerLeaderboardStats> PlayerLeaderboardStats { get; set; }
    }
}
