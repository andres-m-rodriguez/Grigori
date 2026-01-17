namespace Grigori.Core.Embeddings;

public enum EmbeddingInputType
{
    Document,
    Query
}

public interface IEmbeddingProvider
{
    Task<float[]> GetEmbeddingAsync(string text, EmbeddingInputType inputType = EmbeddingInputType.Document, CancellationToken cancellationToken = default);
    Task<float[][]> GetEmbeddingsAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType = EmbeddingInputType.Document, CancellationToken cancellationToken = default);
}
