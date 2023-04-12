using BeatLeader_Server.Models;
using Lucene.Net.Documents;

namespace BeatLeader_Server.Services;

public class SongMetadata
{
    public string Id { get; private set; }

    public string Name { get; private set; }

    public string Hash { get; private set; }

    public string Author { get; private set; }

    public string Mapper { get; private set; }

    public static explicit operator SongMetadata(Song song) =>
        new()
        {
            Id = song.Id.ToLower(),
            Name = song.Name.ToLower(),
            Hash = song.Hash.ToLower(),
            Author = song.Author.ToLower(),
            Mapper = song.Mapper.ToLower(),
        };

    public static explicit operator SongMetadata(Document doc) =>
        new()
        {
            Id = doc.Get(nameof(Id)),
            Name = doc.Get(nameof(Name)),
            Hash = doc.Get(nameof(Hash)),
            Author = doc.Get(nameof(Author)),
            Mapper = doc.Get(nameof(Mapper)),
        };

    public static explicit operator Document(SongMetadata songMetadata)
    {
        Document document = new();
        Field idField = new(nameof(Id), songMetadata.Id, Field.Store.YES, Field.Index.ANALYZED);
        Field nameField = new(nameof(Name), songMetadata.Name, Field.Store.YES, Field.Index.ANALYZED);
        Field hashField = new(nameof(Hash), songMetadata.Hash, Field.Store.YES, Field.Index.ANALYZED);
        Field authorField = new(nameof(Author), songMetadata.Author, Field.Store.YES, Field.Index.ANALYZED);
        Field mapperField = new(nameof(Mapper), songMetadata.Mapper, Field.Store.YES, Field.Index.ANALYZED);

        document.Add(idField);
        document.Add(nameField);
        document.Add(hashField);
        document.Add(authorField);
        document.Add(mapperField);

        return document;
    }
}