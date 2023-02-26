using System.ComponentModel.DataAnnotations.Schema;

namespace BeatLeader_Server.Models
{
    public class ClanRanking
    {
        public int Id { get; set; }
        public Clan? Clan { get; set; }
        public float ClanPP { get; set; }
    }
}
