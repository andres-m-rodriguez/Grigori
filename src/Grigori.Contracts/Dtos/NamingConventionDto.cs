namespace Grigori.Contracts.Dtos;

public sealed record NamingConventionForDetail(
    int Id,
    string Context,
    string Pattern,
    string Example,
    string? Description,
    int? LayerId,
    string? LayerName,
    DateTime CreatedAt);

public sealed record NamingConventionForList(
    int Id,
    string Context,
    string Pattern,
    string Example,
    int? LayerId,
    string? LayerName);

public sealed record NamingConventionForCreate(
    string Context,
    string Pattern,
    string Example,
    string? Description,
    int? LayerId);

public sealed record NamingConventionForUpdate(
    string Context,
    string Pattern,
    string Example,
    string? Description,
    int? LayerId);
