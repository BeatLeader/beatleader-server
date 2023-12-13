using BeatLeader_Server.Models;
using Newtonsoft.Json;

namespace BeatLeader_Server.Utils {
    public class ResponseUtils {
        public class ClanResponse {
            public int Id { get; set; }
            public string Tag { get; set; }
            public string Color { get; set; }
        }

        public class PlayerResponse {
            public string Id { get; set; }
            public string Name { get; set; } = "";
            public string Platform { get; set; } = "";
            public string Avatar { get; set; } = "";
            public string Country { get; set; } = "not set";

            public bool Bot { get; set; }

            public float Pp { get; set; }
            public int Rank { get; set; }
            public int CountryRank { get; set; }
            public string Role { get; set; }
            public ICollection<PlayerSocial>? Socials { get; set; }
            public ICollection<PlayerContextExtension>? ContextExtensions { get; set; }

            public PatreonFeatures? PatreonFeatures { get; set; }
            public ProfileSettings? ProfileSettings { get; set; }
            public IEnumerable<ClanResponse>? Clans { get; set; }

            public virtual void ToContext(PlayerContextExtension extension) {
                Pp = extension.Pp;

                Rank = extension.Rank;
                CountryRank = extension.CountryRank;
            }
        }
        public class PlayerResponseWithFriends : PlayerResponse {
            public ICollection<string>? Friends { get; set; }
        }

        public class PlayerResponseWithStats : PlayerResponse {
            public float AccPp { get; set; }
            public float PassPp { get; set; }
            public float TechPp { get; set; }
            public PlayerScoreStats? ScoreStats { get; set; }
            public float LastWeekPp { get; set; }
            public int LastWeekRank { get; set; }
            public int LastWeekCountryRank { get; set; }
            public IEnumerable<EventPlayer>? EventsParticipating { get; set; }

            public override void ToContext(PlayerContextExtension extension) {
                Pp = extension.Pp;
                AccPp = extension.AccPp;
                TechPp = extension.TechPp;
                PassPp = extension.PassPp;

                Rank = extension.Rank;
                CountryRank = extension.CountryRank;
                ScoreStats = extension.ScoreStats;

                LastWeekPp = extension.LastWeekPp;
                LastWeekRank = extension.LastWeekRank;
                LastWeekCountryRank = extension.LastWeekCountryRank;
            }
        }

        public class PlayerResponseFull : PlayerResponseWithStats {
            public int MapperId { get; set; }

            public bool Banned { get; set; }
            public bool Inactive { get; set; }

            public Ban? BanDescription { get; set; }

            public string ExternalProfileUrl { get; set; } = "";


            public ICollection<PlayerScoreStatsHistory>? History { get; set; }

            public ICollection<Badge>? Badges { get; set; }
            public ICollection<ScoreResponseWithMyScore>? PinnedScores { get; set; }
            public ICollection<PlayerChange>? Changes { get; set; }
        }

        

        public class ScoreSongResponse {
            public string Id { get; set; }
            public string Hash { get; set; }
            public string Cover { get; set; }
            public string Name { get; set; }
            public string? SubName { get; set; }
            public string Author { get; set; }
            public string Mapper { get; set; }
        }

        public class ScoreResponseWithDifficulty : ScoreResponse {
            public DifficultyDescription Difficulty { get; set; }
            public ScoreSongResponse Song { get; set; }
        }

        public class SaverScoreResponse {
            public int Id { get; set; }
            public int BaseScore { get; set; }
            public int ModifiedScore { get; set; }
            public float Accuracy { get; set; }
            public float Pp { get; set; }
            public int Rank { get; set; }
            public string Modifiers { get; set; }
            public string LeaderboardId { get; set; }
            public string Timeset { get; set; }
            public int Timepost { get; set; }
            public string Player { get; set; }
        }

        public class SaverContainerResponse {
            public string LeaderboardId { get; set; }
            public bool Ranked { get; set; }
        }

        public class LeaderboardsResponse {
            public Song Song { get; set; }
            public ICollection<LeaderboardsInfoResponse> Leaderboards { get; set; }
        }

        public class LeaderboardsInfoResponse {
            public string Id { get; set; }
            public DifficultyResponse Difficulty { get; set; }
            public RankQualification? Qualification { get; set; }
            public RankUpdate? Reweight { get; set; }
            public bool ClanRankingContested { get; set; }
            public Clan Clan { get; set; }

