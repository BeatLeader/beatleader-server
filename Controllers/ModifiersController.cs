using System;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
namespace BeatLeader_Server.Controllers
{
    public class ModifiersController
    {
        [HttpGet("~/modifiers")]
        [SwaggerOperation(Summary = "Retrieve Modifiers", Description = "Provides a list of Beat Saber modifiers and their associated values.")]
        [SwaggerResponse(200, "Modifiers retrieved successfully", typeof(Dictionary<string, float>))]
        public ActionResult<Dictionary<string, float>> GetModifiers()
        {
            return ReplayUtils.LegacyModifiers();
        }
    }
}

