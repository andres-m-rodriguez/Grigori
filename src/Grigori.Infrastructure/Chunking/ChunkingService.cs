using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Utilities;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.Chunking;

public class ChunkingService
{
    private readonly ILogger<ChunkingService> _logger;
    private readonly IEnumerable<ILanguageChunker> _chunkers;
    private const int MaxChunkTokens = 500;
    private const int ApproxCharsPerToken = 4;
    private const int MaxChunkChars = MaxChunkTokens * ApproxCharsPerToken;

    public ChunkingService(ILogger<ChunkingService> logger, IEnumerable<ILanguageChunker> chunkers)
    {
        _logger = logger;
        _chunkers = chunkers;
    }

    public List<CodeChunkInput> ChunkFile(string filePath, string content)
    {
        // Try language-specific chunkers first
        foreach (var chunker in _chunkers)
        {
            if (chunker.CanHandle(filePath))
            {
                var chunks = chunker.Chunk(filePath, content);
                if (chunks.Count > 0)
                {
                    return chunks.Select(chunk => EnrichChunk(chunk, filePath)).ToList();
                }
            }
        }

        // Fall back to basic chunking
        var basicChunks = BasicChunk(filePath, content);
        return basicChunks.Select(chunk => EnrichChunk(chunk, filePath)).ToList();
    }

    private CodeChunkInput EnrichChunk(CodeChunkInput chunk, string filePath)
    {
        var contextPrefix = $"// File: {filePath}\n// Lines: {chunk.StartLine}-{chunk.EndLine}\n";
        var features = FeatureExtractor.ExtractFeatures(chunk.Content);

        return chunk with
        {
            Content = contextPrefix + chunk.Content,
            ContentHash = ContentHasher.ComputeHash(chunk.Content),
            Features = features
        };
    }

    private List<CodeChunkInput> BasicChunk(string filePath, string content)
    {
        var chunks = new List<CodeChunkInput>();
        var lines = content.Split('\n');

        if (content.Length <= MaxChunkChars)
        {
            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = 1,
                EndLine = lines.Length,
                Content = content,
                ContentHash = string.Empty
            });
            return chunks;
        }

        var currentChunk = new List<string>();
        var currentStartLine = 1;
        var currentCharCount = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineLength = line.Length + 1;

            if (currentCharCount + lineLength > MaxChunkChars && currentChunk.Count > 0)
            {
                chunks.Add(new CodeChunkInput
                {
                    FilePath = filePath,
                    StartLine = currentStartLine,
                    EndLine = currentStartLine + currentChunk.Count - 1,
                    Content = string.Join('\n', currentChunk),
                    ContentHash = string.Empty
                });

                currentChunk.Clear();
                currentStartLine = i + 1;
                currentCharCount = 0;
            }

            if (string.IsNullOrWhiteSpace(line) && currentChunk.Count > 0 && currentCharCount > MaxChunkChars / 2)
            {
                chunks.Add(new CodeChunkInput
                {
                    FilePath = filePath,
                    StartLine = currentStartLine,
                    EndLine = currentStartLine + currentChunk.Count - 1,
                    Content = string.Join('\n', currentChunk),
                    ContentHash = string.Empty
                });

                currentChunk.Clear();
                currentStartLine = i + 2;
                currentCharCount = 0;
                continue;
            }

            currentChunk.Add(line);
            currentCharCount += lineLength;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = currentStartLine,
                EndLine = currentStartLine + currentChunk.Count - 1,
                Content = string.Join('\n', currentChunk),
                ContentHash = string.Empty
            });
        }

        _logger.LogDebug("Chunked {FilePath} into {ChunkCount} chunks", filePath, chunks.Count);
        return chunks;
    }
}