            public void HideRatings() {
                this.Difficulty.HideRatings();
            }
        }

        public class LeaderboardsResponseWithScores : LeaderboardsResponse {
            public new ICollection<LeaderboardsInfoResponseWithScore> Leaderboards { get; set; }
        }

        public class LeaderboardsInfoResponseWithScore : LeaderboardsInfoResponse { 
            public ScoreResponseWithAcc? MyScore { get; set; }
        }

        public class ClanReturn {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Color { get; set; }
            public string Icon { get; set; }
            public string Tag { get; set; }
            public string LeaderID { get; set; }

            public int PlayersCount { get; set; }
            public float Pp { get; set; }
            public float AverageRank { get; set; }
            public float AverageAccuracy { get; set; }

            public float RankedPoolPercentCaptured { get; set; }
            public ICollection<Leaderboard> CapturedLeaderboards { get; set; } = new List<Leaderboard>();

            public ICollection<string> Players { get; set; } = new List<string>();
            public ICollection<string> PendingInvites { get; set; } = new List<string>();
        }

        public class BanReturn {
            public string Reason { get; set; }
            public int Timeset { get; set; }
            public int Duration { get; set; }
        }

        public class UserReturn {
            public PlayerResponseFull Player { get; set; }

            public ClanReturn? Clan { get; set; }

            public BanReturn? Ban { get; set; }
            public ICollection<Clan> ClanRequest { get; set; } = new List<Clan>();
            public ICollection<Clan> BannedClans { get; set; } = new List<Clan>();
            public ICollection<Playlist>? Playlists { get; set; }
            public ICollection<PlayerResponseFull>? Friends { get; set; }

            public string? Login { get; set; }

            public bool Migrated { get; set; }
            public bool Patreoned { get; set; }
        }

        public class DifficultyDescriptionResponse {
            public int Id { get; set; }
            public int Value { get; set; }
            public int Mode { get; set; }
            public string DifficultyName { get; set; }
            public string ModeName { get; set; }
            public DifficultyStatus Status { get; set; }
            public ModifiersMap? ModifierValues { get; set; } = new ModifiersMap();
            public ModifiersRating? ModifiersRating { get; set; }
            public int NominatedTime { get; set; }
            public int QualifiedTime { get; set; }
            public int RankedTime { get; set; }

            public float? Stars { get; set; }
            public float? PassRating { get; set; }
            public float? AccRating { get; set; }
            public float? TechRating { get; set; }
            public int Type { get; set; }

            public float Njs { get; set; }
            public float Nps { get; set; }
            public int Notes { get; set; }
            public int Bombs { get; set; }
            public int Walls { get; set; }
            public int MaxScore { get; set; }
            public double Duration { get; set; }

            public Requirements Requirements { get; set; }
        }

        public class DifficultyResponse
        {
            public int Id { get; set; }
            public int Value { get; set; }
            public int Mode { get; set; }
            public string DifficultyName { get; set; }
            public string ModeName { get; set; }
            public DifficultyStatus Status { get; set; }
            public ModifiersMap? ModifierValues { get; set; } = new ModifiersMap();
            public ModifiersRating? ModifiersRating { get; set; }
            public int NominatedTime { get; set; }
            public int QualifiedTime { get; set; }
            public int RankedTime { get; set; }

            public float? Stars { get; set; }
            public float? PredictedAcc { get; set; }
            public float? PassRating { get; set; }
            public float? AccRating { get; set; }
            public float? TechRating { get; set; }
            public int Type { get; set; }

            public float Njs { get; set; }
            public float Nps { get; set; }
            public int Notes { get; set; }
            public int Bombs { get; set; }
            public int Walls { get; set; }
            public int MaxScore { get; set; }
            public double Duration { get; set; }

            public Requirements Requirements { get; set; }

            public void HideRatings() {
                this.AccRating = null;
                this.TechRating = null;
                this.PassRating = null;
                this.Stars = null;

                this.ModifiersRating = null;
            }
        }

        public class LeaderboardResponse {
            public string? Id { get; set; }
            public Song? Song { get; set; }
            public DifficultyResponse? Difficulty { get; set; }
            public List<ScoreResponse>? Scores { get; set; }
            public IEnumerable<LeaderboardChange>? Changes { get; set; }

            public RankQualification? Qualification { get; set; }
            public RankUpdate? Reweight { get; set; }

