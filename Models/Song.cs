using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace BeatLeader_Server.Models
{
    [Index(nameof(Hash), IsUnique = true)]
    public class Song
    {
        public string Id { get; set; }
        public string Hash { get; set; }
        public string Name { get; set; }
        [JsonIgnore]
        public string? Description { get; set; }
        public string? SubName { get; set; }
        public string Author { get; set; }
        public string Mapper { get; set; }
        public int MapperId { get; set; }
        public string CoverImage { get; set; }
        public string? FullCoverImage { get; set; }
        public string DownloadUrl { get; set; }
        public double Bpm { get; set; }
        public double Duration { get; set; }
        public string? Tags { get; set; }

        [JsonIgnore]
        public string CreatedTime { get; set; } = "";
        public int UploadTime { get; set; }
        public ICollection<DifficultyDescription> Difficulties { get; set; }

        [JsonIgnore]
        public bool Checked { get; set; }
        [JsonIgnore]
        public bool Refreshed { get; set; }

        [JsonIgnore]
        public ICollection<SongSearch> Searches { get; set; }
    }
}
