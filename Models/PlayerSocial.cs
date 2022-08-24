using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Models
{
    public class PlayerSocial
    {
        public int Id { get; set; }
        public string Service { get; set; }
        public string Link { get; set; }
        public string User { get; set; }

        public string UserId { get; set; }
    }
}
