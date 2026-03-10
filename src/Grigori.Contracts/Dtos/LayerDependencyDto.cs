namespace Grigori.Contracts.Dtos;

public sealed record LayerDependencyForDetail(
    int Id,
    int SourceLayerId,
    string SourceLayerName,
    int TargetLayerId,
    string TargetLayerName,
    bool IsAllowed,
    string? Rationale);

public sealed record LayerDependencyForList(
    int Id,
    int SourceLayerId,
    string SourceLayerName,
    int TargetLayerId,
    string TargetLayerName,
    bool IsAllowed);

public sealed record LayerDependencyForCreate(
    int SourceLayerId,
    int TargetLayerId,
    bool IsAllowed,
    string? Rationale);

public sealed record LayerDependencyForUpdate(
    bool IsAllowed,
    string? Rationale);
