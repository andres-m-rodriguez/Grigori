using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface IChunkRepository
{
    Task<Result<long, GrigoriError>> InsertChunkAsync(
        CodeChunkInput input,
        float[] embedding,
        CancellationToken cancellationToken = default);

    Task<Result<int, GrigoriError>> InsertChunksBatchAsync(
        IReadOnlyList<CodeChunkInput> inputs,
        float[][] embeddings,
        CancellationToken cancellationToken = default);

    Task<Result<List<SearchResultItem>, GrigoriError>> SearchAsync(
        float[] queryEmbedding,
        int limit,
        float scoreThreshold,
        List<string>? requiredFeatures = null,
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default);

    Task<Result<bool, GrigoriError>> DeleteByFilePathAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<bool> HasContentHashAsync(
        string filePath,
        string hash,
        CancellationToken cancellationToken = default);

    Task<Result<List<SearchResultItem>, GrigoriError>> GetChunksByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<(long Id, float[] Embedding)>> GetAllEmbeddingsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all chunks for lexical (BM25) search.
    /// Returns chunk metadata and content without embeddings for efficiency.
    /// </summary>
    Task<Result<List<ChunkForLexicalSearch>, GrigoriError>> GetChunksForLexicalSearchAsync(
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default);
}

public record CodeChunkInput
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public List<string> Features { get; init; } = [];
}

public record SearchResultItem
{
    public required long Id { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required float Score { get; init; }
    public List<string> Features { get; init; } = [];
}

/// <summary>
/// Lightweight chunk data for lexical search (no embedding).
/// </summary>
public record ChunkForLexicalSearch
{
    public required long Id { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public List<string> Features { get; init; } = [];
}
