namespace Grigori.Contracts.Dtos.Search;

public record CodeChunkDto(string FilePath, int StartLine, int EndLine, string Content, float Score);
