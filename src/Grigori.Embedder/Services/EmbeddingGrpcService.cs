using Grpc.Core;

namespace Grigori.Embedder.Services;

public class EmbeddingGrpcService : EmbeddingService.EmbeddingServiceBase
{
    private readonly OnnxEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<EmbeddingGrpcService> _logger;

    public EmbeddingGrpcService(OnnxEmbeddingGenerator embeddingGenerator, ILogger<EmbeddingGrpcService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public override async Task<EmbeddingResponse> GetEmbedding(EmbeddingRequest request, ServerCallContext context)
    {
        try
        {
            await _embeddingGenerator.EnsureInitializedAsync();
            var embedding = _embeddingGenerator.GenerateEmbedding(request.Text);

            return new EmbeddingResponse
            {
                Success = true,
                Embedding = { embedding }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding");
            return new EmbeddingResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override async Task<BatchEmbeddingResponse> GetEmbeddings(BatchEmbeddingRequest request, ServerCallContext context)
    {
        try
        {
            await _embeddingGenerator.EnsureInitializedAsync();

            var response = new BatchEmbeddingResponse { Success = true };

            foreach (var text in request.Texts)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var embedding = _embeddingGenerator.GenerateEmbedding(text);
                var vector = new EmbeddingVector();
                vector.Values.AddRange(embedding);
                response.Embeddings.Add(vector);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings batch");
            return new BatchEmbeddingResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override Task<HealthCheckResponse> HealthCheck(HealthCheckRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HealthCheckResponse
        {
            Ready = _embeddingGenerator.IsReady,
            Status = _embeddingGenerator.IsReady ? "Ready" : "Initializing"
        });
    }
}
