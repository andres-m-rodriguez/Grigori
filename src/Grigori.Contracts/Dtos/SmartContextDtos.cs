namespace Grigori.Contracts.Dtos;

// ===== Task Analysis Response =====

public sealed record TaskAnalysisResponse(
    string Task,
    string Entity,
    List<TaskPatternMatch> Patterns,
    TaskNamingMatch? Naming,
    TaskLayerMatch? Layer,
    List<TaskAvoidanceMatch> Avoid,
    TaskTemplateMatch? Template,
    List<TaskPreferenceMatch> Preferences);

public sealed record TaskPatternMatch(
    string Name,
    string Description,
    string? Example);

public sealed record TaskNamingMatch(
    string Context,
    string Pattern,
    string Suggested,
    string Example);

public sealed record TaskLayerMatch(
    string Name,
    string Responsibility,
    string Description);

public sealed record TaskAvoidanceMatch(
    string Name,
    string Description,
    string Severity);

public sealed record TaskTemplateMatch(
    int Id,
    string Name,
    string Description,
    string Language,
    string Category);

public sealed record TaskPreferenceMatch(
    string Preference,
    string? Rationale,
    int Priority);

// ===== Naming Validation Response =====

public sealed record NamingValidationResponse(
    bool? Valid,
    string Name,
    string Context,
    string? Pattern,
    string? Expected,
    string? Example,
    string? Message,
    List<string> Issues);

// ===== File Path Suggestion Response =====

public sealed record FilePathSuggestionResponse(
    string Name,
    string ComponentType,
    string? SuggestedPath,
    FilePathLayerInfo? Layer,
    List<string> AlternativePaths,
    string? Message);

public sealed record FilePathLayerInfo(
    string Name,
    string Responsibility);

// ===== Dependency Check Response =====

public sealed record DependencyCheckResponse(
    string FromLayer,
    string ToLayer,
    bool? Allowed,
    string? Rationale,
    string Message);

// ===== Quick Context Response =====

public sealed record QuickContextResponse(
    string Architecture,
    List<QuickPatternInfo> TopPatterns,
    List<QuickAvoidanceInfo> CriticalAvoidances,
    List<QuickPreferenceInfo> TopPreferences,
    string Summary);

public sealed record QuickPatternInfo(
    string Name,
    string Description);

public sealed record QuickAvoidanceInfo(
    string Name,
    string Description);

public sealed record QuickPreferenceInfo(
    string Preference,
    string Category);
