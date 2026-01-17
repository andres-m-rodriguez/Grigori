using Grigori.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Grigori.Core.Embeddings;

public class OnnxEmbeddings : IEmbeddingProvider, IDisposable
{
    private const string ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const int MaxSequenceLength = 512;
    private const int EmbeddingDimension = 384;

    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly ILogger<OnnxEmbeddings> _logger;

    public OnnxEmbeddings(IOptions<GrigoriOptions> options, ILogger<OnnxEmbeddings> logger)
    {
        _logger = logger;
        var modelPath = options.Value.Onnx.ModelPath;
        var vocabPath = options.Value.Onnx.VocabPath;

        // Resolve default paths if not specified
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

        // Auto-download if not present
        EnsureModelDownloaded(modelPath, vocabPath).GetAwaiter().GetResult();

        _logger.LogInformation("Loading ONNX model from {ModelPath}", modelPath);
        _session = new InferenceSession(modelPath);
        _tokenizer = new WordPieceTokenizer(vocabPath);
        _logger.LogInformation("ONNX embedding provider initialized successfully");
    }

    public async Task<float[]> GetEmbeddingAsync(
        string text,
        EmbeddingInputType inputType = EmbeddingInputType.Document,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await GetEmbeddingsAsync([text], inputType, cancellationToken);
        return embeddings[0];
    }

    public Task<float[][]> GetEmbeddingsAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType = EmbeddingInputType.Document,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return Task.FromResult(Array.Empty<float[]>());

        _logger.LogDebug("Generating embeddings for {Count} texts using ONNX", texts.Count);

        var results = new float[texts.Count][];

        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = GenerateEmbedding(texts[i]);
        }

        return Task.FromResult(results);
    }

    private float[] GenerateEmbedding(string text)
    {
        // Tokenize
        var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Encode(text, MaxSequenceLength);

        // Create input tensors
        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, inputIds.Length]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, attentionMask.Length]);
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, [1, tokenTypeIds.Length]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        // Run inference
        using var outputs = _session.Run(inputs);

        // Get last_hidden_state output (shape: [1, sequence_length, 384])
        var lastHiddenState = outputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();

        // Mean pooling over token embeddings (excluding padding tokens)
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

        // Average
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

    private async Task EnsureModelDownloaded(string modelPath, string vocabPath)
    {
        var modelDir = Path.GetDirectoryName(modelPath);
        if (!string.IsNullOrEmpty(modelDir))
            Directory.CreateDirectory(modelDir);

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        if (!File.Exists(modelPath))
        {
            _logger.LogInformation("Downloading ONNX model to {ModelPath}...", modelPath);
            var bytes = await httpClient.GetByteArrayAsync(ModelUrl);
            await File.WriteAllBytesAsync(modelPath, bytes);
            _logger.LogInformation("ONNX model downloaded successfully ({Size:F1} MB)", bytes.Length / 1024.0 / 1024.0);
        }

        if (!File.Exists(vocabPath))
        {
            _logger.LogInformation("Downloading vocabulary to {VocabPath}...", vocabPath);
            var content = await httpClient.GetStringAsync(VocabUrl);
            await File.WriteAllTextAsync(vocabPath, content);
            _logger.LogInformation("Vocabulary downloaded successfully");
        }
    }

    public void Dispose()
    {
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal class WordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _clsTokenId;
    private readonly int _sepTokenId;
    private readonly int _unkTokenId;
    private readonly int _padTokenId;

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
        _padTokenId = _vocab.GetValueOrDefault("[PAD]", 0);
    }

    public (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Encode(string text, int maxLength)
    {
        var tokens = Tokenize(text);

        // Truncate if necessary (leave room for [CLS] and [SEP])
        if (tokens.Count > maxLength - 2)
            tokens = tokens.Take(maxLength - 2).ToList();

        // Build input ids: [CLS] + tokens + [SEP]
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

        // Basic tokenization: split on whitespace and punctuation
        var words = BasicTokenize(text);

        foreach (var word in words)
        {
            // WordPiece tokenization
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
