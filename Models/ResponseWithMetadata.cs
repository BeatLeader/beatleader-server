using System;
namespace BeatLeader_Server.Models
{
    public class Metadata
    {
        public int ItemsPerPage { get; set; }
        public int Page { get; set; }
        public int Total { get; set; }
    }
    public class ResponseWithMetadata<T>
    {
        public Metadata Metadata { get; set; }
        public IEnumerable<T> Data { get; set; }
    }
}

