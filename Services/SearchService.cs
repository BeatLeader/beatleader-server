using FuzzySharp;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Services {

    public class SearchService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        class SongMetadata {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Hash { get; set; }
            public string Author { get; set; }
            public string Mapper { get; set; }
        }

        class PlayerMetadata {
            public string Id { get; set; }
            public List<string> Names { get; set; }
        }

        private static List<SongMetadata> songs = new List<SongMetadata>();
        private static List<PlayerMetadata> players = new List<PlayerMetadata>();

        public SearchService(
            IServiceScopeFactory serviceScopeFactory, 
            IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            await FetchSearchItems();
        }

        private async Task FetchSearchItems() {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                songs = _context.Songs.Select(s => new SongMetadata { 
                    Id = s.Id.ToLower(), 
                    Name = s.Name.ToLower(),
                    Hash = s.Hash.ToLower(),
                    Author = s.Author.ToLower(),
                    Mapper = s.Mapper.ToLower()
                }).ToList();

                players = _context.Players.Select(p => new { 
                    p.Id, 
                    p.Name, 
                    OldNames = p.Changes.Where(c => c.NewName != null).Select(c => c.NewName) })
                 .ToList()
                 .Select(p => { 
                    var names = new List<string>();
                    if (p.OldNames != null) {
                        foreach (var item in p.OldNames) {
                            if (item != null) {
                                names.Add(item.ToLower());
                            }
                        }
                    }
                    names.Add(p.Name.ToLower());

                    return new PlayerMetadata {
                        Id = p.Id.ToLower(),
                        Names = names,
                        };
                    })
                 .ToList();
            }
        }

        private static int ComparisonScore(string value, string query) {
            if (value == query) {
                return 100;
            } else {
                return Math.Max(Fuzz.WeightedRatio(value, query), value.Contains(query) ? 71 : 0);
            }   
        }

        private static int MapComparisonScore(string id, string hash, string name, string author, string mapper, string query) {
            if (id == query || hash == query) return 100;
            if (name == query || author == query || mapper == query) return 100;

            var score = name.Length >= 4 ? Fuzz.WeightedRatio(name, query) : 0;

            score = Math.Max(score, author.Length >= 4 ? Fuzz.WeightedRatio(author, query) : 0);
            score = Math.Max(score, mapper.Length >= 4 ? Fuzz.WeightedRatio(mapper, query) : 0);
            score = Math.Max(score, name.Contains(query) ? 71 : 0);

            return score;
        }

        public class SongMatch {
            public string Id { get; set; }
            public int Score { get; set; }
        }

        public static List<SongMatch> SearchMaps(string query) {
            var result = new List<SongMatch>();

            query = query.ToLower();

            foreach (var s in songs) {

                var score = MapComparisonScore(s.Id, s.Hash, s.Name, s.Author, s.Mapper, query);
                if (score > 70) {
                    result.Add(new SongMatch {Id = s.Id, Score = score });
                }
            }

            return result;
        }

        public static List<LeaderboardInfoResponse> SortMaps(IEnumerable<LeaderboardInfoResponse> query, string searchQuery) {
            searchQuery = searchQuery.ToLower();

            return query.OrderByDescending(s => MapComparisonScore(
                s.Song.Id.ToLower(), 
                s.Song.Hash.ToLower(), 
                s.Song.Name.ToLower(), 
                s.Song.Author.ToLower(), 
                s.Song.Mapper.ToLower(), 
                searchQuery)).ToList();
        }

        public static void SongAdded(string id, string hash, string name, string author, string mapper) {
            songs.Add(new SongMetadata { Id = id, Hash = hash, Name = name, Author = author, Mapper = mapper });
        }

        public class PlayerMatch {
            public string Id { get; set; }
            public int Score { get; set; }
        }

        public static List<PlayerMatch> SearchPlayers(string query) {
            var result = new List<PlayerMatch>();

            query = query.ToLower();

            foreach (var s in players) {
                var match = s.Names.FirstOrDefault(x => (x.Length < 4 && x == query) || x.Contains(query) || (x.Length >= 4 && Fuzz.WeightedRatio(x, query) > 70));
                if (match != null) {
                    result.Add(new PlayerMatch {Id = s.Id, Score = ComparisonScore(match, query) });
                }
            }

            return result;
        }

        public static List<PlayerResponseWithStats> SortPlayers(IEnumerable<PlayerResponseWithStats> query, string searchQuery) {
            searchQuery = searchQuery.ToLower();

            return query.OrderByDescending(s => ComparisonScore(s.Name.ToLower(), searchQuery)).ToList();
        }

        public static void PlayerAdded(string id, string name) {
            players.Add(new PlayerMetadata { Id = id, Names = { name } });
        }

        public static void PlayerChangedName(string id, string name) {
            players.FirstOrDefault(p => p.Id == id)?.Names.Add(name);
        }
    }
}