            public IEnumerable<LeaderboardGroupEntry>? LeaderboardGroup { get; set; }
            public int Plays { get; set; }

            public Clan? Clan { get; set; }
            public bool ClanRankingContested { get; set; }
            public void HideRatings()
            {
                this.Difficulty.HideRatings();
            }
        }

        public class LeaderboardClanRankingResponse : LeaderboardResponse {

            public ICollection<ClanRankingResponse>? ClanRanking { get; set; }
        }

        public class ClanRankingResponse
        {
            public int Id { get; set; }
            public Clan Clan { get; set; }
            public int LastUpdateTime { get; set; }
            public float AverageRank { get; set; }
            public float Pp { get; set; }
            public float AverageAccuracy { get; set; }
            public float TotalScore { get; set; }
            public string LeaderboardId { get; set; }
            public Leaderboard Leaderboard { get; set; }
            public ICollection<ScoreResponse>? AssociatedScores { get; set; }
            public int AssociatedScoresCount { get; set; }
        }

        public class LeaderboardGroupEntry {
            public string Id { get; set; }
            public DifficultyStatus Status { get; set; }
            public long Timestamp { get; set; }
        }

        public class LeaderboardInfoResponse {
            public string Id { get; set; }
            public Song Song { get; set; }
            public DifficultyResponse Difficulty { get; set; }
            public int Plays { get; set; }
            public int PositiveVotes { get; set; }
            public int StarVotes { get; set; }
            public int NegativeVotes { get; set; }
            public float VoteStars { get; set; }
            public Clan? Clan { get; set; }
            public bool ClanRankingContested { get; set; }
            public ScoreResponseWithAcc? MyScore { get; set; }
            public RankQualification? Qualification { get; set; }
            public RankUpdate? Reweight { get; set; }

            public void HideRatings() {
                this.Difficulty.HideRatings();
            }
        }

        public class DiffModResponse {
            public string DifficultyName { get; set; }
            public string ModeName { get; set; }
            public float? Stars { get; set; }
            public DifficultyStatus Status { get; set; }
            public int Type { get; set; }
            public float[] Votes { get; set; }
            public ModifiersMap? ModifierValues { get; set; }
            public ModifiersRating? ModifiersRating { get; set; }
            public float? PassRating { get; set; }
            public float? AccRating { get; set; }
            public float? TechRating { get; set; }

            public void HideRatings() {
                this.AccRating = null;
                this.TechRating = null;
                this.PassRating = null;
                this.Stars = null;

                this.ModifiersRating = null;
            }
        }

        public class CompactScore {
            public int Id { get; set; }
            public int BaseScore { get; set; }
            public int ModifiedScore { get; set; }
            public string Modifiers { get; set; }
            public bool FullCombo { get; set; }
            public int MaxCombo { get; set; }
            public int MissedNotes { get; set; }
            public int BadCuts { get; set; }
            public HMD Hmd { get; set; }
            public ControllerEnum Controller { get; set; }
            public float Accuracy { get; set; }
            public float? Pp { get; set; }

            public int EpochTime { get; set; }
        }

        public class CompactLeaderboard {
            public string Id { get; set; }
            public string SongHash { get; set; }
            public string ModeName { get; set; }
            public int Difficulty { get; set; }
        }

        public class CompactScoreResponse {
            public CompactScore Score { get; set; }
            public CompactLeaderboard Leaderboard { get; set; }
        }

        public class EventResponse {
            public int Id { get; set; }
            public string Name { get; set; }
            public int EndDate { get; set; }
            public int PlaylistId { get; set; }
            public string Image { get; set; }

            public int PlayerCount { get; set; }
            public PlayerResponse Leader { get; set; }
        }

