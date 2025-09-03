using Microsoft.EntityFrameworkCore;
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
        do {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            await FetchSearchItems();
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    private async Task FetchSearchItems()
    {
        using IServiceScope scope = serviceScopeFactory.CreateScope();

        AppContext context = scope.ServiceProvider.GetRequiredService<AppContext>();

        SongSearchService.AddNewSongs(
            await context
            .Songs
            .AsNoTracking()
            .Select(s => new SongMetadata {
                Id = s.Id.ToLower(),
                Hash = s.Hash.ToLower(),
                Name = s.Name.ToLower(),
                Author = s.Author.ToLower(),
                Mapper = s.Mapper.ToLower(),
            })
            .ToArrayAsync());

        PlayerSearchService.AddNewPlayers(
            await context
            .Players
            .AsNoTracking()
            .Select(p => new PlayerSearchSelect {
                Id = p.Id,
                Name = p.Name,
                Alias = p.Alias,
                OldAlias = p.OldAlias,
                Changes = p.Changes != null ? p.Changes.Where(c => c.OldName != null).Select(c => c.OldName).ToArray() : null
            })
            .ToArrayAsync());


        ScoreSearch.AvailableScores = await context.Scores.Where(s => s.Leaderboard.Difficulty.Status != Models.DifficultyStatus.outdated).Select(s => s.Id).ToListAsync();
    }
}