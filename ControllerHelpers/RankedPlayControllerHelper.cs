using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.ControllerHelpers {
    public static class RankedPlayControllerHelper {

        // Number of placement matches before MMR becomes visible (§5.6).
        // Kept in sync with RankedPlayMMRService.PlacementMatchCount.
        private const int PlacementMatchCount = 5;

        public static async Task<RankedPlaySeason?> GetActiveSeason(AppContext context) {
            return await context.RankedPlaySeasons
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive);
        }

        public static async Task<int?> GetPreviousSeasonId(AppContext context, int currentSeasonId) {
            return await context.RankedPlaySeasons
                .Where(s => s.Id < currentSeasonId && !s.IsActive)
                .OrderByDescending(s => s.Id)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();
        }

        public static IQueryable<RankedPlayProfileResponse> ProfileResponseQuery(
            IQueryable<RankedPlayProfile> query
        ) {
            return query.Select(p => new RankedPlayProfileResponse {
                Id = p.Id,
                PlayerId = p.PlayerId,
                SeasonId = p.SeasonId != null ? (int)p.SeasonId : 0,
                MMR = p.IsCalibrated ? (float?)p.MMR : null,
                PeakMMR = p.PeakMMR,
                Wins = p.Wins,
                Losses = p.Losses,
                Draws = p.Draws,
                CalibrationMatchesPlayed = p.CalibrationMatchesPlayed,
                CalibrationMatchesRemaining = p.IsCalibrated ? 0 : PlacementMatchCount - p.CalibrationMatchesPlayed,
                IsCalibrated = p.IsCalibrated,
                Tier = p.Tier,
                TierDivision = p.TierDivision,
                GrandmasterRank = p.GrandmasterRank,
                LastMatchTime = p.LastMatchTime,
                Player = new PlayerResponse {
                    Id = p.Player.Id,
                    Name = p.Player.Name,
                    Alias = p.Player.Alias,
                    Platform = p.Player.Platform,
                    Avatar = p.Player.Avatar,
                    Country = p.Player.Country,
                    Pp = p.Player.Pp,
                    Rank = p.Player.Rank,
                    CountryRank = p.Player.CountryRank,
                    Role = p.Player.Role,
                    ProfileSettings = p.Player.ProfileSettings,
                    ClanOrder = p.Player.ClanOrder,
                    Clans = p.Player.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                }
            });
        }

        public static IQueryable<RankedPlayMatchResponse> MatchResponseQuery(
            IQueryable<RankedPlayMatch> query
        ) {
            return query.Select(m => new RankedPlayMatchResponse {
                Id = m.Id,
                SeasonId = m.SeasonId != null ? (int)m.SeasonId : 0,
                PlayerAId = m.PlayerAId,
                PlayerBId = m.PlayerBId,
                PlayerAPreMatchMMR = m.PlayerAPreMatchMMR,
                PlayerBPreMatchMMR = m.PlayerBPreMatchMMR,
                PlayerAGamesWon = m.PlayerAGamesWon,
                PlayerBGamesWon = m.PlayerBGamesWon,
                DrawnGames = m.DrawnGames,
                WinnerId = m.WinnerId,
                Result = m.Result,
                PlayerAMMRChange = m.PlayerAMMRChange,
                PlayerBMMRChange = m.PlayerBMMRChange,
                Timestamp = m.Timestamp,
                Duration = m.Duration,
                PlayerA = new PlayerResponse {
                    Id = m.PlayerA.Id,
                    Name = m.PlayerA.Name,
                    Alias = m.PlayerA.Alias,
                    Platform = m.PlayerA.Platform,
                    Avatar = m.PlayerA.Avatar,
                    Country = m.PlayerA.Country,
                    Pp = m.PlayerA.Pp,
                    Rank = m.PlayerA.Rank,
                    Role = m.PlayerA.Role,
                    ProfileSettings = m.PlayerA.ProfileSettings,
                    ClanOrder = m.PlayerA.ClanOrder,
                    Clans = m.PlayerA.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                },
                PlayerB = new PlayerResponse {
                    Id = m.PlayerB.Id,
                    Name = m.PlayerB.Name,
                    Alias = m.PlayerB.Alias,
                    Platform = m.PlayerB.Platform,
                    Avatar = m.PlayerB.Avatar,
                    Country = m.PlayerB.Country,
                    Pp = m.PlayerB.Pp,
                    Rank = m.PlayerB.Rank,
                    Role = m.PlayerB.Role,
                    ProfileSettings = m.PlayerB.ProfileSettings,
                    ClanOrder = m.PlayerB.ClanOrder,
                    Clans = m.PlayerB.Clans.Select(c => new ClanResponse { Id = c.Id, Tag = c.Tag, Color = c.Color })
                },
                Games = m.Games
                    .OrderBy(g => g.RoundNumber)
                    .Select(g => new RankedPlayGameResponse {
                        Id = g.Id,
                        RoundNumber = g.RoundNumber,
                        HandJson = g.HandJson,
                        PlayerADiscardLeaderboardId = g.PlayerADiscardLeaderboardId,
                        PlayerBDiscardLeaderboardId = g.PlayerBDiscardLeaderboardId,
                        ReplacementsJson = g.ReplacementsJson,
                        PickerId = g.PickerId,
                        LeaderboardId = g.LeaderboardId,
                        PlayerAScore = g.PlayerAScore,
                        PlayerBScore = g.PlayerBScore,
                        PlayerAFinalScore = g.PlayerAFinalScore,
                        PlayerBFinalScore = g.PlayerBFinalScore,
                        PlayerAAccuracy = g.PlayerAAccuracy,
                        PlayerBAccuracy = g.PlayerBAccuracy,
                        PlayerAFailed = g.PlayerAFailed,
                        PlayerBFailed = g.PlayerBFailed,
                        PlayerAForfeit = g.PlayerAForfeit,
                        PlayerBForfeit = g.PlayerBForfeit,
                        PlayerAReplay = g.PlayerAReplay,
                        PlayerBReplay = g.PlayerBReplay,
                        WinnerId = g.WinnerId,
                        Leaderboard = g.Leaderboard == null ? null : new LeaderboardResponse {
                            Id = g.Leaderboard.Id,
                            Song = new SongResponse {
                                Id = g.Leaderboard.Song.Id,
                                Hash = g.Leaderboard.Song.Hash,
                                Name = g.Leaderboard.Song.Name,
                                SubName = g.Leaderboard.Song.SubName,
                                Author = g.Leaderboard.Song.Author,
                                Mapper = g.Leaderboard.Song.Mapper,
                                MapperId = g.Leaderboard.Song.MapperId,
                                CoverImage = g.Leaderboard.Song.CoverImage,
                                DownloadUrl = g.Leaderboard.Song.DownloadUrl,
                                Duration = g.Leaderboard.Song.Duration,
                            },
                            Difficulty = new DifficultyResponse {
                                Id = g.Leaderboard.Difficulty.Id,
                                Value = g.Leaderboard.Difficulty.Value,
                                Mode = g.Leaderboard.Difficulty.Mode,
                                DifficultyName = g.Leaderboard.Difficulty.DifficultyName,
                                ModeName = g.Leaderboard.Difficulty.ModeName,
                                Status = g.Leaderboard.Difficulty.Status,
                                Stars = g.Leaderboard.Difficulty.Stars,
                                Type = g.Leaderboard.Difficulty.Type
                            }
                        }
                    })
                    .ToList()
            });
        }
    }
}
