# Grigori Dashboard & Tray Application: Implementation Plan

## Vision

Provide a user-friendly local application that runs Grigori's consciousness daemon in the background and offers a visual dashboard for managing memories, viewing insights, and monitoring AI session outcomes - all accessible from a system tray icon.

**Core Metaphor**: A quiet assistant that's always watching, with a dashboard you can peek at anytime.

---

## Goals

1. **Zero-friction startup** - Installs once, runs automatically, never think about it
2. **Always-on observation** - Daemon watches codebase 24/7, even when Claude isn't connected
3. **Visual insights** - See patterns, memories, and session outcomes at a glance
4. **Easy management** - Add/edit memories, configure watched directories, manage triggers
5. **Real-time updates** - Live event stream, instant pattern detection notifications

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              USER'S PC                                   │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │                      Grigori.Tray (WPF)                            │ │
│  │                                                                     │ │
│  │  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────────┐│ │
│  │  │ TrayIcon    │    │DaemonHost   │    │  DashboardHost          ││ │
│  │  │ & Menu      │    │(Background) │    │  (Kestrel + Blazor)     ││ │
│  │  └─────────────┘    └──────┬──────┘    └───────────┬─────────────┘│ │
│  │                            │                       │               │ │
│  └────────────────────────────┼───────────────────────┼───────────────┘ │
│                               │                       │                  │
│                               │ writes                │ reads            │
│                               ▼                       ▼                  │
│                        ┌─────────────────────────────────┐              │
│                        │         SQLite Database          │              │
│                        │  ┌───────────┐ ┌─────────────┐  │              │
│                        │  │ events    │ │ memories    │  │              │
│                        │  │ sessions  │ │ patterns    │  │              │
│                        │  │ outcomes  │ │ triggers    │  │              │
│                        │  └───────────┘ └─────────────┘  │              │
│                        └─────────────────────────────────┘              │
│                                         ▲                                │
│                                         │ reads/writes                   │
│                                         │                                │
│  ┌──────────────────┐           ┌───────┴────────┐                      │
│  │   Claude Code    │──spawns──▶│  Grigori.Mcp   │                      │
│  │   (Terminal)     │   stdio   │  (MCP Server)  │                      │
│  └──────────────────┘           └────────────────┘                      │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Component Breakdown

### 1. Grigori.Tray (Main Entry Point)

The tray application is the user-facing "shell" that hosts everything.

**Responsibilities:**
- System tray icon and context menu
- Auto-start on Windows login
- Host the daemon (in-process or subprocess)
- Host the Blazor dashboard (Kestrel server)
- Settings management
- Update notifications

**Technology:** WPF (for modern Windows tray support) or WinForms (simpler)

### 2. Grigori.Daemon (Background Service)

The always-running observer that watches the codebase.

**Responsibilities:**
- File system watching
- Git repository monitoring
- Build output parsing
- Test result parsing
- Log file monitoring
- Pattern analysis (periodic)
- Event storage

**Technology:** .NET BackgroundService / IHostedService

### 3. Grigori.Dashboard (Blazor Server)

The web-based UI for visualization and management.

**Responsibilities:**
- Real-time event stream display
- Memory CRUD interface
- Session history and outcomes
- Pattern visualization
- Trigger management
- Settings configuration
- Health metrics display

**Technology:** Blazor Server (for SignalR real-time updates)

---

## Detailed Component Specifications

### Grigori.Tray

#### Project Structure

```
src/Grigori.Tray/
├── App.xaml
├── App.xaml.cs
├── Program.cs
├── TrayIcon/
│   ├── TrayIconManager.cs
│   ├── TrayContextMenu.cs
│   └── Resources/
│       ├── icon-normal.ico
│       ├── icon-working.ico
│       └── icon-error.ico
├── Hosting/
│   ├── DaemonHostManager.cs
│   ├── DashboardHostManager.cs
│   └── ProcessWatchdog.cs
├── AutoStart/
│   ├── AutoStartManager.cs
│   └── ShortcutHelper.cs
├── Settings/
│   ├── TraySettings.cs
│   ├── SettingsManager.cs
│   └── SettingsWindow.xaml(.cs)
├── Notifications/
│   ├── ToastNotificationManager.cs
│   └── NotificationTemplates.cs
└── appsettings.json
```

#### Core Classes

```csharp
// Program.cs - Single instance application entry
public class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // Ensure single instance
        const string mutexName = "Grigori.Tray.SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance running - signal it to show UI
            SignalExistingInstance();
            return;
        }

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        finally
        {
            _mutex?.ReleaseMutex();
        }
    }
}
```

