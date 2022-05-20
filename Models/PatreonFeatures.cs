namespace BeatLeader_Server.Models
{
    public class PatreonFeatures
    {
        public int Id { get; set; }
        public string Bio { get; set; } = "";

        public string Message { get; set; } = "";
        public string LeftSaberColor { get; set; } = "";
        public string RightSaberColor { get; set; } = "";
    }
}
