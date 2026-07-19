using BeatLeader_Server_Sockets.Controllers;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using ReplayDecoder;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using static BeatLeader_Server.Utils.ResponseUtils;

public class StatusCommand {
    public string status { get; set; }
}

public enum LevelEndType {
        Unknown = 0,
        Clear = 1,
        Fail = 2,
        Restart = 3,
        Quit = 4,
        Practice = 5
    }

namespace BeatLeader_Replay_Sockets {
    public class Player {

        public string playerId;

        public WebSocket? socket;
        public TaskCompletionSource? finishTask;
        public CancellationToken cancellationToken;

        public Replay? replay = null;
        public CompactLeaderboardResponse? map = null;

        public LevelEndType endType = LevelEndType.Unknown;
        public float failTime = -1;
        public bool shouldUpload = false;

        private readonly List<Viewer> _replayViewers = new();
        private readonly List<Viewer> _statusViewers = new();
        private readonly object _viewersLock = new();

        private MultiplierCounter _normalCounter = new();
        private MultiplierCounter _maxCounter = new();
        private int _totalScore;
        private int _maxScore;
        private int _combo;
        private int _mistakes;
        private int _processedNoteCount;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        public bool HasViewers {
            get {
                lock (_viewersLock) {
                    return _replayViewers.Count > 0 || _statusViewers.Count > 0;
                }
            }
        }

        public Player(string playerId) {
            this.playerId = playerId;
        }

        public async Task AttachSocket(WebSocket socket, TaskCompletionSource task, CancellationToken token) {
            this.socket = socket;
            this.finishTask = task;
            this.cancellationToken = token;

            await BroadcastPlayerState(new PlayerState {
                Id = playerId,
                Status = "online"
            });
        }

        public async Task DetachSocket(WebSocket expected) {
            if (this.socket != expected) return;

            this.socket = null;
            this.finishTask = null;
            this.replay = null;
            this.map = null;
            this.endType = LevelEndType.Unknown;
            this.failTime = -1;
            this.shouldUpload = false;
            ResetScoreTracking();

            await BroadcastPlayerState(new PlayerState {
                Id = playerId,
                Status = "offline"
            });
        }

        public void AddReplayViewer(Viewer viewer) {
            lock (_viewersLock) {
                _replayViewers.Add(viewer);
            }
        }

        public void RemoveReplayViewer(Viewer viewer) {
            lock (_viewersLock) {
                _replayViewers.Remove(viewer);
            }
        }

        public void AddStatusViewer(Viewer viewer) {
            lock (_viewersLock) {
                _statusViewers.Add(viewer);
            }
        }

        public void RemoveStatusViewer(Viewer viewer) {
            lock (_viewersLock) {
                _statusViewers.Remove(viewer);
            }
        }

        public void RemoveViewer(Viewer viewer) {
            if (viewer.viewerType == ViewerType.Replay) {
                RemoveReplayViewer(viewer);
            } else if (viewer.viewerType == ViewerType.Status) {
                RemoveStatusViewer(viewer);
            }
        }

        private async Task BroadcastData(byte[] data) {
            List<Viewer> snapshot;
            lock (_viewersLock) {
                for (int i = _replayViewers.Count - 1; i >= 0; i--) {
                    if (_replayViewers[i].socket.State != WebSocketState.Open) {
                        _replayViewers[i].finishTask.TrySetResult();
                        _replayViewers.RemoveAt(i);
                    }
                }
                snapshot = _replayViewers.ToList();
            }

            if (snapshot.Count == 0) return;

            var segment = new ArraySegment<byte>(data);
            await Task.WhenAll(snapshot.Select(v => SafeSendAsync(v, segment)));
        }

        private async Task BroadcastPlayerState(PlayerState state) {
            List<Viewer> snapshot;
            lock (_viewersLock) {
                for (int i = _statusViewers.Count - 1; i >= 0; i--) {
                    if (_statusViewers[i].socket.State != WebSocketState.Open) {
                        _statusViewers[i].finishTask.TrySetResult();
                        _statusViewers.RemoveAt(i);
                    }
                }
                snapshot = _statusViewers.ToList();
            }

            if (snapshot.Count == 0) return;

            var json = JsonConvert.SerializeObject(state);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);
            await Task.WhenAll(snapshot.Select(v => SafeSendTextAsync(v, segment)));
        }

