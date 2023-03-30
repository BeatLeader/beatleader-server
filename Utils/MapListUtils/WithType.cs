using BeatLeader_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> WithType(this IQueryable<Leaderboard> sequence, string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return sequence;
        }

        return type switch
        {
            "ranked"      => sequence.WithRanked(),
            "ranking"     => sequence.WithRanking(),
            "nominated"   => sequence.WithNominated(),
            "qualified"   => sequence.WithQualified(),
            "staff"       => sequence.WithStaff(),
            "reweighting" => sequence.WithReWeighting(),
            "reweighted"  => sequence.WithReWeighted(),
            "unranked"    => sequence.WithUnranked(),
            _             => sequence,
        };
    }

    private static IQueryable<Leaderboard> WithUnranked(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Where(p => p.Difficulty.Status == DifficultyStatus.unranked);

    private static IQueryable<Leaderboard> WithReWeighted(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Reweight)
                .ThenInclude(rankUpdate => rankUpdate.Changes)
                .ThenInclude(rankUpdateChange => rankUpdateChange.OldModifiers)
                .Include(leaderboard => leaderboard.Reweight)
                .ThenInclude(rankUpdate => rankUpdate.Changes)
                .ThenInclude(rankUpdateChange => rankUpdateChange.NewModifiers)
                .Where(leaderboard => leaderboard.Reweight != null && leaderboard.Reweight.Finished);

    private static IQueryable<Leaderboard> WithReWeighting(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Reweight)
                .ThenInclude(rankUpdate => rankUpdate.Changes)
                .ThenInclude(rankUpdateChange => rankUpdateChange.OldModifiers)
                .Include(leaderboard => leaderboard.Reweight)
                .ThenInclude(rankUpdate => rankUpdate.Changes)
                .ThenInclude(rankUpdateChange => rankUpdateChange.NewModifiers)
                .Where(leaderboard => leaderboard.Reweight != null && !leaderboard.Reweight.Finished);

    private static IQueryable<Leaderboard> WithQualified(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Changes)
                .ThenInclude(qualificationChange => qualificationChange.OldModifiers)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Changes)
                .ThenInclude(qualificationChange => qualificationChange.NewModifiers)
                .Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified);

    private static IQueryable<Leaderboard> WithStaff(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Votes)
                .Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.qualified || leaderboard.Difficulty.Status == DifficultyStatus.nominated);

    private static IQueryable<Leaderboard> WithNominated(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Changes)
                .ThenInclude(qualificationChange => qualificationChange.OldModifiers)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Changes)
                .ThenInclude(qualificationChange => qualificationChange.NewModifiers)
                .Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.nominated);

    private static IQueryable<Leaderboard> WithRanking(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Changes)
                .ThenInclude(qualificationChange => qualificationChange.OldModifiers)
                .Include(leaderboard => leaderboard.Qualification)
                .ThenInclude(rankQualification => rankQualification.Changes)
                .ThenInclude(qualificationChange => qualificationChange.NewModifiers)
                .Include(leaderboard => leaderboard.Difficulty)
                .Include(leaderboard => leaderboard.Reweight)
                .ThenInclude(rankUpdate => rankUpdate.Changes)
                .ThenInclude(rankUpdateChange => rankUpdateChange.OldModifiers)
                .Include(leaderboard => leaderboard.Reweight)
                .ThenInclude(rankUpdate => rankUpdate.Changes)
                .ThenInclude(rankUpdateChange => rankUpdateChange.NewModifiers)
                .Where(leaderboard => leaderboard.Difficulty.Status != DifficultyStatus.unranked && leaderboard.Difficulty.Status != DifficultyStatus.outdated);

    private static IQueryable<Leaderboard> WithRanked(this IQueryable<Leaderboard> sequence) =>
        sequence.Include(leaderboard => leaderboard.Difficulty)
                .Where(leaderboard => leaderboard.Difficulty.Status == DifficultyStatus.ranked);
}