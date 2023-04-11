using FuzzySharp;

namespace BeatLeader_Server.Services;

public class SearchService : BackgroundService
{
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IConfiguration configuration;

    public SearchService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        await this.FetchSearchItems();
    }

    private Task FetchSearchItems()
    {
        using IServiceScope scope = this.serviceScopeFactory.CreateScope();

        AppContext context = scope.ServiceProvider.GetRequiredService<AppContext>();

        SongSearchService.AddSongs(context);

        PlayerSearchService.AddPlayers(context);

        return Task.CompletedTask;
    }

    public static int ComparisonScore(string value, string query) => value == query
        ? 100
        : Math.Max(Fuzz.WeightedRatio(value, query), value.Contains(query) ? 71 : 0);

    public static int MapComparisonScore(string id, string hash, string name, string author, string mapper, string query)
    {
        if (id == query
         || hash == query)
        {
            return 100;
        }

        if (name == query
         || author == query
         || mapper == query)
        {
            return 100;
        }

        int score = name.Length >= 4 ? Fuzz.WeightedRatio(name, query) : 0;

        score = Math.Max(score, author.Length >= 4 ? Fuzz.WeightedRatio(author, query) : 0);
        score = Math.Max(score, mapper.Length >= 4 ? Fuzz.WeightedRatio(mapper, query) : 0);
        score = Math.Max(score, name.Contains(query) ? 71 : 0);

        return score;
    }
}