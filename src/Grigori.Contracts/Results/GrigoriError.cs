namespace Grigori.Contracts.Results;

/// <summary>
/// A unified error type for all Grigori operations.
/// Uses a discriminated union pattern with an error code and contextual message.
/// </summary>
public readonly record struct GrigoriError
{
    public GrigoriErrorCode Code { get; init; }
    public string Message { get; init; }
    public string? Details { get; init; }
    public Exception? Exception { get; init; }

    private GrigoriError(GrigoriErrorCode code, string message, string? details = null, Exception? exception = null)
    {
        Code = code;
        Message = message;
        Details = details;
        Exception = exception;
    }

    // Factory methods for each error type
    public static GrigoriError DirectoryNotFound(string path) =>
        new(GrigoriErrorCode.DirectoryNotFound, $"Directory not found: {path}", path);

    public static GrigoriError FileNotFound(string path) =>
        new(GrigoriErrorCode.FileNotFound, $"File not found: {path}", path);

    public static GrigoriError IndexingFailed(string path, string reason, Exception? ex = null) =>
        new(GrigoriErrorCode.IndexingFailed, $"Indexing failed for '{path}': {reason}", path, ex);

    public static GrigoriError SearchFailed(string query, string reason, Exception? ex = null) =>
        new(GrigoriErrorCode.SearchFailed, $"Search failed for query '{query}': {reason}", query, ex);

    public static GrigoriError InvalidQuery(string message) =>
        new(GrigoriErrorCode.InvalidQuery, message);

    public static GrigoriError EmbeddingProviderError(string message, Exception? ex = null) =>
        new(GrigoriErrorCode.EmbeddingProviderError, message, exception: ex);

    public static GrigoriError DatabaseError(string message, Exception? ex = null) =>
        new(GrigoriErrorCode.DatabaseError, message, exception: ex);

    public static GrigoriError ChunkingError(string path, string reason, Exception? ex = null) =>
        new(GrigoriErrorCode.ChunkingError, $"Chunking failed for '{path}': {reason}", path, ex);

    public static GrigoriError ValidationError(string message) =>
        new(GrigoriErrorCode.ValidationError, message);

    public static GrigoriError ConfigurationError(string message) =>
        new(GrigoriErrorCode.ConfigurationError, message);

    public static GrigoriError Unknown(string message, Exception? ex = null) =>
        new(GrigoriErrorCode.Unknown, message, exception: ex);

    public override string ToString() => Details is not null
        ? $"[{Code}] {Message} (Details: {Details})"
        : $"[{Code}] {Message}";
}

/// <summary>
/// Error codes for Grigori operations.
/// </summary>
public enum GrigoriErrorCode
{
    Unknown = 0,
    DirectoryNotFound,
    FileNotFound,
    IndexingFailed,
    SearchFailed,
    InvalidQuery,
    EmbeddingProviderError,
    DatabaseError,
    ChunkingError,
    ValidationError,
    ConfigurationError
}
