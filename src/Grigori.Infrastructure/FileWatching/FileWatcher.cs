using System.Collections.Concurrent;
using Grigori.Contracts.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.FileWatching;

public class FileWatcher : IDisposable
{
    private readonly ILogger<FileWatcher> _logger;
    private readonly HashSet<string> _extensions;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, DateTime> _pendingChanges = new();
    private readonly Timer _debounceTimer;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);

    public event Func<string, FileChangeType, Task>? FileChanged;

    public FileWatcher(IOptions<GrigoriOptions> options, ILogger<FileWatcher> logger)
    {
        _logger = logger;
        _extensions = options.Value.FileExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _debounceTimer = new Timer(ProcessPendingChanges, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Watch(string directory)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Directory does not exist: {Directory}", directory);
            return;
        }

        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        watcher.Created += OnFileEvent;
        watcher.Changed += OnFileEvent;
        watcher.Deleted += OnFileEvent;
        watcher.Renamed += OnFileRenamed;

        _watchers.Add(watcher);
        _logger.LogInformation("Watching directory: {Directory}", directory);
    }

    public IEnumerable<string> GetCodeFiles(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (_extensions.Contains(extension))
            {
                yield return file;
            }
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsCodeFile(e.FullPath))
            return;

        var changeType = e.ChangeType switch
        {
            WatcherChangeTypes.Created => FileChangeType.Created,
            WatcherChangeTypes.Changed => FileChangeType.Modified,
            WatcherChangeTypes.Deleted => FileChangeType.Deleted,
            _ => FileChangeType.Modified
        };

        QueueChange(e.FullPath, changeType);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsCodeFile(e.OldFullPath))
            QueueChange(e.OldFullPath, FileChangeType.Deleted);

        if (IsCodeFile(e.FullPath))
            QueueChange(e.FullPath, FileChangeType.Created);
    }

    private bool IsCodeFile(string path)
    {
        var extension = Path.GetExtension(path);
        return _extensions.Contains(extension);
    }

    private void QueueChange(string path, FileChangeType changeType)
    {
        _pendingChanges[path] = DateTime.UtcNow;
        _debounceTimer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
        _logger.LogDebug("Queued {ChangeType} for {Path}", changeType, path);
    }

    private async void ProcessPendingChanges(object? state)
    {
        var now = DateTime.UtcNow;
        var toProcess = _pendingChanges
            .Where(kvp => now - kvp.Value >= _debounceDelay)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var path in toProcess)
        {
            _pendingChanges.TryRemove(path, out _);

            var changeType = File.Exists(path) ? FileChangeType.Modified : FileChangeType.Deleted;

            try
            {
                if (FileChanged is not null)
                    await FileChanged(path, changeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file change: {Path}", path);
            }
        }
    }

    public void Dispose()
    {
        _debounceTimer.Dispose();
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        GC.SuppressFinalize(this);
    }
}

public enum FileChangeType
{
    Created,
    Modified,
    Deleted
}
