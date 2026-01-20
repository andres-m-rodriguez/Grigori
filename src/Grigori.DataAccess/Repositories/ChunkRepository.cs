using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Database;
using Grigori.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public class ChunkRepository : IChunkRepository
{
    private readonly GrigoriDbContext _dbContext;
    private readonly ILogger<ChunkRepository> _logger;

    public ChunkRepository(
        GrigoriDbContext dbContext,
        ILogger<ChunkRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<long, GrigoriError>> InsertChunkAsync(
        CodeChunkInput input,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = new ChunkEntity
            {
                FilePath = input.FilePath,
                StartLine = input.StartLine,
                EndLine = input.EndLine,
                Content = input.Content,
                ContentHash = input.ContentHash,
                Embedding = new Vector(embedding),
                Features = input.Features.Count > 0 ? string.Join(",", input.Features) : null,
                IndexedAt = DateTime.UtcNow
            };

            _dbContext.Chunks.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return entity.Id;
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

        if (inputs.Count == 0)
            return 0;

        try
        {
            var entities = inputs.Zip(embeddings, (input, embedding) => new ChunkEntity
            {
                FilePath = input.FilePath,
                StartLine = input.StartLine,
                EndLine = input.EndLine,
                Content = input.Content,
                ContentHash = input.ContentHash,
                Embedding = new Vector(embedding),
                Features = input.Features.Count > 0 ? string.Join(",", input.Features) : null,
                IndexedAt = DateTime.UtcNow
            }).ToList();

            await _dbContext.Chunks.AddRangeAsync(entities, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return entities.Count;
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
            var queryVector = new Vector(queryEmbedding);

            // Start with base query
            var query = _dbContext.Chunks.AsQueryable();

            // Apply file extension filter
            if (fileExtensions is { Count: > 0 })
            {
                query = query.Where(c => fileExtensions.Any(ext => c.FilePath.EndsWith(ext)));
            }

            // Apply feature filter
            if (requiredFeatures is { Count: > 0 })
            {
                foreach (var feature in requiredFeatures)
                {
                    query = query.Where(c => c.Features != null && c.Features.Contains(feature));
                }
            }

            // Use pgvector cosine distance for similarity search
            // Cosine distance = 1 - cosine similarity, so we need to convert
            var results = await query
                .Select(c => new
                {
                    Chunk = c,
                    Distance = c.Embedding.CosineDistance(queryVector)
                })
                .OrderBy(x => x.Distance)
                .Take(limit * 2) // Take more to filter by threshold
                .ToListAsync(cancellationToken);

            // Convert distance to similarity and filter by threshold
            var filtered = results
                .Select(r => new SearchResultItem
                {
                    Id = r.Chunk.Id,
                    FilePath = r.Chunk.FilePath,
                    StartLine = r.Chunk.StartLine,
                    EndLine = r.Chunk.EndLine,
                    Content = r.Chunk.Content,
                    Score = 1.0f - (float)r.Distance, // Convert distance to similarity
                    Features = r.Chunk.Features?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? []
                })
                .Where(r => r.Score >= scoreThreshold)
                .Take(limit)
                .ToList();

            return filtered;
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
            var deleted = await _dbContext.Chunks
                .Where(c => c.FilePath == filePath)
                .ExecuteDeleteAsync(cancellationToken);

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
        return await _dbContext.Chunks
            .AnyAsync(c => c.FilePath == filePath && c.ContentHash == hash, cancellationToken);
    }

    public async Task<Result<List<SearchResultItem>, GrigoriError>> GetChunksByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new List<SearchResultItem>();
        }

        try
        {
            var chunks = await _dbContext.Chunks
                .Where(c => ids.Contains(c.Id))
                .ToListAsync(cancellationToken);

            var results = chunks.Select(c => new SearchResultItem
            {
                Id = c.Id,
                FilePath = c.FilePath,
                StartLine = c.StartLine,
                EndLine = c.EndLine,
                Content = c.Content,
                Score = 0f,
                Features = c.Features?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? []
            }).ToList();

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chunks by IDs");
            return GrigoriError.DatabaseError($"Failed to get chunks by IDs: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<(long Id, float[] Embedding)>> GetAllEmbeddingsAsync(
        CancellationToken cancellationToken = default)
    {
        var chunks = await _dbContext.Chunks
            .Select(c => new { c.Id, c.Embedding })
            .ToListAsync(cancellationToken);

        return chunks.Select(c => (c.Id, c.Embedding.ToArray()));
    }

    public async Task<Result<List<ChunkForLexicalSearch>, GrigoriError>> GetChunksForLexicalSearchAsync(
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.Chunks.AsQueryable();

            if (fileExtensions is { Count: > 0 })
            {
                query = query.Where(c => fileExtensions.Any(ext => c.FilePath.EndsWith(ext)));
            }

            var chunks = await query
                .Select(c => new ChunkForLexicalSearch
                {
                    Id = c.Id,
                    FilePath = c.FilePath,
                    StartLine = c.StartLine,
                    EndLine = c.EndLine,
                    Content = c.Content,
                    Features = c.Features != null
                        ? c.Features.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                        : new List<string>()
                })
                .ToListAsync(cancellationToken);

            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chunks for lexical search");
            return GrigoriError.DatabaseError($"Failed to get chunks: {ex.Message}", ex);
        }
    }
}
