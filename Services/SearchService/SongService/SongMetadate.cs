using BeatLeader_Server.Models;
using Lucene.Net.Documents;

namespace BeatLeader_Server.Services;

public class SongMetadata
{
    public string Id   { get; private set; }

    public string Hash { get; private set; }

    public string Name { get; private set; }

    public string Author { get; private set; }

    public string Mapper { get; private set; }

    public static explicit operator SongMetadata(Song song) =>
        new()
        {
            Id = song.Id.ToLower(),
            Hash = song.Hash.ToLower(),
            Name = song.Name.ToLower(),
            Author = song.Author.ToLower(),
            Mapper = song.Mapper.ToLower(),
        };

    public static explicit operator SongMetadata(Document doc) =>
        new()
        {
            Id = doc.Get(nameof(Id)),
            Hash = doc.Get(nameof(Hash)),
            Name = doc.Get(nameof(Name)),
            Author = doc.Get(nameof(Author)),
            Mapper = doc.Get(nameof(Mapper)),
        };

    public static explicit operator Document(SongMetadata songMetadata) =>
        new()
        {
            new StringField(nameof(Id), songMetadata.Id, Field.Store.YES),
            new StringField(nameof(Hash), songMetadata.Hash, Field.Store.YES),
            new TextField(nameof(Name), songMetadata.Name, Field.Store.YES),
            new TextField(nameof(Author), songMetadata.Author, Field.Store.YES),
            new TextField(nameof(Mapper), songMetadata.Mapper, Field.Store.YES),
        };
}