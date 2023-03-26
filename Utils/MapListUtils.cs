using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Linq.Expressions;

namespace BeatLeader_Server.Utils
{
    public enum Operation
    {
        any = 0,
        all = 1,
        not = 2,
    }

    public static class MapListUtils
    {
        public static IQueryable<Leaderboard> Filter(
            this IQueryable<Leaderboard> source, 
            ReadAppContext context,
            string? sortBy = null,
            string? order = null,
            string? search = null,
            string? type = null,
            string? mode = null,
            int? mapType = null,
            Operation allTypes = 0,
            Requirements? mapRequirements = null,
            Operation allRequirements = 0,
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
                        case "ranking":
                            sequence = sequence.Where(s => 
                                (   
                                    date_from == null || 
                                    s.Difficulty.RankedTime >= date_from || 
                                    s.Difficulty.NominatedTime >= date_from || 
                                    s.Difficulty.QualifiedTime >= date_from ||
                                    s.Changes.OrderByDescending(ch => ch.Timeset).FirstOrDefault().Timeset >= date_from) && 
                                (
                                    date_to == null || 
                                    s.Difficulty.RankedTime <= date_to ||
                                    s.Difficulty.NominatedTime <= date_to ||
                                    s.Difficulty.QualifiedTime <= date_to ||
                                    s.Changes.OrderByDescending(ch => ch.Timeset).FirstOrDefault().Timeset <= date_to));
                            break;
                    }

                    break;
                case "name":
                    sequence = sequence
                        .Where(s => (date_from == null || s.Song.UploadTime >= date_from) && (date_to == null || s.Song.UploadTime <= date_to))
                        .Order(order, t => t.Song.Name);
                    break;
                case "stars":
                case "passRating":
                case "accRating":
                case "techRating":
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
                        .Include(lb => lb.Difficulty);
                    if (sortBy == "stars") {
                        sequence = sequence.Order(order, t => t.Difficulty.Stars);
                    } else if (sortBy == "passRating") {
                        sequence = sequence.Order(order, t => t.Difficulty.PassRating);
                    } else if (sortBy == "accRating") {
                        sequence = sequence.Order(order, t => t.Difficulty.AccRating);
                    } else if (sortBy == "techRating") {
                        sequence = sequence.Order(order, t => t.Difficulty.TechRating);
                    }
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
                        .Order(order, lb => lb.PositiveVotes - lb.NegativeVotes);
                    break;
                case "votecount":
                    sequence = sequence.Order(order, lb => lb.PositiveVotes + lb.NegativeVotes);
                    break;
                case "voteratio":
                    sequence = sequence
                        .Where(lb => lb.PositiveVotes > 0 || lb.NegativeVotes > 0)
                        .Order(order, lb => (int)(
                            lb.PositiveVotes
                            /
                            (lb.PositiveVotes + lb.NegativeVotes) * 100.0))
                        .ThenOrder(order, lb => lb.PositiveVotes + lb.NegativeVotes);
                    break;
                default:
                    break;
            }
            if (search != null)
            {
                string lowSearch = search.ToLower();
                sequence = sequence
                    .Where(p => p.Song.Id == lowSearch ||
                                p.Song.Hash == lowSearch ||
                                p.Song.Author.Contains(lowSearch) ||
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
                    case "ranking":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.OldModifiers)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.NewModifiers)
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.OldModifiers)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.NewModifiers)
                            .Where(p => p.Difficulty.Status != DifficultyStatus.unranked && p.Difficulty.Status != DifficultyStatus.outdated);
                        break;
                    case "nominated":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.OldModifiers)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.NewModifiers)
                            .Where(p => p.Difficulty.Status == DifficultyStatus.nominated);
                        break;
                    case "qualified":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.OldModifiers)
                            .Include(lb => lb.Qualification)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.NewModifiers)
                            .Where(p => p.Difficulty.Status == DifficultyStatus.qualified);
                        break;
                    case "reweighting":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.OldModifiers)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.NewModifiers)
                            .Where(p => p.Reweight != null && !p.Reweight.Finished);
                        break;
                    case "reweighted":
                        sequence = sequence
                            .Include(lb => lb.Difficulty)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.OldModifiers)
                            .Include(lb => lb.Reweight)
                            .ThenInclude(q => q.Changes)
                            .ThenInclude(ch => ch.NewModifiers)
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
                switch (allTypes)
                {
                    case Operation.any:
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => (p.Difficulty.Type & maptype) != 0);
                        break;
                    case Operation.all:
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Type == maptype);
                        break;
                    case Operation.not:
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => (p.Difficulty.Type & maptype) == 0);
                        break;
                }
            }

            if (mode != null)
            {
                sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.ModeName == mode);
            }

            if (mapRequirements != null)
            {
                Requirements maprequirements = (Requirements)mapRequirements;
                switch (allRequirements)
                {
                    case Operation.any:
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => (p.Difficulty.Requirements & maprequirements) != 0);
                        break;
                    case Operation.all:
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => p.Difficulty.Requirements == maprequirements);
                        break;
                    case Operation.not:
                        sequence = sequence.Include(lb => lb.Difficulty).Where(p => (p.Difficulty.Requirements & maprequirements) == 0);
                        break;
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