```csharp
// TrayIconManager.cs
public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DaemonHostManager _daemonHost;
    private readonly DashboardHostManager _dashboardHost;
    private readonly SettingsManager _settings;

    public TrayIconManager(
        DaemonHostManager daemonHost,
        DashboardHostManager dashboardHost,
        SettingsManager settings)
    {
        _daemonHost = daemonHost;
        _dashboardHost = dashboardHost;
        _settings = settings;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon("icon-normal"),
            Text = "Grigori - Codebase Consciousness",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (s, e) => OpenDashboard();

        // Subscribe to daemon status changes
        _daemonHost.StatusChanged += OnDaemonStatusChanged;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Status header (non-clickable)
        _statusMenuItem = new ToolStripMenuItem("● Daemon Running")
        {
            Enabled = false,
            ForeColor = Color.Green
        };
        menu.Items.Add(_statusMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        // Actions
        menu.Items.Add("Open Dashboard", null, (s, e) => OpenDashboard());
        menu.Items.Add("Quick Stats", null, (s, e) => ShowQuickStats());

        menu.Items.Add(new ToolStripSeparator());

        // Daemon control
        _daemonControlItem = new ToolStripMenuItem("Pause Daemon", null, (s, e) => ToggleDaemon());
        menu.Items.Add(_daemonControlItem);

        menu.Items.Add(new ToolStripSeparator());

        // Settings & Help
        menu.Items.Add("Watched Directories...", null, (s, e) => OpenWatchedDirs());
        menu.Items.Add("Settings...", null, (s, e) => OpenSettings());
        menu.Items.Add("View Logs", null, (s, e) => OpenLogs());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, (s, e) => ExitApplication());

        return menu;
    }

    private void OpenDashboard()
    {
        // Ensure dashboard is running
        if (!_dashboardHost.IsRunning)
        {
            _dashboardHost.Start();
        }

        // Open in default browser
        var url = $"http://localhost:{_settings.DashboardPort}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnDaemonStatusChanged(object? sender, DaemonStatus status)
    {
        _notifyIcon.Icon = status switch
        {
            DaemonStatus.Running => LoadIcon("icon-normal"),
            DaemonStatus.Working => LoadIcon("icon-working"),
            DaemonStatus.Error => LoadIcon("icon-error"),
            DaemonStatus.Paused => LoadIcon("icon-paused"),
            _ => LoadIcon("icon-normal")
        };

        _statusMenuItem.Text = status switch
        {
            DaemonStatus.Running => "● Daemon Running",
            DaemonStatus.Working => "◐ Daemon Working...",
            DaemonStatus.Error => "● Daemon Error",
            DaemonStatus.Paused => "○ Daemon Paused",
            _ => "? Unknown"
        };

        _statusMenuItem.ForeColor = status switch
        {
            DaemonStatus.Running => Color.Green,
            DaemonStatus.Working => Color.Orange,
            DaemonStatus.Error => Color.Red,
            DaemonStatus.Paused => Color.Gray,
            _ => Color.Black
        };
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }
}
```

```csharp
// DaemonHostManager.cs
public class DaemonHostManager : IDisposable
{
    private IHost? _host;
    private CancellationTokenSource? _cts;
    private readonly ILogger<DaemonHostManager> _logger;
    private readonly TraySettings _settings;

    public event EventHandler<DaemonStatus>? StatusChanged;
    public DaemonStatus Status { get; private set; } = DaemonStatus.Stopped;

    public async Task StartAsync()
    {
        if (_host != null) return;

        _cts = new CancellationTokenSource();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register daemon services
                services.AddSingleton<EventStore>();
                services.AddSingleton<ConsciousnessEngine>();

                // Register observers as hosted services
                services.AddHostedService<FileSystemObserver>();
                services.AddHostedService<GitObserver>();
                services.AddHostedService<BuildObserver>();
                services.AddHostedService<TestObserver>();
                services.AddHostedService<PatternAnalyzer>();
            })
            .Build();

        await _host.StartAsync(_cts.Token);
        Status = DaemonStatus.Running;
        StatusChanged?.Invoke(this, Status);
    }

    public async Task StopAsync()
    {
        if (_host == null) return;

        _cts?.Cancel();
        await _host.StopAsync();
        _host.Dispose();
        _host = null;

        Status = DaemonStatus.Stopped;
        StatusChanged?.Invoke(this, Status);
    }

    public void Pause()
    {
        // Signal observers to pause
        Status = DaemonStatus.Paused;
        StatusChanged?.Invoke(this, Status);
    }

    public void Resume()
    {
        Status = DaemonStatus.Running;
        StatusChanged?.Invoke(this, Status);
    }
}
```

