using System;
using BeatLeader_Server.Models;
using ReplayDecoder;

namespace BeatLeader_Server.Utils
{
    class ReplayStatisticUtils
    {
        public static (ScoreStatistic?, string?) ProcessReplay(Replay replay, Leaderboard leaderboard, bool allow = false)
        {
            try {
                string? error = CheckReplay(replay, leaderboard);
                if (!allow && error != null) {
                    return (null, error);
                }

                return ReplayStatistic.ProcessReplay(replay);
            } catch (Exception e) {
                return (null, e.Message);
            }
        }

        public static string? CheckReplay(Replay replay, Leaderboard leaderboard) {
            float endTime = replay.notes.Count > 0 ? replay.notes.Last().eventTime : 0;

            if (leaderboard.Difficulty.Notes / 3 != 0 && 
                (float)replay.notes.Count < ((float)leaderboard.Difficulty.Notes) * 0.8f && 
                !leaderboard.Difficulty.Requirements.HasFlag(Requirements.Noodles))
            {
                return "Too few notes in the replay";
            }

            if (leaderboard.Difficulty.Status == DifficultyStatus.ranked || 
                leaderboard.Difficulty.Status == DifficultyStatus.qualified ||
                leaderboard.Difficulty.Status == DifficultyStatus.nominated) {

                foreach (var note in replay.notes)
                {
                    NoteParams param = new NoteParams(note.noteID);
                    if (note.noteID < 100000 && note.noteID > 0 && endTime - note.eventTime > 1)
                    {
                        if (note.eventType == NoteEventType.good && param.colorType != note.noteCutInfo.saberType)
                        {
                            return "Wrong saber type on a good cut note";
                        }
                    }
                }
            }

            return null;
        }
    }
}