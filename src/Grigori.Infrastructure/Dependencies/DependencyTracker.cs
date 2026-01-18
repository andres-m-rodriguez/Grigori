using System.Collections.Concurrent;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.Dependencies;

/// <summary>
/// Thread-safe implementation of dependency tracking.
/// </summary>
public class DependencyTracker : IDependencyTracker
{
    private readonly ConcurrentDictionary<string, DependencyStatus> _dependencies = new();
    private readonly ILogger<DependencyTracker> _logger;

    public event Action<DependencyStatus>? OnStatusChanged;

    public DependencyTracker(ILogger<DependencyTracker> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<DependencyStatus> Dependencies =>
        _dependencies.Values.ToList();

    public bool AllReady =>
        _dependencies.Count > 0 && _dependencies.Values.All(d => d.State == DependencyState.Ready);

    public void Register(string id, string name, string description)
    {
        var status = new DependencyStatus
        {
            Id = id,
            Name = name,
            Description = description,
            State = DependencyState.Pending
        };

        if (_dependencies.TryAdd(id, status))
        {
            _logger.LogDebug("Registered dependency: {Id} ({Name})", id, name);
            OnStatusChanged?.Invoke(status);
        }
    }

    public void UpdateStatus(string id, DependencyState state, double? progress = null, string? message = null)
    {
        if (!_dependencies.TryGetValue(id, out var status))
        {
            _logger.LogWarning("Attempted to update unregistered dependency: {Id}", id);
            return;
        }

        var previousState = status.State;
        status.State = state;
        status.Progress = progress;
        status.Message = message;

        // Track timing
        if (previousState == DependencyState.Pending && state != DependencyState.Pending)
        {
            status.StartedAt = DateTime.UtcNow;
        }

        if (state is DependencyState.Ready or DependencyState.Failed)
        {
            status.CompletedAt = DateTime.UtcNow;
        }

        // Log state changes
        if (previousState != state)
        {
            var logLevel = state switch
            {
                DependencyState.Failed => LogLevel.Error,
                DependencyState.Ready => LogLevel.Information,
                _ => LogLevel.Debug
            };

            _logger.Log(logLevel, "Dependency {Id} state changed: {PreviousState} -> {NewState} {Message}",
                id, previousState, state, message ?? "");
        }

        OnStatusChanged?.Invoke(status);
    }

    public DependencyStatus? GetStatus(string id) =>
        _dependencies.TryGetValue(id, out var status) ? status : null;
}
