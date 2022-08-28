using System;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace BeatLeader_Server.Controllers
{
    public class ModifiersController
    {
        [HttpGet("~/modifiers")]
        public ActionResult<Dictionary<string, float>> GetModifiers()
        {
            return ReplayUtils.LegacyModifiers();
        }
    }
}

