using System.ComponentModel.DataAnnotations;

namespace BeatLeader_Server.Models
{
    public class ModifiersMap
    {
        [Key]
        public int ModifierId { get; set; }

        public float DA { get; set; } = 0.005f;
        public float FS { get; set; } = 0.11f;
        public float SS { get; set; } = -0.3f;
        public float SF { get; set; } = 0.25f;
        public float GN { get; set; } = 0.04f;
        public float NA { get; set; } = -0.3f;
        public float NB { get; set; } = -0.2f;
        public float NF { get; set; } = -0.5f;
        public float NO { get; set; } = -0.2f;
        public float PM { get; set; } = 0.0f;
        public float SC { get; set; } = 0.0f;

        public bool EqualTo(ModifiersMap? other) {
            return other != null && DA == other.DA && FS == other.FS && SS == other.SS && SF == other.SF && GN == other.GN && NA == other.NA && NB == other.NB && NF == other.NF && NO == other.NO && PM == other.PM && SC == other.SC;
        }
    }
}