        public static T RemoveLeaderboard<T>(Score s, int i) where T : ScoreResponse, new() {
            return new T {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                PlayerId = s.PlayerId,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
                FcAccuracy = s.FcAccuracy,
                FcPp = s.FcPp,
                BonusPp = s.BonusPp,
                Rank = s.Rank,
                Replay = s.Replay,
                Modifiers = s.Modifiers,
                BadCuts = s.BadCuts,
                MissedNotes = s.MissedNotes,
                BombCuts = s.BombCuts,
                WallsHit = s.WallsHit,
                Pauses = s.Pauses,
                FullCombo = s.FullCombo,
                Hmd = s.Hmd,
                Controller = s.Controller,
                MaxCombo = s.MaxCombo,
                Timeset = s.Timeset,
                ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                Timepost = s.Timepost,
                LeaderboardId = s.LeaderboardId,
                Platform = s.Platform,
                Player = s.Player != null ? new PlayerResponse {
                    Id = s.Player.Id,
                    Name = s.Player.Name,
                    Platform = s.Player.Platform,
                    Avatar = s.Player.Avatar,
                    Country = s.Player.Country,

                    Pp = s.Player.Pp,
                    Rank = s.Player.Rank,
                    CountryRank = s.Player.CountryRank,
                    Role = s.Player.Role,
                    Socials = s.Player.Socials,
                    PatreonFeatures = s.Player.PatreonFeatures,
                    ProfileSettings = s.Player.ProfileSettings,
                    ContextExtensions = s.Player.ContextExtensions != null ? s.Player.ContextExtensions.Select(ce => new PlayerContextExtension {
                        Context = ce.Context,
                        Pp = ce.Pp,
                        AccPp = ce.AccPp,
                        TechPp = ce.TechPp,
                        PassPp = ce.PassPp,
                        PlayerId = ce.PlayerId,

                        Rank = ce.Rank,
                        Country  = ce.Country,
                        CountryRank  = ce.CountryRank,
                    }).ToList() : null,
                    Clans = s.Player.Clans?.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                } : null,
                ScoreImprovement = s.ScoreImprovement,
                RankVoting = s.RankVoting,
                Metadata = s.Metadata,
                Country = s.Country,
                Offsets = s.ReplayOffsets,
                MaxStreak = s.MaxStreak
            };
        }

        public static ScoreResponse RemoveLeaderboard(Score s, int i) {
            return RemoveLeaderboard<ScoreResponse>(s, i);
        }

        public static ScoreResponseWithAcc ToScoreResponseWithAcc(Score s, int i) {
            var result = RemoveLeaderboard<ScoreResponseWithAcc>(s, i);
            result.Weight = s.Weight;
            result.AccLeft = s.AccLeft;
            result.AccRight = s.AccRight;

            return result;
        }

        public static T RemoveLeaderboardCE<T>(ScoreContextExtension s, int i) where T : ScoreResponse, new() {
            return new T {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                PlayerId = s.PlayerId,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
                FcAccuracy = s.Score.FcAccuracy,
                FcPp = s.Score.FcPp,
                BonusPp = s.BonusPp,
                Rank = s.Rank,
                Replay = s.Score.Replay,
                Modifiers = s.Modifiers,
                BadCuts = s.Score.BadCuts,
                MissedNotes = s.Score.MissedNotes,
                BombCuts = s.Score.BombCuts,
                WallsHit = s.Score.WallsHit,
                Pauses = s.Score.Pauses,
                FullCombo = s.Score.FullCombo,
                Hmd = s.Score.Hmd,
                Controller = s.Score.Controller,
                MaxCombo = s.Score.MaxCombo,
                Timeset = s.Score.Timeset,
                ReplaysWatched = s.Score.AnonimusReplayWatched + s.Score.AuthorizedReplayWatched,
                Timepost = s.Timeset,
                LeaderboardId = s.LeaderboardId,
                Platform = s.Score.Platform,
                Player = s.Player != null ? new PlayerResponse {
                    Id = s.Player.Id,
                    Name = s.Player.Name,
                    Platform = s.Player.Platform,
                    Avatar = s.Player.Avatar,
                    Country = s.Player.Country,

                    Pp = s.Player.Pp,
                    Rank = s.Player.Rank,
                    CountryRank = s.Player.CountryRank,
                    Role = s.Player.Role,
                    Socials = s.Player.Socials,
                    PatreonFeatures = s.Player.PatreonFeatures,
                    ProfileSettings = s.Player.ProfileSettings,
                    ContextExtensions = s.Player.ContextExtensions != null ? s.Player.ContextExtensions.Select(ce => new PlayerContextExtension {
                        Context = ce.Context,
                        Pp = ce.Pp,
                        AccPp = ce.AccPp,
                        TechPp = ce.TechPp,
                        PassPp = ce.PassPp,

                        Rank = ce.Rank,
                        Country  = ce.Country,
                        CountryRank  = ce.CountryRank,
                    }).ToList() : null,
                    Clans = s.Player.Clans?.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                } : null,
                ScoreImprovement = s.ScoreImprovement,
                RankVoting = s.Score.RankVoting,
                Metadata = s.Score.Metadata,
                Country = s.Score.Country,
                Offsets = s.Score.ReplayOffsets,
                MaxStreak = s.Score.MaxStreak
            };
        }

