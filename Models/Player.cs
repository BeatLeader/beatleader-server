namespace BeatLeader_Server.Models
{
    public class Player
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Platform { get; set; }
        public string Avatar { get; set; }
        public string Country { get; set; }
        public float Pp { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
    }
}
