using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    public class ResponseUtils
    {
        public class ClanResponse
        {
            public int Id { get; set; }
            public string Tag { get; set; }
            public string Color { get; set; }
        }

        public class PlayerResponse
        {
            public string Id { get; set; }
            public string Name { get; set; } = "";
            public string Platform { get; set; } = "";
            public string Avatar { get; set; } = "";
            public string Country { get; set; } = "not set";

            public float Pp { get; set; }
            public int Rank { get; set; }
            public int CountryRank { get; set; }
            public string Role { get; set; }
            public ICollection<PlayerSocial>? Socials { get; set; }

            public PatreonFeatures? PatreonFeatures { get; set; }
            public ProfileSettings? ProfileSettings { get; set; }
            public IEnumerable<ClanResponse> Clans { get; set; }
        }

        public class PlayerResponseWithStats : PlayerResponse
        {
            public string Histories { get; set; } = "";
            public PlayerScoreStats ScoreStats { get; set; }
            public IEnumerable<EventPlayer>? EventsParticipating { get; set; }
        }

        public class PlayerResponseFull : PlayerResponseWithStats
        {
            public int MapperId { get; set; }

            public bool Banned { get; set; }
            public bool Inactive { get; set; }

            public string ExternalProfileUrl { get; set; } = "";
            

            public PlayerStatsHistory? StatsHistory { get; set; }

            public ICollection<Badge>? Badges { get; set; }
            public ICollection<ScoreResponseWithMyScore>? PinnedScores { get; set; }
            public ICollection<PlayerChange>? Changes { get; set; }
        }

        public class ScoreResponse
        {
            public int Id { get; set; }
            public int BaseScore { get; set; }
            public int ModifiedScore { get; set; }
            public float Accuracy { get; set; }
            public string PlayerId { get; set; }
            public float Pp { get; set; }
            public float BonusPp { get; set; }
            public int Rank { get; set; }
            public int CountryRank { get; set; }
            public string Replay { get; set; }
            public string Modifiers { get; set; }
            public int BadCuts { get; set; }
            public int MissedNotes { get; set; }
            public int BombCuts { get; set; }
            public int WallsHit { get; set; }
            public int Pauses { get; set; }
            public bool FullCombo { get; set; }
            public string Platform { get; set; }
            public HMD Hmd { get; set; }
            public string LeaderboardId { get; set; }
            public string Timeset { get; set; }
            public int Timepost { get; set; }
            public PlayerResponse Player { get; set; }
            public ScoreImprovement? ScoreImprovement { get; set; }
            public RankVoting? RankVoting { get; set; }
            public ScoreMetadata? Metadata { get; set; }
        }

        public class LeaderboardsResponse
        {
            public Song Song { get; set; }
            public ICollection<LeaderboardsInfoResponse> Leaderboards { get; set; }
        }

        public class LeaderboardsInfoResponse {
            public string Id { get; set; }
            public DifficultyDescription Difficulty { get; set; }
            public RankQualification? Qualification { get; set; }
            public RankUpdate? Reweight { get; set; }
        }

        public class ClanReturn
        {
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

            public ICollection<string> Players { get; set; } = new List<string>();
            public ICollection<string> PendingInvites { get; set; } = new List<string>();
        }

        public class BanReturn
        {
            public string Reason { get; set; }
            public int Timeset { get; set; }
            public int Duration { get; set; }
        }

        public class UserReturn
        {
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

        public class LeaderboardResponse {
            public string Id { get; set; }
            public Song Song { get; set; }
            public DifficultyDescription Difficulty { get; set; }
            public IEnumerable<ScoreResponse> Scores { get; set; }
            public IEnumerable<LeaderboardChange>? Changes { get; set; }

            public RankQualification? Qualification { get; set; }
            public RankUpdate? Reweight { get; set; }
            public int Plays { get; set; }
        }

        public class ScoreResponseWithAcc : ScoreResponse
        {
            public float Weight { get; set; }

            public float AccLeft { get; set; }
            public float AccRight { get; set; }
        }

        public class ScoreResponseWithMyScore : ScoreResponseWithAcc
        {
            public ScoreResponse? MyScore { get; set; }

            public LeaderboardResponse Leaderboard { get; set; }
        }

        public class VotingResponse {
            public float Rankability { get; set; } = 0;
            public float Stars { get; set; } = 0;
            public int Type { get; set; } = 0;
            public int Timeset { get; set; } = 0;

        }

        public class LeaderboardInfoResponse
        {
            public string Id { get; set; }
            public Song Song { get; set; }
            public DifficultyDescription Difficulty { get; set; }
            public int Plays { get; set; }

            public ScoreResponseWithAcc? MyScore { get; set; }
            public RankQualification? Qualification { get; set; }
            public RankUpdate? Reweight { get; set; }

            public IEnumerable<VotingResponse> Votes { get; set; }
        }

        public class DiffModResponse
        {
            public string DifficultyName { get; set; }
            public string ModeName { get; set; }
            public float? Stars { get; set; }
            public DifficultyStatus Status { get; set; }
            public int Type { get; set; }
            public float[] Votes { get; set; }
            public ModifiersMap? ModifierValues { get; set; }
        }

        public class EventResponse
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int EndDate { get; set; }
            public int PlaylistId { get; set; }
            public string Image { get; set; }

            public int PlayerCount { get; set; }
            public PlayerResponse Leader { get; set; }
        }

        public static T RemoveLeaderboard<T>  (Score s, int i) where T : ScoreResponse, new()
        {
            return new T
            {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                PlayerId = s.PlayerId,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
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
                Timeset = s.Timeset,
                Timepost = s.Timepost,
                LeaderboardId = s.LeaderboardId,
                Platform = s.Platform,
                Player = ResponseFromPlayer(s.Player),
                ScoreImprovement = s.ScoreImprovement,
                RankVoting = s.RankVoting,
                Metadata = s.Metadata
            };
        }

        public static ScoreResponse RemoveLeaderboard(Score s, int i) {
            return RemoveLeaderboard<ScoreResponse>(s, i);
        }

        public static ScoreResponse? RemoveNullableLeaderboard(Score? s, int i)
        {
            return s == null ? null : RemoveLeaderboard<ScoreResponse>(s, i);
        }

        public static ScoreResponseWithMyScore ScoreWithMyScore(Score s, int i) {
            var result = RemoveLeaderboard<ScoreResponseWithMyScore>(s, i);
            result.Leaderboard = new LeaderboardResponse
            {
                Id = s.Leaderboard.Id,
                Song = s.Leaderboard.Song,
                Difficulty = s.Leaderboard.Difficulty
            };
            result.Weight = s.Weight;
            result.AccLeft = s.AccLeft;
            result.AccRight = s.AccRight;
            return result;
        }


        public static PlayerResponse? ResponseFromPlayer(Player? p)
        {
            if (p == null) return null;

            return new PlayerResponse
            {
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
                Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
            };
        }

        public static LeaderboardResponse ResponseFromLeaderboard(Leaderboard l) {
            return new LeaderboardResponse {
                Id = l.Id,
                Song = l.Song,
                Difficulty = l.Difficulty,
                Scores = l.Scores.Select(RemoveLeaderboard),
                Plays = l.Plays,
                Qualification = l.Qualification,
                Reweight = l.Reweight,
                Changes = l.Changes,
            };
        }

        public static PlayerResponseWithStats? ResponseWithStatsFromPlayer(Player? p)
        {
            if (p == null) return null;

            return new PlayerResponseWithStats
            {
                Id = p.Id,
                Name = p.Name,
                Platform = p.Platform,
                Avatar = p.Avatar,
                Country = p.Country,
                Histories = p.Histories,
                ScoreStats = p.ScoreStats,

                Pp = p.Pp,
                Rank = p.Rank,
                CountryRank = p.CountryRank,
                Role = p.Role,
                EventsParticipating = p.EventsParticipating,
                PatreonFeatures = p.PatreonFeatures,
                ProfileSettings = p.ProfileSettings,
                Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
            };
        }

        public static PlayerResponseFull? ResponseFullFromPlayer(Player? p)
        {
            if (p == null) return null;

            return new PlayerResponseFull
            {
                Id = p.Id,
                Name = p.Name,
                Platform = p.Platform,
                Avatar = p.Avatar,
                Country = p.Country,
                Histories = p.Histories,
                ScoreStats = p.ScoreStats,

                MapperId = p.MapperId,

                Banned = p.Banned,
                Inactive = p.Inactive,

                ExternalProfileUrl = p.ExternalProfileUrl,

                StatsHistory = p.StatsHistory,

                Badges = p.Badges,
                Changes = p.Changes,

                Pp = p.Pp,
                Rank = p.Rank,
                CountryRank = p.CountryRank,
                Role = p.Role,
                Socials = p.Socials,
                EventsParticipating = p.EventsParticipating,
                PatreonFeatures = p.PatreonFeatures,
                ProfileSettings = p.ProfileSettings,
                Clans = p.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
            };
        }
        
        public static DiffModResponse DiffModResponseFromDiffAndVotes(DifficultyDescription diff, float[] votes)
        {
            return new DiffModResponse
            {
                DifficultyName = diff.DifficultyName,
                ModeName = diff.ModeName,
                Stars = diff.Stars,
                Status = diff.Status,
                Type = diff.Type,
                Votes = votes,
                ModifierValues = diff.ModifierValues
            };
        }

        public static T PostProcessSettings<T>(T input) where T: PlayerResponse? {
            if (input == null) return null;

            var role = input.Role;
            var settings = input.ProfileSettings;
            var patreonFeatures = input.PatreonFeatures;

            if (!role.Contains("sponsor")) {
                if (settings != null) {
                    settings.Message = null;
                }
                if (patreonFeatures != null) {
                    patreonFeatures.Message = "";
                }
            }
            
            if (settings != null) {
                if (settings.EffectName?.Contains("Tier1") == true) {
                    if (!role.Contains("tipper") && 
                        !role.Contains("supporter") && 
                        !role.Contains("sponsor") && 
                        !role.Contains("creator") && 
                        !role.Contains("rankedteam") &&
                        !role.Contains("juniorrankedteam") && 
                        !role.Contains("admin")) {
                        settings.EffectName = "";
                    }
                }
                if (settings.EffectName?.Contains("Tier2") == true)
                {
                    if (!role.Contains("tipper") &&
                        !role.Contains("supporter") &&
                        !role.Contains("sponsor") &&
                        !role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin"))
                    {
                        settings.EffectName = "";
                    }
                }
                if (settings.EffectName?.Contains("Tier3") == true)
                {
                    if (!role.Contains("tipper") &&
                        !role.Contains("supporter") &&
                        !role.Contains("sponsor") &&
                        !role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin"))
                    {
                        settings.EffectName = "";
                    }
                }
                if (settings.EffectName?.Contains("Special") == true)
                {
                    if (!role.Contains("creator") &&
                        !role.Contains("rankedteam") &&
                        !role.Contains("juniorrankedteam") &&
                        !role.Contains("admin"))
                    {
                        settings.EffectName = "";
                    }
                }
            }

            return input;
        }
    }
}
