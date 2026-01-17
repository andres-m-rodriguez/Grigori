using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface IEmbeddingProvider
{
    Task<Result<float[], GrigoriError>> GetEmbeddingAsync(
        string text,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default);

    Task<Result<float[][], GrigoriError>> GetEmbeddingsAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default);
}

public enum EmbeddingInputType
{
    Document,
    Query
}
