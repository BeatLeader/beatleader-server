namespace BeatLeader_Server.Models
{
    public class PlayerFriends
    {
        public string Id { get; set; }
        public ICollection<Player> Friends { get; set; } = new List<Player>();
    }
}
