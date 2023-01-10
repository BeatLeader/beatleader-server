using System.Net;

namespace BeatLeader_Server.Services
{
    public class _10MinRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public _10MinRefresh(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                try {
                    //await WebRequest.Create($"https://beatleaderhelperbot20221206231439.azurewebsites.net/").GetResponseAsync();
                } catch {}

                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
    }
}
