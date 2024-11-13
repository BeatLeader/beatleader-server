using BeatLeader_Server.Models;
using Lucene.Net.Documents;

namespace BeatLeader_Server.Services;

public class PlayerSearchSelect {
    public string Id { get; set; }

    public string Name { get; set; }
    public string? Alias { get; set; }
    public string? OldAlias { get; set; }
    public string[]? Changes { get; set; }
}

public class PlayerMetadata
{
    public string Id { get; set; }

    public string Name { get; set; }

    public int Score { get; set; }

    public static List<PlayerMetadata> GetPlayerMetadata(PlayerSearchSelect player) {
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
            foreach (string change in player.Changes)
            {
                result.Add(new PlayerMetadata
                {
                    Id = player.Id.ToLower() + "_change_" + changeIndex,
                    Name = change.Replace(" ", "").ToLower()
                });
                changeIndex++;
            }
        }
        if (player.Alias != null) {
            result.Add(new PlayerMetadata
            {
                Id = player.Id.ToLower() + "_change_alias",
                Name = player.Alias.Replace(" ", "").ToLower()
            });
        }
        if (player.OldAlias != null) {
            result.Add(new PlayerMetadata
            {
                Id = player.Id.ToLower() + "_change_oldalias",
                Name = player.OldAlias.Replace(" ", "").ToLower()
            });
        }
        return result;
    }

    public static List<PlayerMetadata> GetPlayerMetadata(Player player) {
        return GetPlayerMetadata(new PlayerSearchSelect {
            Id = player.Id,
            Name = player.Name,
            Alias = player.Alias,
            OldAlias = player.OldAlias,
            Changes = player.Changes != null ? player.Changes.Where(c => c.OldName != null).Select(c => c.OldName).ToArray() : null
        });
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