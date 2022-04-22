namespace BeatLeader_Server.Models
{
    public class Clan
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public string Icon { get; set; }
        public string Tag { get; set; }
        public string LeaderID { get; set; }

        public int PlayersCount { get; set; }
        public int Pp { get; set; }
        public int AverageRank { get; set; }
        public int AverageAccuracy { get; set; }
    }
}
