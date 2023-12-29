using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Util;

namespace BeatLeader_Server.Services
{
    public class CustomAnalyzer : Analyzer
    {
        private readonly LuceneVersion _luceneVersion;
        public CustomAnalyzer(LuceneVersion luceneVersion) {
            _luceneVersion = luceneVersion;
        }
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            Tokenizer source = new WhitespaceTokenizer(_luceneVersion, reader);
            TokenStream filter = new LowerCaseFilter(_luceneVersion, source);

            // We can add more filters if necessary
            return new TokenStreamComponents(source, filter);
        }
    }
}