        private async Task SafeSendAsync(Viewer viewer, ArraySegment<byte> data) {
            try {
                await viewer.socket.SendAsync(data, WebSocketMessageType.Binary, true, viewer.cancellationToken);
            } catch (Exception ex) {
                Console.WriteLine($"Failed to send to viewer for player {playerId}: {ex.Message}");
                viewer.finishTask.TrySetResult();
            }
        }

        private async Task SafeSendTextAsync(Viewer viewer, ArraySegment<byte> data) {
            try {
                await viewer.socket.SendAsync(data, WebSocketMessageType.Text, true, viewer.cancellationToken);
            } catch (Exception ex) {
                Console.WriteLine($"Failed to send status to viewer for player {playerId}: {ex.Message}");
                viewer.finishTask.TrySetResult();
            }
        }

        public async Task StartProcessing() {
            if (socket == null) return;
            var ws = socket;

            var buffer = new byte[64 * 1024];
            using var messageStream = new MemoryStream();

            try {
                while (!cancellationToken.IsCancellationRequested) {
                    WebSocketReceiveResult result;
                    try {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    } catch (WebSocketException) { break; }

                    if (result.MessageType == WebSocketMessageType.Close || result.Count == 0) break;

                    if (result.MessageType == WebSocketMessageType.Binary) {
                        messageStream.Write(buffer, 0, result.Count);

                        if (!result.EndOfMessage) continue;

                        var messageData = messageStream.ToArray();
                        messageStream.SetLength(0);

                        await ProcessBinaryMessage(messageData);
                        await BroadcastData(messageData);
                    } else if (result.MessageType == WebSocketMessageType.Text) {
                        messageStream.Write(buffer, 0, result.Count);

                        if (!result.EndOfMessage) continue;

                        var text = Encoding.UTF8.GetString(messageStream.ToArray());
                        messageStream.SetLength(0);

                        StatusCommand? statusMessage = null;
                        try {
                            statusMessage = JsonConvert.DeserializeObject<StatusCommand>(text);
                        } catch {}

                        if (statusMessage != null) {
                            await BroadcastPlayerState(new PlayerState {
                                Id = playerId,
                                Status = statusMessage.status
                            });
                        }
                    }

                    if (finishTask != null && finishTask.Task.IsCompleted) break;
                }
            } catch (OperationCanceledException) { }
            catch (Exception ex) {
                Console.WriteLine($"Player {playerId} processing error: {ex}");
            }
        }

        private async Task ProcessBinaryMessage(byte[] data) {
            int pointer = 0;
            int length = data.Length;

            while (pointer < length) {
                int value = data[pointer++];
                try {
                    if (value >= 0 && value <= (int)StructType.pauses) {
                        StructType type = (StructType)value;

                        switch (type) {
                            case StructType.info:
                                replay = new Replay();
                                replay.info = ReplayDecoder.ReplayDecoder.DecodeInfo(data, ref pointer);
                                ResetScoreTracking();
                                await OnMapStarted();
                                break;
                            case StructType.frames:
                                if (replay != null) replay.frames.AddRange(ReplayDecoder.ReplayDecoder.DecodeFrames(data, ref pointer));
                                break;
                            case StructType.notes:
                                if (replay != null) {
                                    var newNotes = ReplayDecoder.ReplayDecoder.DecodeNotes(data, ref pointer);
                                    replay.notes.AddRange(newNotes);
                                    await ProcessIncrementalNotes(newNotes);
                                }
                                break;
                            case StructType.walls:
                                if (replay != null) {
                                    var newWalls = ReplayDecoder.ReplayDecoder.DecodeWalls(data, ref pointer);
                                    replay.walls.AddRange(newWalls);
                                    await ProcessIncrementalWalls(newWalls);
                                }
                                break;
                            case StructType.heights:
                                if (replay != null) replay.heights.AddRange(ReplayDecoder.ReplayDecoder.DecodeHeights(data, ref pointer));
                                break;
                            case StructType.pauses:
                                if (replay != null) {
                                    var newPauses = ReplayDecoder.ReplayDecoder.DecodePauses(data, ref pointer);
                                    replay.pauses.AddRange(newPauses);
                                    foreach (var pause in newPauses) {
                                        await BroadcastPlayerState(new PlayerState {
                                            Id = playerId,
                                            Status = "paused",
                                            Context = BuildPlayingContext(),
                                            Event = new StreamEvent { AtTime = pause.time, Event = EventType.Pause }
                                        });
                                    }
                                }
                                break;
                        }
                    } else if (value == 99) {
                        if (replay != null) {
                            replay.info = ReplayDecoder.ReplayDecoder.DecodeInfo(data, ref pointer);
                        }
                        endType = (LevelEndType)ReplayDecoder.ReplayDecoder.DecodeInt(data, ref pointer);
                        failTime = ReplayDecoder.ReplayDecoder.DecodeFloat(data, ref pointer);
                        shouldUpload = ReplayDecoder.ReplayDecoder.DecodeBool(data, ref pointer);

                        await BroadcastPlayerState(new PlayerState {
                            Id = playerId,
                            Status = endType.ToString(),
                            Context = BuildPlayingContext()
                        });
                    }
                } catch (Exception e) {
                    Console.WriteLine($"Chunk parse exception {e}"); 
                }
            }
        }

