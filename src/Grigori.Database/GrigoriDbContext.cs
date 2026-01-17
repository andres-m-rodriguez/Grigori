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
