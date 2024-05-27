using BeatLeader_Server.Models;
using Lucene.Net.Documents;

namespace BeatLeader_Server.Services;

public class PlayerMetadata
{
    public string Id { get; set; }

    public string Name { get; set; }

    public float Score { get; set; }

    public static List<PlayerMetadata> GetPlayerMetadata(Player player) {
        var result = new List<PlayerMetadata> {
            new PlayerMetadata
            {
                Id = player.Id.ToLower(),
                Name = player.Name.Replace(" ", "").ToLower()
            }
        };
        if (player.Changes != null)
        {
            int changeIndex = 0;
            foreach (PlayerChange change in player.Changes)
            {
                if (change.NewName != null)
                {
                    result.Add(new PlayerMetadata
                    {
                        Id = player.Id.ToLower() + "_change_" + changeIndex,
                        Name = change.NewName.Replace(" ", "").ToLower()
                    });
                    changeIndex++;
                }
            }
        }
        return result;
    }

    public static explicit operator PlayerMetadata(Document doc) =>
        new()
        {
            Id = doc.Get(nameof(Id)).Split("_").First(),
            Name = doc.Get(nameof(Name)),
        };

    public static explicit operator Document(PlayerMetadata playerMetadata) =>
        new()
        {
            new StringField(nameof(Id), playerMetadata.Id, Field.Store.YES),
            new TextField(nameof(Name), playerMetadata.Name, Field.Store.YES),
        };
}