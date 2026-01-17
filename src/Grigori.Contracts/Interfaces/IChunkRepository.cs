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