```csharp
// DashboardHostManager.cs
public class DashboardHostManager : IDisposable
{
    private WebApplication? _app;
    private readonly TraySettings _settings;

    public bool IsRunning => _app != null;

    public async Task StartAsync()
    {
        if (_app != null) return;

        var builder = WebApplication.CreateBuilder();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Register shared services
        builder.Services.AddSingleton<EventStore>();
        builder.Services.AddSingleton<MemoryStore>();
        builder.Services.AddSingleton<ConsciousnessEngine>();

        // SignalR hub for real-time updates
        builder.Services.AddSignalR();

        _app = builder.Build();

        _app.UseStaticFiles();
        _app.UseAntiforgery();

        _app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        _app.MapHub<ConsciousnessHub>("/consciousness-hub");

        await _app.StartAsync($"http://localhost:{_settings.DashboardPort}");
    }

    public async Task StopAsync()
    {
        if (_app == null) return;

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }
}
```

```csharp
// AutoStartManager.cs
public static class AutoStartManager
{
    private const string AppName = "Grigori";

    public static bool IsEnabled
    {
        get
        {
            var startupPath = GetStartupShortcutPath();
            return File.Exists(startupPath);
        }
    }

    public static void Enable()
    {
        var shortcutPath = GetStartupShortcutPath();
        var targetPath = Environment.ProcessPath!;

        CreateShortcut(shortcutPath, targetPath, "--minimized");
    }

    public static void Disable()
    {
        var shortcutPath = GetStartupShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string GetStartupShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, $"{AppName}.lnk");
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments)
    {
        // Use Windows Script Host to create shortcut
        var shell = new IWshRuntimeLibrary.WshShell();
        var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.Description = "Grigori Codebase Consciousness";
        shortcut.Save();
    }
}
```

#### Settings Model

```csharp
// TraySettings.cs
public class TraySettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool StartDashboardOnLaunch { get; set; } = false;
    public int DashboardPort { get; set; } = 5151;

    public List<WatchedDirectory> WatchedDirectories { get; set; } = new();

    public bool ShowNotifications { get; set; } = true;
    public bool NotifyOnPatternDetected { get; set; } = true;
    public bool NotifyOnBuildFailure { get; set; } = true;
    public bool NotifyOnTestFailure { get; set; } = true;

    public string DatabasePath { get; set; } = "./grigori-consciousness.db";
    public int EventRetentionDays { get; set; } = 90;
}

public class WatchedDirectory
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool WatchGit { get; set; } = true;
    public bool WatchBuilds { get; set; } = true;
    public bool WatchTests { get; set; } = true;
    public bool WatchLogs { get; set; } = false;
    public string? LogPattern { get; set; }
    public List<string> ExcludePatterns { get; set; } = new()
    {
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/.git/**"
    };
}
```

---

### Grigori.Dashboard (Blazor Server)

#### Project Structure

```
src/Grigori.Dashboard/
├── Components/
│   ├── App.razor
│   ├── Routes.razor
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   ├── NavMenu.razor
│   │   └── TopBar.razor
│   ├── Pages/
│   │   ├── Dashboard.razor
│   │   ├── Memories.razor
│   │   ├── Sessions.razor
│   │   ├── Patterns.razor
│   │   ├── Triggers.razor
│   │   ├── FileExplorer.razor
│   │   └── Settings.razor
│   └── Shared/
│       ├── EventStream.razor
│       ├── HealthCard.razor
│       ├── PatternCard.razor
│       ├── MemoryCard.razor
│       ├── MemoryEditor.razor
│       ├── SessionCard.razor
│       ├── TriggerCard.razor
│       ├── TriggerEditor.razor
│       ├── CorrelationGraph.razor
│       ├── ChurnHeatmap.razor
│       ├── TimelineView.razor
│       └── ConfirmDialog.razor
├── Hubs/
│   └── ConsciousnessHub.cs
├── Services/
│   ├── DashboardState.cs
│   ├── EventStreamService.cs
│   └── ChartDataService.cs
├── wwwroot/
│   ├── css/
│   │   └── app.css
│   ├── js/
│   │   └── charts.js
│   └── favicon.ico
└── Program.cs
```

#### Page Specifications

##### 1. Dashboard Page (Home)

