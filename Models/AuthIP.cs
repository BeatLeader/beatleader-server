using System;
namespace BeatLeader_Server.Models
{
    public class AuthIP
    {
        public int Id { get; set; }
        public string IP { get; set; }
        public int Timestamp { get; set; }
    }

    public class AuthID
    {
        public string Id { get; set; }
        public int Timestamp { get; set; }
    }

    public class CountryChange
    {
        public string Id { get; set; }
        public int Timestamp { get; set; }

        public string OldCountry { get; set; }
        public string NewCountry { get; set; }
    }
}

