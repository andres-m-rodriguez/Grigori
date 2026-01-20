namespace Grigori.Contracts.Dtos.Search;

public record SearchRequestDto(string Query, int Limit = 5, string OutputMode = "full", string? FileTypes = null, string SearchMode = "hybrid");
