namespace Grigori.Contracts.Dtos;

public sealed record CodingPatternForDetail(
    int Id,
    string Name,
    string Description,
    string Category,
    string? Example,
    bool IsActive,
    DateTime CreatedAt);

public sealed record CodingPatternForList(
    int Id,
    string Name,
    string Description,
    string Category,
    string? Example,
    bool IsActive);

public sealed record CodingPatternForCreate(
    string Name,
    string Description,
    string Category,
    string? Example);

public sealed record CodingPatternForUpdate(
    string Name,
    string Description,
    string Category,
    string? Example,
    bool IsActive);