        public static ScoreResponse RemoveLeaderboardCE(ScoreContextExtension s, int i) {
            return RemoveLeaderboardCE<ScoreResponse>(s, i);
        }

        public static ScoreResponseWithAcc ToScoreCEResponseWithAcc(ScoreContextExtension s, int i) {
            var result = RemoveLeaderboardCE<ScoreResponseWithAcc>(s, i);
            result.Weight = s.Weight;
            result.AccLeft = s.Score.AccLeft;
            result.AccRight = s.Score.AccRight;

            return result;
        }

        public static ScoreResponseWithMyScore ScoreWithMyScore(Score s, int i) {

            return new ScoreResponseWithMyScore {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                PlayerId = s.PlayerId,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
                FcAccuracy = s.FcAccuracy,
                FcPp = s.FcPp,
                BonusPp = s.BonusPp,
                Rank = s.Rank,
                Replay = s.Replay,
                Modifiers = s.Modifiers,
                BadCuts = s.BadCuts,
                MissedNotes = s.MissedNotes,
                BombCuts = s.BombCuts,
                WallsHit = s.WallsHit,
                Pauses = s.Pauses,
                FullCombo = s.FullCombo,
                Hmd = s.Hmd,
                Controller = s.Controller,
                MaxCombo = s.MaxCombo,
                Timeset = s.Timeset,
                ReplaysWatched = s.AnonimusReplayWatched + s.AuthorizedReplayWatched,
                Timepost = s.Timepost,
                LeaderboardId = s.LeaderboardId,
                Platform = s.Platform,
                Player = s.Player != null ? new PlayerResponse {
                    Id = s.Player.Id,
                    Name = s.Player.Name,
                    Platform = s.Player.Platform,
                    Avatar = s.Player.Avatar,
                    Country = s.Player.Country,

                    Pp = s.Player.Pp,
                    Rank = s.Player.Rank,
                    CountryRank = s.Player.CountryRank,
                    Role = s.Player.Role,
                    Socials = s.Player.Socials,
                    PatreonFeatures = s.Player.PatreonFeatures,
                    ProfileSettings = s.Player.ProfileSettings,
                    Clans = s.Player.Clans?.OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                } : null,
                ScoreImprovement = s.ScoreImprovement,
                RankVoting = s.RankVoting,
                Metadata = s.Metadata,
                Country = s.Country,
                Offsets = s.ReplayOffsets,
                Leaderboard = new LeaderboardResponse {
                    Id = s.LeaderboardId,
                    Song = s.Leaderboard?.Song,
                    Difficulty = s.Leaderboard?.Difficulty != null ? new DifficultyResponse {
                        Id = s.Leaderboard.Difficulty.Id,
                        Value = s.Leaderboard.Difficulty.Value,
                        Mode = s.Leaderboard.Difficulty.Mode,
                        DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                        ModeName = s.Leaderboard.Difficulty.ModeName,
                        Status = s.Leaderboard.Difficulty.Status,
                        ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                        ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                        NominatedTime  = s.Leaderboard.Difficulty.NominatedTime,
                        QualifiedTime  = s.Leaderboard.Difficulty.QualifiedTime,
                        RankedTime = s.Leaderboard.Difficulty.RankedTime,

                        Stars  = s.Leaderboard.Difficulty.Stars,
                        PredictedAcc  = s.Leaderboard.Difficulty.PredictedAcc,
                        PassRating  = s.Leaderboard.Difficulty.PassRating,
                        AccRating  = s.Leaderboard.Difficulty.AccRating,
                        TechRating  = s.Leaderboard.Difficulty.TechRating,
                        Type  = s.Leaderboard.Difficulty.Type,

                        Njs  = s.Leaderboard.Difficulty.Njs,
                        Nps  = s.Leaderboard.Difficulty.Nps,
                        Notes  = s.Leaderboard.Difficulty.Notes,
                        Bombs  = s.Leaderboard.Difficulty.Bombs,
                        Walls  = s.Leaderboard.Difficulty.Walls,
                        MaxScore = s.Leaderboard.Difficulty.MaxScore,
                        Duration  = s.Leaderboard.Difficulty.Duration,

                        Requirements = s.Leaderboard.Difficulty.Requirements,
                    } : null
                },
                Weight = s.Weight,
                AccLeft = s.AccLeft,
                AccRight = s.AccRight,
                MaxStreak = s.MaxStreak,
                ValidContexts = s.ValidContexts
            };
        }

