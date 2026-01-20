namespace Grigori.Contracts.Options;

public class GrigoriOptions
{
    public const string SectionName = "Grigori";

    public string EmbeddingProvider { get; set; } = "grpc";
    public OnnxOptions Onnx { get; set; } = new();
    public VoyageOptions Voyage { get; set; } = new();
    public EmbeddingServiceOptions EmbeddingService { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public string IndexPath { get; set; } = "./grigori.db";
    public List<string> WatchedDirectories { get; set; } = [];
    public List<string> FileExtensions { get; set; } = [".cs", ".ts", ".js", ".py", ".go", ".rs", ".java", ".tsx", ".jsx"];
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
    public QuantizationMode Quantization { get; set; } = QuantizationMode.None;
    public HnswOptions Hnsw { get; set; } = new();
    public ApiOptions Api { get; set; } = new();
}

public class OnnxOptions
{
    public string ModelPath { get; set; } = string.Empty;
    public string VocabPath { get; set; } = string.Empty;
}

public class VoyageOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "voyage-code-3";
    public string BaseUrl { get; set; } = "https://api.voyageai.com/v1";
}

public class HnswOptions
{
    public bool Enabled { get; set; } = true;
    public int M { get; set; } = 16;
    public int EfConstruction { get; set; } = 200;
    public int EfSearch { get; set; } = 50;
}

public class ApiOptions
{
    public int Port { get; set; } = 5150;
}

public enum QuantizationMode
{
    None,
    Int8
}

public class DatabaseOptions
{
    public string ConnectionString { get; set; } = "Host=localhost;Database=grigori;Username=grigori;Password=grigori";
}

public class EmbeddingServiceOptions
{
    public string Address { get; set; } = "http://localhost:50051";
}
