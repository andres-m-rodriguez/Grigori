using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Grigori.Embedder.Services;

public class OnnxEmbeddingGenerator : IDisposable
{
    private const string ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const int MaxSequenceLength = 512;
    private const int EmbeddingDimension = 384;

    private readonly ILogger<OnnxEmbeddingGenerator> _logger;
    private readonly IConfiguration _configuration;

    private InferenceSession? _session;
    private WordPieceTokenizer? _tokenizer;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private Exception? _initException;

    public OnnxEmbeddingGenerator(ILogger<OnnxEmbeddingGenerator> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Start initialization in background
        _ = InitializeAsync();
    }

    public bool IsReady => _initialized && _session != null;

    private string ModelPath => _configuration["Embedder:ModelPath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grigori", "models", "all-MiniLM-L6-v2.onnx");

    private string VocabPath => _configuration["Embedder:VocabPath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grigori", "models", "vocab.txt");

    private async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _logger.LogInformation("Initializing ONNX embedding generator...");

            await EnsureModelDownloadedAsync();

            _logger.LogInformation("Loading ONNX model from {ModelPath}", ModelPath);
            _session = new InferenceSession(ModelPath);
            _tokenizer = new WordPieceTokenizer(VocabPath);

            _initialized = true;
            _logger.LogInformation("ONNX embedding generator initialized successfully");
        }
        catch (Exception ex)
        {
            _initException = ex;
            _logger.LogError(ex, "Failed to initialize ONNX embedding generator");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized && _initException == null)
            {
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

    public float[] GenerateEmbedding(string text)
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

        // L2 normalize
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
        var modelDir = Path.GetDirectoryName(ModelPath);
        if (!string.IsNullOrEmpty(modelDir))
            Directory.CreateDirectory(modelDir);

        var needsModelDownload = !File.Exists(ModelPath);
        var needsVocabDownload = !File.Exists(VocabPath);

        if (!needsModelDownload && !needsVocabDownload)
        {
            _logger.LogInformation("Model files found at {ModelPath}", ModelPath);
            return;
        }

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        if (needsModelDownload)
        {
            _logger.LogInformation("Downloading ONNX model to {ModelPath}...", ModelPath);
            await DownloadFileAsync(httpClient, ModelUrl, ModelPath);
            _logger.LogInformation("ONNX model downloaded successfully");
        }

        if (needsVocabDownload)
        {
            _logger.LogInformation("Downloading vocabulary to {VocabPath}...", VocabPath);
            var content = await httpClient.GetStringAsync(VocabUrl);
            await File.WriteAllTextAsync(VocabPath, content);
            _logger.LogInformation("Vocabulary downloaded successfully");
        }
    }

    private static async Task DownloadFileAsync(HttpClient client, string url, string destPath)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await contentStream.CopyToAsync(fileStream);
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
