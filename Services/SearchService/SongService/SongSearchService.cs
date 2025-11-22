using BeatLeader_Server.Models;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Sandbox.Queries;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System.Text.RegularExpressions;

namespace BeatLeader_Server.Services;

public static class SongSearchService
{
    private const int HitsLimit = 5000;
    private static readonly string LuceneDir = Path.Combine(System.AppContext.BaseDirectory, "lucene_index_songs");
    private static readonly LuceneVersion LuceneVersion = LuceneVersion.LUCENE_48;

    private static FSDirectory Directory => FSDirectory.Open(new DirectoryInfo(LuceneDir));

    public static void AddNewSongs(SongMetadata[] songs)
    {
        using CustomAnalyzer analyzer = new(LuceneVersion);
        IndexWriterConfig config = new(LuceneVersion, analyzer);
        using IndexWriter writer = new(Directory, config);

        foreach (SongMetadata songMetadata in songs)
        {
            songMetadata.Mapper = songMetadata.Mapper.Replace(",", " ").Replace("&", " ");
            AddToLuceneIndex(songMetadata, writer);
        }

        writer.Commit();
    }

    public static void AddNewSong(Song song)
    {
        lock (Directory)
        {
            using CustomAnalyzer analyzer = new(LuceneVersion);
            IndexWriterConfig config = new(LuceneVersion, analyzer);
            using IndexWriter writer = new(Directory, config);

            AddToLuceneIndex((SongMetadata)song, writer);

            writer.Commit();
        }
    }

    public static string CleanWord(string input)
    {
        return Regex.Replace(input, @"[^a-zA-Z0-9]", "");
    }

    public static List<SongMetadata> Search(string searchQuery)
    {
        if (string.IsNullOrEmpty(searchQuery.Replace("*", "").Replace("?", "")))
        {
            return new List<SongMetadata>(0);
        }

        using DirectoryReader directoryReader = DirectoryReader.Open(Directory);
        IndexSearcher searcher = new(directoryReader);

        return searchQuery.Split(",").SelectMany((queryPart, i) => {
            queryPart = queryPart.Trim();
            bool exactMatch = queryPart.StartsWith("\"") && queryPart.EndsWith("\"");
            queryPart = queryPart.Replace("\"", "");
            Query query = GetQuery(queryPart);

            TopFieldDocs topFieldDocs = searcher.Search(query, null, HitsLimit, Sort.RELEVANCE, true, false);
            ScoreDoc[] hits = topFieldDocs.ScoreDocs;
            string lowerQuery = queryPart.ToLower();
            var words = lowerQuery.Split(" ").Select(CleanWord).ToArray();

            return hits.Select(scoreDoc =>
            {
                SongMetadata result = (SongMetadata)searcher.Doc(scoreDoc.Doc);
                if (result.Hash == lowerQuery ||
                    result.Mapper == lowerQuery ||
                    result.Name == lowerQuery ||
                    result.Author == lowerQuery) {
                    result.Score = 500;
                } else {
                    result.Score = (int)(scoreDoc.Score * 100.0f);
                }
                if (result.Name.StartsWith(lowerQuery)) {
                    result.Score += 250;
                } else if (result.Name.Contains(lowerQuery)) {
                    result.Score += 150;
                }
                if (result.Mapper.StartsWith(lowerQuery)) {
                    result.Score += 250;
                } else if (result.Mapper.Contains(lowerQuery)) {
                    result.Score += 150;
                }
                if (result.Author.StartsWith(lowerQuery)) {
                    result.Score += 250;
                } else if (result.Author.Contains(lowerQuery)) {
                    result.Score += 150;
                }
            
                if (words.Length > 1) {
                    result.Score += words.Intersect(result.Name.Split(" ").Select(CleanWord).ToArray()).Count() * 60;
                }

                result.Score = (int)((float)result.Score * ((float)1 - 0.1 * (float)i));

                return result;
            }).Where(h => !exactMatch || h.Score > 300).ToList();
        }).ToList();
    }

    private static Query GetQuery(string searchQuery)
    {
        searchQuery = searchQuery.ToLower();

        int wordsLength = searchQuery.Split(' ').Length;
        FuzzyLikeThisQuery fuzzyWordsQueryName = new(wordsLength, new CustomAnalyzer(LuceneVersion));
        fuzzyWordsQueryName.AddTerms(searchQuery, nameof(SongMetadata.Name), 0.7f, 1);
        FuzzyLikeThisQuery fuzzyWordsQueryAuthor = new(wordsLength, new CustomAnalyzer(LuceneVersion));
        fuzzyWordsQueryAuthor.AddTerms(searchQuery, nameof(SongMetadata.Author), 0.7f, 1);
        FuzzyLikeThisQuery fuzzyWordsQueryMapper = new(wordsLength, new CustomAnalyzer(LuceneVersion));
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