```razor
@page "/"
@inject ConsciousnessEngine Engine
@inject EventStreamService EventStream

<PageTitle>Grigori Dashboard</PageTitle>

<div class="dashboard-grid">
    <!-- Top row: Health metrics -->
    <div class="health-section">
        <HealthCard Title="Daemon" Status="@_daemonStatus" />
        <HealthCard Title="Build" Status="@_buildStatus" />
        <HealthCard Title="Tests" Status="@_testStatus" Value="@_testSummary" />
        <HealthCard Title="Events (24h)" Value="@_eventCount" />
    </div>

    <!-- Middle row: Event stream and quick stats -->
    <div class="main-section">
        <div class="event-stream-panel">
            <h3>Live Events</h3>
            <EventStream Events="@_recentEvents" />
        </div>

        <div class="stats-panel">
            <h3>Quick Stats</h3>
            <div class="stat-item">
                <span class="stat-label">Active Patterns</span>
                <span class="stat-value">@_patternCount</span>
            </div>
            <div class="stat-item">
                <span class="stat-label">Memories</span>
                <span class="stat-value">@_memoryCount</span>
            </div>
            <div class="stat-item">
                <span class="stat-label">AI Sessions (7d)</span>
                <span class="stat-value">@_sessionCount</span>
            </div>
            <div class="stat-item">
                <span class="stat-label">Pending Triggers</span>
                <span class="stat-value">@_triggerCount</span>
            </div>
        </div>
    </div>

    <!-- Bottom row: Recent patterns and active triggers -->
    <div class="bottom-section">
        <div class="patterns-panel">
            <h3>Recent Patterns <a href="/patterns">View All</a></h3>
            @foreach (var pattern in _recentPatterns)
            {
                <PatternCard Pattern="@pattern" Compact="true" />
            }
        </div>

        <div class="triggers-panel">
            <h3>Active Triggers <a href="/triggers">View All</a></h3>
            @foreach (var trigger in _activeTriggers)
            {
                <TriggerCard Trigger="@trigger" Compact="true" />
            }
        </div>
    </div>
</div>

@code {
    private DaemonStatus _daemonStatus;
    private BuildStatus _buildStatus;
    private TestStatus _testStatus;
    private string _testSummary = "";
    private int _eventCount;
    private int _patternCount;
    private int _memoryCount;
    private int _sessionCount;
    private int _triggerCount;
    private List<CodebaseEvent> _recentEvents = new();
    private List<DetectedPattern> _recentPatterns = new();
    private List<Trigger> _activeTriggers = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadDashboardData();

        // Subscribe to real-time updates
        EventStream.OnEvent += HandleNewEvent;
    }

    private async Task LoadDashboardData()
    {
        var stats = await Engine.GetDashboardStatsAsync();
        _daemonStatus = stats.DaemonStatus;
        _buildStatus = stats.BuildStatus;
        _testStatus = stats.TestStatus;
        _testSummary = $"{stats.TestsPassing}/{stats.TestsTotal}";
        _eventCount = stats.EventCount24h;
        _patternCount = stats.ActivePatterns;
        _memoryCount = stats.MemoryCount;
        _sessionCount = stats.SessionCount7d;
        _triggerCount = stats.ActiveTriggers;

        _recentEvents = await Engine.GetRecentEventsAsync(20);
        _recentPatterns = await Engine.GetRecentPatternsAsync(5);
        _activeTriggers = await Engine.GetActiveTriggersAsync(5);
    }

    private async void HandleNewEvent(CodebaseEvent evt)
    {
        _recentEvents.Insert(0, evt);
        if (_recentEvents.Count > 20)
            _recentEvents.RemoveAt(_recentEvents.Count - 1);

        _eventCount++;

        await InvokeAsync(StateHasChanged);
    }
}
```

##### 2. Memories Page

```razor
@page "/memories"
@inject MemoryStore MemoryStore

<PageTitle>Memories - Grigori</PageTitle>

<div class="page-header">
    <h1>Memories</h1>
    <button class="btn btn-primary" @onclick="ShowAddMemory">
        + Add Memory
    </button>
</div>

<!-- Filters -->
<div class="filters">
    <select @bind="_typeFilter">
        <option value="">All Types</option>
        <option value="decision">Decisions</option>
        <option value="convention">Conventions</option>
        <option value="warning">Warnings</option>
        <option value="todo">TODOs</option>
        <option value="context">Context</option>
    </select>

    <input type="text"
           placeholder="Search memories..."
           @bind="_searchQuery"
           @bind:event="oninput"
           @onkeyup="HandleSearchKeyUp" />
</div>

<!-- Memory list -->
<div class="memory-list">
    @if (_loading)
    {
        <div class="loading">Loading...</div>
    }
    else if (_memories.Count == 0)
    {
        <div class="empty-state">
            <p>No memories yet.</p>
            <p>Memories help Claude remember important decisions, conventions, and context across sessions.</p>
        </div>
    }
    else
    {
        @foreach (var memory in _memories)
        {
            <MemoryCard Memory="@memory"
                        OnEdit="() => EditMemory(memory)"
                        OnDelete="() => DeleteMemory(memory)" />
        }
    }
</div>

<!-- Add/Edit Modal -->
@if (_showEditor)
{
    <MemoryEditor Memory="@_editingMemory"
                  OnSave="SaveMemory"
                  OnCancel="() => _showEditor = false" />
}

@code {
    private List<Memory> _memories = new();
    private string _typeFilter = "";
    private string _searchQuery = "";
    private bool _loading = true;
    private bool _showEditor = false;
    private Memory? _editingMemory;

    protected override async Task OnInitializedAsync()
    {
        await LoadMemories();
    }

    private async Task LoadMemories()
    {
        _loading = true;
        StateHasChanged();

        if (!string.IsNullOrEmpty(_searchQuery))
        {
            _memories = await MemoryStore.SearchMemoriesAsync(_searchQuery, limit: 50);
        }
        else if (!string.IsNullOrEmpty(_typeFilter))
        {
            var type = Enum.Parse<MemoryType>(_typeFilter, ignoreCase: true);
            _memories = await MemoryStore.GetMemoriesByTypeAsync(type);
        }
        else
        {
            _memories = await MemoryStore.GetAllMemoriesAsync(limit: 50);
        }

        _loading = false;
    }

    private void ShowAddMemory()
    {
        _editingMemory = new Memory
        {
            Type = MemoryType.Context,
            CreatedAt = DateTime.UtcNow
        };
        _showEditor = true;
    }

    private void EditMemory(Memory memory)
    {
        _editingMemory = memory;
        _showEditor = true;
    }

    private async Task SaveMemory(Memory memory)
    {
        if (memory.Id == 0)
        {
            await MemoryStore.RememberAsync(memory.Type, memory.Key, memory.Content);
        }
        else
        {
            await MemoryStore.UpdateAsync(memory);
        }

        _showEditor = false;
        await LoadMemories();
    }

    private async Task DeleteMemory(Memory memory)
    {
        await MemoryStore.ForgetAsync(memory.Key);
        await LoadMemories();
    }
}
```

