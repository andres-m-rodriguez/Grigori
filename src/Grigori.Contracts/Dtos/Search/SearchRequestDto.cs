namespace Grigori.Contracts.Dtos.Search;

public record SearchRequestDto
{
    public required string Query { get; init; }
    public int Limit { get; init; } = 5;
    public string OutputMode { get; init; } = "full";
    public string? FileTypes { get; init; }

    /// <summary>
    /// Search mode: "semantic" (vector-based), "lexical" (BM25 keyword), or "hybrid" (both combined with RRF).
    /// Default is "hybrid" for best results.
    /// </summary>
    public string SearchMode { get; init; } = "hybrid";
}
