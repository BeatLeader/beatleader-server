using BeatLeader_Server.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Sandbox.Queries;
using Lucene.Net.Search;
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

        using DirectoryReader directoryReader = DirectoryReader.Open(Directory);
        IndexSearcher searcher = new(directoryReader);

        Query query = GetQuery(searchQuery);

        TopFieldDocs topFieldDocs = searcher.Search(query, null, HitsLimit, Sort.RELEVANCE, true, false);
        ScoreDoc[] hits = topFieldDocs.ScoreDocs;

        return hits.Select(scoreDoc =>
        {
            SongMetadata result = (SongMetadata)searcher.Doc(scoreDoc.Doc);
            result.Score = (int)(scoreDoc.Score * 100.0f);

            return result;
        }).ToList();
    }

    private static Query GetQuery(string searchQuery)
    {
        searchQuery = searchQuery.ToLower();

        Console.WriteLine(searchQuery);
        string[] words = searchQuery.Split(' ');
        int wordsLength = words.Length;
        FuzzyLikeThisQuery fuzzyWordsQueryName = new(wordsLength, new StandardAnalyzer(LuceneVersion.LUCENE_48));
        fuzzyWordsQueryName.AddTerms(searchQuery, nameof(SongMetadata.Name), 0.7f, 1);
        FuzzyLikeThisQuery fuzzyWordsQueryAuthor = new(wordsLength, new StandardAnalyzer(LuceneVersion.LUCENE_48));
        fuzzyWordsQueryAuthor.AddTerms(searchQuery, nameof(SongMetadata.Author), 0.7f, 1);
        FuzzyLikeThisQuery fuzzyWordsQueryMapper = new(wordsLength, new StandardAnalyzer(LuceneVersion.LUCENE_48));
        fuzzyWordsQueryMapper.AddTerms(searchQuery, nameof(SongMetadata.Mapper), 0.7f, 1);

        BooleanQuery booleanQuery = new()
        {
            { new PrefixQuery(new Term(nameof(SongMetadata.Id), searchQuery)), Occur.SHOULD },
            { new PrefixQuery(new Term(nameof(SongMetadata.Hash), searchQuery)), Occur.SHOULD },
            { fuzzyWordsQueryName, Occur.SHOULD },
            { fuzzyWordsQueryAuthor, Occur.SHOULD },
            { fuzzyWordsQueryMapper, Occur.SHOULD },
            { new PrefixQuery(new Term(nameof(SongMetadata.Name), searchQuery)), Occur.SHOULD },
            { new PrefixQuery(new Term(nameof(SongMetadata.Author), searchQuery)), Occur.SHOULD },
            { new PrefixQuery(new Term(nameof(SongMetadata.Mapper), searchQuery)), Occur.SHOULD }
        };

        return booleanQuery;
    }

    private static void AddToLuceneIndex(SongMetadata songMetadata, IndexWriter writer)
    {
        Term playerMetadataTerm = new(nameof(SongMetadata.Id), songMetadata.Id);

        writer.UpdateDocument(playerMetadataTerm, (Document)songMetadata);
    }
}