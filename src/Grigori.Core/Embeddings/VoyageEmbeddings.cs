using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grigori.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Core.Embeddings;

public class VoyageEmbeddings : IEmbeddingProvider, IDisposable
{
    private const int MaxBatchSize = 128; // Voyage API limit

    private readonly HttpClient _httpClient;
    private readonly ILogger<VoyageEmbeddings> _logger;
    private readonly string _model;

    public VoyageEmbeddings(IOptions<GrigoriOptions> options, ILogger<VoyageEmbeddings> logger)
    {
        _logger = logger;
        _model = options.Value.Anthropic.EmbeddingModel;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.Value.Anthropic.EmbeddingBaseUrl.TrimEnd('/') + "/")
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.Value.Anthropic.ApiKey}");
    }

    public async Task<float[]> GetEmbeddingAsync(string text, EmbeddingInputType inputType = EmbeddingInputType.Document, CancellationToken cancellationToken = default)
    {
        var embeddings = await GetEmbeddingsAsync([text], inputType, cancellationToken);
        return embeddings[0];
    }

    public async Task<float[][]> GetEmbeddingsAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType = EmbeddingInputType.Document, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        // Handle batches larger than Voyage API limit
        if (texts.Count <= MaxBatchSize)
        {
            return await GetEmbeddingsBatchAsync(texts, inputType, cancellationToken);
        }

        _logger.LogDebug("Splitting {Count} texts into batches of {BatchSize}", texts.Count, MaxBatchSize);

        var allEmbeddings = new List<float[]>(texts.Count);
        var batchCount = (texts.Count + MaxBatchSize - 1) / MaxBatchSize;

        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = texts
                .Skip(batchIndex * MaxBatchSize)
                .Take(MaxBatchSize)
                .ToList();

            _logger.LogDebug("Processing batch {BatchIndex}/{BatchCount} with {Count} texts",
                batchIndex + 1, batchCount, batch.Count);

            var batchEmbeddings = await GetEmbeddingsBatchAsync(batch, inputType, cancellationToken);
            allEmbeddings.AddRange(batchEmbeddings);
        }

        return allEmbeddings.ToArray();
    }

    private async Task<float[][]> GetEmbeddingsBatchAsync(IReadOnlyList<string> texts, EmbeddingInputType inputType, CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest
        {
            Model = _model,
            Input = texts.ToList(),
            InputType = inputType == EmbeddingInputType.Query ? "query" : "document"
        };

        _logger.LogDebug("Requesting embeddings for {Count} texts using model {Model}", texts.Count, _model);

        var response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);
        if (result?.Data is null)
            throw new InvalidOperationException("Failed to get embeddings: empty response");

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; set; }

        [JsonPropertyName("input")]
        public required List<string> Input { get; set; }

        [JsonPropertyName("input_type")]
        public required string InputType { get; set; }
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
