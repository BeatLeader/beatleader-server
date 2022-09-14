using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Linq.Expressions;

namespace BeatLeader_Server.Utils
{
    public static class MapListUtils
    {
        public static IQueryable<Leaderboard> Filter(this IQueryable<Leaderboard> source, 
            ReadAppContext context,
            string? sortBy = null,
            string? order = null,
             string? search = null,
            string? type = null,
             int? mapType = null,
           bool allTypes = false,
           string? mytype = null,
            float? stars_from = null,
           float? stars_to = null,
             int? date_from = null,
             int? date_to = null,
             string? currentID = null)
        {
            var sequence = source;
            switch (sortBy)
            {
                case "timestamp":
                    switch (type)
                    {
                        default:
                            sequence = sequence.Where(s => (date_from == null || s.Difficulty.RankedTime >= date_from) && (date_to == null || s.Difficulty.RankedTime <= date_to))
                            .Order(order, t => t.Difficulty.RankedTime);
                            break;
                        case "nominated":
                            sequence = sequence.Where(s => (date_from == null || s.Difficulty.NominatedTime >= date_from) && (date_to == null || s.Difficulty.NominatedTime <= date_to))
                            .Order(order, t => t.Difficulty.NominatedTime);
                            break;
                        case "qualified":
                            sequence = sequence.Where(s => (date_from == null || s.Difficulty.QualifiedTime >= date_from) && (date_to == null || s.Difficulty.QualifiedTime <= date_to))
                            .Order(order, t => t.Difficulty.QualifiedTime);
                            break;
                    }

                    break;
                case "name":
                    sequence = sequence
                        .Where(s => (date_from == null || s.Song.UploadTime >= date_from) && (date_to == null || s.Song.UploadTime <= date_to))
                        .Order(order == "desc" ? "asc" : "desc", t => t.Song.Name);
                    break;
                case "stars":
                    sequence = sequence
                        .Where(s => (date_from == null || (
                                        (s.Difficulty.Status == DifficultyStatus.nominated && s.Difficulty.NominatedTime >= date_from) ||
                                        (s.Difficulty.Status == DifficultyStatus.qualified && s.Difficulty.QualifiedTime >= date_from) ||
                                        (s.Difficulty.Status == DifficultyStatus.ranked && s.Difficulty.RankedTime >= date_from)
                                        ))
                                 && (date_to == null || (
                                        (s.Difficulty.Status == DifficultyStatus.nominated && s.Difficulty.NominatedTime <= date_to) ||
                                        (s.Difficulty.Status == DifficultyStatus.qualified && s.Difficulty.QualifiedTime <= date_to) ||
                                        (s.Difficulty.Status == DifficultyStatus.ranked && s.Difficulty.RankedTime <= date_to)
                                        )))
                        .Include(lb => lb.Difficulty).Order(order, t => t.Difficulty.Stars);
                    break;
                case "scoreTime":
                    if (mytype == "played")
                    {
                        sequence = sequence
                            .Order(order, lb =>
                                lb.Scores
                                    .Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to) && s.PlayerId == currentID)
                                    .Max(s => s.Timepost));
                    }
                    else
                    {
                        sequence = sequence
                            .Order(order, lb =>
                                lb.Scores
                                    .Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to))
                                    .Max(s => s.Timepost));
                    }

                    break;
                case "playcount":
                    sequence = sequence
                        .Order(order, lb =>
                            lb.Scores
                                .Where(s => (date_from == null || s.Timepost >= date_from) && (date_to == null || s.Timepost <= date_to))
                                .Count());
                    break;
                case "voting":
                    sequence = sequence
                        .Order(order, lb =>
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting.Rankability > 0).Count()
                            -
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting.Rankability <= 0).Count());
                    break;
                case "votecount":
                    sequence = sequence
                        .Order(order, lb =>
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null)
                                .Count());
                    break;
                case "voteratio":
                    sequence = sequence
                        .Where(lb =>
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null)
                                .FirstOrDefault() != null)
                        .Order(order, lb => (int)(
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting.Rankability > 0).Count()
                            /
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null).Count() * 100.0))
                        .ThenOrder(order, lb =>
                            lb.Scores
                                .Where(s => (date_from == null || s.RankVoting.Timeset >= date_from) && (date_to == null || s.RankVoting.Timeset <= date_to) && s.RankVoting != null).Count());
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Include(lb => lb.Song)
                    .Where(p => p.Song.Author.Contains(lowSearch) ||
                                p.Song.Mapper.Contains(lowSearch) ||
                                p.Song.Name.Contains(lowSearch));
            }

            if (type != null && type.Length != 0)
            {
                switch (type)
                {
                    case "ranked":
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Status == DifficultyStatus.ranked);
                        break;
                    case "nominated":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .Where(p => p.Difficulty.Status == DifficultyStatus.nominated);
                        break;
                    case "qualified":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .Where(p => p.Difficulty.Status == DifficultyStatus.qualified);
                        break;
                    case "reweighting":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .Where(p => p.Reweight != null && !p.Reweight.Finished);
                        break;
                    case "reweighted":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .Where(p => p.Reweight != null && p.Reweight.Finished);
                        break;
                    case "unranked":
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Status == DifficultyStatus.unranked);
                        break;
                }
            }
            if (mapType != null)
            {
                int maptype = (int)mapType;
                if (allTypes)
                {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Type == maptype);
                }
                else
                {
                    sequence = sequence.Include(lb => lb.Difficulty).Where(p => (p.Difficulty.Type & maptype) != 0);
                }
            }

            if (mytype != null && mytype.Length != 0)
            {
                switch (mytype)
                {
                    case "played":
                        sequence = sequence.Where(p => p.Scores.FirstOrDefault(s => s.PlayerId == currentID) != null);
                        break;
                    case "unplayed":
                        sequence = sequence.Where(p => p.Scores.FirstOrDefault(s => s.PlayerId == currentID) == null);
                        break;
                    case "mynominated":
                        sequence = sequence.Where(p => p.Qualification != null && p.Qualification.RTMember == currentID);
                        break;
                    case "othersnominated":
                        sequence = sequence.Where(p => p.Qualification != null && p.Qualification.RTMember != currentID);
                        break;
                    case "mymaps":
                        var currentPlayer = context.Players.Find(currentID);
                        sequence = sequence.Where(p => p.Song.MapperId == currentPlayer.MapperId);
                        break;
                }
            }

            if (stars_from != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Stars >= stars_from);
            }
            if (stars_to != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Stars <= stars_to);
            }

            return sequence;
        }
    }
}