##### 3. Sessions Page

```razor
@page "/sessions"
@inject ConsciousnessEngine Engine

<PageTitle>AI Sessions - Grigori</PageTitle>

<div class="page-header">
    <h1>AI Sessions</h1>
</div>

<!-- Timeline view -->
<div class="sessions-timeline">
    @foreach (var group in _sessionsByDate)
    {
        <div class="date-group">
            <h3 class="date-header">@group.Key.ToString("MMMM d, yyyy")</h3>

            @foreach (var session in group.Value)
            {
                <SessionCard Session="@session"
                             Outcomes="@GetOutcomes(session.SessionId)"
                             OnViewDetails="() => ViewSessionDetails(session)" />
            }
        </div>
    }
</div>

<!-- Session detail modal -->
@if (_selectedSession != null)
{
    <div class="modal">
        <div class="modal-content session-detail">
            <h2>Session @_selectedSession.SessionId</h2>

            <div class="session-meta">
                <span>Started: @_selectedSession.StartedAt.ToString("g")</span>
                <span>Duration: @GetDuration(_selectedSession)</span>
            </div>

            <h3>Summary</h3>
            <p>@_selectedSession.Summary</p>

            <h3>Files Touched</h3>
            <ul class="file-list">
                @foreach (var file in _selectedSession.FilesTouched)
                {
                    <li>@file</li>
                }
            </ul>

            <h3>Outcomes</h3>
            @foreach (var outcome in GetOutcomes(_selectedSession.SessionId))
            {
                <div class="outcome-detail">
                    <strong>@outcome.ChangeDescription</strong>
                    <div class="outcome-metrics">
                        <span class="@(outcome.BuildSucceeded == true ? "success" : "failure")">
                            Build: @(outcome.BuildSucceeded == true ? "✓" : "✗")
                        </span>
                        <span class="@(outcome.TestsFailed == 0 ? "success" : "warning")">
                            Tests: @outcome.TestsPassed/@(outcome.TestsPassed + outcome.TestsFailed)
                        </span>
                        <span class="@(outcome.WasReverted == true ? "failure" : "success")">
                            @(outcome.WasReverted == true ? "Reverted" : "Kept")
                        </span>
                        <span>Errors in logs: @outcome.ErrorsInLogs</span>
                    </div>
                </div>
            }

            <button @onclick="() => _selectedSession = null">Close</button>
        </div>
    </div>
}

@code {
    private Dictionary<DateTime, List<AISession>> _sessionsByDate = new();
    private Dictionary<string, List<ChangeOutcome>> _outcomesBySession = new();
    private AISession? _selectedSession;

    protected override async Task OnInitializedAsync()
    {
        var sessions = await Engine.GetSessionsAsync(limit: 50);
        _sessionsByDate = sessions
            .GroupBy(s => s.StartedAt.Date)
            .OrderByDescending(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sessionIds = sessions.Select(s => s.SessionId).ToList();
        var outcomes = await Engine.GetOutcomesForSessionsAsync(sessionIds);
        _outcomesBySession = outcomes
            .GroupBy(o => o.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private List<ChangeOutcome> GetOutcomes(string sessionId)
    {
        return _outcomesBySession.GetValueOrDefault(sessionId, new List<ChangeOutcome>());
    }
}
```

##### 4. Patterns Page

