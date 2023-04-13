using BeatLeader_Server.Models;
using Lucene.Net.Documents;

namespace BeatLeader_Server.Services;

public class PlayerMetadata
{
    public string Id { get; set; }

    public List<string> Names { get; set; } = new();

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

    // null char is the only one that i could think of that players wouldnt have in their name
    public static explicit operator PlayerMetadata(Document doc) =>
        new()
        {
            Id = doc.Get(nameof(Id)),
            Names = doc.Get(nameof(Names)).Split('\0').ToList(),
        };

    public static explicit operator Document(PlayerMetadata playerMetadata)
    {
        Document document = new();
        Field idField = new(nameof(Id), playerMetadata.Id, Field.Store.YES, Field.Index.ANALYZED);
        Field namesField = new(nameof(Names), string.Join('\0', playerMetadata.Names), Field.Store.YES, Field.Index.ANALYZED);

        document.Add(idField);
        document.Add(namesField);

        return document;
    }
}