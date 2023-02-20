using System;
namespace BeatLeader_Server.Models
{
    public class AccountLink
    {
        public int Id { get; set; }
        public string SteamID { get; set; } = "";
        public int OculusID { get; set; }
        public string PCOculusID { get; set; } = "";
    }

    public class AccountLinkRequest
    {
        public int Id { get; set; }
        public string OculusID { get; set; } = "";

        public int Random { get; set; }
        public string IP { get; set; }
    }
}

