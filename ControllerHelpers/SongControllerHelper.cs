using BeatLeader_Server.Bot;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Services;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class SongControllerHelper {
        public static async Task AddNewSong(Song song, string hash, AppContext dbContext) {
            string songId = song.Id;
            Song? existingSong = await dbContext
                .Songs
                .Include(s => s.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .FirstOrDefaultAsync(i => i.Id == songId);
            Song? baseSong = existingSong;

            List<Song> songsToMigrate = new List<Song>();
            while (existingSong != null)
            {
                if (song.Hash.ToLower() == hash.ToLower())
                {
                    songsToMigrate.Add(existingSong);
                }
                songId += "x";
                existingSong = await dbContext.Songs.Include(s => s.Difficulties).FirstOrDefaultAsync(i => i.Id == songId);
            }

            song.Id = songId;
            song.Hash = hash;

            try {
                dbContext.Songs.Add(song);
                await dbContext.SaveChangesAsync();
                SongSearchService.AddNewSong(song);

                foreach (var oldSong in songsToMigrate)
                {
                    foreach (var item in oldSong.Difficulties)
                    {
                        await MigrateLeaderboards(dbContext, song, oldSong, baseSong, item);
                        item.Status = DifficultyStatus.outdated;
                        item.Stars = 0;
                    }
                }
            } catch (Exception e) {
                Console.WriteLine($"ADD SONG EXCEPTION: {e.Message}");
                dbContext.RejectChanges();
            }
                    
            try {
                await dbContext.SaveChangesAsync();
            } catch (Exception e) {
                Console.WriteLine($"ADD SONG EXCEPTION: {e.Message}");
                dbContext.RejectChanges();
            }
        }

        public static async Task<Song?> GetOrAddSong(AppContext dbContext, string hash)
        {
            Song? song = await GetSongWithDiffsFromHash(dbContext, hash);

            if (song == null)
            {
                (var map, _) = await SongUtils.GetSongFromBeatSaver(hash);

                if (map == null)
                {
                    return null;
                }
                else
                {
                    song = new Song();
                    song.FromMapDetails(map);
                    await AddNewSong(song, hash, dbContext);
                    await UpdateFromMap(dbContext, song, map);
                }
            }

            return song;
        }

        public static async Task<Leaderboard?> NewLeaderboard(AppContext dbContext, Song song, Song? baseSong, string diff, string mode)
        {
            IEnumerable<DifficultyDescription> difficulties = song.Difficulties.Where(el => el.DifficultyName.ToLower() == diff.ToLower());
            DifficultyDescription? difficulty = difficulties.FirstOrDefault(x => x.ModeName.ToLower() == mode.ToLower());
   
            if (difficulty == null)
            {
                difficulty = difficulties.FirstOrDefault(x => x.ModeName == "Standard");
                if (difficulty == null)
                {
                    return null;
                }
                else
                {
                    CustomMode? customMode = await dbContext.CustomModes.FirstOrDefaultAsync(m => m.Name == mode);
                    if (customMode == null)
                    {
                        customMode = new CustomMode
                        {
                            Name = mode
                        };
                        dbContext.CustomModes.Add(customMode);
                        await dbContext.SaveChangesAsync();
                    }

                    ModifiersMap? modifiersMap = null;
                    int maxScore = difficulty.MaxScore;
                    if (mode == ReBeatUtils.MODE_IDENTIFIER) {
                        maxScore = ReBeatUtils.MaxScoreForNote(difficulty.Notes + difficulty.Chains);
                        modifiersMap = ModifiersMap.ReBeatMap();
                    }

                    difficulty = new DifficultyDescription
                    {
                        Value = difficulty.Value,
                        Mode = customMode.Id + 10,
                        DifficultyName = difficulty.DifficultyName,
                        MaxScore = maxScore,
                        ModifierValues = modifiersMap,
                        MaxScoreGraph = difficulty.MaxScoreGraph,
                        ModeName = mode,

                        Njs = difficulty.Njs,
                        Nps = difficulty.Nps,
                        Notes = difficulty.Notes,
                        Chains = difficulty.Chains,
                        Sliders = difficulty.Sliders,
                        Bombs = difficulty.Bombs,
                        Walls = difficulty.Walls,
                        Requirements = difficulty.Requirements,
                    };
                    song.Difficulties.Add(difficulty);
                    await dbContext.SaveChangesAsync();
                }
            }

            string newLeaderboardId = $"{song.Id}{difficulty.Value}{difficulty.Mode}";
            var leaderboard = await dbContext.Leaderboards.Include(lb => lb.Difficulty).Where(l => l.Id == newLeaderboardId).FirstOrDefaultAsync();

            if (leaderboard == null) {
                leaderboard = new Leaderboard();
                leaderboard.SongId = song.Id;

                leaderboard.Difficulty = difficulty;
                leaderboard.Scores = new List<Score>();
                leaderboard.Id = newLeaderboardId;
                leaderboard.Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

                dbContext.Leaderboards.Add(leaderboard);
                try {
                    await dbContext.SaveChangesAsync();
                } catch (Exception e) {
                    Console.WriteLine($"ADD LEADERBOARD EXCEPTION: {e.Message}");
                    dbContext.RejectChanges();
                }
            }

            if (baseSong != null) {
                var baseId = $"{baseSong.Id}{difficulty.Value}{difficulty.Mode}";
                var baseLeaderboard = await dbContext.Leaderboards
                    .Include(lb => lb.LeaderboardGroup)
                    .ThenInclude(lbg => lbg.Leaderboards)
                    .FirstOrDefaultAsync(lb => lb.Id == baseId);

                if (baseLeaderboard != null) {
                    var group = baseLeaderboard.LeaderboardGroup ?? new LeaderboardGroup {
                        Leaderboards = new List<Leaderboard>()
                    };

                    if (baseLeaderboard.LeaderboardGroup == null) {
                        group.Leaderboards.Add(baseLeaderboard);
                        baseLeaderboard.LeaderboardGroup = group;
                    }

                    if (group.Leaderboards.FirstOrDefault(lb => lb.Id == leaderboard.Id) == null) {
                        group.Leaderboards.Add(leaderboard);

                        leaderboard.LeaderboardGroup = group;
                    }
                }
            }

            try {
                await dbContext.SaveChangesAsync();
            } catch (Exception e) {
                Console.WriteLine($"ADD LEADERBOARD EXCEPTION: {e.Message}");
                dbContext.RejectChanges();
            }

            return leaderboard;
        }

        public static async Task MigrateLeaderboards(AppContext dbContext, Song newSong, Song oldSong, Song? baseSong, DifficultyDescription diff)
        {
            var newLeaderboard = await NewLeaderboard(dbContext, newSong, baseSong, diff.DifficultyName, diff.ModeName);
            if (newLeaderboard != null && diff.Status != DifficultyStatus.ranked && diff.Status != DifficultyStatus.outdated) {
                await RatingUtils.UpdateFromExMachina(newLeaderboard.Difficulty, newSong, null);
                newLeaderboard.Difficulty.Status = diff.Status;
                newLeaderboard.Difficulty.Type = diff.Type;
                newLeaderboard.Difficulty.NominatedTime = diff.NominatedTime;
                newLeaderboard.Difficulty.QualifiedTime = diff.QualifiedTime;
                newLeaderboard.Difficulty.ModifierValues = diff.ModifierValues;
            }

            var oldLeaderboardId = $"{oldSong.Id}{diff.Value}{diff.Mode}";
            var oldLeaderboard = await dbContext.Leaderboards.Where(lb => lb.Id == oldLeaderboardId).Include(lb => lb.Qualification).FirstOrDefaultAsync();

            if (oldLeaderboard?.Qualification != null) {
                newLeaderboard.Qualification = oldLeaderboard.Qualification;
                newLeaderboard.NegativeVotes = oldLeaderboard.NegativeVotes;
                newLeaderboard.PositiveVotes = oldLeaderboard.PositiveVotes;
                if (oldLeaderboard.Qualification.DiscordRTChannelId.Length > 0 && diff.Status.WithRating()) {
                    await RTNominationsForum.NominationReuploaded(dbContext, oldLeaderboard.Qualification, oldLeaderboardId);
                }
                oldLeaderboard.Qualification = null;
            }
        }

        public static async Task UpdateFromMap(AppContext dbContext, Song song, MapDetail? map, bool save = true) {

            if (map == null || map.Versions[0].State != "Published") {
                if (song.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.unranked) != null) {
                    foreach (var diff in song.Difficulties) {
                        if (diff.Status == DifficultyStatus.unranked) {
                            diff.Status = DifficultyStatus.outdated;
                        }
                    }
                    if (save) {
                        dbContext.SaveChanges();
                    }
                }
            } else {
                if (map.Versions[0].Hash.ToLower() == song.Hash.ToLower() && song.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.outdated) != null) {
                    foreach (var diff in song.Difficulties) {
                        if (diff.Status == DifficultyStatus.outdated) {
                            diff.Status = DifficultyStatus.unranked;
                        }
                    }
                    if (save) {
                        dbContext.SaveChanges();
                    }
                }
            }

            if (map != null) {
                var mappers = (map.Collaborators ?? new List<UserDetail>()).Append(map.Uploader);

                if (string.Join(",", song.Mappers?.Select(m => m.Id) ?? []) != string.Join(",", mappers.Select(m => m.Id) ?? [])) {
                    song.Mappers = new List<Mapper>();
                    foreach (var mapper in mappers) {
                        var dbMapper = await dbContext.Mappers.FindAsync(mapper.Id);
                        if (dbMapper == null) {
                            dbMapper = Mapper.MapperFromBeatSaverUser(mapper);
                            dbContext.Mappers.Add(dbMapper);
                        }

                        song.Mappers.Add(dbMapper);
                        dbMapper.UpdateFromBeatSaverUser(mapper);
                    }
                    if (save) {
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
        }

        private static async Task<Song?> GetSongWithDiffsFromHash(AppContext dbContext, string hash)
        {
            return await dbContext
                .Songs
                .Where(el => el.Hash.ToLower() == hash.ToLower())
                .Include(song => song.Difficulties)
                .ThenInclude(d => d.ModifierValues)
                .Include(song => song.Difficulties)
                .ThenInclude(d => d.ModifiersRating)
                .FirstOrDefaultAsync();
        }
    }
}
