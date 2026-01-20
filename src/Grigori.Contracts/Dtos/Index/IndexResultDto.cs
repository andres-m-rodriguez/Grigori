namespace Grigori.Contracts.Dtos.Index;

public record IndexResultDto(bool Success, int FilesIndexed, int ChunksCreated, long DurationMs, string? Message = null);
