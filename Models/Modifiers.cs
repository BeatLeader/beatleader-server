using System.ComponentModel.DataAnnotations;

namespace BeatLeader_Server.Models
{
    public class ModifiersRating 
    {
        public int Id { get; set; }
        public float FSPredictedAcc { get; set; }
        public float FSPassRating { get; set; }
        public float FSAccRating { get; set; }
        public float FSTechRating { get; set; }
        public float FSStars { get; set; }

        public float SSPredictedAcc { get; set; }
        public float SSPassRating { get; set; }
        public float SSAccRating { get; set; }
        public float SSTechRating { get; set; }
        public float SSStars { get; set; }

        public float SFPredictedAcc { get; set; }
        public float SFPassRating { get; set; }
        public float SFAccRating { get; set; }
        public float SFTechRating { get; set; }
        public float SFStars { get; set; }
    }

    public class ModifiersMap
    {
        [Key]
        public int ModifierId { get; set; }

        public float DA { get; set; } = 0.0f;
        public float FS { get; set; } = 0.20f;
        public float SF { get; set; } = 0.36f;
        public float SS { get; set; } = -0.3f;
        public float GN { get; set; } = 0.04f;
        public float NA { get; set; } = -0.3f;
        public float NB { get; set; } = -0.2f;
        public float NF { get; set; } = -0.5f;
        public float NO { get; set; } = -0.2f;
        public float PM { get; set; } = 0.0f;
        public float SC { get; set; } = 0.0f;
        public float SA { get; set; } = 0.0f;
        public float OP { get; set; } = -0.5f;

        public static ModifiersMap RankedMap() {
            return new ModifiersMap {
                DA = 0.0f,
                FS = 0.20f * 2,
                SF = 0.36f * 2,
                SS = -0.3f,
                GN = 0.04f * 2,
                NA = -0.3f,
                NB = -0.2f,
                NF = -1.0f,
                NO = -0.2f,
                PM = 0.0f,
                SC = 0.0f,
                SA = 0.0f,
                OP = -0.5f,
            };
        }

        public bool EqualTo(ModifiersMap? other) {
            return other != null && DA == other.DA && FS == other.FS && SS == other.SS && SF == other.SF && GN == other.GN && NA == other.NA && NB == other.NB && NF == other.NF && NO == other.NO && PM == other.PM && SC == other.SC && SA == other.SA && OP == other.OP;
        }
    }
}
