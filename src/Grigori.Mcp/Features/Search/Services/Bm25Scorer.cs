using System.Text.RegularExpressions;

namespace Grigori.Mcp.Features.Search.Services;

/// <summary>
/// BM25 (Okapi BM25) implementation for lexical scoring of code chunks.
/// Provides keyword-based relevance scoring to complement semantic search.
/// </summary>
public partial class Bm25Scorer
{
    // BM25 parameters
    private const double K1 = 1.2;  // Term frequency saturation parameter
    private const double B = 0.75;  // Length normalization parameter

    private readonly Dictionary<string, int> _documentFrequencies = new();
    private readonly int _totalDocuments;
    private readonly double _averageDocumentLength;

    /// <summary>
    /// Creates a BM25 scorer initialized with corpus statistics.
    /// </summary>
    /// <param name="documents">All documents in the corpus (for IDF calculation)</param>
    public Bm25Scorer(IEnumerable<string> documents)
    {
        var docs = documents.ToList();
        _totalDocuments = docs.Count;

        if (_totalDocuments == 0)
        {
            _averageDocumentLength = 0;
            return;
        }

        var totalLength = 0L;

        foreach (var doc in docs)
        {
            var tokens = Tokenize(doc);
            totalLength += tokens.Count;

            var uniqueTokens = tokens.Distinct();
            foreach (var token in uniqueTokens)
            {
                _documentFrequencies.TryGetValue(token, out var count);
                _documentFrequencies[token] = count + 1;
            }
        }

        _averageDocumentLength = (double)totalLength / _totalDocuments;
    }

    /// <summary>
    /// Calculates BM25 score for a document given a query.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="document">Document content to score</param>
    /// <returns>BM25 relevance score (higher = more relevant)</returns>
    public double Score(string query, string document)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(document))
            return 0;

        var queryTokens = Tokenize(query);
        var docTokens = Tokenize(document);

        if (queryTokens.Count == 0 || docTokens.Count == 0)
            return 0;

        var docLength = docTokens.Count;
        var termFrequencies = GetTermFrequencies(docTokens);

        var score = 0.0;

        foreach (var queryToken in queryTokens.Distinct())
        {
            if (!termFrequencies.TryGetValue(queryToken, out var tf))
                continue;

            var idf = CalculateIdf(queryToken);
            var numerator = tf * (K1 + 1);
            var denominator = tf + K1 * (1 - B + B * (docLength / _averageDocumentLength));

            score += idf * (numerator / denominator);
        }

        return score;
    }

    /// <summary>
    /// Scores multiple documents against a query.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="documents">Documents to score (id, content pairs)</param>
    /// <returns>Scored documents ordered by score descending</returns>
    public IEnumerable<(long Id, double Score)> ScoreDocuments(string query, IEnumerable<(long Id, string Content)> documents)
    {
        return documents
            .Select(doc => (doc.Id, Score: Score(query, doc.Content)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score);
    }

    /// <summary>
    /// Normalizes BM25 scores to 0-1 range for fusion with semantic scores.
    /// Uses min-max normalization within the result set.
    /// </summary>
    /// <param name="scores">Raw BM25 scores</param>
    /// <returns>Normalized scores between 0 and 1</returns>
    public static IEnumerable<(long Id, double Score)> NormalizeScores(IEnumerable<(long Id, double Score)> scores)
    {
        var scoreList = scores.ToList();

        if (scoreList.Count == 0)
            return scoreList;

        var maxScore = scoreList.Max(x => x.Score);
        var minScore = scoreList.Min(x => x.Score);

        if (Math.Abs(maxScore - minScore) < 0.0001)
        {
            // All scores are the same, normalize to 0.5
            return scoreList.Select(x => (x.Id, Score: 0.5));
        }

        return scoreList.Select(x => (x.Id, Score: (x.Score - minScore) / (maxScore - minScore)));
    }

    private double CalculateIdf(string term)
    {
        _documentFrequencies.TryGetValue(term, out var docFreq);

        if (docFreq == 0)
            return 0;

        // BM25 IDF formula
        return Math.Log((_totalDocuments - docFreq + 0.5) / (docFreq + 0.5) + 1);
    }

    private static Dictionary<string, int> GetTermFrequencies(List<string> tokens)
    {
        var frequencies = new Dictionary<string, int>();
        foreach (var token in tokens)
        {
            frequencies.TryGetValue(token, out var count);
            frequencies[token] = count + 1;
        }
        return frequencies;
    }

    /// <summary>
    /// Tokenizes text for BM25 scoring.
    /// Handles code-specific tokenization (camelCase, snake_case, etc.)
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Split on whitespace and punctuation
        var rawTokens = TokenizerRegex().Split(text.ToLowerInvariant());

        var tokens = new List<string>();

        foreach (var token in rawTokens)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
                continue;

            // Add the original token
            tokens.Add(token);

            // Split camelCase and PascalCase
            var camelParts = SplitCamelCase(token);
            if (camelParts.Count > 1)
            {
                tokens.AddRange(camelParts.Where(p => p.Length >= 2));
            }

            // Split snake_case (already split by regex, but handle any missed)
            if (token.Contains('_'))
            {
                var snakeParts = token.Split('_', StringSplitOptions.RemoveEmptyEntries);
                tokens.AddRange(snakeParts.Where(p => p.Length >= 2));
            }
        }

        return tokens;
    }

    private static List<string> SplitCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        var parts = new List<string>();
        var currentPart = new List<char>();

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (i > 0 && char.IsUpper(c) && !char.IsUpper(input[i - 1]))
            {
                // Start of new word (lowercase followed by uppercase)
                if (currentPart.Count > 0)
                {
                    parts.Add(new string(currentPart.ToArray()).ToLowerInvariant());
                    currentPart.Clear();
                }
            }
            else if (i > 0 && i < input.Length - 1 && char.IsUpper(c) && char.IsUpper(input[i - 1]) && char.IsLower(input[i + 1]))
            {
                // Handle acronyms: "XMLParser" -> "XML", "Parser"
                if (currentPart.Count > 0)
                {
                    parts.Add(new string(currentPart.ToArray()).ToLowerInvariant());
                    currentPart.Clear();
                }
            }

            currentPart.Add(c);
        }

        if (currentPart.Count > 0)
        {
            parts.Add(new string(currentPart.ToArray()).ToLowerInvariant());
        }

        return parts;
    }

    [GeneratedRegex(@"[\s\W_]+")]
    private static partial Regex TokenizerRegex();
}
