using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Infrastructure.Embeddings.Grpc;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.Embeddings;

public class GrpcEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    public const string DependencyId = "grpc-embedding";

    private readonly ILogger<GrpcEmbeddingProvider> _logger;
    private readonly IDependencyTracker _tracker;
    private readonly GrpcChannel _channel;
    private readonly EmbeddingService.EmbeddingServiceClient _client;

    public GrpcEmbeddingProvider(
        IOptions<GrigoriOptions> options,
        ILogger<GrpcEmbeddingProvider> logger,
        IDependencyTracker tracker)
    {
        _logger = logger;
        _tracker = tracker;

        var address = options.Value.EmbeddingService.Address;
        _logger.LogInformation("Connecting to gRPC embedding service at {Address}", address);

        _channel = GrpcChannel.ForAddress(address);
        _client = new EmbeddingService.EmbeddingServiceClient(_channel);

        _tracker.Register(DependencyId, "gRPC Embedding Service", $"Remote embedding service at {address}");
        _tracker.UpdateStatus(DependencyId, DependencyState.Ready, progress: 100, message: "Connected");
    }

    public bool IsReady => true;

    public async Task<Result<float[], GrigoriError>> GetEmbeddingAsync(
        string text,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new EmbeddingRequest
            {
                Text = text,
                InputType = inputType == EmbeddingInputType.Query ? InputType.Query : InputType.Document
            };

            var response = await _client.GetEmbeddingAsync(request, cancellationToken: cancellationToken);

            if (!response.Success)
            {
                return GrigoriError.EmbeddingProviderError($"gRPC embedding failed: {response.ErrorMessage}");
            }

            return response.Embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embedding from gRPC service");
            return GrigoriError.EmbeddingProviderError($"Failed to get embedding: {ex.Message}", ex);
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
            var request = new BatchEmbeddingRequest
            {
                InputType = inputType == EmbeddingInputType.Query ? InputType.Query : InputType.Document
            };
            request.Texts.AddRange(texts);

            var response = await _client.GetEmbeddingsAsync(request, cancellationToken: cancellationToken);

            if (!response.Success)
            {
                return GrigoriError.EmbeddingProviderError($"gRPC batch embedding failed: {response.ErrorMessage}");
            }

            var result = new float[response.Embeddings.Count][];
            for (var i = 0; i < response.Embeddings.Count; i++)
            {
                result[i] = response.Embeddings[i].Values.ToArray();
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embeddings from gRPC service");
            return GrigoriError.EmbeddingProviderError($"Failed to get embeddings: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
        GC.SuppressFinalize(this);
    }
}
