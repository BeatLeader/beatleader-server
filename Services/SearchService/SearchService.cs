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
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        this.FetchSearchItems();
    }

    private void FetchSearchItems()
    {
        using IServiceScope scope = this.serviceScopeFactory.CreateScope();

        AppContext context = scope.ServiceProvider.GetRequiredService<AppContext>();

        SongSearchService.AddNewSongs(context.Songs);

        PlayerSearchService.AddNewPlayers(context.Players.Include(p => p.Changes));
    }
}