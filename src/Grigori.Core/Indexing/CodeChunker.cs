using Grigori.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Grigori.Core.Indexing;

public class CodeChunker
{
    private readonly ILogger<CodeChunker> _logger;
    private readonly RoslynCodeChunker _roslynChunker;
    private const int MaxChunkTokens = 500;
    private const int ApproxCharsPerToken = 4;
    private const int MaxChunkChars = MaxChunkTokens * ApproxCharsPerToken;

    public CodeChunker(ILogger<CodeChunker> logger, RoslynCodeChunker roslynChunker)
    {
        _logger = logger;
        _roslynChunker = roslynChunker;
    }

    public List<CodeChunkInput> ChunkFile(string filePath, string content)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Use Roslyn for C# files
        if (extension == ".cs")
        {
            var roslynChunks = _roslynChunker.ChunkCSharpFile(filePath, content);
            if (roslynChunks.Count > 0)
            {
                // Extract features for each chunk
                return roslynChunks.Select(chunk => chunk with
                {
                    Features = FeatureExtractor.ExtractFeatures(chunk.Content)
                }).ToList();
            }
            // Fall through to basic chunking if Roslyn fails
        }

        // Basic chunking for non-C# files or as fallback
        var chunks = BasicChunk(filePath, content);

        // Add context prefix and extract features for each chunk
        return chunks.Select(chunk => EnrichChunk(chunk, filePath)).ToList();
    }

    private CodeChunkInput EnrichChunk(CodeChunkInput chunk, string filePath)
    {
        // Build context prefix
        var contextPrefix = $"// File: {filePath}\n// Lines: {chunk.StartLine}-{chunk.EndLine}\n";

        // Extract features
        var features = FeatureExtractor.ExtractFeatures(chunk.Content);

        return chunk with
        {
            Content = contextPrefix + chunk.Content,
            Features = features
        };
    }

    private List<CodeChunkInput> BasicChunk(string filePath, string content)
    {
        var chunks = new List<CodeChunkInput>();
        var lines = content.Split('\n');

        if (content.Length <= MaxChunkChars)
        {
            // Entire file fits in one chunk
            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = 1,
                EndLine = lines.Length,
                Content = content
            });
            return chunks;
        }

        // Split by logical breaks (blank lines, significant whitespace changes)
        var currentChunk = new List<string>();
        var currentStartLine = 1;
        var currentCharCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineLength = line.Length + 1; // +1 for newline

            // Check if adding this line would exceed the limit
            if (currentCharCount + lineLength > MaxChunkChars && currentChunk.Count > 0)
            {
                // Save current chunk
                chunks.Add(new CodeChunkInput
                {
                    FilePath = filePath,
                    StartLine = currentStartLine,
                    EndLine = currentStartLine + currentChunk.Count - 1,
                    Content = string.Join('\n', currentChunk)
                });

                currentChunk.Clear();
                currentStartLine = i + 1;
                currentCharCount = 0;
            }

            // Check for logical break points (empty line after content)
            if (string.IsNullOrWhiteSpace(line) && currentChunk.Count > 0 && currentCharCount > MaxChunkChars / 2)
            {
                chunks.Add(new CodeChunkInput
                {
                    FilePath = filePath,
                    StartLine = currentStartLine,
                    EndLine = currentStartLine + currentChunk.Count - 1,
                    Content = string.Join('\n', currentChunk)
                });

                currentChunk.Clear();
                currentStartLine = i + 2; // Skip the blank line
                currentCharCount = 0;
                continue;
            }

            currentChunk.Add(line);
            currentCharCount += lineLength;
        }

        // Don't forget the last chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = currentStartLine,
                EndLine = currentStartLine + currentChunk.Count - 1,
                Content = string.Join('\n', currentChunk)
            });
        }

        _logger.LogDebug("Chunked {FilePath} into {ChunkCount} chunks", filePath, chunks.Count);
        return chunks;
    }
}
