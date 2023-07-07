namespace BeatLeader_Server.Models {
    public class SongSearch {
        public int Id { get; set; }
        public int Score { get; set; }
        public int SearchId { get; set; }

        public string? SongId { get; set; }
        public Song? Song { get; set; }
    }
}
