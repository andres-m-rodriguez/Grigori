namespace Grigori.Contracts.Interfaces;

public interface ILanguageChunker
{
    bool CanHandle(string filePath);
    List<CodeChunkInput> Chunk(string filePath, string content);
}
