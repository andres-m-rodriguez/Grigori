namespace Grigori.Contracts.Dtos.Metrics;

public record ActivityEvent(
    DateTime Timestamp,
    string FilePath,
    string ProjectName,
    int ChunksCreated);
