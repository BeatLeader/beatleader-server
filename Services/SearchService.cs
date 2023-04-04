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

        public static List<string> SearchMaps(string query) {
            List<string> result = new List<string>();

            query = query.ToLower();

            foreach (var s in songs) {
                bool match = s.Id == query;
                if (!match) {
                    match |= s.Hash == query;
                }

                if (!match) {
                    match |= (s.Name.Length < 4 && s.Name == query) || (s.Name.Length >= 4 && Fuzz.WeightedRatio(s.Name, query) > 70);
                }

                if (!match) {
                    match |= (s.Author.Length < 4 && s.Author == query) || (s.Author.Length >= 4 && Fuzz.WeightedRatio(s.Author, query) > 70);
                }

                if (!match) {
                    match |= (s.Mapper.Length < 4 && s.Mapper == query) || (s.Mapper.Length >= 4 && Fuzz.WeightedRatio(s.Mapper, query) > 70);
                }

                if (!match) {
                    match |= s.Name.Contains(query);
                }

                if (match) {
                    result.Add(s.Id);
                }
            }

            return result;
        }

        public static List<LeaderboardInfoResponse> SortMaps(IEnumerable<LeaderboardInfoResponse> query, string searchQuery) {
            searchQuery = searchQuery.ToLower();

            return query.OrderByDescending(s => {
                if (s.Song.Id.ToLower() == searchQuery || s.Song.Hash.ToLower() == searchQuery) {
                    return 100;
                } else {
                    return Math.Max(
                        Math.Max(
                            Fuzz.WeightedRatio(s.Song.Name.ToLower(), searchQuery), Fuzz.WeightedRatio(s.Song.Author.ToLower(), searchQuery)
                            ), 
                        Fuzz.WeightedRatio(s.Song.Mapper.ToLower(), searchQuery));
                }
            }).ToList();
        }

        public static void SongAdded(string id, string hash, string name, string author, string mapper) {
            songs.Add(new SongMetadata { Id = id, Hash = hash, Name = name, Author = author, Mapper = mapper });
        }

        public static List<string> SearchPlayers(string query) {
            List<string> result = new List<string>();

            query = query.ToLower();

            var sdsd = players.Where(p => p.Names.FirstOrDefault(n => n == null) != null).ToList();

            foreach (var s in players) {
                if (s.Names.FirstOrDefault(x => (x.Length < 4 && x == query) || x.Contains(query) || (x.Length >= 4 && Fuzz.WeightedRatio(x, query) > 70)) != null) {
                    result.Add(s.Id);
                }
            }

            return result;
        }

        public static List<PlayerResponseWithStats> SortPlayers(IEnumerable<PlayerResponseWithStats> query, string searchQuery) {
            searchQuery = searchQuery.ToLower();

            return query.OrderByDescending(s => s.Name.Contains(searchQuery) ? 100 : Fuzz.WeightedRatio(s.Name.ToLower(), searchQuery)).ToList();
        }

        public static void PlayerAdded(string id, string name) {
            players.Add(new PlayerMetadata { Id = id, Names = { name } });
        }

        public static void PlayerChangedName(string id, string name) {
            players.FirstOrDefault(p => p.Id == id)?.Names.Add(name);
        }
    }
}
