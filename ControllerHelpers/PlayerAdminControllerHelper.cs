using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.ControllerHelpers {
    public class VersionsReport {
        public int Count { get; set; }
        public List<VersionStat> Stats { get; set; }
    }

    public class VersionStat {
        public string Version { get; set; }
        public string Value { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public int Count { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public float FloatValue { get; set; }
    }

    public class PlayerAdminControllerHelper {

        public static async Task<VersionsReport> GetVersionStats(AppContext _context, int time = 60 * 60 * 24 * 7 * 2, int? maxRank = null)
        {
            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var activeTreshold = timeset - time;

            var query = maxRank != null ? _context
                .Scores
                .AsNoTracking()
                .Where(s => s.Player.ScoreStats.FirstScoreTime > activeTreshold && s.Timepost > activeTreshold && s.Player.Rank < maxRank) : _context
                .Scores
                .AsNoTracking()
                .Where(s => s.Player.ScoreStats.FirstScoreTime > activeTreshold && s.Timepost > activeTreshold);

            var scores = (await query
                .Select(s => new {
                    s.PlayerId,
                    s.Platform,
                    s.Timepost
                })
                .ToListAsync())
                .Select(s => new {
                    s.PlayerId,
                    Platform = s.Platform.Split(",")[1].Split("_").First() + (s.Platform.Split(",")[0] == "oculus" ? "quest" : "pc"),
                    s.Timepost
                })
                .Where(s => !s.Platform.Contains("quest"))
                .ToList();

            var keys = scores.DistinctBy(s => s.Platform).Select(s => s.Platform).ToList();
            var groups = scores.GroupBy(s => s.PlayerId + s.Platform).ToList();

            var totalCount = groups.Count();
            var result = new List<VersionStat>();
            foreach (var key in keys)
            {
                float value = ((float)groups.Count(g => g.First().Platform == key) / totalCount) * 100f;

                result.Add(new VersionStat {
                    Version = key,
                    Count = groups.Count(g => g.First().Platform == key),
                    Value = Math.Round(value, 2) + "%",
                    FloatValue = value
                });
            }

            return new VersionsReport {
                Stats = result.OrderByDescending(s => s.FloatValue).ToList(),
                Count = totalCount
            };
        }

        public static async Task<VersionsReport> GetVersionStatsScores(AppContext _context, int time = 60 * 60 * 24 * 7 * 2, int? maxRank = null)
        {
            var timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            var activeTreshold = timeset - time;

            var query = maxRank != null ? _context
                .Scores
                .AsNoTracking()
                .Where(s => s.Timepost > activeTreshold && s.Player.Rank < maxRank) : _context
                .Scores
                .AsNoTracking()
                .Where(s => s.Timepost > activeTreshold);

            var scores = (await query
                .Select(s => new {
                    s.PlayerId,
                    s.Platform
                })
                .ToListAsync())
                .Select(s => new {
                    s.PlayerId,
                    Platform = s.Platform.Split(",")[1].Split("_").First()
                })
                .ToList();

            var keys = scores.DistinctBy(s => s.Platform).Select(s => s.Platform).ToList();
            var groups = scores.GroupBy(s => s.Platform).ToList();

            var totalCount = scores.Count();
            var result = new List<VersionStat>();
            foreach (var group in groups)
            {
                float value = ((float)group.Count() / totalCount) * 100f;

                result.Add(new VersionStat {
                    Version = group.Key,
                    Value = Math.Round(value, 2) + "%",
                    FloatValue = value
                });
            }

            return new VersionsReport {
                Stats = result.OrderByDescending(s => s.FloatValue).ToList(),
                Count = totalCount
            };
        }
    }
}
