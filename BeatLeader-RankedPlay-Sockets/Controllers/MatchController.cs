using BeatLeader_RankedPlay_Sockets.Core;
using BeatLeader_RankedPlay_Sockets.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;

namespace BeatLeader_RankedPlay_Sockets.Controllers {
    [ApiExplorerSettings(IgnoreApi = true)]
    public class MatchController : Controller {
        private readonly MatchmakingService _matchmaking;
        private readonly IHostApplicationLifetime _lifetime;

        public MatchController(
            MatchmakingService matchmaking,
            IHostApplicationLifetime lifetime) {
            _matchmaking = matchmaking;
            _lifetime = lifetime;
        }

        [Route("/rankedplay/match")]
        public async Task<ActionResult> ConnectMatchSocket() {
            if (!HttpContext.WebSockets.IsWebSocketRequest) {
                return BadRequest();
            }

            string? localId = HttpContext?.User?.Claims?
                .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                .Split("/").LastOrDefault();

            if (localId == null) {
                return Unauthorized();
            }

            var profile = await _matchmaking.RequestPlayerProfile(localId);
            if (profile == null) {
                return StatusCode(503, "Unable to fetch player profile");
            }

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            try {
                await _matchmaking.RegisterConnection(profile, webSocket);
                await ProcessPlayerConnection(webSocket, profile);
            } finally {
                _matchmaking.UnregisterConnection(profile.PlayerId, webSocket);
                await _matchmaking.LeaveQueue(profile.PlayerId);
                await _matchmaking.HandlePlayerDisconnect(profile.PlayerId);
            }

            return new EmptyResult();
        }

        private async Task ProcessPlayerConnection(WebSocket socket, BridgePlayerProfileMessage profile) {
            var buffer = new byte[4096];
            using var messageStream = new MemoryStream();

            while (socket.State == WebSocketState.Open && !_lifetime.ApplicationStopped.IsCancellationRequested) {
                WebSocketReceiveResult result;
                try {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _lifetime.ApplicationStopped);
                } catch (WebSocketException) { break; }

                if (result.MessageType == WebSocketMessageType.Close || result.Count == 0) break;

                if (result.MessageType == WebSocketMessageType.Text) {
                    messageStream.Write(buffer, 0, result.Count);

                    if (!result.EndOfMessage) continue;

                    var json = Encoding.UTF8.GetString(messageStream.ToArray());
                    messageStream.SetLength(0);

                    await HandlePlayerMessage(socket, json, profile);
                }
            }
        }

        private async Task HandlePlayerMessage(WebSocket socket, string json, BridgePlayerProfileMessage profile) {
            try {
                var obj = JObject.Parse(json);
                var type = obj["Type"]?.ToString();

                switch (type) {
                    case "joinQueue":
                        var entry = new QueueEntry {
                            PlayerId = profile.PlayerId,
                            PlayerName = profile.PlayerName,
                            MMR = profile.MMR,
                            Tier = profile.Tier,
                            TierDivision = profile.TierDivision,
                            IsCalibrated = profile.IsCalibrated,
                            CalibrationMatchesPlayed = profile.CalibrationMatchesPlayed,
                            GlobalRank = profile.GlobalRank,
                            IsBot = profile.IsBot,
                            Socket = socket,
                            JoinedAt = Time.UnixNow()
                        };

                        bool joined = await _matchmaking.JoinQueue(entry);
                        if (!joined) {
                            await MatchSession.SendMessage(socket, new ErrorMessage {
                                Type = "error",
                                Error = "Already in queue or in a match"
                            });
                        }
                        break;

                    case "leaveQueue":
                        await _matchmaking.LeaveQueue(profile.PlayerId);
                        await MatchSession.SendMessage(socket, new QueueStatusMessage {
                            Type = "queueStatus",
                            Status = "left"
                        });
                        break;

                    case "discardMap":
                        // LeaderboardId is optional — null/missing/empty = pass (no discard).
                        var discardId = obj["LeaderboardId"]?.ToString();
                        await _matchmaking.HandleDiscardMap(profile.PlayerId, discardId);
                        break;

                    case "pickMap":
                        var pickId = obj["LeaderboardId"]?.ToString() ?? "";
                        await _matchmaking.HandlePickMap(profile.PlayerId, pickId);
                        break;

                    case "mapReady":
                        await _matchmaking.HandleMapReady(profile.PlayerId);
                        break;

                    case "mapDownloadFailed":
                        // Client gave up — let the phase-timeout flow treat the round as a
                        // draw round. The simplest implementation is to mark the player as
                        // forfeiting just the current game; the matchmaking service then
                        // resolves the round once the opponent also signals. If the opponent
                        // also fails to download, the round resolves as a 0-0 draw.
                        await _matchmaking.HandleForfeitGame(profile.PlayerId);
                        break;

                    case "forfeitGame":
                        await _matchmaking.HandleForfeitGame(profile.PlayerId);
                        break;

                    case "forfeitMatch":
                        await _matchmaking.HandleForfeitMatch(profile.PlayerId);
                        break;
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error handling player message: {ex.Message}");
            }
        }
    }
}
