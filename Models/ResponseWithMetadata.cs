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
        public Metadata Metadata { get; set; } = new Metadata();
        public IEnumerable<T> Data { get; set; } = new List<T>();
    }
    public class ResponseWithMetadataAndSelection<T>
    {
        public Metadata Metadata { get; set; } = new Metadata();
        public IEnumerable<T> Data { get; set; } = new List<T>();
        public T? Selection { get; set; }
    }

    public class ResponseWithMetadataAndContainer<T, C>
    {
        public Metadata Metadata { get; set; } = new Metadata();
        public IEnumerable<T> Data { get; set; } = new List<T>();
        public C? Container { get; set; }
    }

    public class RangeMetadata
    {
        public int First { get; set; }
        public int Last { get; set; }
        public int Total { get; set; }
    }
    public class ResponseWithRangeMetadata<T>
    {
        public RangeMetadata Metadata { get; set; } = new RangeMetadata();
        public IEnumerable<T> Data { get; set; } = new List<T>();
    }
}

