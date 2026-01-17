namespace Grigori.Core.Storage;

public record CodeChunk
{
    public required long Id { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public required float[] Embedding { get; init; }
    public required DateTime IndexedAt { get; init; }
    public List<string> Features { get; init; } = [];
}

public record CodeChunkInput
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public List<string> Features { get; init; } = [];
}

public record SearchResult
{
    public required CodeChunk Chunk { get; init; }
    public required float Score { get; init; }
}

public class GrigoriOptions
{
    public string EmbeddingProvider { get; set; } = "onnx"; // "onnx" or "voyage"
    public OnnxOptions Onnx { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public string IndexPath { get; set; } = "./grigori.db";
    public List<string> WatchedDirectories { get; set; } = [];
    public List<string> FileExtensions { get; set; } = [".cs", ".ts", ".js", ".py", ".go", ".rs", ".java", ".tsx", ".jsx"];
    public ApiOptions Api { get; set; } = new();

    /// <summary>
    /// Directory patterns to exclude from indexing.
    /// Uses glob-style patterns. Default excludes common build/dependency folders.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } =
    [
        "**/obj/**",
        "**/bin/**",
        "**/node_modules/**",
        "**/.git/**",
        "**/dist/**",
        "**/build/**",
        "**/.vs/**",
        "**/packages/**",
        "**/TestResults/**"
    ];

    /// <summary>
    /// Quantization mode for embedding storage.
    /// None: Store embeddings as float32 (default, full precision)
    /// Int8: Quantize to int8 for ~4x storage reduction with minimal accuracy loss
    /// </summary>
    public QuantizationMode Quantization { get; set; } = QuantizationMode.None;

    #region HNSW Index Configuration

    /// <summary>
    /// Enable HNSW (Hierarchical Navigable Small World) approximate nearest neighbor indexing.
    /// Provides 50-100x faster search for large codebases at the cost of some memory.
    /// The HNSW index is persisted alongside the SQLite database.
    /// </summary>
    public bool UseHnswIndex { get; set; } = true;

    /// <summary>
    /// HNSW M parameter: Maximum number of connections per node.
    /// Higher values improve recall but increase memory and build time.
    /// Recommended: 12-48. Default: 16.
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW efConstruction parameter: Size of dynamic candidate list during index construction.
    /// Higher values improve index quality but increase build time.
    /// Recommended: 100-500. Default: 200.
    /// </summary>
    public int HnswEfConstruction { get; set; } = 200;

    /// <summary>
    /// HNSW efSearch parameter: Size of dynamic candidate list during search.
    /// Higher values improve recall but increase search time.
    /// Can be adjusted at runtime for recall/speed tradeoff.
    /// Recommended: 50-200. Default: 50.
    /// </summary>
    public int HnswEfSearch { get; set; } = 50;

    #endregion
}

/// <summary>
/// Quantization mode for embedding storage
/// </summary>
public enum QuantizationMode
{
    /// <summary>
    /// No quantization - store as float32 (1536 bytes for 384-dim embeddings)
    /// </summary>
    None,

    /// <summary>
    /// Int8 quantization - reduces storage by ~4x (392 bytes for 384-dim embeddings)
    /// Format: [4 bytes scale][4 bytes zeroPoint][N bytes quantized values]
    /// </summary>
    Int8
}

public class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "voyage-code-3";
    public string EmbeddingBaseUrl { get; set; } = "https://api.voyageai.com/v1";
}

public class ApiOptions
{
    public int Port { get; set; } = 5150;
}

public class OnnxOptions
{
    public string ModelPath { get; set; } = string.Empty; // Defaults to %LOCALAPPDATA%/grigori/models/all-MiniLM-L6-v2.onnx
    public string VocabPath { get; set; } = string.Empty; // Defaults to %LOCALAPPDATA%/grigori/models/vocab.txt
}
