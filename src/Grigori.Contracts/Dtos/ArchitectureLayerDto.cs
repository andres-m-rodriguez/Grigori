namespace Grigori.Contracts.Dtos;

public sealed record ArchitectureLayerForDetail(
    int Id,
    int PatternId,
    string PatternName,
    string Name,
    string Description,
    string Responsibility,
    string? Contains,
    int Order,
    List<LayerDependencyForList> OutgoingDependencies,
    List<LayerDependencyForList> IncomingDependencies,
    List<CodeTemplateForList> Templates,
    List<NamingConventionForList> NamingConventions);

public sealed record ArchitectureLayerForList(
    int Id,
    int PatternId,
    string Name,
    string Description,
    string Responsibility,
    int Order);

public sealed record ArchitectureLayerForCreate(
    int PatternId,
    string Name,
    string Description,
    string Responsibility,
    string? Contains,
    int Order);

public sealed record ArchitectureLayerForUpdate(
    string Name,
    string Description,
    string Responsibility,
    string? Contains,
    int Order);
