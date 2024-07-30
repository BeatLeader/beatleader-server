using BeatLeader_Server.ControllerHelpers;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BeatLeader_Server.Services {
    public class DeleteMessage {
        public string Type { get; set; }
        public string Msg { get; set; }
    }
    public class UpdateMessage {
        public string Type { get; set; }
        public MapDetail Msg { get; set; }
    }
    public class SongService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public static WebSocketClient SocketClient;

        public SongService(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (SocketClient == null) {
                SocketClient = new WebSocketClient("wss://ws.beatsaver.com/maps");
                await SocketClient.ConnectAsync();
            }
            do {
                try {
                    var message = await SocketClient.ReceiveMessagesAsync(stoppingToken);
                    if (message != null) {
                        UpdateMessage? mapMessage = null;
                        try {
                            mapMessage = JsonConvert.DeserializeObject<UpdateMessage>(message);
                        } catch {}
                        if (mapMessage != null) {
                            await MapWasUpdated(mapMessage);
                        } else { 
                            DeleteMessage? deleteMessage = null;
                            try {
                                deleteMessage = JsonConvert.DeserializeObject<DeleteMessage>(message);
                            } catch {}
                            if (deleteMessage != null) {
                                await MapWasDeleted(deleteMessage); 
                            }
                        }
                    }
                } catch (Exception e) {
                    Console.WriteLine($"EXCEPTION SongService {e}");
                }
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        private async Task MapWasUpdated(UpdateMessage mapMessage) {
            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();

                var map = mapMessage.Msg;

                var existingSong = await _context.Songs.Include(s => s.Mappers).Where(el => el.Hash.ToLower() == map.Versions[0].Hash.ToLower()).Include(s => s.Difficulties).FirstOrDefaultAsync();
                if (existingSong != null) {
                    await SongControllerHelper.UpdateFromMap(_context, existingSong, map);
                } else {
                    if (map.Versions[0].State == "Published") {
                        Song song = new Song();
                        song.FromMapDetails(map);
                        await SongControllerHelper.UpdateFromMap(_context, song, map);

                        await SongControllerHelper.AddNewSong(song, song.Hash, _context);
                        foreach (var item in song.Difficulties) {
                            await SongControllerHelper.NewLeaderboard(_context, song, null, item.DifficultyName, item.ModeName);
                        }
                    }
                }
            }
        }

        private async Task MapWasDeleted(DeleteMessage deleteMessage) {
            if (deleteMessage.Type != "MAP_DELETE") return;

            using (var scope = _serviceScopeFactory.CreateScope()) {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();
                var _s3Client = _configuration.GetS3Client();
                var songs = await _context.Songs.Where(el => el.Id == deleteMessage.Msg || el.Id.StartsWith(deleteMessage.Msg + "x")).Include(s => s.Difficulties).ToListAsync();
                foreach (var existingSong in songs) {
                    if (existingSong.Difficulties.FirstOrDefault(d => d.Status == DifficultyStatus.unranked) != null) {
                        foreach (var diff in existingSong.Difficulties) {
                            if (diff.Status == DifficultyStatus.unranked) {
                                diff.Status = DifficultyStatus.outdated;
                            }
                        }
                        _context.SaveChanges();
                    }
                }
            }
        }
    }    
}
