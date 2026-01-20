using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Mcp.Features.Search.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Grigori.Tests;

/// <summary>
/// Tests for hybrid search to verify it returns results when only one
/// search method (semantic or lexical) finds matches.
/// </summary>
public class HybridSearchTests
{
    private readonly Mock<IChunkRepository> _mockChunkRepository;
    private readonly Mock<IEmbeddingProvider> _mockEmbeddingProvider;
    private readonly Mock<IMetricsService> _mockMetricsService;
    private readonly IOptions<GrigoriOptions> _options;
    private readonly SearchService _searchService;

    public HybridSearchTests()
    {
        _mockChunkRepository = new Mock<IChunkRepository>();
        _mockEmbeddingProvider = new Mock<IEmbeddingProvider>();
        _mockMetricsService = new Mock<IMetricsService>();

        _options = Options.Create(new GrigoriOptions());

        // Setup default metrics mock
        _mockMetricsService
            .Setup(m => m.RecordSearchAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockMetricsService
            .Setup(m => m.RecordEmbeddingGeneration(It.IsAny<long>(), It.IsAny<int>()));

        _searchService = new SearchService(
            _mockChunkRepository.Object,
            _mockEmbeddingProvider.Object,
            _mockMetricsService.Object,
            _options,
            NullLogger<SearchService>.Instance);
    }

    [Fact]
    public async Task HybridSearch_WithOnlySemanticResults_ReturnsSemanticResults()
    {
        // Arrange
        var query = "authentication logic";
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        var semanticResults = new List<SearchResultItem>
        {
            new() { Id = 1, FilePath = "auth.cs", StartLine = 10, EndLine = 20, Content = "public class Auth {}", Score = 0.9f },
            new() { Id = 2, FilePath = "login.cs", StartLine = 1, EndLine = 15, Content = "public void Login() {}", Score = 0.8f }
        };

        // Setup embedding provider to return valid embedding
        _mockEmbeddingProvider
            .Setup(e => e.GetEmbeddingAsync(query, EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[], GrigoriError>.Success(queryEmbedding));

        // Setup repository to return semantic results
        _mockChunkRepository
            .Setup(r => r.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SearchResultItem>, GrigoriError>.Success(semanticResults));

        // Setup repository to return empty lexical results (no content matches BM25)
        _mockChunkRepository
            .Setup(r => r.GetChunksForLexicalSearchAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChunkForLexicalSearch>, GrigoriError>.Success(new List<ChunkForLexicalSearch>()));

        // Act
        var result = await _searchService.SearchAsync(new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            SearchMode = "hybrid"
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("auth.cs", result.Value.Results[0].FilePath);
        Assert.Equal("login.cs", result.Value.Results[1].FilePath);
    }

    [Fact]
    public async Task HybridSearch_WithOnlyLexicalResults_ReturnsLexicalResults()
    {
        // Arrange
        var query = "XYZ_UNIQUE_KEYWORD_123"; // A query that won't have semantic meaning but exact match
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Semantic search returns nothing (no semantic similarity)
        var emptySemanticResults = new List<SearchResultItem>();

        // Lexical search finds exact keyword matches
        var lexicalChunks = new List<ChunkForLexicalSearch>
        {
            new() { Id = 10, FilePath = "config.cs", StartLine = 5, EndLine = 10, Content = "const string KEY = \"XYZ_UNIQUE_KEYWORD_123\";" },
            new() { Id = 11, FilePath = "settings.cs", StartLine = 20, EndLine = 25, Content = "// XYZ_UNIQUE_KEYWORD_123 is used here" }
        };

        _mockEmbeddingProvider
            .Setup(e => e.GetEmbeddingAsync(query, EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[], GrigoriError>.Success(queryEmbedding));

        _mockChunkRepository
            .Setup(r => r.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SearchResultItem>, GrigoriError>.Success(emptySemanticResults));

        _mockChunkRepository
            .Setup(r => r.GetChunksForLexicalSearchAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChunkForLexicalSearch>, GrigoriError>.Success(lexicalChunks));

        // Act
        var result = await _searchService.SearchAsync(new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            SearchMode = "hybrid"
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        // Results should be from lexical search
        Assert.Contains(result.Value.Results, r => r.FilePath == "config.cs");
        Assert.Contains(result.Value.Results, r => r.FilePath == "settings.cs");
    }

    [Fact]
    public async Task HybridSearch_WithBothResultsEmpty_ReturnsEmptyResults()
    {
        // Arrange
        var query = "nonexistent gibberish query";
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        _mockEmbeddingProvider
            .Setup(e => e.GetEmbeddingAsync(query, EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[], GrigoriError>.Success(queryEmbedding));

        _mockChunkRepository
            .Setup(r => r.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SearchResultItem>, GrigoriError>.Success(new List<SearchResultItem>()));

        _mockChunkRepository
            .Setup(r => r.GetChunksForLexicalSearchAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChunkForLexicalSearch>, GrigoriError>.Success(new List<ChunkForLexicalSearch>()));

        // Act
        var result = await _searchService.SearchAsync(new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            SearchMode = "hybrid"
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Count);
    }

    [Fact]
    public async Task HybridSearch_WithBothResultsPresent_FusesAndReturnsResults()
    {
        // Arrange
        var query = "database connection";
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Semantic results
        var semanticResults = new List<SearchResultItem>
        {
            new() { Id = 1, FilePath = "db.cs", StartLine = 1, EndLine = 10, Content = "class DbConnection {}", Score = 0.95f },
            new() { Id = 2, FilePath = "repo.cs", StartLine = 5, EndLine = 15, Content = "void Connect() {}", Score = 0.85f }
        };

        // Lexical results - note Id=1 appears in both
        var lexicalChunks = new List<ChunkForLexicalSearch>
        {
            new() { Id = 1, FilePath = "db.cs", StartLine = 1, EndLine = 10, Content = "class DbConnection { // database connection }" },
            new() { Id = 3, FilePath = "config.cs", StartLine = 20, EndLine = 30, Content = "connectionString = \"database connection\"" }
        };

        _mockEmbeddingProvider
            .Setup(e => e.GetEmbeddingAsync(query, EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[], GrigoriError>.Success(queryEmbedding));

        _mockChunkRepository
            .Setup(r => r.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SearchResultItem>, GrigoriError>.Success(semanticResults));

        _mockChunkRepository
            .Setup(r => r.GetChunksForLexicalSearchAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChunkForLexicalSearch>, GrigoriError>.Success(lexicalChunks));

        // Act
        var result = await _searchService.SearchAsync(new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            SearchMode = "hybrid"
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Count >= 2); // At least the unique items from both lists

        // Item 1 (db.cs) should be ranked highly as it appears in both lists
        var dbResult = result.Value.Results.FirstOrDefault(r => r.FilePath == "db.cs");
        Assert.NotNull(dbResult);
    }

    [Fact]
    public async Task HybridSearch_WithSemanticFailure_StillReturnsLexicalResults()
    {
        // Arrange
        var query = "error handling";
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Embedding provider fails
        _mockEmbeddingProvider
            .Setup(e => e.GetEmbeddingAsync(query, EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[], GrigoriError>.Success(queryEmbedding));

        // Semantic search fails
        _mockChunkRepository
            .Setup(r => r.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrigoriError.SearchFailed(query, "Semantic search failed"));

        // Lexical search succeeds
        var lexicalChunks = new List<ChunkForLexicalSearch>
        {
            new() { Id = 1, FilePath = "errors.cs", StartLine = 1, EndLine = 10, Content = "catch (Exception ex) { // error handling }" }
        };
        _mockChunkRepository
            .Setup(r => r.GetChunksForLexicalSearchAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChunkForLexicalSearch>, GrigoriError>.Success(lexicalChunks));

        // Act
        var result = await _searchService.SearchAsync(new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            SearchMode = "hybrid"
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Count);
        Assert.Equal("errors.cs", result.Value.Results[0].FilePath);
    }

    [Fact]
    public async Task HybridSearch_WithLexicalFailure_StillReturnsSemanticResults()
    {
        // Arrange
        var query = "user authentication";
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        var semanticResults = new List<SearchResultItem>
        {
            new() { Id = 1, FilePath = "auth.cs", StartLine = 1, EndLine = 20, Content = "public class Authentication {}", Score = 0.9f }
        };

        _mockEmbeddingProvider
            .Setup(e => e.GetEmbeddingAsync(query, EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[], GrigoriError>.Success(queryEmbedding));

        _mockChunkRepository
            .Setup(r => r.SearchAsync(queryEmbedding, It.IsAny<int>(), It.IsAny<float>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SearchResultItem>, GrigoriError>.Success(semanticResults));

        // Lexical search fails
        _mockChunkRepository
            .Setup(r => r.GetChunksForLexicalSearchAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrigoriError.SearchFailed(query, "Lexical search failed"));

        // Act
        var result = await _searchService.SearchAsync(new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            SearchMode = "hybrid"
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Count);
        Assert.Equal("auth.cs", result.Value.Results[0].FilePath);
    }
}
