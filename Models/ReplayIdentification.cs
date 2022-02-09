namespace BeatLeader_Server.Models
{
    public class ReplayIdentification
    {
        public Guid Id { get; set; }
        public byte[] Order { get; set; }
        public byte[] Value { get; set; }
    }
}
