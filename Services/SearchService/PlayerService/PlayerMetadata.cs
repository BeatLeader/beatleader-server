using BeatLeader_Server.Models;

namespace BeatLeader_Server.Services;

internal class PlayerMetadata
{
    public string Id { get; set; }

    public List<string> Names { get; set; }

    public static explicit operator PlayerMetadata(Player player)
    {
        PlayerMetadata playerMetadata = new()
        {
            Id = player.Id.ToLower(),
            Names = { player.Name.ToLower() },
        };

        if (player.Changes != null)
        {
            foreach (PlayerChange change in player.Changes)
            {
                if (change.NewName != null)
                {
                    playerMetadata.Names.Add(change.NewName.ToLower());
                }
            }
        }

        return playerMetadata;
    }
}