```razor
@page "/patterns"
@inject ConsciousnessEngine Engine

<PageTitle>Patterns - Grigori</PageTitle>

<div class="page-header">
    <h1>Detected Patterns</h1>
    <button class="btn btn-secondary" @onclick="RefreshPatterns">
        ↻ Refresh Analysis
    </button>
</div>

<!-- Visualization tabs -->
<div class="viz-tabs">
    <button class="@(_activeTab == "list" ? "active" : "")" @onclick='() => _activeTab = "list"'>
        List View
    </button>
    <button class="@(_activeTab == "graph" ? "active" : "")" @onclick='() => _activeTab = "graph"'>
        Correlation Graph
    </button>
    <button class="@(_activeTab == "heatmap" ? "active" : "")" @onclick='() => _activeTab = "heatmap"'>
        Churn Heatmap
    </button>
</div>

<div class="viz-content">
    @if (_activeTab == "list")
    {
        <div class="patterns-list">
            @foreach (var pattern in _patterns)
            {
                <PatternCard Pattern="@pattern"
                             OnDismiss="() => DismissPattern(pattern)" />
            }
        </div>
    }
    else if (_activeTab == "graph")
    {
        <CorrelationGraph Correlations="@_correlations" />
    }
    else if (_activeTab == "heatmap")
    {
        <ChurnHeatmap Data="@_churnData" />
    }
</div>

@code {
    private string _activeTab = "list";
    private List<DetectedPattern> _patterns = new();
    private List<FileCorrelation> _correlations = new();
    private List<ChurnDataPoint> _churnData = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadData();
    }

    private async Task LoadData()
    {
        _patterns = await Engine.GetActivePatternsAsync();
        _correlations = await Engine.GetFileCorrelationsAsync(minCorrelation: 0.7f);
        _churnData = await Engine.GetChurnDataAsync(days: 30);
    }

    private async Task RefreshPatterns()
    {
        await Engine.RunPatternAnalysisAsync();
        await LoadData();
    }

    private async Task DismissPattern(DetectedPattern pattern)
    {
        await Engine.DismissPatternAsync(pattern.Id);
        _patterns.Remove(pattern);
    }
}
```

##### 5. Triggers Page

```razor
@page "/triggers"
@inject ConsciousnessEngine Engine

<PageTitle>Triggers - Grigori</PageTitle>

<div class="page-header">
    <h1>Triggers</h1>
    <button class="btn btn-primary" @onclick="ShowAddTrigger">
        + New Trigger
    </button>
</div>

<!-- Active triggers -->
<section>
    <h2>Active Triggers</h2>
    <div class="trigger-list">
        @foreach (var trigger in _activeTriggers)
        {
            <TriggerCard Trigger="@trigger"
                         OnDisable="() => DisableTrigger(trigger)"
                         OnDelete="() => DeleteTrigger(trigger)" />
        }
        @if (_activeTriggers.Count == 0)
        {
            <p class="empty-state">No active triggers</p>
        }
    </div>
</section>

<!-- Recently fired triggers -->
<section>
    <h2>Recently Fired</h2>
    <div class="trigger-list fired">
        @foreach (var trigger in _firedTriggers)
        {
            <TriggerCard Trigger="@trigger" ShowFiredInfo="true" />
        }
        @if (_firedTriggers.Count == 0)
        {
            <p class="empty-state">No triggers fired recently</p>
        }
    </div>
</section>

<!-- Add/Edit Modal -->
@if (_showEditor)
{
    <TriggerEditor Trigger="@_editingTrigger"
                   OnSave="SaveTrigger"
                   OnCancel="() => _showEditor = false" />
}

@code {
    private List<Trigger> _activeTriggers = new();
    private List<Trigger> _firedTriggers = new();
    private bool _showEditor = false;
    private Trigger? _editingTrigger;

    protected override async Task OnInitializedAsync()
    {
        await LoadTriggers();
    }

    private async Task LoadTriggers()
    {
        _activeTriggers = await Engine.GetActiveTriggersAsync();
        _firedTriggers = await Engine.GetFiredTriggersAsync(days: 7);
    }

    private void ShowAddTrigger()
    {
        _editingTrigger = new Trigger
        {
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        _showEditor = true;
    }

    private async Task SaveTrigger(Trigger trigger)
    {
        await Engine.CreateTriggerAsync(trigger);
        _showEditor = false;
        await LoadTriggers();
    }

    private async Task DisableTrigger(Trigger trigger)
    {
        await Engine.DisableTriggerAsync(trigger.Id);
        await LoadTriggers();
    }

    private async Task DeleteTrigger(Trigger trigger)
    {
        await Engine.DeleteTriggerAsync(trigger.Id);
        await LoadTriggers();
    }
}
```

#### SignalR Hub for Real-Time Updates

