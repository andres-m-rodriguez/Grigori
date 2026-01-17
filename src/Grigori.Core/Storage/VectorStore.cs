using System.Security.Cryptography;
using System.Text;
using Grigori.Core.Indexing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Core.Storage;

public class VectorStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<VectorStore> _logger;
    private readonly QuantizationMode _quantizationMode;
    private readonly HnswIndex? _hnswIndex;
    private readonly string _hnswIndexPath;
    private readonly bool _useHnsw;
    private bool _hnswDirty;

    public const float DefaultScoreThreshold = 0.3f;

    /// <summary>
    /// Header size for quantized embeddings: 4 bytes scale + 4 bytes zeroPoint
    /// </summary>
    private const int QuantizationHeaderSize = 8;

    /// <summary>
    /// Multiplier for HNSW candidate retrieval. We get this many times the limit from HNSW,
    /// then re-rank with exact cosine similarity for better accuracy.
    /// </summary>
    private const int HnswCandidateMultiplier = 10;

    public VectorStore(IOptions<GrigoriOptions> options, ILogger<VectorStore> logger, ILogger<HnswIndex>? hnswLogger = null)
    {
        _logger = logger;
        _quantizationMode = options.Value.Quantization;
        var connectionString = $"Data Source={options.Value.IndexPath}";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeDatabase();

        if (_quantizationMode == QuantizationMode.Int8)
        {
            _logger.LogInformation("Int8 quantization enabled - embeddings will be stored with ~4x compression");
        }

        // Initialize HNSW index if enabled
        _useHnsw = options.Value.UseHnswIndex;
        _hnswIndexPath = Path.ChangeExtension(options.Value.IndexPath, ".hnsw");

        if (_useHnsw && hnswLogger != null)
        {
            _hnswIndex = new HnswIndex(
                hnswLogger,
                m: options.Value.HnswM,
                efConstruction: options.Value.HnswEfConstruction,
                efSearch: options.Value.HnswEfSearch
            );

            // Try to load existing HNSW index
            LoadHnswIndex();
            _logger.LogInformation("HNSW index enabled for fast approximate nearest neighbor search");
        }
    }

    private void InitializeDatabase()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                content TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                embedding BLOB NOT NULL,
                features TEXT,
                indexed_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_file_path ON chunks(file_path);
            CREATE INDEX IF NOT EXISTS idx_chunks_content_hash ON chunks(content_hash);
            """;
        command.ExecuteNonQuery();

        // Add features column if it doesn't exist (for existing databases)
        MigrateAddFeaturesColumn();

        _logger.LogInformation("Database initialized");
    }

    private void MigrateAddFeaturesColumn()
    {
        try
        {
            using var checkCommand = _connection.CreateCommand();
            checkCommand.CommandText = "SELECT features FROM chunks LIMIT 1";
            checkCommand.ExecuteScalar();
        }
        catch (SqliteException)
        {
            // Column doesn't exist, add it
            using var alterCommand = _connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE chunks ADD COLUMN features TEXT";
            alterCommand.ExecuteNonQuery();
            _logger.LogInformation("Added features column to chunks table");
        }
    }

    public async Task<long> InsertChunkAsync(CodeChunkInput input, float[] embedding, CancellationToken cancellationToken = default)
    {
        var contentHash = ComputeHash(input.Content);
        var featuresString = FeatureExtractor.FeaturesToString(input.Features);

        await using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chunks (file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at)
            VALUES (@filePath, @startLine, @endLine, @content, @contentHash, @embedding, @features, @indexedAt);
            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("@filePath", input.FilePath);
        command.Parameters.AddWithValue("@startLine", input.StartLine);
        command.Parameters.AddWithValue("@endLine", input.EndLine);
        command.Parameters.AddWithValue("@content", input.Content);
        command.Parameters.AddWithValue("@contentHash", contentHash);
        command.Parameters.AddWithValue("@embedding", SerializeEmbedding(embedding, _quantizationMode));
        command.Parameters.AddWithValue("@features", featuresString);
        command.Parameters.AddWithValue("@indexedAt", DateTime.UtcNow.ToString("O"));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var insertedId = (long)result!;

        // Mark HNSW index as dirty (needs rebuild)
        if (_useHnsw)
        {
            _hnswDirty = true;
        }

        return insertedId;
    }

    public async Task InsertChunksBatchAsync(IReadOnlyList<CodeChunkInput> inputs, IReadOnlyList<float[]> embeddings, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return;

        if (inputs.Count != embeddings.Count)
            throw new ArgumentException("Inputs and embeddings must have the same count");

        var indexedAt = DateTime.UtcNow.ToString("O");

        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO chunks (file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at)
                VALUES (@filePath, @startLine, @endLine, @content, @contentHash, @embedding, @features, @indexedAt)
                """;

            var filePathParam = command.Parameters.Add("@filePath", SqliteType.Text);
            var startLineParam = command.Parameters.Add("@startLine", SqliteType.Integer);
            var endLineParam = command.Parameters.Add("@endLine", SqliteType.Integer);
            var contentParam = command.Parameters.Add("@content", SqliteType.Text);
            var contentHashParam = command.Parameters.Add("@contentHash", SqliteType.Text);
            var embeddingParam = command.Parameters.Add("@embedding", SqliteType.Blob);
            var featuresParam = command.Parameters.Add("@features", SqliteType.Text);
            var indexedAtParam = command.Parameters.Add("@indexedAt", SqliteType.Text);

            for (var i = 0; i < inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var input = inputs[i];
                filePathParam.Value = input.FilePath;
                startLineParam.Value = input.StartLine;
                endLineParam.Value = input.EndLine;
                contentParam.Value = input.Content;
                contentHashParam.Value = ComputeHash(input.Content);
                embeddingParam.Value = SerializeEmbedding(embeddings[i], _quantizationMode);
                featuresParam.Value = FeatureExtractor.FeaturesToString(input.Features);
                indexedAtParam.Value = indexedAt;

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Batch inserted {Count} chunks", inputs.Count);

            // Mark HNSW index as dirty (needs rebuild)
            if (_useHnsw)
            {
                _hnswDirty = true;
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chunks WHERE file_path = @filePath";
        command.Parameters.AddWithValue("@filePath", filePath);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogDebug("Deleted chunks for file: {FilePath}", filePath);

        // Mark HNSW index as dirty (needs rebuild)
        if (_useHnsw)
        {
            _hnswDirty = true;
        }
    }

    /// <summary>
    /// Searches for code chunks similar to the query embedding using cosine similarity.
    /// When HNSW indexing is enabled and the index is built, uses approximate nearest neighbor
    /// search to get candidates (50-100x faster), then re-ranks with exact cosine similarity.
    /// </summary>
    /// <param name="queryEmbedding">The embedding vector to search for.</param>
    /// <param name="limit">Maximum number of results to return. Default is 5.</param>
    /// <param name="scoreThreshold">Minimum similarity score threshold. Default is 0.3.</param>
    /// <param name="requiredFeatures">
    /// Optional set of features to pre-filter chunks at the database level before computing similarity.
    /// When provided, only chunks containing at least one of the specified features will be loaded.
    /// Features are stored as comma-separated values (e.g., "authentication,jwt,token").
    /// This dramatically reduces the number of chunks requiring cosine similarity computation.
    /// Note: When feature filtering is active, HNSW is bypassed and full scan is used.
    /// </param>
    /// <param name="fileExtensions">
    /// Optional list of file extensions to filter results (e.g., [".cs", ".ts"]).
    /// When provided, only chunks from files matching these extensions will be returned.
    /// Extensions should include the leading dot. Case-insensitive matching.
    /// Note: When file extension filtering is active, HNSW is bypassed and full scan is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of search results ordered by similarity score, descending.</returns>
    public async Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int limit = 5,
        float scoreThreshold = DefaultScoreThreshold,
        HashSet<string>? requiredFeatures = null,
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default)
    {
        // Rebuild HNSW index if dirty (changes were made since last search)
        if (_useHnsw && _hnswDirty && _hnswIndex != null)
        {
            await RebuildHnswIndexAsync(cancellationToken);
        }

        // Use HNSW for fast search if available and no filtering
        // Feature/extension filtering requires full scan since HNSW doesn't support filtering
        var hasFilters = requiredFeatures != null || (fileExtensions != null && fileExtensions.Count > 0);
        if (_useHnsw && _hnswIndex?.IsBuilt == true && !hasFilters)
        {
            return await SearchWithHnswAsync(queryEmbedding, limit, scoreThreshold, cancellationToken);
        }

        // Fall back to full scan (or when filtering is needed)
        return await SearchFullScanAsync(queryEmbedding, limit, scoreThreshold, requiredFeatures, fileExtensions, cancellationToken);
    }

    /// <summary>
    /// Performs fast approximate nearest neighbor search using HNSW index.
    /// Gets top candidates from HNSW, then re-ranks with exact cosine similarity.
    /// </summary>
    private async Task<List<SearchResult>> SearchWithHnswAsync(
        float[] queryEmbedding,
        int limit,
        float scoreThreshold,
        CancellationToken cancellationToken)
    {
        // Get more candidates from HNSW than we need, then re-rank
        var candidateCount = Math.Max(limit * HnswCandidateMultiplier, 50);
        var hnswResults = _hnswIndex!.Search(queryEmbedding, candidateCount);

        if (hnswResults.Count == 0)
        {
            return [];
        }

        // Get the candidate IDs for database lookup
        var candidateIds = hnswResults.Select(r => r.Id).ToHashSet();

        // Fetch full chunks for candidates from database
        var results = new List<SearchResult>();

        await using var command = _connection.CreateCommand();
        var idParams = string.Join(",", candidateIds.Select((_, i) => $"@id{i}"));
        command.CommandText = $"SELECT id, file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at FROM chunks WHERE id IN ({idParams})";

        var paramIndex = 0;
        foreach (var id in candidateIds)
        {
            command.Parameters.AddWithValue($"@id{paramIndex++}", id);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var featuresString = reader.IsDBNull(7) ? null : reader.GetString(7);
            var chunk = new CodeChunk
            {
                Id = reader.GetInt64(0),
                FilePath = reader.GetString(1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                Content = reader.GetString(4),
                ContentHash = reader.GetString(5),
                Embedding = DeserializeEmbedding((byte[])reader["embedding"]),
                Features = FeatureExtractor.StringToFeatures(featuresString),
                IndexedAt = DateTime.Parse(reader.GetString(8))
            };

            // Re-rank with exact cosine similarity
            var score = CosineSimilarity(queryEmbedding, chunk.Embedding);

            if (score >= scoreThreshold)
            {
                results.Add(new SearchResult { Chunk = chunk, Score = score });
            }
        }

        _logger.LogDebug("HNSW search returned {CandidateCount} candidates, {ResultCount} above threshold",
            candidateIds.Count, results.Count);

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Performs full scan search with optional feature and file extension filtering.
    /// </summary>
    private async Task<List<SearchResult>> SearchFullScanAsync(
        float[] queryEmbedding,
        int limit,
        float scoreThreshold,
        HashSet<string>? requiredFeatures,
        List<string>? fileExtensions,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();

        await using var command = _connection.CreateCommand();

        // Build the SQL query with optional feature and extension pre-filtering
        var sql = new StringBuilder("SELECT id, file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at FROM chunks");
        var whereConditions = new List<string>();
        var paramIndex = 0;

        // Add feature filter conditions
        if (requiredFeatures != null && requiredFeatures.Count > 0)
        {
            var featureConditions = new List<string>();

            foreach (var feature in requiredFeatures)
            {
                var paramName = $"@feature{paramIndex++}";
                featureConditions.Add($"features LIKE {paramName}");
                command.Parameters.AddWithValue(paramName, $"%{feature}%");
            }

            whereConditions.Add($"({string.Join(" OR ", featureConditions)})");

            _logger.LogDebug("Pre-filtering search with {FeatureCount} required features: {Features}",
                requiredFeatures.Count, string.Join(", ", requiredFeatures));
        }

        // Add file extension filter conditions
        if (fileExtensions != null && fileExtensions.Count > 0)
        {
            var extensionConditions = new List<string>();

            foreach (var ext in fileExtensions)
            {
                var normalizedExt = ext.StartsWith('.') ? ext : $".{ext}";
                var paramName = $"@ext{paramIndex++}";
                extensionConditions.Add($"file_path LIKE {paramName}");
                command.Parameters.AddWithValue(paramName, $"%{normalizedExt}");
            }

            whereConditions.Add($"({string.Join(" OR ", extensionConditions)})");

            _logger.LogDebug("Pre-filtering search with {ExtensionCount} file extensions: {Extensions}",
                fileExtensions.Count, string.Join(", ", fileExtensions));
        }

        if (whereConditions.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereConditions));
        }

        command.CommandText = sql.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var featuresString = reader.IsDBNull(7) ? null : reader.GetString(7);
            var chunk = new CodeChunk
            {
                Id = reader.GetInt64(0),
                FilePath = reader.GetString(1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                Content = reader.GetString(4),
                ContentHash = reader.GetString(5),
                Embedding = DeserializeEmbedding((byte[])reader["embedding"]),
                Features = FeatureExtractor.StringToFeatures(featuresString),
                IndexedAt = DateTime.Parse(reader.GetString(8))
            };

            var score = CosineSimilarity(queryEmbedding, chunk.Embedding);

            // Apply score threshold - filter out low similarity results
            if (score >= scoreThreshold)
            {
                results.Add(new SearchResult { Chunk = chunk, Score = score });
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    public async Task<bool> HasContentHashAsync(string filePath, string contentHash, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM chunks WHERE file_path = @filePath AND content_hash = @contentHash";
        command.Parameters.AddWithValue("@filePath", filePath);
        command.Parameters.AddWithValue("@contentHash", contentHash);

        var count = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return count > 0;
    }

    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Serializes an embedding to bytes, optionally applying int8 quantization.
    /// </summary>
    /// <param name="embedding">The float embedding to serialize.</param>
    /// <param name="quantizationMode">The quantization mode to use.</param>
    /// <returns>Serialized bytes - either raw float32 or quantized int8 with header.</returns>
    private static byte[] SerializeEmbedding(float[] embedding, QuantizationMode quantizationMode)
    {
        if (quantizationMode == QuantizationMode.Int8)
        {
            return SerializeQuantizedEmbedding(embedding);
        }

        // Default: store as raw float32
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Serializes an embedding using int8 quantization.
    /// Format: [4 bytes scale][4 bytes zeroPoint][N bytes quantized values]
    /// </summary>
    private static byte[] SerializeQuantizedEmbedding(float[] embedding)
    {
        var (quantized, scale, zeroPoint) = QuantizeToInt8(embedding);

        // Format: [scale (4 bytes)][zeroPoint (4 bytes)][quantized values (N bytes)]
        var bytes = new byte[QuantizationHeaderSize + quantized.Length];

        // Write scale factor
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), scale);
        // Write zero point
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), zeroPoint);
        // Write quantized values
        Buffer.BlockCopy(quantized, 0, bytes, QuantizationHeaderSize, quantized.Length);

        return bytes;
    }

    /// <summary>
    /// Deserializes an embedding from bytes, automatically detecting format.
    /// Supports both raw float32 and quantized int8 formats for backward compatibility.
    /// </summary>
    /// <param name="bytes">The serialized embedding bytes.</param>
    /// <returns>The reconstructed float embedding.</returns>
    private static float[] DeserializeEmbedding(byte[] bytes)
    {
        // Detect format by checking if the byte count is divisible by sizeof(float)
        // and if the expected dimension matches common embedding sizes.
        // Float32 format: length is exactly N * 4 where N is a typical dimension (128, 256, 384, 512, 768, 1024, 1536)
        // Int8 format: length is 8 (header) + N where N is the dimension

        // Common embedding dimensions
        int[] commonDimensions = [128, 256, 384, 512, 768, 1024, 1536, 3072];

        // Check if it's a valid float32 format
        if (bytes.Length % sizeof(float) == 0)
        {
            var floatCount = bytes.Length / sizeof(float);
            if (Array.Exists(commonDimensions, d => d == floatCount))
            {
                // This is float32 format
                var floats = new float[floatCount];
                Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
                return floats;
            }
        }

        // Check if it's a valid int8 quantized format
        if (bytes.Length > QuantizationHeaderSize)
        {
            var quantizedLength = bytes.Length - QuantizationHeaderSize;
            if (Array.Exists(commonDimensions, d => d == quantizedLength))
            {
                // This is int8 quantized format
                return DeserializeQuantizedEmbedding(bytes);
            }
        }

        // Fallback: assume float32 format for unknown dimensions
        var fallbackFloats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, fallbackFloats, 0, bytes.Length);
        return fallbackFloats;
    }

    /// <summary>
    /// Deserializes an int8 quantized embedding.
    /// </summary>
    private static float[] DeserializeQuantizedEmbedding(byte[] bytes)
    {
        // Read scale and zeroPoint from header
        var scale = BitConverter.ToSingle(bytes, 0);
        var zeroPoint = BitConverter.ToSingle(bytes, 4);

        // Extract quantized values
        var quantizedLength = bytes.Length - QuantizationHeaderSize;
        var quantized = new sbyte[quantizedLength];
        Buffer.BlockCopy(bytes, QuantizationHeaderSize, quantized, 0, quantizedLength);

        return DequantizeFromInt8(quantized, scale, zeroPoint);
    }

    /// <summary>
    /// Quantizes a float32 embedding to int8.
    /// Uses affine quantization: quantized = round((value - zeroPoint) / scale)
    /// </summary>
    /// <param name="embedding">The float32 embedding to quantize.</param>
    /// <returns>Tuple of (quantized values, scale factor, zero point).</returns>
    internal static (sbyte[] Quantized, float Scale, float ZeroPoint) QuantizeToInt8(float[] embedding)
    {
        // Find min and max values
        float minVal = float.MaxValue;
        float maxVal = float.MinValue;

        for (int i = 0; i < embedding.Length; i++)
        {
            if (embedding[i] < minVal) minVal = embedding[i];
            if (embedding[i] > maxVal) maxVal = embedding[i];
        }

        // Handle edge case where all values are the same
        if (Math.Abs(maxVal - minVal) < float.Epsilon)
        {
            // All values are the same, return zeros with the value as zero point
            var result = new sbyte[embedding.Length];
            return (result, 1.0f, minVal);
        }

        // Calculate scale and zero point for affine quantization
        // We want to map [minVal, maxVal] to [-128, 127]
        const float qMin = -128f;
        const float qMax = 127f;

        float scale = (maxVal - minVal) / (qMax - qMin);
        float zeroPoint = minVal - qMin * scale;

        // Quantize values
        var quantized = new sbyte[embedding.Length];
        for (int i = 0; i < embedding.Length; i++)
        {
            // Affine quantization: q = round((x - zeroPoint) / scale)
            float q = (embedding[i] - zeroPoint) / scale;
            // Clamp to int8 range and round
            int qClamped = (int)MathF.Round(Math.Clamp(q, qMin, qMax));
            quantized[i] = (sbyte)qClamped;
        }

        return (quantized, scale, zeroPoint);
    }

    /// <summary>
    /// Dequantizes an int8 embedding back to float32.
    /// Uses affine dequantization: value = quantized * scale + zeroPoint
    /// </summary>
    /// <param name="quantized">The quantized int8 values.</param>
    /// <param name="scale">The scale factor used during quantization.</param>
    /// <param name="zeroPoint">The zero point used during quantization.</param>
    /// <returns>The reconstructed float32 embedding.</returns>
    internal static float[] DequantizeFromInt8(sbyte[] quantized, float scale, float zeroPoint)
    {
        var embedding = new float[quantized.Length];

        for (int i = 0; i < quantized.Length; i++)
        {
            // Affine dequantization: x = q * scale + zeroPoint
            embedding[i] = quantized[i] * scale + zeroPoint;
        }

        return embedding;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length");

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    #region HNSW Index Management

    /// <summary>
    /// Loads the HNSW index from disk if it exists.
    /// </summary>
    private void LoadHnswIndex()
    {
        if (_hnswIndex == null || !File.Exists(_hnswIndexPath))
        {
            return;
        }

        var loaded = _hnswIndex.Load(_hnswIndexPath, GetVectorsByIds);
        if (loaded)
        {
            _hnswDirty = false;
            _logger.LogInformation("Loaded HNSW index from {Path}", _hnswIndexPath);
        }
        else
        {
            _logger.LogWarning("Failed to load HNSW index, will rebuild on first search");
            _hnswDirty = true;
        }
    }

    /// <summary>
    /// Rebuilds the HNSW index from all vectors in the database.
    /// </summary>
    private async Task RebuildHnswIndexAsync(CancellationToken cancellationToken)
    {
        if (_hnswIndex == null)
        {
            return;
        }

        _logger.LogInformation("Rebuilding HNSW index...");

        var vectors = await GetAllVectorsAsync(cancellationToken);
        if (vectors.Count == 0)
        {
            _logger.LogDebug("No vectors to build HNSW index");
            _hnswDirty = false;
            return;
        }

        _hnswIndex.BuildIndex(vectors);
        _hnswDirty = false;

        // Save to disk
        try
        {
            _hnswIndex.Save(_hnswIndexPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save HNSW index to disk");
        }
    }

    /// <summary>
    /// Gets all vectors from the database for HNSW index building.
    /// </summary>
    private async Task<List<(long Id, float[] Embedding)>> GetAllVectorsAsync(CancellationToken cancellationToken)
    {
        var vectors = new List<(long Id, float[] Embedding)>();

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, embedding FROM chunks";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var embedding = DeserializeEmbedding((byte[])reader["embedding"]);
            vectors.Add((id, embedding));
        }

        return vectors;
    }

    /// <summary>
    /// Gets vectors by their database IDs for HNSW index loading.
    /// </summary>
    private IEnumerable<(long Id, float[] Embedding)> GetVectorsByIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            yield break;
        }

        using var command = _connection.CreateCommand();
        var idParams = string.Join(",", idList.Select((_, i) => $"@id{i}"));
        command.CommandText = $"SELECT id, embedding FROM chunks WHERE id IN ({idParams})";

        for (int i = 0; i < idList.Count; i++)
        {
            command.Parameters.AddWithValue($"@id{i}", idList[i]);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var embedding = DeserializeEmbedding((byte[])reader["embedding"]);
            yield return (id, embedding);
        }
    }

    /// <summary>
    /// Manually triggers a rebuild of the HNSW index.
    /// Useful after bulk operations or when index quality degrades.
    /// </summary>
    public async Task RebuildHnswIndexManuallyAsync(CancellationToken cancellationToken = default)
    {
        if (!_useHnsw || _hnswIndex == null)
        {
            _logger.LogWarning("HNSW indexing is not enabled");
            return;
        }

        _hnswDirty = true;
        await RebuildHnswIndexAsync(cancellationToken);
    }

    /// <summary>
    /// Saves the HNSW index to disk.
    /// </summary>
    public void SaveHnswIndex()
    {
        if (_hnswIndex?.IsBuilt == true)
        {
            try
            {
                _hnswIndex.Save(_hnswIndexPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save HNSW index");
            }
        }
    }

    /// <summary>
    /// Gets whether HNSW indexing is enabled and the index is built.
    /// </summary>
    public bool IsHnswIndexReady => _useHnsw && _hnswIndex?.IsBuilt == true && !_hnswDirty;

    /// <summary>
    /// Gets the number of vectors in the HNSW index.
    /// </summary>
    public int HnswIndexCount => _hnswIndex?.Count ?? 0;

    #endregion

    public void Dispose()
    {
        // Save HNSW index before disposing
        SaveHnswIndex();
        _hnswIndex?.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
