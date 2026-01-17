using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.DataAccess.Repositories;

public class ChunkRepository : IChunkRepository
{
    private readonly GrigoriDbContext _dbContext;
    private readonly ILogger<ChunkRepository> _logger;
    private readonly GrigoriOptions _options;

    public ChunkRepository(
        GrigoriDbContext dbContext,
        ILogger<ChunkRepository> logger,
        IOptions<GrigoriOptions> options)
    {
        _dbContext = dbContext;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<Result<long, GrigoriError>> InsertChunkAsync(
        CodeChunkInput input,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var embeddingBytes = SerializeEmbedding(embedding);
            var features = input.Features.Count > 0 ? string.Join(",", input.Features) : null;

            var id = await _dbContext.InsertChunkAsync(
                input.FilePath,
                input.StartLine,
                input.EndLine,
                input.Content,
                input.ContentHash,
                embeddingBytes,
                features,
                cancellationToken);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert chunk for {FilePath}", input.FilePath);
            return GrigoriError.DatabaseError($"Failed to insert chunk: {ex.Message}", ex);
        }
    }

    public async Task<Result<int, GrigoriError>> InsertChunksBatchAsync(
        IReadOnlyList<CodeChunkInput> inputs,
        float[][] embeddings,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count != embeddings.Length)
        {
            return GrigoriError.ValidationError("Chunk inputs and embeddings count mismatch");
        }

        try
        {
            var chunks = inputs.Zip(embeddings, (input, embedding) =>
            {
                var embeddingBytes = SerializeEmbedding(embedding);
                var features = input.Features.Count > 0 ? string.Join(",", input.Features) : null;
                return (input.FilePath, input.StartLine, input.EndLine, input.Content, input.ContentHash, embeddingBytes, features);
            }).ToList();

            var count = await _dbContext.InsertChunksBatchAsync(chunks, cancellationToken);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert chunks batch");
            return GrigoriError.DatabaseError($"Failed to insert chunks batch: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<SearchResultItem>, GrigoriError>> SearchAsync(
        float[] queryEmbedding,
        int limit,
        float scoreThreshold,
        List<string>? requiredFeatures = null,
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chunks = await _dbContext.SearchChunksAsync(fileExtensions, requiredFeatures, cancellationToken);

            var results = chunks
                .Select(chunk =>
                {
                    var embedding = DeserializeEmbedding(chunk.Embedding);
                    var score = CosineSimilarity(queryEmbedding, embedding);
                    return new SearchResultItem
                    {
                        Id = chunk.Id,
                        FilePath = chunk.FilePath,
                        StartLine = chunk.StartLine,
                        EndLine = chunk.EndLine,
                        Content = chunk.Content,
                        Score = score,
                        Features = chunk.Features?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? []
                    };
                })
                .Where(r => r.Score >= scoreThreshold)
                .OrderByDescending(r => r.Score)
                .Take(limit)
                .ToList();

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            return GrigoriError.SearchFailed("repository search", ex.Message, ex);
        }
    }

    public async Task<Result<bool, GrigoriError>> DeleteByFilePathAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _dbContext.DeleteByFilePathAsync(filePath, cancellationToken);
            return deleted > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chunks for {FilePath}", filePath);
            return GrigoriError.DatabaseError($"Failed to delete chunks: {ex.Message}", ex);
        }
    }

    public async Task<bool> HasContentHashAsync(
        string filePath,
        string hash,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.HasContentHashAsync(filePath, hash, cancellationToken);
    }

    private byte[] SerializeEmbedding(float[] embedding)
    {
        if (_options.Quantization == QuantizationMode.Int8)
        {
            return QuantizeToInt8(embedding);
        }

        // Float32 serialization
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private float[] DeserializeEmbedding(byte[] bytes)
    {
        // Detect format by size
        // Int8 format: 8 header bytes + N quantized bytes
        // Float32 format: N * 4 bytes

        var isFloat32 = IsFloat32Format(bytes);

        if (isFloat32)
        {
            var embedding = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
            return embedding;
        }

        return DequantizeFromInt8(bytes);
    }

    private static bool IsFloat32Format(byte[] bytes)
    {
        // Common embedding dimensions for float32
        int[] commonDimensions = [384, 768, 1024, 1536];
        var floatCount = bytes.Length / sizeof(float);
        return Array.Exists(commonDimensions, d => d == floatCount);
    }

    private static byte[] QuantizeToInt8(float[] embedding)
    {
        var min = embedding.Min();
        var max = embedding.Max();
        var scale = (max - min) / 255f;
        var zeroPoint = -min / scale;

        var bytes = new byte[8 + embedding.Length]; // 4 bytes scale + 4 bytes zeroPoint + quantized
        BitConverter.GetBytes(scale).CopyTo(bytes, 0);
        BitConverter.GetBytes(zeroPoint).CopyTo(bytes, 4);

        for (var i = 0; i < embedding.Length; i++)
        {
            var quantized = (embedding[i] - min) / scale;
            bytes[8 + i] = (byte)Math.Clamp(quantized, 0, 255);
        }

        return bytes;
    }

    private static float[] DequantizeFromInt8(byte[] bytes)
    {
        var scale = BitConverter.ToSingle(bytes, 0);
        var zeroPoint = BitConverter.ToSingle(bytes, 4);

        var embedding = new float[bytes.Length - 8];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (bytes[8 + i] - zeroPoint) * scale;
        }

        return embedding;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dot / denominator : 0f;
    }
}
