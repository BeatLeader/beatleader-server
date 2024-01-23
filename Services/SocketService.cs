using BeatLeader_Server.Utils;

namespace BeatLeader_Server.Services
{
    public class SocketService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private static List<WebSocketClient> Sockets = new List<WebSocketClient>();

        public SocketService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public static void AddSocketToManage(WebSocketClient webSocket) {
            Sockets.Add(webSocket);
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                foreach (var socket in Sockets)
                {
                    await socket.Ping();
                    await socket.ReceiveMessagesAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }
    }    
}
