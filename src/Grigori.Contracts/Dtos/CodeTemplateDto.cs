namespace Grigori.Contracts.Dtos;

public sealed record CodeTemplateForDetail(
    int Id,
    string Name,
    string Description,
    string Language,
    string Category,
    string Template,
    int? LayerId,
    string? LayerName,
    DateTime CreatedAt);

public sealed record CodeTemplateForList(
    int Id,
    string Name,
    string Description,
    string Language,
    string Category,
    int? LayerId,
    string? LayerName);

public sealed record CodeTemplateForCreate(
    string Name,
    string Description,
    string Language,
    string Category,
    string Template,
    int? LayerId);

public sealed record CodeTemplateForUpdate(
    string Name,
    string Description,
    string Language,
    string Category,
    string Template,
    int? LayerId);
