namespace Grigori.Contracts.Dtos;

public enum AvoidanceSeverity
{
    Avoid = 0,
    StronglyAvoid = 1,
    Never = 2
}

public sealed record PreferredAlternativeForList(
    int Id,
    string Preference,
    string Category);

public sealed record AvoidanceRuleForDetail(
    int Id,
    string Name,
    string Description,
    string Category,
    AvoidanceSeverity Severity,
    int? PreferredAlternativeId,
    PreferredAlternativeForList? PreferredAlternative,
    DateTime CreatedAt);

public sealed record AvoidanceRuleForList(
    int Id,
    string Name,
    string Description,
    string Category,
    AvoidanceSeverity Severity,
    int? PreferredAlternativeId,
    PreferredAlternativeForList? PreferredAlternative);

public sealed record AvoidanceRuleForCreate(
    string Name,
    string Description,
    string Category,
    AvoidanceSeverity Severity,
    int? PreferredAlternativeId = null);

public sealed record AvoidanceRuleForUpdate(
    string Name,
    string Description,
    string Category,
    AvoidanceSeverity Severity,
    int? PreferredAlternativeId = null);
