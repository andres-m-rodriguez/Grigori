using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Grigori.Infrastructure.Embeddings;

public class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    public const string DependencyId = "onnx-model";

    private const string ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const int MaxSequenceLength = 512;
    private const int EmbeddingDimension = 384;

    private readonly ILogger<OnnxEmbeddingProvider> _logger;
    private readonly IDependencyTracker _tracker;
    private readonly string _modelPath;
    private readonly string _vocabPath;

    private InferenceSession? _session;
    private WordPieceTokenizer? _tokenizer;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private Exception? _initException;

    public OnnxEmbeddingProvider(
        IOptions<GrigoriOptions> options,
        ILogger<OnnxEmbeddingProvider> logger,
        IDependencyTracker tracker)
    {
        _logger = logger;
        _tracker = tracker;

        var modelPath = options.Value.Onnx.ModelPath;
        var vocabPath = options.Value.Onnx.VocabPath;

        if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(vocabPath))
        {
            var defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "grigori", "models");

            if (string.IsNullOrEmpty(modelPath))
                modelPath = Path.Combine(defaultDir, "all-MiniLM-L6-v2.onnx");
            if (string.IsNullOrEmpty(vocabPath))
                vocabPath = Path.Combine(defaultDir, "vocab.txt");
        }

        _modelPath = modelPath;
        _vocabPath = vocabPath;

        // Register with tracker
        _tracker.Register(DependencyId, "ONNX Model", "all-MiniLM-L6-v2 embedding model (~23 MB)");

        // Start initialization in background
        _ = InitializeAsync();
    }

    public bool IsReady => _initialized && _session != null;

    private async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _tracker.UpdateStatus(DependencyId, DependencyState.Checking, message: "Checking model files...");

            await EnsureModelDownloadedAsync();

            _tracker.UpdateStatus(DependencyId, DependencyState.Loading, message: "Loading ONNX model...");
            _logger.LogInformation("Loading ONNX model from {ModelPath}", _modelPath);

            _session = new InferenceSession(_modelPath);
            _tokenizer = new WordPieceTokenizer(_vocabPath);

            _initialized = true;
            _tracker.UpdateStatus(DependencyId, DependencyState.Ready, progress: 100, message: "Ready");
            _logger.LogInformation("ONNX embedding provider initialized successfully");
        }
        catch (Exception ex)
        {
            _initException = ex;
            _tracker.UpdateStatus(DependencyId, DependencyState.Failed, message: ex.Message);
            _logger.LogError(ex, "Failed to initialize ONNX embedding provider");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized && _initException == null)
            {
                // Initialization is still in progress, wait for it
                _initLock.Release();
                while (!_initialized && _initException == null)
                {
                    await Task.Delay(100);
                }
                return;
            }

            if (_initException != null)
            {
                throw new InvalidOperationException("ONNX model failed to initialize", _initException);
            }
        }
        finally
        {
            if (_initLock.CurrentCount == 0)
                _initLock.Release();
        }
    }

    public async Task<Result<float[], GrigoriError>> GetEmbeddingAsync(
        string text,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var embedding = GenerateEmbedding(text);
            return embedding;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding");
            return GrigoriError.EmbeddingProviderError($"Failed to generate embedding: {ex.Message}", ex);
        }
    }

    public async Task<Result<float[][], GrigoriError>> GetEmbeddingsAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return Array.Empty<float[]>();

        try
        {
            await EnsureInitializedAsync();
            _logger.LogDebug("Generating embeddings for {Count} texts using ONNX", texts.Count);

            var results = new float[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = GenerateEmbedding(texts[i]);
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings batch");
            return GrigoriError.EmbeddingProviderError($"Failed to generate embeddings: {ex.Message}", ex);
        }
    }

    private float[] GenerateEmbedding(string text)
    {
        if (_session == null || _tokenizer == null)
            throw new InvalidOperationException("ONNX model not initialized");

        var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Encode(text, MaxSequenceLength);

        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, inputIds.Length]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, attentionMask.Length]);
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, [1, tokenTypeIds.Length]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using var outputs = _session.Run(inputs);
        var lastHiddenState = outputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();

        return MeanPool(lastHiddenState, attentionMask);
    }

    private static float[] MeanPool(Tensor<float> lastHiddenState, long[] attentionMask)
    {
        var sequenceLength = attentionMask.Length;
        var embedding = new float[EmbeddingDimension];
        var tokenCount = 0f;

        for (var i = 0; i < sequenceLength; i++)
        {
            if (attentionMask[i] == 0) continue;

            tokenCount++;
            for (var j = 0; j < EmbeddingDimension; j++)
            {
                embedding[j] += lastHiddenState[0, i, j];
            }
        }

        if (tokenCount > 0)
        {
            for (var j = 0; j < EmbeddingDimension; j++)
            {
                embedding[j] /= tokenCount;
            }
        }

        var norm = 0f;
        for (var j = 0; j < EmbeddingDimension; j++)
        {
            norm += embedding[j] * embedding[j];
        }
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (var j = 0; j < EmbeddingDimension; j++)
            {
                embedding[j] /= norm;
            }
        }

        return embedding;
    }

    private async Task EnsureModelDownloadedAsync()
    {
        var modelDir = Path.GetDirectoryName(_modelPath);
        if (!string.IsNullOrEmpty(modelDir))
            Directory.CreateDirectory(modelDir);

        var needsModelDownload = !File.Exists(_modelPath);
        var needsVocabDownload = !File.Exists(_vocabPath);

        if (!needsModelDownload && !needsVocabDownload)
        {
            _tracker.UpdateStatus(DependencyId, DependencyState.Checking, progress: 100, message: "Model files found");
            return;
        }

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        if (needsModelDownload)
        {
            _tracker.UpdateStatus(DependencyId, DependencyState.Downloading, progress: 0, message: "Downloading ONNX model...");
            _logger.LogInformation("Downloading ONNX model to {ModelPath}...", _modelPath);

            await DownloadWithProgressAsync(httpClient, ModelUrl, _modelPath, "model");
        }

        if (needsVocabDownload)
        {
            _tracker.UpdateStatus(DependencyId, DependencyState.Downloading, progress: 95, message: "Downloading vocabulary...");
            _logger.LogInformation("Downloading vocabulary to {VocabPath}...", _vocabPath);

            var content = await httpClient.GetStringAsync(VocabUrl);
            await File.WriteAllTextAsync(_vocabPath, content);
            _logger.LogInformation("Vocabulary downloaded successfully");
        }
    }

    private async Task DownloadWithProgressAsync(HttpClient client, string url, string destPath, string name)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var progress = (double)downloadedBytes / totalBytes * 90; // Reserve 10% for vocab + loading
                var sizeMb = downloadedBytes / 1024.0 / 1024.0;
                var totalMb = totalBytes / 1024.0 / 1024.0;
                _tracker.UpdateStatus(DependencyId, DependencyState.Downloading, progress,
                    $"Downloading {name}: {sizeMb:F1} / {totalMb:F1} MB");
            }
        }

        _logger.LogInformation("Downloaded {Name} successfully ({Size:F1} MB)", name, downloadedBytes / 1024.0 / 1024.0);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal class WordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _clsTokenId;
    private readonly int _sepTokenId;
    private readonly int _unkTokenId;

    public WordPieceTokenizer(string vocabPath)
    {
        _vocab = new Dictionary<string, int>();
        var lines = File.ReadAllLines(vocabPath);

        for (var i = 0; i < lines.Length; i++)
        {
            _vocab[lines[i]] = i;
        }

        _clsTokenId = _vocab.GetValueOrDefault("[CLS]", 101);
        _sepTokenId = _vocab.GetValueOrDefault("[SEP]", 102);
        _unkTokenId = _vocab.GetValueOrDefault("[UNK]", 100);
    }

    public (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Encode(string text, int maxLength)
    {
        var tokens = Tokenize(text);

        if (tokens.Count > maxLength - 2)
            tokens = tokens.Take(maxLength - 2).ToList();

        var inputIds = new long[tokens.Count + 2];
        var attentionMask = new long[tokens.Count + 2];
        var tokenTypeIds = new long[tokens.Count + 2];

        inputIds[0] = _clsTokenId;
        attentionMask[0] = 1;

        for (var i = 0; i < tokens.Count; i++)
        {
            inputIds[i + 1] = _vocab.GetValueOrDefault(tokens[i], _unkTokenId);
            attentionMask[i + 1] = 1;
        }

        inputIds[tokens.Count + 1] = _sepTokenId;
        attentionMask[tokens.Count + 1] = 1;

        return (inputIds, attentionMask, tokenTypeIds);
    }

    private List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        text = text.ToLowerInvariant();

        var words = BasicTokenize(text);
        foreach (var word in words)
        {
            var subTokens = WordPieceTokenize(word);
            tokens.AddRange(subTokens);
        }

        return tokens;
    }

    private static List<string> BasicTokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                tokens.Add(c.ToString());
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private List<string> WordPieceTokenize(string word)
    {
        if (_vocab.ContainsKey(word))
            return [word];

        var tokens = new List<string>();
        var start = 0;

        while (start < word.Length)
        {
            var end = word.Length;
            string? curSubstr = null;

            while (start < end)
            {
                var substr = word[start..end];
                if (start > 0)
                    substr = "##" + substr;

                if (_vocab.ContainsKey(substr))
                {
                    curSubstr = substr;
                    break;
                }
                end--;
            }

            if (curSubstr == null)
            {
                tokens.Add("[UNK]");
                break;
            }

            tokens.Add(curSubstr);
            start = end;
        }

        return tokens;
    }
}
