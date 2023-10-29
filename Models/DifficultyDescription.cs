using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public enum DifficultyStatus
    {
        unranked = 0,
        nominated = 1,
        qualified = 2,
        ranked = 3,
        unrankable = 4,
        outdated = 5,
        inevent = 6
    }

    [Flags]
    public enum Requirements
    {
        Ignore = -1,
        None = 0,
        Chroma = 1 << 1,
        Noodles = 1 << 2,
        MappingExtensions = 1 << 3,
        Cinema = 1 << 4,
        V3 = 1 << 5,
        OptionalProperties = 1 << 6,
    }

    [Index(nameof(Status), IsUnique = false)]
    public class DifficultyDescription
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public int Mode { get; set; }
        public string DifficultyName { get; set; }
        public string ModeName { get; set; }
        public DifficultyStatus Status { get; set; }
        public ModifiersMap? ModifierValues { get; set; } = new ModifiersMap();
        public ModifiersRating? ModifiersRating { get; set; }
        public int NominatedTime { get; set; }
        public int QualifiedTime { get; set; }
        public int RankedTime { get; set; }

        public float? Stars { get; set; }
        public float? PredictedAcc { get; set; }
        public float? PassRating { get; set; }
        public float? AccRating { get; set; }
        public float? TechRating { get; set; }
        public int Type { get; set; }

        public float Njs { get; set; }
        public float Nps { get; set; }
        public int Notes { get; set; }
        public int Bombs { get; set; }
        public int Walls { get; set; }
        public int MaxScore { get; set; }
        public double Duration { get; set; }

        public Requirements Requirements { get; set; }
    }
}