        private void ResetScoreTracking() {
            _normalCounter = new MultiplierCounter();
            _maxCounter = new MultiplierCounter();
            _totalScore = 0;
            _maxScore = 0;
            _combo = 0;
            _mistakes = 0;
            _processedNoteCount = 0;
        }

        private async Task OnMapStarted() {
            if (replay?.info == null) return;

            map = await SocketsController.RequestMapInfo(
                replay.info.hash,
                replay.info.difficulty,
                replay.info.mode);

            await BroadcastPresenceUpdate("Playing");

            var context = new PlayingWithMap {
                Time = 0,
                Accuracy = 1,
                Score = 0,
                Pp = 0,
                Map = map
            };

            await BroadcastPlayerState(new PlayerState {
                Id = playerId,
                Status = "startedMap",
                Context = context
            });
        }

        private async Task ProcessIncrementalNotes(List<NoteEvent> newNotes) {
            StreamEvent? lastEvent = null;

            foreach (var note in newNotes) {
                var param = new NoteParams(note.noteID, note.eventType);
                int scoreValue = ReplayStatistic.ScoreForNote(note, param.scoringType);
                var scoreDefinition = ScoringExtensions.ScoreDefinitions[param.scoringType];

                if (note.eventType != NoteEventType.bomb) {
                    _maxCounter.Increase();
                    _maxScore += _maxCounter.Multiplier * scoreDefinition.maxCutScore;
                }

                if (scoreValue < 0) {
                    _normalCounter.Decrease();
                    _combo = 0;
                    _mistakes++;

                    switch (note.eventType) {
                        case NoteEventType.miss:
                            lastEvent = new StreamEvent { AtTime = note.eventTime, Event = EventType.Miss };
                            break;
                        case NoteEventType.bad:
                            lastEvent = new StreamEvent { AtTime = note.eventTime, Event = EventType.BadCut };
                            break;
                        case NoteEventType.bomb:
                            lastEvent = new StreamEvent { AtTime = note.eventTime, Event = EventType.BombHit };
                            break;
                    }
                } else {
                    _normalCounter.Increase();
                    _combo++;
                    _totalScore += _normalCounter.Multiplier * scoreValue;
                }

                _processedNoteCount++;
            }

            float accuracy = _maxScore > 0 ? (float)_totalScore / _maxScore : 1f;
            float lastTime = newNotes.LastOrDefault()?.eventTime ?? 0;

            await BroadcastPlayerState(new PlayerState {
                Id = playerId,
                Status = "playing",
                Context = new Playing {
                    Time = lastTime,
                    Accuracy = accuracy,
                    Score = _totalScore,
                    Pp = 0,
                    Combo = _combo,
                    Mistakes = _mistakes
                },
                Event = lastEvent
            });
        }

