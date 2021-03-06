using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils
{
    public class ResponseUtils
    {
        public class ClanResponse
        {
            public string Tag { get; set; }
            public string Color { get; set; }
        }

        public class PatreonResponse
        {
            public string Message { get; set; } = "";
            public string LeftSaberColor { get; set; } = "";
            public string RightSaberColor { get; set; } = "";
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

            public PatreonResponse? PatreonFeatures { get; set; }
            public IEnumerable<ClanResponse> Clans { get; set; }
        }

        public class PlayerResponseWithStats : PlayerResponse
        {
            public float LastTwoWeeksTime { get; set; }
            public float AllTime { get; set; }
            public string Histories { get; set; } = "";

            public PlayerScoreStats ScoreStats { get; set; }
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
            public int Hmd { get; set; }
            public string LeaderboardId { get; set; }
            public string Timeset { get; set; }
            public PlayerResponse Player { get; set; }
            public ScoreImprovement? ScoreImprovement { get; set; }
        }

        public class LeaderboardResponse {
            public string Id { get; set; }
            public Song Song { get; set; }
            public DifficultyDescription Difficulty { get; set; }
            public IEnumerable<ScoreResponse> Scores { get; set; }
            public int Plays { get; set; }
        }

        public class ScoreResponseWithMyScore : ScoreResponse
        {
            public ScoreResponse? MyScore { get; set; }

            public float Weight { get; set; }

            public float AccLeft { get; set; }
            public float AccRight { get; set; }

            public LeaderboardResponse Leaderboard { get; set; }
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
                LeaderboardId = s.LeaderboardId,
                Platform = s.Platform,
                Player = ResponseFromPlayer(s.Player),
                ScoreImprovement = s.ScoreImprovement
            };
        }

        public static ScoreResponse RemoveLeaderboard(Score s, int i) {
            return RemoveLeaderboard<ScoreResponse>(s, i);
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
                PatreonFeatures = p.PatreonFeatures == null ? null :
                    new PatreonResponse
                    {
                        Message = p.PatreonFeatures.Message,
                        LeftSaberColor = p.PatreonFeatures.LeftSaberColor,
                        RightSaberColor = p.PatreonFeatures.RightSaberColor,
                    },
                Clans = p.Clans.Select(c => new ClanResponse { Tag = c.Tag, Color = c.Color })
            };
        }

        public static LeaderboardResponse ResponseFromLeaderboard(Leaderboard l) {
            return new LeaderboardResponse {
                Id = l.Id,
                Song = l.Song,
                Difficulty = l.Difficulty,
                Scores = l.Scores.Select(RemoveLeaderboard),
                Plays = l.Plays
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
                LastTwoWeeksTime = p.LastTwoWeeksTime,
                AllTime = p.AllTime,
                ScoreStats = p.ScoreStats,

                Pp = p.Pp,
                Rank = p.Rank,
                CountryRank = p.CountryRank,
                Role = p.Role,
                PatreonFeatures = p.PatreonFeatures == null ? null :
                    new PatreonResponse
                    {
                        Message = p.PatreonFeatures.Message,
                        LeftSaberColor = p.PatreonFeatures.LeftSaberColor,
                        RightSaberColor = p.PatreonFeatures.RightSaberColor,
                    },
                Clans = p.Clans.Select(c => new ClanResponse { Tag = c.Tag, Color = c.Color })
            };
        }
    }
}
