namespace BeatMapEvaluator.Model
{
    /// <summary>A "map queue" element model AKA QueuedMap.xaml</summary>
    public class MapQueueModel {
        //All difficulties available
        public MapDiffs diffsAvailable { get; set; }
        public string MapSongName { get; set; } = "";
        public string MapSongSubName { get; set; } = "";
        public string MapAuthors { get; set; } = "";
    }
}