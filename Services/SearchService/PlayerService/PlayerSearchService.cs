using BeatLeader_Server.Models;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Sandbox.Queries;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace BeatLeader_Server.Services;

public static class PlayerSearchService
{
    private const int HitsLimit = 1000;
    private static readonly string LuceneDir = Path.Combine(System.AppContext.BaseDirectory, "lucene_index_players");
    private static readonly LuceneVersion LuceneVersion = LuceneVersion;

    private static FSDirectory Directory { get; } = FSDirectory.Open(new DirectoryInfo(LuceneDir));

    public static void AddNewPlayers(IEnumerable<Player> players)
    {
        lock (Directory)
        {
            using CustomAnalyzer analyzer = new(LuceneVersion);
            IndexWriterConfig config = new(LuceneVersion, analyzer);
            using IndexWriter writer = new(Directory, config);

            foreach (Player player in players)
            {
                foreach (var playerMetadata in PlayerMetadata.GetPlayerMetadata(player)) {
                    AddToLuceneIndex(playerMetadata, writer);
                }
            }

            writer.Commit();
        }
    }

    public static void AddNewPlayer(Player player)
    {
        lock (Directory)
        {
            using CustomAnalyzer analyzer = new(LuceneVersion);
            IndexWriterConfig config = new(LuceneVersion, analyzer);
            using IndexWriter writer = new(Directory, config);

            foreach (var playerMetadata in PlayerMetadata.GetPlayerMetadata(player)) {
                AddToLuceneIndex(playerMetadata, writer);
            }

            writer.Commit();
        }
    }

    public static void RemovePlayer(Player player)
    {
        lock (Directory)
        {
            using CustomAnalyzer analyzer = new(LuceneVersion);
            IndexWriterConfig config = new(LuceneVersion, analyzer);
            using IndexWriter writer = new(Directory, config);

            foreach (var playerMetadata in PlayerMetadata.GetPlayerMetadata(player))
            {
                writer.DeleteDocuments(new Term(nameof(PlayerMetadata.Id), playerMetadata.Id.ToString()));
            }

            writer.Commit();
        }
    }

    public static void PlayerChangedName(Player player)
    {
        AddNewPlayer(player);
    }

    public static List<PlayerMetadata> Search(string searchQuery)
    {
        if (string.IsNullOrEmpty(searchQuery.Replace("*", "").Replace("?", "")))
        {
            return new List<PlayerMetadata>(0);
        }

        using DirectoryReader directoryReader = DirectoryReader.Open(Directory);
        IndexSearcher searcher = new(directoryReader);

        var lowerQuery = searchQuery.Replace(" ", "");
        Query query = GetQuery(lowerQuery);

        TopFieldDocs topFieldDocs = searcher.Search(query, null, HitsLimit, Sort.RELEVANCE, true, false);
        ScoreDoc[] hits = topFieldDocs.ScoreDocs;

        return hits.Select(scoreDoc =>
        {
            PlayerMetadata result = (PlayerMetadata)searcher.Doc(scoreDoc.Doc);
            if (result.Name == lowerQuery || (lowerQuery.Length > 3 && result.Name.StartsWith(lowerQuery))) {
                result.Score = 500;
            } else {
                result.Score = (int)(scoreDoc.Score * 100.0f);
            }

            return result;
        })
        .ToList()
        .GroupBy(h => h.Id)
        .Select(g => g.OrderByDescending(h => h.Score).First())
        .ToList();
    }

    private static Query GetQuery(string searchQuery)
    {
        searchQuery = searchQuery.ToLower();

        Term namesTerm = new(nameof(PlayerMetadata.Name), searchQuery);

        Query prefixQuery = new PrefixQuery(namesTerm);
        Query fuzzyPrefix = new FuzzyQuery(namesTerm, 2, searchQuery.Length);
        Query hardFuzzyQuery = new FuzzyQuery(namesTerm, 2, 3);
        Query softFuzzyQuery = new SlowFuzzyQuery(namesTerm, 0.7f);
        Query wildcardQuery = new WildcardQuery(new Term(nameof(PlayerMetadata.Name), "*" + searchQuery + "*"));

        Query fuzzyPrefixBoost = new BoostingQuery(prefixQuery, fuzzyPrefix, 2);
        Query hardFuzzyPrefixBoost = new BoostingQuery(fuzzyPrefixBoost, hardFuzzyQuery, 2);
        Query softHardFuzzyPrefixBoost = new BoostingQuery(hardFuzzyPrefixBoost, softFuzzyQuery, 2);

        BooleanQuery booleanQuery = new()
        {
            { new PrefixQuery(new Term(nameof(PlayerMetadata.Id), searchQuery)), Occur.SHOULD },
            { softHardFuzzyPrefixBoost, Occur.SHOULD },
            { softFuzzyQuery, Occur.SHOULD },
            { wildcardQuery, Occur.SHOULD },
        };

        return booleanQuery;
    }

    private static void AddToLuceneIndex(PlayerMetadata playerMetadata, IndexWriter writer)
    {
        Term playerMetadataTerm = new(nameof(PlayerMetadata.Id), playerMetadata.Id);

        writer.UpdateDocument(playerMetadataTerm, (Document)playerMetadata);
    }
}