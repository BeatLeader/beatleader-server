using BeatLeader_Server.Models;

namespace BeatLeader_Server.Services;

internal class SongMetadata
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
}