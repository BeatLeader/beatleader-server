using BeatLeader_Server.Models;

namespace BeatLeader_Server.Utils;

public static partial class MapListUtils
{
    private static IQueryable<Leaderboard> FilterByMyType(this IQueryable<Leaderboard> sequence, ReadAppContext context, string? mytype, string? currentId)
    {
        if (string.IsNullOrEmpty(mytype))
        {
            return sequence;
        }

        return mytype switch
        {
            "played"          => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId) != null),
            "unplayed"        => sequence.Where(leaderboard => leaderboard.Scores.FirstOrDefault(score => score.PlayerId == currentId) == null),
            "mynominated"     => sequence.Where(leaderboard => leaderboard.Qualification != null && leaderboard.Qualification.RTMember == currentId),
            "othersnominated" => sequence.Where(leaderboard => leaderboard.Qualification != null && leaderboard.Qualification.RTMember != currentId),
            "mymaps"          => sequence.Where(leaderboard => leaderboard.Song.MapperId == context.Players.Find(currentId)!.MapperId),
            _                 => sequence,
        };
    }
}