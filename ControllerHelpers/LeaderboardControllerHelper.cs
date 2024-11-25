using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class LeaderboardControllerHelper {
        public static async Task<Leaderboard?> GetByHash(AppContext dbContext, string hash, string diff, string mode, bool recursive = true) {
            Leaderboard? leaderboard;

            leaderboard = await dbContext
                .Leaderboards
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.MaxScoreGraph)
                .TagWithCaller()
                .AsSplitQuery()
                .FirstOrDefaultAsync(lb => lb.Song.Hash == hash && lb.Difficulty.ModeName == mode && lb.Difficulty.DifficultyName == diff);

            if (leaderboard == null) {
                Song? song = await SongControllerHelper.GetOrAddSong(dbContext, hash);
                if (song == null) {
                    return null;
                }
                // Song migrated leaderboards
                if (recursive) {
                    return await GetByHash(dbContext, hash, diff, mode, false);
                } else {
                    leaderboard = await SongControllerHelper.NewLeaderboard(dbContext, song, null, diff, mode);
                }

                if (leaderboard == null) {
                    return null;
                }
            }

            return leaderboard;
        }

        public class QualificationInfo {
            public int Id { get; set; }
            public int Timeset { get; set; }
            public string RTMember { get; set; }
            public int CriteriaMet { get; set; }
            public int CriteriaTimeset { get; set; }
            public string CriteriaChecker { get; set; }
            public string CriteriaCommentary { get; set; }
            public bool MapperAllowed { get; set; }
            public string MapperId { get; set; }
            public bool MapperQualification { get; set; }
            public int ApprovalTimeset { get; set; }
            public bool Approved { get; set; }
            public string Approvers { get; set; }
        }

        public class MassLeaderboardsInfoResponse {
            public string Id { get; set; }
            public SongInfo Song { get; set; }
            public MassLeaderboardsDiffInfo Difficulty { get; set; }
            public QualificationInfo? Qualification { get; set; }

            public void HideRatings() {
                Difficulty.HideRatings();
            }

            public void RemoveSpeedMultipliers() {
                Difficulty.RemoveSpeedMultipliers();
            }
        }

        public class SongInfo {
            public string Id { get; set; }
            public string Hash { get; set; }
        }

        public class MassLeaderboardsDiffInfo {
            public int Id { get; set; }
            public int Value { get; set; }
            public int Mode { get; set; }
            public DifficultyStatus Status { get; set; }
            public string ModeName { get; set; }
            public string DifficultyName { get; set; }
            public int NominatedTime { get; set; }
            public int QualifiedTime { get; set; }
            public int RankedTime { get; set; }
            public float? Stars { get; set; }
            public float? AccRating { get; set; }
            public float? PassRating { get; set; }
            public float? TechRating { get; set; }
            public int MaxScore { get; set; }
            public int Type { get; set; }
            public ModifiersMap ModifierValues { get; set; }
            public ModifiersRating? ModifiersRating { get; set; }

            public void HideRatings() {
                this.AccRating = null;
                this.TechRating = null;
                this.PassRating = null;
                this.Stars = null;

                this.ModifiersRating = null;
            }

            public void RemoveSpeedMultipliers() {
                if (ModifierValues != null) {
                    ModifierValues.SF = 0;
                    ModifierValues.FS = 0;
                    ModifierValues.SS = 0;
                }
            }
        }

        public static async Task<ResponseWithMetadata<MassLeaderboardsInfoResponse>> GetModList(
            AppContext dbContext,
            bool showRatings,
            int page = 1,
            int count = 10,
            MapSortBy sortBy = MapSortBy.None,
            Order order = Order.Desc,
            int? date_from = null,
            int? date_to = null
            ) {
            var sequence = dbContext.Leaderboards.AsQueryable();
            (sequence, int totalMatches) = await sequence.FilterRanking(dbContext, page, count, sortBy, order, date_from, date_to);

            var result = new ResponseWithMetadata<MassLeaderboardsInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = totalMatches,
                }
            };

            sequence = sequence
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating);

            var resultList = await sequence
                .Select(lb => new MassLeaderboardsInfoResponse {
                    Id = lb.Id,
                    Song = new SongInfo {
                        Id = lb.Song.Id,
                        Hash = lb.Song.Hash
                    },
                    Difficulty = new MassLeaderboardsDiffInfo {
                        Id = lb.Difficulty.Id,
                        Value = lb.Difficulty.Value,
                        Mode = lb.Difficulty.Mode,
                        DifficultyName = lb.Difficulty.DifficultyName,
                        ModeName = lb.Difficulty.ModeName,
                        Status = lb.Difficulty.Status,
                        ModifierValues = lb.Difficulty.ModifierValues,
                        ModifiersRating = lb.Difficulty.ModifiersRating,
                        NominatedTime  = lb.Difficulty.NominatedTime,
                        QualifiedTime  = lb.Difficulty.QualifiedTime,
                        RankedTime = lb.Difficulty.RankedTime,

                        Stars  = lb.Difficulty.Stars,
                        PassRating  = lb.Difficulty.PassRating,
                        AccRating  = lb.Difficulty.AccRating,
                        TechRating  = lb.Difficulty.TechRating,
                        Type  = lb.Difficulty.Type,
                        MaxScore = lb.Difficulty.MaxScore,
                    },
                    Qualification = lb.Qualification != null ? new QualificationInfo {
                        Id = lb.Qualification.Id,
                        Timeset = lb.Qualification.Timeset,
                        RTMember = lb.Qualification.RTMember,
                        CriteriaMet = lb.Qualification.CriteriaMet,
                        CriteriaTimeset = lb.Qualification.CriteriaTimeset,
                        CriteriaChecker = lb.Qualification.CriteriaChecker,
                        CriteriaCommentary = lb.Qualification.CriteriaCommentary,
                        MapperAllowed = lb.Qualification.MapperAllowed,
                        MapperId = lb.Qualification.MapperId,
                        MapperQualification = lb.Qualification.MapperQualification,
                        ApprovalTimeset = lb.Qualification.ApprovalTimeset,
                        Approved = lb.Qualification.Approved,
                        Approvers = lb.Qualification.Approvers,
                    } : null
                })
                .ToListAsync();

            if (resultList.Count > 0) {
                foreach (var leaderboard in resultList) {
                    if (!showRatings && !leaderboard.Difficulty.Status.WithRating()) {
                        leaderboard.HideRatings();
                    } else {
                        leaderboard.RemoveSpeedMultipliers();
                    }
                }
            }

            result.Data = resultList;

            return result;
        }
    }
}
