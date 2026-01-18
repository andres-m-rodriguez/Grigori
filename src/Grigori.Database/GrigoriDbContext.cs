using System.Security.Cryptography;
using System.Text;
using Grigori.Contracts.Options;
using Grigori.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Database;

public class GrigoriDbContext : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<GrigoriDbContext> _logger;
    private readonly GrigoriOptions _options;
    private bool _disposed;

    public GrigoriDbContext(
        IOptions<GrigoriOptions> options,
        ILogger<GrigoriDbContext> logger)
    {
        _options = options.Value;
        _logger = logger;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.IndexPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureCreated();
    }

    public SqliteConnection Connection => _connection;

    private void EnsureCreated()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
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

            CREATE TABLE IF NOT EXISTS activity_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                activity_type TEXT NOT NULL,
                description TEXT NOT NULL,
                duration_ms INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                details TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_activity_log_timestamp ON activity_log(timestamp);
            CREATE INDEX IF NOT EXISTS idx_activity_log_type ON activity_log(activity_type);

            CREATE TABLE IF NOT EXISTS search_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                query TEXT NOT NULL,
                result_count INTEGER NOT NULL,
                duration_ms INTEGER NOT NULL,
                cache_hit INTEGER NOT NULL,
                used_hnsw INTEGER NOT NULL,
                timestamp TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_search_history_timestamp ON search_history(timestamp);
            """;
        cmd.ExecuteNonQuery();

        _logger.LogDebug("Database schema ensured at {Path}", _options.IndexPath);
    }

    public async Task<long> InsertChunkAsync(
        string filePath,
        int startLine,
        int endLine,
        string content,
        string contentHash,
        byte[] embedding,
        string? features,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks (file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at)
            VALUES (@filePath, @startLine, @endLine, @content, @contentHash, @embedding, @features, @indexedAt);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@filePath", filePath);
        cmd.Parameters.AddWithValue("@startLine", startLine);
        cmd.Parameters.AddWithValue("@endLine", endLine);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@contentHash", contentHash);
        cmd.Parameters.AddWithValue("@embedding", embedding);
        cmd.Parameters.AddWithValue("@features", (object?)features ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@indexedAt", DateTime.UtcNow.ToString("O"));

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (long)result!;
    }

    public async Task<int> InsertChunksBatchAsync(
        IReadOnlyList<(string FilePath, int StartLine, int EndLine, string Content, string ContentHash, byte[] Embedding, string? Features)> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return 0;

        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var count = 0;
            foreach (var chunk in chunks)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = """
                    INSERT INTO chunks (file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at)
                    VALUES (@filePath, @startLine, @endLine, @content, @contentHash, @embedding, @features, @indexedAt);
                    """;

                cmd.Parameters.AddWithValue("@filePath", chunk.FilePath);
                cmd.Parameters.AddWithValue("@startLine", chunk.StartLine);
                cmd.Parameters.AddWithValue("@endLine", chunk.EndLine);
                cmd.Parameters.AddWithValue("@content", chunk.Content);
                cmd.Parameters.AddWithValue("@contentHash", chunk.ContentHash);
                cmd.Parameters.AddWithValue("@embedding", chunk.Embedding);
                cmd.Parameters.AddWithValue("@features", (object?)chunk.Features ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@indexedAt", DateTime.UtcNow.ToString("O"));

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                count++;
            }

            await transaction.CommitAsync(cancellationToken);
            return count;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<List<CodeChunk>> GetAllChunksAsync(CancellationToken cancellationToken = default)
    {
        var chunks = new List<CodeChunk>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at FROM chunks";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(ReadChunk(reader));
        }

        return chunks;
    }

    public async Task<List<CodeChunk>> GetChunksByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var chunks = new List<CodeChunk>();

        await using var cmd = _connection.CreateCommand();
        var idParams = string.Join(",", ids.Select((_, i) => $"@id{i}"));
        cmd.CommandText = $"SELECT id, file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at FROM chunks WHERE id IN ({idParams})";

        for (var i = 0; i < ids.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@id{i}", ids[i]);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(ReadChunk(reader));
        }

        return chunks;
    }

    public async Task<List<CodeChunk>> SearchChunksAsync(
        List<string>? fileExtensions = null,
        List<string>? requiredFeatures = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<CodeChunk>();

        var sql = new StringBuilder("SELECT id, file_path, start_line, end_line, content, content_hash, embedding, features, indexed_at FROM chunks WHERE 1=1");
        var parameters = new List<SqliteParameter>();

        if (fileExtensions is { Count: > 0 })
        {
            var extensionConditions = fileExtensions
                .Select((ext, i) => $"file_path LIKE @ext{i}")
                .ToList();
            sql.Append($" AND ({string.Join(" OR ", extensionConditions)})");

            for (var i = 0; i < fileExtensions.Count; i++)
            {
                parameters.Add(new SqliteParameter($"@ext{i}", $"%{fileExtensions[i]}"));
            }
        }

        if (requiredFeatures is { Count: > 0 })
        {
            foreach (var (feature, i) in requiredFeatures.Select((f, i) => (f, i)))
            {
                sql.Append($" AND features LIKE @feat{i}");
                parameters.Add(new SqliteParameter($"@feat{i}", $"%{feature}%"));
            }
        }

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(param);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(ReadChunk(reader));
        }

        return chunks;
    }

    public async Task<int> DeleteByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM chunks WHERE file_path = @filePath";
        cmd.Parameters.AddWithValue("@filePath", filePath);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> HasContentHashAsync(string filePath, string contentHash, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunks WHERE file_path = @filePath AND content_hash = @contentHash";
        cmd.Parameters.AddWithValue("@filePath", filePath);
        cmd.Parameters.AddWithValue("@contentHash", contentHash);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result) > 0;
    }

    public async Task<int> GetChunkCountAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunks";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Gets chunks for lexical search (without embeddings for efficiency).
    /// </summary>
    public async Task<List<(long Id, string FilePath, int StartLine, int EndLine, string Content, string? Features)>> GetChunksForLexicalSearchAsync(
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<(long Id, string FilePath, int StartLine, int EndLine, string Content, string? Features)>();

        var sql = new StringBuilder("SELECT id, file_path, start_line, end_line, content, features FROM chunks WHERE 1=1");
        var parameters = new List<SqliteParameter>();

        if (fileExtensions is { Count: > 0 })
        {
            var extensionConditions = fileExtensions
                .Select((ext, i) => $"file_path LIKE @ext{i}")
                .ToList();
            sql.Append($" AND ({string.Join(" OR ", extensionConditions)})");

            for (var i = 0; i < fileExtensions.Count; i++)
            {
                parameters.Add(new SqliteParameter($"@ext{i}", $"%{fileExtensions[i]}"));
            }
        }

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(param);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }

        return chunks;
    }

    private static CodeChunk ReadChunk(SqliteDataReader reader)
    {
        return new CodeChunk
        {
            Id = reader.GetInt64(0),
            FilePath = reader.GetString(1),
            StartLine = reader.GetInt32(2),
            EndLine = reader.GetInt32(3),
            Content = reader.GetString(4),
            ContentHash = reader.GetString(5),
            Embedding = (byte[])reader.GetValue(6),
            Features = reader.IsDBNull(7) ? null : reader.GetString(7),
            IndexedAt = DateTime.Parse(reader.GetString(8))
        };
    }

    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    #region Activity Log Methods

    public async Task<long> InsertActivityLogAsync(
        string activityType,
        string description,
        long durationMs,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO activity_log (activity_type, description, duration_ms, timestamp, details)
            VALUES (@activityType, @description, @durationMs, @timestamp, @details);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@activityType", activityType);
        cmd.Parameters.AddWithValue("@description", description);
        cmd.Parameters.AddWithValue("@durationMs", durationMs);
        cmd.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (long)result!;
    }

    public async Task<List<Models.ActivityLog>> GetRecentActivityLogsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<Models.ActivityLog>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, activity_type, description, duration_ms, timestamp, details
            FROM activity_log
            ORDER BY timestamp DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new Models.ActivityLog
            {
                Id = reader.GetInt64(0),
                ActivityType = reader.GetString(1),
                Description = reader.GetString(2),
                DurationMs = reader.GetInt64(3),
                Timestamp = DateTime.Parse(reader.GetString(4)),
                Details = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return logs;
    }

    #endregion

    #region Search History Methods

    public async Task<long> InsertSearchHistoryAsync(
        string query,
        int resultCount,
        long durationMs,
        bool cacheHit,
        bool usedHnsw,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO search_history (query, result_count, duration_ms, cache_hit, used_hnsw, timestamp)
            VALUES (@query, @resultCount, @durationMs, @cacheHit, @usedHnsw, @timestamp);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@resultCount", resultCount);
        cmd.Parameters.AddWithValue("@durationMs", durationMs);
        cmd.Parameters.AddWithValue("@cacheHit", cacheHit ? 1 : 0);
        cmd.Parameters.AddWithValue("@usedHnsw", usedHnsw ? 1 : 0);
        cmd.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O"));

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (long)result!;
    }

    public async Task<List<Models.SearchHistory>> GetRecentSearchHistoryAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var history = new List<Models.SearchHistory>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, query, result_count, duration_ms, cache_hit, used_hnsw, timestamp
            FROM search_history
            ORDER BY timestamp DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new Models.SearchHistory
            {
                Id = reader.GetInt64(0),
                Query = reader.GetString(1),
                ResultCount = reader.GetInt32(2),
                DurationMs = reader.GetInt64(3),
                CacheHit = reader.GetInt32(4) == 1,
                UsedHnsw = reader.GetInt32(5) == 1,
                Timestamp = DateTime.Parse(reader.GetString(6))
            });
        }

        return history;
    }

    public async Task<List<(DateTime Hour, double AvgSearchTimeMs, double AvgIndexTimeMs)>> GetHourlyPerformanceMetricsAsync(
        int hoursBack = 24,
        CancellationToken cancellationToken = default)
    {
        var metrics = new List<(DateTime Hour, double AvgSearchTimeMs, double AvgIndexTimeMs)>();
        var cutoff = DateTime.UtcNow.AddHours(-hoursBack).ToString("O");

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH hours AS (
                SELECT DISTINCT strftime('%Y-%m-%d %H:00:00', timestamp) as hour
                FROM (
                    SELECT timestamp FROM search_history WHERE timestamp >= @cutoff
                    UNION ALL
                    SELECT timestamp FROM activity_log WHERE timestamp >= @cutoff AND activity_type = 'indexing'
                )
            ),
            search_stats AS (
                SELECT strftime('%Y-%m-%d %H:00:00', timestamp) as hour, AVG(duration_ms) as avg_ms
                FROM search_history
                WHERE timestamp >= @cutoff
                GROUP BY strftime('%Y-%m-%d %H:00:00', timestamp)
            ),
            index_stats AS (
                SELECT strftime('%Y-%m-%d %H:00:00', timestamp) as hour, AVG(duration_ms) as avg_ms
                FROM activity_log
                WHERE timestamp >= @cutoff AND activity_type = 'indexing'
                GROUP BY strftime('%Y-%m-%d %H:00:00', timestamp)
            )
            SELECT h.hour, COALESCE(s.avg_ms, 0) as avg_search_ms, COALESCE(i.avg_ms, 0) as avg_index_ms
            FROM hours h
            LEFT JOIN search_stats s ON h.hour = s.hour
            LEFT JOIN index_stats i ON h.hour = i.hour
            ORDER BY h.hour ASC
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            metrics.Add((
                DateTime.Parse(reader.GetString(0)),
                reader.GetDouble(1),
                reader.GetDouble(2)
            ));
        }

        return metrics;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _connection.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _connection.DisposeAsync();
        _disposed = true;
    }
}
