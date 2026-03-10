using Grigori.Common.Pagination;
using Grigori.Contracts.Dtos;

namespace Grigori.Contracts.Parameters;

/// <summary>
/// Parameters for paginated coding pattern queries.
/// Uses AfterId for int-based cursor since entities have int IDs.
/// </summary>
public sealed record CodingPatternParameters(
    int PageSize = 20,
    int? AfterId = null,
    string? Category = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated design preference queries.
/// </summary>
public sealed record DesignPreferenceParameters(
    int PageSize = 20,
    int? AfterId = null,
    string? Category = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated avoidance rule queries.
/// </summary>
public sealed record AvoidanceRuleParameters(
    int PageSize = 20,
    int? AfterId = null,
    string? Category = null,
    AvoidanceSeverity? Severity = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated architecture pattern queries.
/// </summary>
public sealed record ArchitecturePatternParameters(
    int PageSize = 20,
    int? AfterId = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated architecture layer queries.
/// </summary>
public sealed record ArchitectureLayerParameters(
    int PageSize = 20,
    int? AfterId = null,
    int? PatternId = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated layer dependency queries.
/// </summary>
public sealed record LayerDependencyParameters(
    int PageSize = 20,
    int? AfterId = null,
    int? PatternId = null,
    int? LayerId = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated code template queries.
/// </summary>
public sealed record CodeTemplateParameters(
    int PageSize = 20,
    int? AfterId = null,
    string? Category = null,
    string? Language = null,
    int? LayerId = null) : CursorParameters(PageSize);

/// <summary>
/// Parameters for paginated naming convention queries.
/// </summary>
public sealed record NamingConventionParameters(
    int PageSize = 20,
    int? AfterId = null,
    string? Context = null,
    int? LayerId = null) : CursorParameters(PageSize);
