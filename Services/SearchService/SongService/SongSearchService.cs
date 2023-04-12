using BeatLeader_Server.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Version = Lucene.Net.Util.Version;

namespace BeatLeader_Server.Services;

public static class SongSearchService
{
    private const float SimilarityPercentage = 0.7f;
    private const int HitsLimit = 1000;
    private const int OptimizeCycleCount = 20; // docs recommend to optimize every once in awhile, so i just chose every 20 maps arbitrarily.
    private static readonly string LuceneDir = Path.Combine(System.AppContext.BaseDirectory, "lucene_index_songs");
    private static readonly string[] Fields = { nameof(SongMetadata.Id), nameof(SongMetadata.Name), nameof(SongMetadata.Hash), nameof(SongMetadata.Author), nameof(SongMetadata.Mapper) };

    private static int optimizeCycle;
    private static FSDirectory? directoryTemp;

    private static FSDirectory Directory => directoryTemp ??= FSDirectory.Open(new DirectoryInfo(LuceneDir));

    public static void AddNewSongs(IEnumerable<Song> songs)
    {
        lock (Directory)
        {
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            using IndexWriter writer = new(Directory, analyzer, IndexWriter.MaxFieldLength.UNLIMITED);

            foreach (SongMetadata songMetadata in songs)
            {
                AddToLuceneIndex(songMetadata, writer);
            }

            writer.Optimize();
        }
    }

    public static void AddNewSong(Song song)
    {
        lock (Directory)
        {
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            using IndexWriter writer = new(Directory, analyzer, IndexWriter.MaxFieldLength.UNLIMITED);

            AddToLuceneIndex((SongMetadata)song, writer);

            if (++optimizeCycle == OptimizeCycleCount)
            {
                writer.Optimize();
                optimizeCycle = 0;
            }
        }
    }

    public static List<SongMetadata> Search(string searchQuery)
    {
        if (string.IsNullOrEmpty(searchQuery.Replace("*", "").Replace("?", "")))
        {
            return new List<SongMetadata>(0);
        }

        lock (Directory)
        {
            using IndexSearcher searcher = new(Directory, false);
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            MultiFieldQueryParser parser = new(Version.LUCENE_30, Fields, analyzer);
            Query query = parser.GetQuery(searchQuery);

            TopFieldDocs topFieldDocs = searcher.Search(query, null, HitsLimit, Sort.RELEVANCE);
            ScoreDoc[] hits = topFieldDocs.ScoreDocs;

            return hits.Select(scoreDoc => (SongMetadata)searcher.Doc(scoreDoc.Doc)).ToList();
        }
    }

    private static Query GetQuery(this QueryParser parser, string searchQuery)
    {
        string continuousSearch = searchQuery.Replace(" ", "+");

        BooleanQuery booleanQuery = new()
        {
            { parser.Parse(continuousSearch), Occur.SHOULD }, // allow for multi word searching (order matters). EX: "dear maria" can find "Dear Maria, Count Me In (Japanese Cover)"
            { new FuzzyQuery(new Term(nameof(SongMetadata.Id), searchQuery), SimilarityPercentage), Occur.SHOULD },
            { new FuzzyQuery(new Term(nameof(SongMetadata.Name), searchQuery), SimilarityPercentage), Occur.SHOULD },
            { new FuzzyQuery(new Term(nameof(SongMetadata.Hash), searchQuery), SimilarityPercentage), Occur.SHOULD },
            { new FuzzyQuery(new Term(nameof(SongMetadata.Author), searchQuery), SimilarityPercentage), Occur.SHOULD },
            { new FuzzyQuery(new Term(nameof(SongMetadata.Mapper), searchQuery), SimilarityPercentage), Occur.SHOULD },
        };

        return booleanQuery;
    }

    private static void AddToLuceneIndex(SongMetadata songMetadata, IndexWriter writer)
    {
        Term songMetadataTerm = new(nameof(SongMetadata.Id), songMetadata.Id);
        TermQuery searchQuery = new(songMetadataTerm);
        writer.DeleteDocuments(searchQuery);

        writer.AddDocument((Document)songMetadata);
    }
}