using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
namespace BeatLeader_Server.Controllers
{
    public class LegacyModifiers {
        [SwaggerSchema("Dissapearing arrows")]
        public float DA { get; set; } = 0.005f;
        [SwaggerSchema("Faster song")]
        public float FS { get; set; } = 0.11f;
        [SwaggerSchema("Slower song")]
        public float SS { get; set; } = -0.3f;
        [SwaggerSchema("Super fast song")]
        public float SF { get; set; } = 0.25f;
        [SwaggerSchema("Ghost notes")]
        public float GN { get; set; } = 0.04f;
        [SwaggerSchema("No arrows")]
        public float NA { get; set; } = -0.3f;
        [SwaggerSchema("No bombs")]
        public float NB { get; set; } = -0.2f;
        [SwaggerSchema("No fail")]
        public float NF { get; set; } = -0.5f;
        [SwaggerSchema("No walls")]
        public float NO { get; set; } = -0.2f;
        [SwaggerSchema("Pro mode")]
        public float PM { get; set; } = 0.0f;
        [SwaggerSchema("Smaller notes")]
        public float SC { get; set; } = 0.0f;
    }
    public class ModifiersController
    {
        [HttpGet("~/modifiers")]
        [SwaggerOperation(Summary = "Retrieve Legacy Modifiers", Description = "Provides a list of Beat Saber modifiers and their associated score multiplier values. This is legacy support, for the recent values please use `modifierValues` and `modifierRatings` on leaderboards.")]
        [SwaggerResponse(200, "Modifiers retrieved successfully", typeof(LegacyModifiers))]
        public ActionResult<LegacyModifiers> GetModifiers()
        {
            return new LegacyModifiers();
        }
    }
}