        public static T? GeneralResponseFromPlayer<T>(Player? p) where T : PlayerResponse, new() {
            if (p == null) return null;

            return new T {
                Id = p.Id,
                Name = p.Name,
                Platform = p.Platform,
                Avatar = p.Avatar,
                Country = p.Country,

                Pp = p.Pp,
                Rank = p.Rank,
                CountryRank = p.CountryRank,
                Role = p.Role,
                Socials = p.Socials,
                PatreonFeatures = p.PatreonFeatures,
                ProfileSettings = p.ProfileSettings,
                ContextExtensions = p.ContextExtensions,
                Clans = p.Clans?.OrderBy(c => p.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
            };
        }

        public static PlayerResponse? ResponseFromPlayer(Player? p) {
            return GeneralResponseFromPlayer<PlayerResponse>(p);
        }

        public static LeaderboardResponse ResponseFromLeaderboard(Leaderboard l) {
            return new LeaderboardResponse {
                Id = l.Id,
                Song = l.Song,
                Difficulty = new DifficultyResponse {
                    Id = l.Difficulty.Id,
                    Value = l.Difficulty.Value,
                    Mode = l.Difficulty.Mode,
                    DifficultyName = l.Difficulty.DifficultyName,
                    ModeName = l.Difficulty.ModeName,
                    Status = l.Difficulty.Status,
                    ModifierValues = l.Difficulty.ModifierValues,
                    ModifiersRating = l.Difficulty.ModifiersRating,
                    NominatedTime  = l.Difficulty.NominatedTime,
                    QualifiedTime  = l.Difficulty.QualifiedTime,
                    RankedTime = l.Difficulty.RankedTime,

                    Stars  = l.Difficulty.Stars,
                    PredictedAcc  = l.Difficulty.PredictedAcc,
                    PassRating  = l.Difficulty.PassRating,
                    AccRating  = l.Difficulty.AccRating,
                    TechRating  = l.Difficulty.TechRating,
                    Type  = l.Difficulty.Type,

                    Njs  = l.Difficulty.Njs,
                    Nps  = l.Difficulty.Nps,
                    Notes  = l.Difficulty.Notes,
                    Bombs  = l.Difficulty.Bombs,
                    Walls  = l.Difficulty.Walls,
                    MaxScore = l.Difficulty.MaxScore,
                    Duration  = l.Difficulty.Duration,

                    Requirements = l.Difficulty.Requirements,
                },
                Scores = l.Scores.Select(RemoveLeaderboard).ToList(),
                Plays = l.Plays,
                Qualification = l.Qualification,
                Reweight = l.Reweight,
                Changes = l.Changes,
                LeaderboardGroup = l.LeaderboardGroup?.Leaderboards?.Select(it =>
                    new LeaderboardGroupEntry {
                        Id = it.Id,
                        Status = it.Difficulty.Status,
                        Timestamp = it.Timestamp
                    }
                )
            };
        }

        public static PlayerResponseWithStats ResponseWithStatsFromPlayer(Player p) {
            return new PlayerResponseWithStats {
                Id = p.Id,
                Name = p.Name,
                Platform = p.Platform,
                Avatar = p.Avatar,
                Country = p.Country,
                ScoreStats = p.ScoreStats,

                Pp = p.Pp,
                Rank = p.Rank,
                CountryRank = p.CountryRank,
                LastWeekPp = p.LastWeekPp,
                LastWeekRank = p.LastWeekRank,
                LastWeekCountryRank = p.LastWeekCountryRank,
                Role = p.Role,
                EventsParticipating = p.EventsParticipating,
                PatreonFeatures = p.PatreonFeatures,
                ProfileSettings = p.ProfileSettings,
                Clans = p.Clans?.OrderBy(c => p.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
            };
        }

        public static PlayerResponseFull? ResponseFullFromPlayerNullable(Player? p) {
            if (p == null) return null;

            return ResponseFullFromPlayer(p);
        }

        public static PlayerResponseFull ResponseFullFromPlayer(Player p) {
            return new PlayerResponseFull {
                Id = p.Id,
                Name = p.Name,
                Platform = p.Platform,
                Avatar = p.Avatar,
                Country = p.Country,
                ScoreStats = p.ScoreStats,

                MapperId = p.MapperId,

                Banned = p.Banned,
                Inactive = p.Inactive,
                Bot = p.Bot,

                ExternalProfileUrl = p.ExternalProfileUrl,

                History = p.History,

                Badges = p.Badges,
                Changes = p.Changes,

                Pp = p.Pp,
                AccPp = p.AccPp,
                TechPp = p.TechPp,
                PassPp = p.PassPp,
                Rank = p.Rank,
                CountryRank = p.CountryRank,
                LastWeekPp = p.LastWeekPp,
                LastWeekRank = p.LastWeekRank,
                LastWeekCountryRank = p.LastWeekCountryRank,
                Role = p.Role,
                Socials = p.Socials,
                EventsParticipating = p.EventsParticipating,
                PatreonFeatures = p.PatreonFeatures,
                ProfileSettings = p.ProfileSettings,
                ContextExtensions = p.ContextExtensions,
                Clans = p.Clans?.OrderBy(c => p.ClanOrder.IndexOf(c.Tag))
                            .ThenBy(c => c.Id).Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
            };
        }

        public static DiffModResponse DiffModResponseFromDiffAndVotes(DifficultyDescription diff, float[] votes) {
            return new DiffModResponse {
                DifficultyName = diff.DifficultyName,
                ModeName = diff.ModeName,
                Stars = diff.Stars,
                Status = diff.Status,
                Type = diff.Type,
                Votes = votes,
                ModifierValues = diff.ModifierValues,
                ModifiersRating = diff.ModifiersRating,
                PassRating = diff.PassRating,
                AccRating = diff.AccRating,
                TechRating = diff.TechRating
            };
        }

        public static T PostProcessSettings<T>(T input) where T : PlayerResponse? {
            if (input == null) return null;

            if (input.ProfileSettings == null) {
                input.ProfileSettings = new ProfileSettings();
            }

            PostProcessSettings(input.Role, input.ProfileSettings, input.PatreonFeatures);

            return input;
        }

        public static void PostProcessSettings(string role, ProfileSettings? settings, PatreonFeatures? patreonFeatures, bool hideStarredFriends = true) {
            if (settings != null && hideStarredFriends) {
                settings.StarredFriends = "";
            }

            if (settings != null && settings.ProfileAppearance == null) {
                settings.ProfileAppearance = "topPp,averageRankedAccuracy,topPlatform,topHMD";
            }

            if (!role.Contains("sponsor")) {
                if (settings != null) {
                    settings.Message = null;
                }
                if (patreonFeatures != null) {
                    patreonFeatures.Message = "";
                }
            }

            if (settings != null) {
                if (settings.EffectName?.Contains("Special") == true) {
                    if (!role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("qualityteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin")) {
                        settings.EffectName = "";
                    }
                } else if (settings.EffectName?.Contains("Tier1") == true) {
                    if (!role.Contains("tipper") &&
                        !role.Contains("supporter") &&
                        !role.Contains("sponsor") &&
                        !role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("qualityteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin") &&
                        !role.Contains("booster")) {
                        settings.EffectName = "";
                    }
                } else if (settings.EffectName?.Contains("Tier2") == true) {
                    if (!role.Contains("supporter") &&
                        !role.Contains("sponsor") &&
                        !role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("qualityteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin")) {
                        settings.EffectName = "";
                    }
                } else if (settings.EffectName?.Contains("Tier3") == true) {
                    if (!role.Contains("sponsor") &&
                        !role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("qualityteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin")) {
                        settings.EffectName = "";
                    }
                } else {
                    settings.EffectName = "";
                }

                if (!role.Contains("tipper") &&
                        !role.Contains("supporter") &&
                        !role.Contains("sponsor") &&
                        !role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("qualityteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin")) {
                    settings.RightSaberColor = null;
                    settings.LeftSaberColor = null;
                }
            }
        }

        public enum FriendActivityType {
            Achievement = 1,
            MapLiked = 2,
            MapRanked = 3,
            MapPublished = 4,
        }

        public class FriendActivity {
            public PlayerResponse Player { get; set; }
            public FriendActivityType Type { get; set; }
            public dynamic ActivityObject { get; set; }
        }
    }
}
