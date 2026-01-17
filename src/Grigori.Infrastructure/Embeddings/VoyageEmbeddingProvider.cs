using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.Embeddings;

public class VoyageEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private const int MaxBatchSize = 128; // Voyage API limit

    private readonly HttpClient _httpClient;
    private readonly ILogger<VoyageEmbeddingProvider> _logger;
    private readonly string _model;

    public VoyageEmbeddingProvider(IOptions<GrigoriOptions> options, ILogger<VoyageEmbeddingProvider> logger)
    {
        _logger = logger;
        _model = options.Value.Voyage.Model;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.Value.Voyage.BaseUrl.TrimEnd('/') + "/")
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.Value.Voyage.ApiKey}");
    }

    public VoyageEmbeddingProvider(HttpClient httpClient, IOptions<GrigoriOptions> options, ILogger<VoyageEmbeddingProvider> logger)
    {
        _logger = logger;
        _model = options.Value.Voyage.Model;
        _httpClient = httpClient;
    }

    public async Task<Result<float[], GrigoriError>> GetEmbeddingAsync(
        string text,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        var result = await GetEmbeddingsAsync([text], inputType, cancellationToken);
        return result.Map(embeddings => embeddings[0]);
    }

    public async Task<Result<float[][], GrigoriError>> GetEmbeddingsAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return Array.Empty<float[]>();

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

            var batchResult = await GetEmbeddingsBatchAsync(batch, inputType, cancellationToken);
            if (batchResult.IsFailure)
                return batchResult;

            allEmbeddings.AddRange(batchResult.Value);
        }

        return allEmbeddings.ToArray();
    }

    private async Task<Result<float[][], GrigoriError>> GetEmbeddingsBatchAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest
        {
            Model = _model,
            Input = texts.ToList(),
            InputType = inputType == EmbeddingInputType.Query ? "query" : "document"
        };

        _logger.LogDebug("Requesting embeddings for {Count} texts using model {Model}", texts.Count, _model);

        try
        {
            var response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Voyage API error: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return GrigoriError.EmbeddingProviderError($"Voyage API error: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);
            if (result?.Data is null)
                return GrigoriError.EmbeddingProviderError("Failed to get embeddings: empty response");

            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToArray();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Voyage API");
            return GrigoriError.EmbeddingProviderError($"HTTP error: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error from Voyage API");
            return GrigoriError.EmbeddingProviderError($"JSON parsing error: {ex.Message}", ex);
        }
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
