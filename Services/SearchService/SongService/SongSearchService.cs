using BeatLeader_Server.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace BeatLeader_Server.Services;

public static class SongSearchService
{
    private const int HitsLimit = 1000;
    private static readonly string LuceneDir = Path.Combine(System.AppContext.BaseDirectory, "lucene_index_songs");

    private static FSDirectory Directory { get; } = FSDirectory.Open(new DirectoryInfo(LuceneDir));

    public static void AddNewSongs(IEnumerable<Song> songs)
    {
        lock (Directory)
        {
            using StandardAnalyzer analyzer = new(LuceneVersion.LUCENE_48);
            IndexWriterConfig config = new(LuceneVersion.LUCENE_48, analyzer);
            using IndexWriter writer = new(Directory, config);

            foreach (SongMetadata songMetadata in songs)
            {
                AddToLuceneIndex(songMetadata, writer);
            }

            writer.Commit();
        }
    }

    public static void AddNewSong(Song song)
    {
        lock (Directory)
        {
            using StandardAnalyzer analyzer = new(LuceneVersion.LUCENE_48);
            IndexWriterConfig config = new(LuceneVersion.LUCENE_48, analyzer);
            using IndexWriter writer = new(Directory, config);

            AddToLuceneIndex((SongMetadata)song, writer);

            writer.Commit();
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
            using DirectoryReader directoryReader = DirectoryReader.Open(Directory);
            IndexSearcher searcher = new(directoryReader);

            Query query = GetQuery(searchQuery);

            TopFieldDocs topFieldDocs = searcher.Search(query, null, HitsLimit, Sort.RELEVANCE, true, false);
            ScoreDoc[] hits = topFieldDocs.ScoreDocs;

            return hits.Select(scoreDoc =>
            {
                SongMetadata result = (SongMetadata)searcher.Doc(scoreDoc.Doc);
                result.Score = scoreDoc.Score;

                return result;
            }).ToList();;
        }
    }

    private static Query GetQuery(string searchQuery)
    {
        searchQuery = searchQuery.ToLower();

        BooleanQuery booleanQuery = new()
        {
            { new PrefixQuery(new Term(nameof(SongMetadata.Id), searchQuery)), Occur.SHOULD },
            { new PrefixQuery(new Term(nameof(SongMetadata.Hash), searchQuery)), Occur.SHOULD },
            { nameof(SongMetadata.Name).GetMultiWordQuery(searchQuery), Occur.SHOULD },
            { nameof(SongMetadata.Author).GetMultiWordQuery(searchQuery), Occur.SHOULD },
            { nameof(SongMetadata.Mapper).GetMultiWordQuery(searchQuery), Occur.SHOULD },
        };

        return booleanQuery;
    }

    private static Query GetMultiWordQuery(this string name, string searchQuery)
    {
        string[] words = searchQuery.Split(' ');
        int wordsLength = words.Length;
        SpanQuery[] queries = new SpanQuery[wordsLength];

        for (int i = 0; i < wordsLength; i++)
        {
            queries[i] = new SpanMultiTermQueryWrapper<FuzzyQuery>(new FuzzyQuery(new Term(name, words[i])));
        }

        return new SpanNearQuery(queries, 2, true);
    }

    private static void AddToLuceneIndex(SongMetadata songMetadata, IndexWriter writer)
    {
        Term playerMetadataTerm = new(nameof(SongMetadata.Id), songMetadata.Id);

        writer.UpdateDocument(playerMetadataTerm, (Document)songMetadata);
    }
}