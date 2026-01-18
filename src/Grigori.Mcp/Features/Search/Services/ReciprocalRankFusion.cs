namespace Grigori.Mcp.Features.Search.Services;

/// <summary>
/// Reciprocal Rank Fusion (RRF) for combining multiple ranked lists.
/// RRF is effective at merging semantic and lexical search results.
/// Formula: RRF(d) = Σ 1/(k + rank(d)) for each ranking list
/// </summary>
public static class ReciprocalRankFusion
{
    /// <summary>
    /// Default k parameter for RRF. Higher values reduce the impact of high-ranked items.
    /// Standard value from the original RRF paper is 60.
    /// </summary>
    private const int DefaultK = 60;

    /// <summary>
    /// Fuses multiple ranked lists using Reciprocal Rank Fusion.
    /// </summary>
    /// <typeparam name="T">Type of the item identifier</typeparam>
    /// <param name="rankedLists">List of ranked results, each ordered by relevance descending</param>
    /// <param name="k">RRF parameter (default: 60). Higher values flatten rank differences.</param>
    /// <returns>Fused list ordered by combined RRF score descending</returns>
    public static List<(T Id, double Score)> Fuse<T>(
        IEnumerable<IEnumerable<T>> rankedLists,
        int k = DefaultK) where T : notnull
    {
        var rrfScores = new Dictionary<T, double>();

        foreach (var rankedList in rankedLists)
        {
            var rank = 1;
            foreach (var item in rankedList)
            {
                var rrfScore = 1.0 / (k + rank);

                if (rrfScores.TryGetValue(item, out var existingScore))
                {
                    rrfScores[item] = existingScore + rrfScore;
                }
                else
                {
                    rrfScores[item] = rrfScore;
                }

                rank++;
            }
        }

        return rrfScores
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    /// <summary>
    /// Fuses two ranked lists with optional weighting.
    /// </summary>
    /// <typeparam name="T">Type of the item identifier</typeparam>
    /// <param name="list1">First ranked list (e.g., semantic results)</param>
    /// <param name="list2">Second ranked list (e.g., lexical results)</param>
    /// <param name="weight1">Weight for first list (default: 1.0)</param>
    /// <param name="weight2">Weight for second list (default: 1.0)</param>
    /// <param name="k">RRF parameter (default: 60)</param>
    /// <returns>Fused list ordered by weighted RRF score descending</returns>
    public static List<(T Id, double Score)> FuseWeighted<T>(
        IEnumerable<T> list1,
        IEnumerable<T> list2,
        double weight1 = 1.0,
        double weight2 = 1.0,
        int k = DefaultK) where T : notnull
    {
        var rrfScores = new Dictionary<T, double>();

        // Process first list
        var rank = 1;
        foreach (var item in list1)
        {
            var rrfScore = weight1 / (k + rank);
            rrfScores[item] = rrfScore;
            rank++;
        }

        // Process second list
        rank = 1;
        foreach (var item in list2)
        {
            var rrfScore = weight2 / (k + rank);

            if (rrfScores.TryGetValue(item, out var existingScore))
            {
                rrfScores[item] = existingScore + rrfScore;
            }
            else
            {
                rrfScores[item] = rrfScore;
            }

            rank++;
        }

        return rrfScores
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    /// <summary>
    /// Fuses results while preserving original scores for reference.
    /// </summary>
    /// <typeparam name="T">Type of the item identifier</typeparam>
    /// <param name="semanticResults">Semantic search results with scores</param>
    /// <param name="lexicalResults">Lexical search results with scores</param>
    /// <param name="semanticWeight">Weight for semantic results</param>
    /// <param name="lexicalWeight">Weight for lexical results</param>
    /// <param name="k">RRF parameter</param>
    /// <returns>Fused results with combined score and original scores</returns>
    public static List<FusedResult<T>> FuseWithScores<T>(
        IEnumerable<(T Id, float Score)> semanticResults,
        IEnumerable<(T Id, double Score)> lexicalResults,
        double semanticWeight = 1.0,
        double lexicalWeight = 1.0,
        int k = DefaultK) where T : notnull
    {
        var semanticList = semanticResults.ToList();
        var lexicalList = lexicalResults.ToList();

        // Build lookup dictionaries for original scores
        var semanticScores = semanticList.ToDictionary(x => x.Id, x => x.Score);
        var lexicalScores = lexicalList.ToDictionary(x => x.Id, x => x.Score);

        // Calculate RRF scores
        var rrfScores = new Dictionary<T, double>();

        var rank = 1;
        foreach (var (id, _) in semanticList)
        {
            rrfScores[id] = semanticWeight / (k + rank);
            rank++;
        }

        rank = 1;
        foreach (var (id, _) in lexicalList)
        {
            var rrfScore = lexicalWeight / (k + rank);
            if (rrfScores.TryGetValue(id, out var existing))
            {
                rrfScores[id] = existing + rrfScore;
            }
            else
            {
                rrfScores[id] = rrfScore;
            }
            rank++;
        }

        return rrfScores
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => new FusedResult<T>
            {
                Id = kvp.Key,
                FusedScore = kvp.Value,
                SemanticScore = semanticScores.GetValueOrDefault(kvp.Key),
                LexicalScore = lexicalScores.GetValueOrDefault(kvp.Key),
                InBothLists = semanticScores.ContainsKey(kvp.Key) && lexicalScores.ContainsKey(kvp.Key)
            })
            .ToList();
    }
}

/// <summary>
/// Result from fusing semantic and lexical search results.
/// </summary>
/// <typeparam name="T">Type of the item identifier</typeparam>
public record FusedResult<T>
{
    /// <summary>Item identifier</summary>
    public required T Id { get; init; }

    /// <summary>Combined RRF score</summary>
    public required double FusedScore { get; init; }

    /// <summary>Original semantic search score (0 if not in semantic results)</summary>
    public float SemanticScore { get; init; }

    /// <summary>Original lexical search score (0 if not in lexical results)</summary>
    public double LexicalScore { get; init; }

    /// <summary>Whether this result appeared in both semantic and lexical results</summary>
    public bool InBothLists { get; init; }
}
