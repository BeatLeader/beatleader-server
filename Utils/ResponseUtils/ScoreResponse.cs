using BeatLeader_Server.Models;
using Newtonsoft.Json;
using System.Linq.Expressions;
using static BeatLeader_Server.Utils.ResponseUtils;
using ReplayDecoder;

namespace BeatLeader_Server.Utils
{
    public class ScoreResponse
    {
        public int Id { get; set; }
        public int BaseScore { get; set; }
        public int ModifiedScore { get; set; }
        public float Accuracy { get; set; }
        public string PlayerId { get; set; }
        public float Pp { get; set; }
        public float BonusPp { get; set; }
        public float PassPP { get; set; }
        public float AccPP { get; set; }
        public float TechPP { get; set; }
        public int Rank { get; set; }
        public string? Country { get; set; }
        public float FcAccuracy { get; set; }
        public float FcPp { get; set; }
        public float Weight { get; set; }
        public string Replay { get; set; }
        public string Modifiers { get; set; }
        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int BombCuts { get; set; }
        public int WallsHit { get; set; }
        public int Pauses { get; set; }
        public bool FullCombo { get; set; }
        public string Platform { get; set; }
        public int MaxCombo { get; set; }
        public int? MaxStreak { get; set; }
        public HMD Hmd { get; set; }
        public ControllerEnum Controller { get; set; }
        public string LeaderboardId { get; set; }
        public string Timeset { get; set; }
        public int Timepost { get; set; }
        public int ReplaysWatched { get; set; }
        public int PlayCount { get; set; }
        [JsonIgnore]
        public int Priority { get; set; }
        public PlayerResponse? Player { get; set; }
        public ScoreImprovement? ScoreImprovement { get; set; }
        public RankVoting? RankVoting { get; set; }
        public ScoreMetadata? Metadata { get; set; }
        public ReplayOffsets? Offsets { get; set; }

        public void ToContext(ScoreContextExtension? extension)
        {
            if (extension == null) return;

            Weight = extension.Weight;
            Rank = extension.Rank;
            BaseScore = extension.BaseScore;
            ModifiedScore = extension.ModifiedScore;
            Accuracy = extension.Accuracy;
            Pp = extension.Pp;
            TechPP = extension.TechPP;
            PassPP = extension.PassPP;
            BonusPp = extension.BonusPp;
            Modifiers = extension.Modifiers;
        }
    }

    public class ScoreResponseWithAcc : ScoreResponse
    {
        public float AccLeft { get; set; }
        public float AccRight { get; set; }
    }

    public class ScoreResponseWithMyScore : ScoreResponseWithAcc
    {
        public ScoreResponseWithAcc? MyScore { get; set; }
        public LeaderboardContexts ValidContexts { get; set; }

        public LeaderboardResponse Leaderboard { get; set; }
        public ICollection<ScoreContextExtension> ContextExtensions { get; set; }
    }

    public static class ScoreResponseQuery
    {
        public static Expression<Func<Score, ScoreResponseWithMyScore>> SelectWithMyScore()
        {
            return s => new ScoreResponseWithMyScore
            {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                PlayerId = s.PlayerId,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
                PassPP = s.PassPP,
                AccPP = s.AccPP,
                TechPP = s.TechPP,
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
                Player = new PlayerResponse
                {
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
                    Clans = s.Player.Clans
                                .OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                                .ThenBy(c => c.Id)
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                },
                ScoreImprovement = s.ScoreImprovement,
                RankVoting = s.RankVoting,
                Metadata = s.Metadata,
                Country = s.Country,
                Offsets = s.ReplayOffsets,
                Leaderboard = new LeaderboardResponse
                {
                    Id = s.LeaderboardId,
                    Song = s.Leaderboard.Song,
                    Difficulty = new DifficultyResponse
                    {
                        Id = s.Leaderboard.Difficulty.Id,
                        Value = s.Leaderboard.Difficulty.Value,
                        Mode = s.Leaderboard.Difficulty.Mode,
                        DifficultyName = s.Leaderboard.Difficulty.DifficultyName,
                        ModeName = s.Leaderboard.Difficulty.ModeName,
                        Status = s.Leaderboard.Difficulty.Status,
                        ModifierValues = s.Leaderboard.Difficulty.ModifierValues,
                        ModifiersRating = s.Leaderboard.Difficulty.ModifiersRating,
                        NominatedTime = s.Leaderboard.Difficulty.NominatedTime,
                        QualifiedTime = s.Leaderboard.Difficulty.QualifiedTime,
                        RankedTime = s.Leaderboard.Difficulty.RankedTime,

                        Stars = s.Leaderboard.Difficulty.Stars,
                        PredictedAcc = s.Leaderboard.Difficulty.PredictedAcc,
                        PassRating = s.Leaderboard.Difficulty.PassRating,
                        AccRating = s.Leaderboard.Difficulty.AccRating,
                        TechRating = s.Leaderboard.Difficulty.TechRating,
                        Type = s.Leaderboard.Difficulty.Type,

                        Njs = s.Leaderboard.Difficulty.Njs,
                        Nps = s.Leaderboard.Difficulty.Nps,
                        Notes = s.Leaderboard.Difficulty.Notes,
                        Bombs = s.Leaderboard.Difficulty.Bombs,
                        Walls = s.Leaderboard.Difficulty.Walls,
                        MaxScore = s.Leaderboard.Difficulty.MaxScore,
                        Duration = s.Leaderboard.Difficulty.Duration,

                        Requirements = s.Leaderboard.Difficulty.Requirements,
                    }
                },
                Weight = s.Weight,
                AccLeft = s.AccLeft,
                AccRight = s.AccRight,
                MaxStreak = s.MaxStreak
            };
        }

        public static Expression<Func<Score, ScoreResponseWithAcc>> SelectWithAcc() 
        {
            return s => new ScoreResponseWithAcc
            {
                Id = s.Id,
                BaseScore = s.BaseScore,
                ModifiedScore = s.ModifiedScore,
                PlayerId = s.PlayerId,
                Accuracy = s.Accuracy,
                Pp = s.Pp,
                PassPP = s.PassPP,
                AccPP = s.AccPP,
                TechPP = s.TechPP,
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
                Player = new PlayerResponse
                {
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
                    Clans = s.Player.Clans
                                .OrderBy(c => s.Player.ClanOrder.IndexOf(c.Tag))
                                .ThenBy(c => c.Id)
                                .Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                },
                ScoreImprovement = s.ScoreImprovement,
                RankVoting = s.RankVoting,
                Metadata = s.Metadata,
                Country = s.Country,
                Offsets = s.ReplayOffsets,
                Weight = s.Weight,
                AccLeft = s.AccLeft,
                AccRight = s.AccRight,
                MaxStreak = s.MaxStreak
            };
        }
    }
}
