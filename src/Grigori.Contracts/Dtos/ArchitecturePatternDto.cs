namespace Grigori.Contracts.Dtos;

public sealed record ArchitecturePatternForDetail(
    int Id,
    string Name,
    string Description,
    string? Diagram,
    bool IsActive,
    DateTime CreatedAt,
    List<ArchitectureLayerForList> Layers);

public sealed record ArchitecturePatternForList(
    int Id,
    string Name,
    string Description,
    bool IsActive,
    int LayerCount);

public sealed record ArchitecturePatternForCreate(
    string Name,
    string Description,
    string? Diagram);

public sealed record ArchitecturePatternForUpdate(
    string Name,
    string Description,
    string? Diagram,
    bool IsActive);
