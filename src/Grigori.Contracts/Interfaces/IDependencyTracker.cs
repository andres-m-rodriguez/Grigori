namespace Grigori.Contracts.Interfaces;

/// <summary>
/// Tracks the initialization status of dependencies like ONNX models, databases, etc.
/// </summary>
public interface IDependencyTracker
{
    /// <summary>
    /// All registered dependencies and their current status.
    /// </summary>
    IReadOnlyList<DependencyStatus> Dependencies { get; }

    /// <summary>
    /// Fired when any dependency status changes.
    /// </summary>
    event Action<DependencyStatus>? OnStatusChanged;

    /// <summary>
    /// Register a dependency to track.
    /// </summary>
    void Register(string id, string name, string description);

    /// <summary>
    /// Update the status of a dependency.
    /// </summary>
    void UpdateStatus(string id, DependencyState state, double? progress = null, string? message = null);

    /// <summary>
    /// Get the status of a specific dependency.
    /// </summary>
    DependencyStatus? GetStatus(string id);

    /// <summary>
    /// Check if all dependencies are ready.
    /// </summary>
    bool AllReady { get; }
}

/// <summary>
/// Current status of a tracked dependency.
/// </summary>
public record DependencyStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public DependencyState State { get; set; } = DependencyState.Pending;
    public double? Progress { get; set; }
    public string? Message { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public TimeSpan? Duration => State switch
    {
        DependencyState.Ready or DependencyState.Failed when StartedAt.HasValue && CompletedAt.HasValue
            => CompletedAt.Value - StartedAt.Value,
        _ when StartedAt.HasValue => DateTime.UtcNow - StartedAt.Value,
        _ => null
    };
}

/// <summary>
/// State of a dependency during initialization.
/// </summary>
public enum DependencyState
{
    Pending,
    Checking,
    Downloading,
    Loading,
    Ready,
    Failed
}
