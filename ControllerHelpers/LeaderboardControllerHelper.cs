using Amazon.S3;
using BeatLeader_Server.Enums;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class LeaderboardControllerHelper {
        public static async Task<Leaderboard?> GetByHash(AppContext dbContext, string hash, string diff, string mode, bool allowCustom, bool recursive) {
            Leaderboard? leaderboard;

            leaderboard = await dbContext
                .Leaderboards
                .TagWithCallerS()
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifierValues)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.ModifiersRating)
                .Include(lb => lb.Difficulty)
                .ThenInclude(d => d.MaxScoreGraph)
                .AsSplitQuery()
                .FirstOrDefaultAsync(lb => lb.Song.Hash == hash && lb.Difficulty.ModeName == mode && lb.Difficulty.DifficultyName == diff);

            if (leaderboard == null) {
                Song? song = await SongControllerHelper.GetOrAddSong(dbContext, hash);
                if (song == null) {
                    return null;
                }
                // Song migrated leaderboards
                if (recursive) {
                    return await GetByHash(dbContext, hash, diff, mode, allowCustom, false);
                } else if (allowCustom || song.Difficulties.Any(d => d.DifficultyName == diff && d.ModeName == mode)) {
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
            public int UploadTime { get; set; }
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
            public MapTypes Type { get; set; }
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
            IDbContextFactory<AppContext> contextFactory,
            bool showRatings,
            int page = 1,
            int count = 10,
            int? date_from = null,
            int? date_to = null
            ) {

            var allBoards = MinuteRefresh.massLeaderboards;

            var result = new ResponseWithMetadata<MassLeaderboardsInfoResponse>() {
                Metadata = new Metadata() {
                    Page = page,
                    ItemsPerPage = count,
                    Total = allBoards.Where(lb => (date_from == null || lb.Song.UploadTime >= date_from) && (date_to == null || lb.Song.UploadTime <= date_to)).Count(),
                }
            };

            var resultList = allBoards.Where(lb => (date_from == null || lb.Song.UploadTime >= date_from) && (date_to == null || lb.Song.UploadTime <= date_to)).OrderBy(lb => lb.Id).Skip((page - 1) * count).Take(count).ToList();

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

         public static async Task<List<PlaylistResponse>?> GetPlaylistList(
            AppContext _context,
            string? currentID,
            IAmazonS3 _s3Client,
            string? playlistIds,
            List<PlaylistResponse>? playlists
            ) {
            var result = playlists;
            if (playlistIds != null) {
                var playlistIntIds = playlistIds.Split(",").Select(s => (int?)(int.TryParse(s, out int result) ? result : null)).Where(s => s != null).Select(s => (int)s).ToList();
                if (playlistIntIds.Count == 0 && playlists == null) {
                    return null;
                }

                if (result == null) {
                    result = new List<PlaylistResponse>();
                }

                var remotePlaylists = await _context.Playlists.Where(p => playlistIntIds.Contains(p.Id) && (p.OwnerId == currentID || p.IsShared)).ToListAsync();
                foreach (var remotePlaylist in remotePlaylists) {
                    using (var stream = await _s3Client.DownloadPlaylist(remotePlaylist.Id + ".bplist")) {
                        var toAdd = stream.ObjectFromStream<PlaylistResponse>();
                        if (toAdd != null) {
                            result.Add(toAdd);
                        }
                    }
                }
            }

            return result;
        }
    }
}