```csharp
// ConsciousnessHub.cs
public class ConsciousnessHub : Hub
{
    private readonly EventStore _eventStore;
    private readonly ILogger<ConsciousnessHub> _logger;

    public ConsciousnessHub(EventStore eventStore, ILogger<ConsciousnessHub> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Dashboard client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public async Task SubscribeToEvents()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "EventStream");
    }

    public async Task UnsubscribeFromEvents()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "EventStream");
    }

    // Called by daemon when new event occurs
    public static async Task BroadcastEvent(IHubContext<ConsciousnessHub> hubContext, CodebaseEvent evt)
    {
        await hubContext.Clients.Group("EventStream").SendAsync("NewEvent", evt);
    }

    public static async Task BroadcastPatternDetected(IHubContext<ConsciousnessHub> hubContext, DetectedPattern pattern)
    {
        await hubContext.Clients.All.SendAsync("PatternDetected", pattern);
    }

    public static async Task BroadcastTriggerFired(IHubContext<ConsciousnessHub> hubContext, Trigger trigger)
    {
        await hubContext.Clients.All.SendAsync("TriggerFired", trigger);
    }
}
```

---

## Installation & First Run Experience

### Installer Tasks

1. Copy files to `%ProgramFiles%\Grigori\`
2. Create Start Menu shortcut
3. Ask: "Start Grigori when Windows starts?" → Create Startup shortcut
4. Ask: "Add a directory to watch?" → Open directory picker
5. Launch tray app

### First Run Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                  │
│     Welcome to Grigori                                          │
│     ─────────────────                                           │
│                                                                  │
│     Grigori watches your codebase and provides AI assistants    │
│     with persistent memory and contextual awareness.            │
│                                                                  │
│                                                                  │
│     Let's get started:                                          │
│                                                                  │
│     1. Add a directory to watch                                 │
│        ┌────────────────────────────────────────────┐           │
│        │ C:\Projects\MyApp                     [📁] │           │
│        └────────────────────────────────────────────┘           │
│        [+ Add another]                                          │
│                                                                  │
│     2. Startup options                                          │
│        ☑ Start Grigori when Windows starts                     │
│        ☑ Start minimized to system tray                        │
│                                                                  │
│                                                                  │
│                              [Get Started]                       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Project Structure (Complete)

```
src/
├── Grigori.Core/                    # Shared core logic
│   ├── Embeddings/                  # (existing)
│   ├── Indexing/                    # (existing)
│   ├── Search/                      # (existing)
│   ├── Storage/                     # (existing)
│   └── Consciousness/               # (from consciousness plan)
│       ├── Models/
│       ├── Observers/
│       ├── Analysis/
│       └── Storage/
│
├── Grigori.Mcp/                     # MCP server (spawned by Claude)
│   ├── Tools/
│   └── Program.cs
│
├── Grigori.Api/                     # REST API (optional)
│   └── ...
│
├── Grigori.Daemon/                  # Background observation service
│   ├── Observers/
│   │   ├── FileSystemObserver.cs
│   │   ├── GitObserver.cs
│   │   ├── BuildObserver.cs
│   │   ├── TestObserver.cs
│   │   └── LogObserver.cs
│   ├── Analysis/
│   │   └── PeriodicPatternAnalyzer.cs
│   └── Program.cs
│
├── Grigori.Dashboard/               # Blazor Server UI
│   ├── Components/
│   │   ├── Layout/
│   │   ├── Pages/
│   │   └── Shared/
│   ├── Hubs/
│   ├── Services/
│   └── Program.cs
│
└── Grigori.Tray/                    # Windows tray application
    ├── TrayIcon/
    ├── Hosting/
    ├── AutoStart/
    ├── Settings/
    ├── Notifications/
    └── Program.cs