        private async Task ProcessIncrementalWalls(List<WallEvent> newWalls) {
            foreach (var wall in newWalls) {
                _normalCounter.Decrease();
                _combo = 0;
                _mistakes++;

                await BroadcastPlayerState(new PlayerState {
                    Id = playerId,
                    Status = "playing",
                    Context = BuildPlayingContext(),
                    Event = new StreamEvent { AtTime = wall.time, Event = EventType.WallHit }
                });
            }
        }

        private Playing BuildPlayingContext() {
            float accuracy = _maxScore > 0 ? (float)_totalScore / _maxScore : 1f;
            float lastTime = replay?.notes.LastOrDefault()?.eventTime
                ?? replay?.frames.LastOrDefault()?.time ?? 0;

            return new Playing {
                Time = lastTime,
                Accuracy = accuracy,
                Score = _totalScore,
                Pp = 0,
                Combo = _combo,
                Mistakes = _mistakes
            };
        }

        private async Task BroadcastPresenceUpdate(string status) {
            await SocketsController.BroadcastPresenceUpdate(playerId, status);
        }

        public async Task AddViewer(Viewer viewer) {
            if (viewer.viewerType == ViewerType.Replay) {
                AddReplayViewer(viewer);
            }

            if (viewer.viewerType == ViewerType.Status) {
                AddStatusViewer(viewer);

                PlayerState state;
                if (IsConnected) {
                    if (replay?.info != null && map != null) {
                        state = new PlayerState {
                            Id = playerId,
                            Status = "startedMap",
                            Context = new PlayingWithMap {
                                Time = replay.notes.LastOrDefault()?.eventTime
                                    ?? replay.frames.LastOrDefault()?.time ?? 0,
                                Accuracy = _maxScore > 0 ? (float)_totalScore / _maxScore : 1f,
                                Score = _totalScore,
                                Pp = 0,
                                Combo = _combo,
                                Mistakes = _mistakes,
                                Map = map
                            }
                        };
                    } else if (replay?.info != null) {
                        state = new PlayerState {
                            Id = playerId,
                            Status = "playing",
                            Context = BuildPlayingContext()
                        };
                    } else {
                        state = new PlayerState {
                            Id = playerId,
                            Status = "online"
                        };
                    }
                } else {
                    state = new PlayerState {
                        Id = playerId,
                        Status = "offline"
                    };
                }

                var json = JsonConvert.SerializeObject(state);
                var bytes = Encoding.UTF8.GetBytes(json);
                await SafeSendTextAsync(viewer, new ArraySegment<byte>(bytes));

                return;
            }

            if (replay != null && viewer.viewerType == ViewerType.Replay) {
                using var recalculatedStream = new MemoryStream();
                var stream = new BinaryWriter(recalculatedStream, Encoding.UTF8);

                for (int a = 0; a < ((int)StructType.pauses) + 1; a++) {
                    StructType type = (StructType)a;
                    switch (type) {
                        case StructType.info:
                            stream.Write((byte)a);
                            ReplayEncoder.EncodeInfo(replay.info, stream);
                            break;
                        case StructType.frames:
                            if (replay.frames.Count > 0) {
                                stream.Write((byte)a);
                                ReplayEncoder.EncodeFrames(replay.frames, stream);
                            }
                            break;
                        case StructType.notes:
                            if (replay.notes.Count > 0) {
                                stream.Write((byte)a);
                                ReplayEncoder.EncodeNotes(replay.notes, stream);
                            }
                            break;
                        case StructType.walls:
                            if (replay.walls.Count > 0) {
                                stream.Write((byte)a);
                                ReplayEncoder.EncodeWalls(replay.walls, stream);
                            }
                            break;
                        case StructType.heights:
                            if (replay.heights.Count > 0) {
                                stream.Write((byte)a);
                                ReplayEncoder.EncodeHeights(replay.heights, stream);
                            }
                            break;
                        case StructType.pauses:
                            if (replay.pauses.Count > 0) {
                                stream.Write((byte)a);
                                ReplayEncoder.EncodePauses(replay.pauses, stream);
                            }
                            break;
                    }
                }

                await SafeSendAsync(viewer, new ArraySegment<byte>(recalculatedStream.ToArray()));
            }
        }
    }
}
