namespace Grigori.Contracts.Dtos;

public sealed record DesignPreferenceForDetail(
    int Id,
    string Category,
    string Preference,
    string? Rationale,
    int Priority,
    DateTime CreatedAt);

public sealed record DesignPreferenceForList(
    int Id,
    string Category,
    string Preference,
    string? Rationale,
    int Priority);

public sealed record DesignPreferenceForCreate(
    string Category,
    string Preference,
    string? Rationale,
    int Priority);

public sealed record DesignPreferenceForUpdate(
    string Category,
    string Preference,
    string? Rationale,
    int Priority);