```

---

## Implementation Phases

### Phase 1: Tray Application Shell

**Goal:** Basic tray app that can host services

1. Create Grigori.Tray WPF project
2. Implement TrayIconManager with basic menu
3. Implement single-instance detection
4. Implement AutoStartManager
5. Create settings storage and SettingsWindow
6. Implement DaemonHostManager (placeholder)
7. Implement DashboardHostManager (placeholder)

**Deliverables:**
- Tray app that starts, shows icon, has working menu
- Can enable/disable auto-start
- Settings window with basic options

### Phase 2: Daemon Integration

**Goal:** Run daemon from tray app

1. Create Grigori.Daemon project with IHostedService structure
2. Move/refactor observers from consciousness plan
3. Integrate daemon hosting into tray app
4. Add daemon status display in tray menu
5. Add pause/resume functionality
6. Add basic notifications for daemon events

**Deliverables:**
- Daemon runs as part of tray app
- File system and git observation working
- Events being written to database
- Status visible in tray icon/menu

### Phase 3: Dashboard Foundation

**Goal:** Basic Blazor dashboard

1. Create Grigori.Dashboard project
2. Implement MainLayout and navigation
3. Create Dashboard page with health metrics
4. Implement EventStream component with SignalR
5. Integrate dashboard hosting into tray app
6. "Open Dashboard" from tray menu works

**Deliverables:**
- Dashboard accessible at localhost:5151
- Live event stream working
- Basic health metrics displayed

### Phase 4: Memory Management UI

**Goal:** Full memory CRUD in dashboard

1. Create Memories page
2. Implement MemoryCard component
3. Implement MemoryEditor modal
4. Add search and filtering
5. Connect to MemoryStore backend

**Deliverables:**
- View all memories
- Add/edit/delete memories
- Search memories
- Filter by type

### Phase 5: Sessions & Outcomes UI

**Goal:** View AI session history and outcomes

1. Create Sessions page with timeline view
2. Implement SessionCard component
3. Implement session detail modal
4. Display outcomes with visual indicators
5. Add filtering by date range

**Deliverables:**
- View all past AI sessions
- See what each session did
- See outcomes (build/test/revert status)

### Phase 6: Patterns & Visualization

**Goal:** Pattern display and visualizations

1. Create Patterns page
2. Implement PatternCard component
3. Implement CorrelationGraph visualization
4. Implement ChurnHeatmap visualization
5. Add pattern dismiss functionality

**Deliverables:**
- View detected patterns
- Correlation graph visualization
- File churn heatmap
- Can dismiss false-positive patterns

### Phase 7: Triggers UI

**Goal:** Trigger management

1. Create Triggers page
2. Implement TriggerCard component
3. Implement TriggerEditor modal
4. Show fired trigger history
5. Connect to trigger backend

**Deliverables:**
- View active triggers
- Create new triggers
- View fired trigger history
- Disable/delete triggers

### Phase 8: Polish & Notifications

**Goal:** Production-ready experience

1. Add Windows toast notifications
2. Implement notification preferences
3. Add first-run setup wizard
4. Improve error handling throughout
5. Add logging and diagnostics view
6. Performance optimization
7. Create installer

**Deliverables:**
- Toast notifications for important events
- Smooth first-run experience
- Installer package

---

## Technical Considerations

### Tray App Technology Choice

**WPF (Recommended)**
- Modern Windows look and feel
- Good NotifyIcon support via Hardcodet.NotifyIcon.Wpf
- Can share styles with dashboard
- Better for settings windows

**WinForms (Alternative)**
- Simpler for basic tray functionality
- Native NotifyIcon support
- Less ceremony for small UI needs

### Dashboard Port Conflict Handling

```csharp
public int FindAvailablePort(int preferredPort)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    try
    {
        // Try preferred port first
        listener = new TcpListener(IPAddress.Loopback, preferredPort);
        listener.Start();
        listener.Stop();
        return preferredPort;
    }
    catch (SocketException)
    {
        // Port in use, find available one
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
```

### Database Location

Default: `%LOCALAPPDATA%\Grigori\grigori.db`

```csharp
public static string GetDefaultDatabasePath()
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var grigoriDir = Path.Combine(appData, "Grigori");
    Directory.CreateDirectory(grigoriDir);
    return Path.Combine(grigoriDir, "grigori.db");
}
```

### Resource Usage

- **Memory:** Target < 100MB idle, < 200MB during analysis
- **CPU:** Near-zero when idle, brief spikes during file events
- **Disk:** SQLite writes batched, not on every event

### Multi-Project Watching

Support watching multiple unrelated projects:

```csharp
public class MultiProjectDaemon
{
    private readonly Dictionary<string, ProjectWatcher> _watchers = new();

    public void AddProject(string path)
    {
        if (_watchers.ContainsKey(path)) return;

        var watcher = new ProjectWatcher(path);
        watcher.Start();
        _watchers[path] = watcher;
    }

    public void RemoveProject(string path)
    {
        if (_watchers.TryGetValue(path, out var watcher))
        {
            watcher.Stop();
            _watchers.Remove(path);
        }
    }
}
```

---

## Dependencies

### Grigori.Tray
```xml
<PackageReference Include="Hardcodet.NotifyIcon.Wpf" Version="1.1.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
```

### Grigori.Dashboard
```xml
<PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="9.0.0" />
```

### Grigori.Daemon
```xml
<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="9.0.0" />
```

---

## Success Metrics

1. **Startup time:** Tray app visible within 2 seconds of login
2. **Resource usage:** < 100MB RAM, < 1% CPU when idle
3. **Event latency:** File changes reflected in dashboard < 500ms
4. **Dashboard load:** Home page renders in < 1 second
5. **Reliability:** No crashes over 7-day period

---

## Future Enhancements (Out of Scope)

- macOS/Linux support (different tray implementations)
- Cloud sync for memories across machines
- Team sharing of patterns/memories
- Mobile companion app
- IDE extensions (VS Code, Rider)
- Electron-based cross-platform version
