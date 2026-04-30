using ReplayDecoder;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeatLeader {
    public enum LevelEndType {
        Unknown = 0,
        Clear = 1,
        Fail = 2,
        Restart = 3,
        Quit = 4,
        Practice = 5
    }

    public class PlayEndData {
        public LevelEndType EndType { get; set; }
        public float Time { get; set; }
    }

    public class ReplaySocket {
        public static WebSocketClient? SocketClient;
        public static string SocketHost = "wss://sockets.api.beatleader.com/stream/player/post";

        private static readonly SemaphoreSlim _connectLock = new(1, 1);
        private static string? _cachedCookie;

        private static readonly string LoginUrl = "https://api.beatleader.com/signinoculus";
        private static readonly string OculusLogin = "StreamTester"; // 343133
        private static readonly string OculusPassword = "gb345v474bv43574bv74";

        private static async Task<string?> EnsureLoggedInAsync() {
            if (_cachedCookie != null) return _cachedCookie;

            var handler = new HttpClientHandler {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            using var client = new HttpClient(handler);

            var content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("login", OculusLogin),
                new KeyValuePair<string, string>("action", "login"),
                new KeyValuePair<string, string>("password", OculusPassword)
            });

            var loginResponse = await client.PostAsync(LoginUrl, content);

            var cookies = handler.CookieContainer.GetCookies(new Uri("https://api.beatleader.com"));
            var sb = new StringBuilder();
            foreach (Cookie c in cookies) {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append($"{c.Name}={c.Value}");
            }

            if (sb.Length == 0) {
                var setCookies = loginResponse.Headers
                    .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(h => h.Value);
                foreach (var raw in setCookies) {
                    var part = raw.Split(';')[0];
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(part);
                }
            }

            var cookie = sb.ToString();
            if (string.IsNullOrEmpty(cookie)) {
                Console.WriteLine($"Login failed: {loginResponse.StatusCode}");
                return null;
            }

            Console.WriteLine("Logged in successfully");
            _cachedCookie = cookie;
            return cookie;
        }

        public static async Task EnsureConnected() {
            if (SocketClient != null && SocketClient.IsAlive()) return;

            await _connectLock.WaitAsync();
            try {
                if (SocketClient != null && SocketClient.IsAlive()) return;

                SocketClient?.Dispose();
                SocketClient = null;

                string? cookie = await EnsureLoggedInAsync();
                if (cookie != null) {
                    SocketClient = new WebSocketClient(SocketHost, cookie);
                    await SocketClient.ConnectAsync();
                    Console.WriteLine("WebSocket connected");
                }
            } catch (Exception e) {
                Console.WriteLine($"Connection error: {e.Message}");
            } finally {
                _connectLock.Release();
            }
        }

        public static async Task PublishNewMessage(string message) {
            await EnsureConnected();
            SocketClient?.QueueText(message);
        }

        private static void QueueData(byte[] data) {
            SocketClient?.QueueBinary(data);
        }

        public static async Task LaunchedMap(ReplayInfo info) {
            try {
                await EnsureConnected();
            } catch {
                return;
            }

            using var stream = new MemoryStream();
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)StructType.info);
            ReplayEncoder.EncodeInfo(info, writer);
            QueueData(stream.ToArray());
        }

        public static void SendFrame(Frame frame) {
            using var stream = new MemoryStream(128);
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)StructType.frames);
            writer.Write((uint)1);
            ReplayEncoder.EncodeFrame(frame, writer);
            QueueData(stream.ToArray());
        }

        public static void SendNote(NoteEvent note) {
            using var stream = new MemoryStream(128);
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)StructType.notes);
            writer.Write((uint)1);
            ReplayEncoder.EncodeNote(note, writer);
            QueueData(stream.ToArray());
        }

        public static void SendWall(WallEvent wall) {
            using var stream = new MemoryStream(32);
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)StructType.walls);
            writer.Write((uint)1);
            ReplayEncoder.EncodeWall(wall, writer);
            QueueData(stream.ToArray());
        }

        public static void SendPause(Pause pause) {
            using var stream = new MemoryStream(32);
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)StructType.pauses);
            writer.Write((uint)1);
            ReplayEncoder.EncodePause(pause, writer);
            QueueData(stream.ToArray());
        }

        public static void SendHeight(AutomaticHeight height) {
            using var stream = new MemoryStream(16);
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)StructType.heights);
            writer.Write((uint)1);
            ReplayEncoder.EncodeHeight(height, writer);
            QueueData(stream.ToArray());
        }

        public static void FinishedMap(Replay replay, PlayEndData playEndData, bool submit) {
            using var stream = new MemoryStream();
            var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write((byte)99);
            ReplayEncoder.EncodeInfo(replay.info, writer);
            writer.Write((int)playEndData.EndType);
            writer.Write(playEndData.Time);
            writer.Write(submit);
            QueueData(stream.ToArray());
        }
    }
}
