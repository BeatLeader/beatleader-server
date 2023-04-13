using BeatLeader_Server.Models;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Version = Lucene.Net.Util.Version;

namespace BeatLeader_Server.Services;

public static class PlayerSearchService
{
    private const float SimilarityPercentage = 0.7f;
    private const int HitsLimit = 1000;
    private const int OptimizeCycleCount = 20; // docs recommend to optimize every once in awhile, so i just chose every 20 maps arbitrarily.
    private static readonly string LuceneDir = Path.Combine(System.AppContext.BaseDirectory, "lucene_index_players");
    private static readonly string[] Fields = { nameof(PlayerMetadata.Id), nameof(PlayerMetadata.Names) };

    private static int optimizeCycle;
    private static FSDirectory? directoryTemp;

    private static FSDirectory Directory => directoryTemp ??= FSDirectory.Open(new DirectoryInfo(LuceneDir));

    public static void AddNewPlayers(IEnumerable<Player> players)
    {
        lock (Directory)
        {
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            using IndexWriter writer = new(Directory, analyzer, IndexWriter.MaxFieldLength.UNLIMITED);

            foreach (PlayerMetadata playerMetadata in players)
            {
                AddToLuceneIndex(playerMetadata, writer);
            }

            writer.Optimize();
        }
    }

    public static void AddNewPlayer(Player player)
    {
        lock (Directory)
        {
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            using IndexWriter writer = new(Directory, analyzer, IndexWriter.MaxFieldLength.UNLIMITED);

            AddToLuceneIndex((PlayerMetadata)player, writer);

            if (++optimizeCycle == OptimizeCycleCount)
            {
                writer.Optimize();
                optimizeCycle = 0;
            }
        }
    }

    // not 100% sure if this works, but i think it does
    public static void PlayerChangedName(Player player)
    {
        lock (Directory)
        {
            using IndexSearcher searcher = new(Directory, false);
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            using IndexWriter writer = new(Directory, analyzer, IndexWriter.MaxFieldLength.UNLIMITED);

            Term playerMetadataTerm = new(nameof(PlayerMetadata.Id), player.Id);
            TermQuery searchQuery = new(playerMetadataTerm);
            TopFieldDocs topFieldDocs = searcher.Search(searchQuery, null, HitsLimit, Sort.RELEVANCE);

            PlayerMetadata playerMetadata = (PlayerMetadata)searcher.Doc(topFieldDocs.ScoreDocs[0].Doc);
            playerMetadata.Names.Add(player.Name);
            writer.DeleteDocuments(searchQuery);
            writer.AddDocument((Document)playerMetadata);
        }
    }

    public static List<PlayerMetadata> Search(string searchQuery)
    {
        if (string.IsNullOrEmpty(searchQuery.Replace("*", "").Replace("?", "")))
        {
            return new List<PlayerMetadata>(0);
        }

        lock (Directory)
        {
            using IndexSearcher searcher = new(Directory, false);
            using StandardAnalyzer analyzer = new(Version.LUCENE_30);
            MultiFieldQueryParser parser = new(Version.LUCENE_30, Fields, analyzer);
            Query query = parser.GetQuery(searchQuery);

            TopFieldDocs topFieldDocs = searcher.Search(query, null, HitsLimit, Sort.RELEVANCE);
            ScoreDoc[] hits = topFieldDocs.ScoreDocs;

            return hits.Select(scoreDoc => (PlayerMetadata)searcher.Doc(scoreDoc.Doc)).ToList();
        }
    }

    private static Query GetQuery(this QueryParser parser, string searchQuery)
    {
        string continuousSearch = searchQuery.Replace(" ", "+");

        BooleanQuery booleanQuery = new()
        {
            { parser.Parse(continuousSearch), Occur.SHOULD },
            { new FuzzyQuery(new Term(nameof(PlayerMetadata.Id), searchQuery), SimilarityPercentage), Occur.SHOULD },
            { new FuzzyQuery(new Term(nameof(PlayerMetadata.Names), searchQuery), SimilarityPercentage), Occur.SHOULD },
        };

        return booleanQuery;
    }

    private static void AddToLuceneIndex(PlayerMetadata playerMetadata, IndexWriter writer)
    {
        Term playerMetadataTerm = new(nameof(PlayerMetadata.Id), playerMetadata.Id);
        TermQuery searchQuery = new(playerMetadataTerm);
        writer.DeleteDocuments(searchQuery);

        writer.AddDocument((Document)playerMetadata);
    }
}