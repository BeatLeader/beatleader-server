﻿using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using static BeatLeader_Server.Utils.SongUtils;

namespace BeatLeader_Server.Utils
{
    public class RatingUtils
    {
        private static float toPass(float original)
        {
            if (original < 24.4)
            {
                return original;
            } else
            {
                return 16 + MathF.Sqrt(original) * 1.7f;
            }
        }

        public static async Task UpdateFromExMachina(Leaderboard leaderboard, LeaderboardChange? rankChange)
        {

            await UpdateFromExMachina(leaderboard.Difficulty, leaderboard.Song, rankChange);
        }

        private static void UpdateFromResponse(DifficultyDescription diff, ExmachinaResponse? response, LeaderboardChange? rankChange)
        {
            if (response != null)
            {
                diff.PassRating = response.none.LackMapCalculation.PassRating;
                diff.TechRating = response.none.LackMapCalculation.TechRating;
                diff.PredictedAcc = response.none.PredictedAcc;
                diff.AccRating = response.none.AccRating;
                diff.Stars = ReplayUtils.ToStars(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);

                if (rankChange != null)
                {
                    rankChange.NewAccRating = diff.AccRating ?? 0;
                    rankChange.NewPassRating = diff.PassRating ?? 0;
                    rankChange.NewTechRating = diff.TechRating ?? 0;
                    rankChange.NewStars = diff.Stars ?? 0;
                }

                var modrating = diff.ModifiersRating = new ModifiersRating
                {
                    SSPassRating = response.SS.LackMapCalculation.PassRating,
                    SSTechRating = response.SS.LackMapCalculation.TechRating,
                    SSPredictedAcc = response.SS.PredictedAcc,
                    SSAccRating = response.SS.AccRating,

                    FSPassRating = response.FS.LackMapCalculation.PassRating,
                    FSTechRating = response.FS.LackMapCalculation.TechRating,
                    FSPredictedAcc = response.FS.PredictedAcc,
                    FSAccRating = response.FS.AccRating,

                    SFPassRating = response.SFS.LackMapCalculation.PassRating,
                    SFTechRating = response.SFS.LackMapCalculation.TechRating,
                    SFPredictedAcc = response.SFS.PredictedAcc,
                    SFAccRating = response.SFS.AccRating,

                    BFSPassRating = response.BFS.LackMapCalculation.PassRating,
                    BFSTechRating = response.BFS.LackMapCalculation.TechRating,
                    BFSPredictedAcc = response.BFS.PredictedAcc,
                    BFSAccRating = response.BFS.AccRating,

                    BSFPassRating = response.BSF.LackMapCalculation.PassRating,
                    BSFTechRating = response.BSF.LackMapCalculation.TechRating,
                    BSFPredictedAcc = response.BSF.PredictedAcc,
                    BSFAccRating = response.BSF.AccRating,
                };

                modrating.SFStars = ReplayUtils.ToStars(modrating.SFAccRating, modrating.SFPassRating, modrating.SFTechRating);
                modrating.FSStars = ReplayUtils.ToStars(modrating.FSAccRating, modrating.FSPassRating, modrating.FSTechRating);
                modrating.BSFStars = ReplayUtils.ToStars(modrating.BSFAccRating, modrating.BSFPassRating, modrating.BSFTechRating);
                modrating.BFSStars = ReplayUtils.ToStars(modrating.BFSAccRating, modrating.BFSPassRating, modrating.BFSTechRating);
                modrating.SSStars = ReplayUtils.ToStars(modrating.SSAccRating, modrating.SSPassRating, modrating.SSTechRating);
            } else
            {
                diff.PassRating = 0.0f;
                diff.PredictedAcc = 1.0f;
                diff.TechRating = 0.0f;
            }

        }

        public static async Task UpdateFromExMachina(DifficultyDescription diff, Song song, LeaderboardChange? rankChange)
        {
            try
            {
                var response = await ExmachinaStars(song.Hash, diff.Value, diff.ModeName);
                UpdateFromResponse(diff, response, rankChange);

            } catch {}
        }

        public static async Task UpdateFromExMachina(DifficultyDescription diff, string link, LeaderboardChange? rankChange)
        {
            try
            {
                var response = await ExmachinaStarsLink(link, diff.Value, diff.ModeName);
                UpdateFromResponse(diff, response, rankChange);

            } catch {}
        }

        public static async Task UpdateTags(Leaderboard leaderboard, LeaderboardChange rankChange)
        {
            try
            {
                var diff = leaderboard.Difficulty;
                var response = await ApiTags(diff.AccRating ?? 0, diff.PassRating ?? 0, diff.TechRating ?? 0);
                switch (response) {
                    case "acc":
                        diff.Type = 1;
                        break;
                    case "tech":
                        diff.Type = 2;
                        break;
                    case "midspeed":
                        diff.Type = 4;
                        break;
                    case "speed":
                        diff.Type = 8;
                        break;
                    default:
                        break;
                }
                rankChange.NewType = diff.Type;

            } catch {}
        }
    }
}
