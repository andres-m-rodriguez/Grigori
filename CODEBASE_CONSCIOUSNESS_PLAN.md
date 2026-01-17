# Codebase Consciousness: Implementation Plan

## Vision

Transform Grigori from a semantic search tool into a **persistent codebase intelligence system** that observes, remembers, and learns - giving AI assistants instant contextual awareness of what's happening in a project over time.

**Core Metaphor**: A team member who's been paying attention 24/7, not a contractor who shows up fresh each day.

---

## Goals

1. **Temporal Awareness** - Know what changed since the AI's last session
2. **Pattern Recognition** - Detect recurring issues, correlations, anomalies
3. **Outcome Tracking** - Know if previous AI-suggested changes succeeded or failed
4. **Proactive Insights** - Surface important information without being asked
5. **Contextual Memory** - Remember decisions, conventions, and project-specific knowledge

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        MCP Server                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │ get_briefing│  │ get_insights│  │   remember  │   ...tools   │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘              │
│         │                │                │                      │
│  ┌──────┴────────────────┴────────────────┴──────┐              │
│  │              Consciousness Engine              │              │
│  │  ┌──────────┐ ┌──────────┐ ┌───────────────┐  │              │
│  │  │ Briefing │ │ Pattern  │ │    Memory     │  │              │
│  │  │Generator │ │ Analyzer │ │    Store      │  │              │
│  │  └──────────┘ └──────────┘ └───────────────┘  │              │
│  └───────────────────────┬───────────────────────┘              │
│                          │                                       │
│  ┌───────────────────────┴───────────────────────┐              │
│  │              Event Store (SQLite)              │              │
│  │  events │ sessions │ outcomes │ memories       │              │
│  └───────────────────────┬───────────────────────┘              │
└──────────────────────────┼───────────────────────────────────────┘
                           │
┌──────────────────────────┴───────────────────────────────────────┐
│                    Background Daemon                              │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐ │
│  │ FileWatcher│  │ GitWatcher │  │ BuildWatcher│ │ LogWatcher │ │
│  └────────────┘  └────────────┘  └────────────┘  └────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

---

## Data Models

### 1. Events (Raw Observations)

```csharp
public record CodebaseEvent
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public EventType Type { get; init; }
    public string Source { get; init; }           // "git", "filesystem", "build", "test", "logs"
    public string? FilePath { get; init; }
    public string Data { get; init; }             // JSON payload
    public string? SessionId { get; init; }       // Which AI session caused this, if any
}

public enum EventType
{
    // File events
    FileCreated,
    FileModified,
    FileDeleted,
    FileRenamed,

    // Git events
    CommitCreated,
    BranchCreated,
    BranchMerged,
    PullRequestOpened,
    PullRequestMerged,
    PullRequestClosed,

    // Build events
    BuildStarted,
    BuildSucceeded,
    BuildFailed,

    // Test events
    TestRunStarted,
    TestRunCompleted,
    TestFailed,
    TestFixed,

    // Log events
    ErrorLogged,
    WarningLogged,
    ExceptionThrown,

    // AI session events
    SessionStarted,
    SessionEnded,
    ChangeProposed,
    ChangeApplied
}
```

### 2. Sessions (AI Interaction Tracking)

```csharp
public record AISession
{
    public string SessionId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public List<string> FilesTouched { get; init; }
    public List<string> ChangesApplied { get; init; }
    public string? Summary { get; init; }          // AI-generated summary of what was done
}
```

### 3. Outcomes (Feedback Loop)

```csharp
public record ChangeOutcome
{
    public long Id { get; init; }
    public string SessionId { get; init; }
    public string FilePath { get; init; }
    public string ChangeDescription { get; init; }
    public DateTime ChangedAt { get; init; }

    // What happened after
    public bool? BuildSucceeded { get; init; }
    public int? TestsPassed { get; init; }
    public int? TestsFailed { get; init; }
    public bool? WasReverted { get; init; }
    public int? ErrorsInLogs { get; init; }
    public string? PRStatus { get; init; }         // "merged", "closed", "pending"
    public List<string>? ReviewComments { get; init; }
}
```

### 4. Memories (Persistent Knowledge)

```csharp
public record Memory
{
    public long Id { get; init; }
    public MemoryType Type { get; init; }
    public string Key { get; init; }
    public string Content { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? SessionId { get; init; }        // Which session created this
    public float[]? Embedding { get; init; }       // For semantic retrieval
}

public enum MemoryType
{
    Decision,           // "We chose X because Y"
    Convention,         // "Always use snake_case for..."
    Warning,            // "Don't touch X without checking Y"
    Todo,               // "Need to refactor X eventually"
    Context,            // General project knowledge
    PersonPreference    // "User prefers detailed explanations"
}
```

### 5. Patterns (Detected Insights)

```csharp
public record DetectedPattern
{
    public long Id { get; init; }
    public PatternType Type { get; init; }
    public string Description { get; init; }
    public float Confidence { get; init; }
    public string Evidence { get; init; }          // JSON - supporting data
    public DateTime DetectedAt { get; init; }
    public DateTime? LastOccurrence { get; init; }
    public int OccurrenceCount { get; init; }
}

public enum PatternType
{
    // Temporal patterns
    RecurringFailure,       // "Tests fail every Monday"
    FlakeyTest,             // "This test fails intermittently"
    SlowDegradation,        // "Build time increasing 5%/week"

    // Correlation patterns
    CoChangeCorrelation,    // "These files always change together"
    BreakageCorrelation,    // "Changes to X often break Y"

    // Anomalies
    UnusualActivity,        // "10x normal commits today"
    SuddenFailure,          // "This was stable, now failing"

    // Code health
    HighChurn,              // "This file changes constantly"
    FrequentReverts,        // "Changes here often get reverted"
    GrowingComplexity       // "This module is getting unwieldy"
}
```

---

## Background Daemon Components

### 1. File System Watcher

```csharp
public class FileSystemObserver : IHostedService
{
    // Watch for file changes in configured directories
    // Debounce rapid changes
    // Record FileCreated, FileModified, FileDeleted, FileRenamed events
    // Ignore patterns: .git, node_modules, bin, obj, etc.
}
```

### 2. Git Watcher

```csharp
public class GitObserver : IHostedService
{
    // Poll git status/log periodically (or use filesystem watcher on .git)
    // Detect new commits, branches, merges
    // Parse commit messages for context
    // Track which files changed in each commit
    // Link commits to AI sessions if possible (by time correlation or commit message)
}
```

### 3. Build Watcher

```csharp
public class BuildObserver : IHostedService
{
    // Watch for build output files/logs
    // Parse MSBuild/dotnet build output
    // Record build success/failure with error details
    // Track build duration trends
}
```

### 4. Test Watcher

```csharp
public class TestObserver : IHostedService
{
    // Watch for test result files (trx, junit xml, etc.)
    // Parse test results
    // Track which tests passed/failed
    // Detect flaky tests (pass/fail pattern)
    // Detect newly failing tests vs consistently failing
}
```

### 5. Log Watcher

```csharp
public class LogObserver : IHostedService
{
    // Watch application log files
    // Parse for errors, warnings, exceptions
    // Correlate errors with recent code changes
    // Track error frequency and patterns
}
```

---

## Consciousness Engine Components

### 1. Briefing Generator

Generates a summary of what happened since the AI's last session.

```csharp
public class BriefingGenerator
{
    public async Task<SessionBriefing> GenerateBriefingAsync(string? lastSessionId)
    {
        // 1. Find events since last session ended
        // 2. Summarize git activity (commits, PRs, merges)
        // 3. Report build/test status changes
        // 4. Highlight any errors or failures
        // 5. Report outcomes of previous session's changes
        // 6. Surface any detected patterns/anomalies
        // 7. Include relevant memories
    }
}

public record SessionBriefing
{
    public DateTime LastSessionEnded { get; init; }
    public TimeSpan TimeSinceLastSession { get; init; }

    public GitSummary GitActivity { get; init; }
    public BuildTestSummary BuildTestStatus { get; init; }
    public List<ChangeOutcome> PreviousChangeOutcomes { get; init; }
    public List<DetectedPattern> RelevantPatterns { get; init; }
    public List<ErrorSummary> RecentErrors { get; init; }
    public List<Memory> RelevantMemories { get; init; }

    public string NarrativeSummary { get; init; }  // Human-readable summary
}
```

### 2. Pattern Analyzer

Detects patterns from accumulated events.

```csharp
public class PatternAnalyzer
{
    // Run periodically or on-demand

    public async Task AnalyzePatternsAsync()
    {
        await DetectCoChangeCorrelations();    // Files that change together
        await DetectRecurringFailures();       // Time-based failure patterns
        await DetectFlakeyTests();             // Inconsistent test results
        await DetectHighChurnFiles();          // Frequently modified files
        await DetectBreakageCorrelations();    // Changes that cause failures
        await DetectAnomalies();               // Unusual activity
    }

    private async Task DetectCoChangeCorrelations()
    {
        // Analyze commit history
        // Find files with high co-change frequency
        // Calculate correlation coefficient
        // Store patterns above threshold
    }

    private async Task DetectRecurringFailures()
    {
        // Analyze failure events by time
        // Look for day-of-week patterns
        // Look for time-of-day patterns
        // Look for periodicity
    }
}
```

### 3. Memory Store

Semantic storage and retrieval of memories.

```csharp
public class MemoryStore
{
    public async Task RememberAsync(MemoryType type, string key, string content);
    public async Task<Memory?> RecallAsync(string key);
    public async Task<List<Memory>> SearchMemoriesAsync(string query, int limit = 5);
    public async Task<List<Memory>> GetMemoriesByTypeAsync(MemoryType type);
    public async Task ForgetAsync(string key);
    public async Task CleanExpiredAsync();
}
```

---

## MCP Tools

### 1. `get_briefing` - What happened since last time

```json
{
  "name": "get_briefing",
  "description": "Get a summary of what happened in the codebase since your last session. Call this at the start of every conversation.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "session_id": {
        "type": "string",
        "description": "Optional: your previous session ID for continuity"
      }
    }
  }
}
```

**Output Example:**
```json
{
  "time_since_last_session": "2 days, 4 hours",
  "summary": "3 commits merged, build is green, but 2 tests are now flaky. Your auth refactor from last session was merged successfully with no issues.",

  "git_activity": {
    "commits": 3,
    "files_changed": 12,
    "highlights": [
      "feat: Added user preferences API",
      "fix: Resolved memory leak in cache",
      "chore: Updated dependencies"
    ]
  },

  "build_status": {
    "current": "passing",
    "last_failure": "3 days ago"
  },

  "test_status": {
    "total": 247,
    "passing": 245,
    "failing": 0,
    "flaky": 2,
    "flaky_tests": ["AuthTokenRefreshTest", "CacheEvictionTest"]
  },

  "your_previous_changes": [
    {
      "description": "Refactored auth token handling",
      "files": ["src/Auth/TokenService.cs", "src/Auth/TokenValidator.cs"],
      "outcome": {
        "pr_status": "merged",
        "build_passed": true,
        "tests_passed": true,
        "errors_in_logs": 0,
        "was_reverted": false
      }
    }
  ],

  "patterns_detected": [
    {
      "type": "flaky_test",
      "description": "AuthTokenRefreshTest has failed 3 of last 10 runs",
      "confidence": 0.85
    }
  ],

  "relevant_memories": [
    {
      "type": "warning",
      "content": "The cache module has a known race condition under high load - be careful when modifying"
    }
  ]
}
```

### 2. `get_insights` - What's interesting/concerning

```json
{
  "name": "get_insights",
  "description": "Get detected patterns, anomalies, and insights about the codebase health",
  "inputSchema": {
    "type": "object",
    "properties": {
      "focus_area": {
        "type": "string",
        "description": "Optional: specific area to focus on (e.g., 'tests', 'performance', 'specific/path')"
      },
      "time_range": {
        "type": "string",
        "enum": ["day", "week", "month", "all"],
        "default": "week"
      }
    }
  }
}
```

**Output Example:**
```json
{
  "patterns": [
    {
      "type": "co_change_correlation",
      "description": "UserService.cs and UserRepository.cs change together 94% of the time",
      "recommendation": "Consider if these should be refactored together or merged",
      "confidence": 0.94
    },
    {
      "type": "high_churn",
      "description": "config/settings.json has been modified 23 times this month",
      "recommendation": "Frequent config changes may indicate missing abstraction",
      "confidence": 0.88
    },
    {
      "type": "breakage_correlation",
      "description": "Changes to PaymentProcessor often break NotificationService tests",
      "recommendation": "These modules may have hidden coupling",
      "confidence": 0.76
    }
  ],

  "anomalies": [
    {
      "type": "sudden_failure",
      "description": "DatabaseConnectionTest started failing 2 days ago after being stable for 3 months",
      "likely_cause": "Commit abc123 modified connection string handling"
    }
  ],

  "health_metrics": {
    "build_success_rate_7d": 0.92,
    "test_success_rate_7d": 0.97,
    "avg_build_time_trend": "+12% over 30 days",
    "code_churn_hotspots": ["src/Services/Payment/", "src/Config/"]
  }
}
```

### 3. `remember` - Store persistent memory

```json
{
  "name": "remember",
  "description": "Store information to remember across sessions",
  "inputSchema": {
    "type": "object",
    "required": ["type", "key", "content"],
    "properties": {
      "type": {
        "type": "string",
        "enum": ["decision", "convention", "warning", "todo", "context"],
        "description": "Type of memory"
      },
      "key": {
        "type": "string",
        "description": "Short identifier for this memory"
      },
      "content": {
        "type": "string",
        "description": "The information to remember"
      },
      "expires_in_days": {
        "type": "integer",
        "description": "Optional: auto-expire after N days"
      }
    }
  }
}
```

### 4. `recall` - Retrieve memories

```json
{
  "name": "recall",
  "description": "Retrieve stored memories, either by key or semantic search",
  "inputSchema": {
    "type": "object",
    "properties": {
      "key": {
        "type": "string",
        "description": "Exact key to recall"
      },
      "query": {
        "type": "string",
        "description": "Semantic search query"
      },
      "type": {
        "type": "string",
        "enum": ["decision", "convention", "warning", "todo", "context", "all"],
        "description": "Filter by memory type"
      },
      "limit": {
        "type": "integer",
        "default": 5
      }
    }
  }
}
```

### 5. `get_file_history` - Deep history for a file

```json
{
  "name": "get_file_history",
  "description": "Get comprehensive history and context for a specific file",
  "inputSchema": {
    "type": "object",
    "required": ["file_path"],
    "properties": {
      "file_path": {
        "type": "string"
      }
    }
  }
}
```

**Output Example:**
```json
{
  "file_path": "src/Services/PaymentProcessor.cs",

  "stats": {
    "created": "2024-03-15",
    "total_commits": 47,
    "contributors": ["alice", "bob", "claude"],
    "last_modified": "2025-01-15",
    "churn_percentile": 89
  },

  "recent_changes": [
    {
      "date": "2025-01-15",
      "author": "claude",
      "message": "Refactored retry logic",
      "session_id": "session_abc123"
    }
  ],

  "patterns": [
    {
      "type": "breakage_correlation",
      "description": "Changes here broke NotificationService 3 times in past month"
    }
  ],

  "related_files": [
    {"path": "src/Services/PaymentValidator.cs", "correlation": 0.87},
    {"path": "tests/PaymentProcessorTests.cs", "correlation": 0.92}
  ],

  "memories": [
    {
      "type": "warning",
      "content": "Don't modify the retry delays without load testing - we had a cascade failure in prod"
    }
  ],

  "test_coverage": {
    "covered": true,
    "test_file": "tests/PaymentProcessorTests.cs",
    "recent_test_results": "12 pass, 0 fail, 1 flaky"
  }
}
```

### 6. `record_session` - Track AI session

```json
{
  "name": "record_session",
  "description": "Record the start/end of an AI session and what was done",
  "inputSchema": {
    "type": "object",
    "required": ["action"],
    "properties": {
      "action": {
        "type": "string",
        "enum": ["start", "end", "log_change"]
      },
      "session_id": {
        "type": "string"
      },
      "summary": {
        "type": "string",
        "description": "For 'end' action: summary of what was accomplished"
      },
      "change": {
        "type": "object",
        "description": "For 'log_change' action: details of a change made",
        "properties": {
          "file_path": {"type": "string"},
          "description": {"type": "string"}
        }
      }
    }
  }
}
```

### 7. `set_trigger` - Time-shifted actions

```json
{
  "name": "set_trigger",
  "description": "Set up a trigger for future conditions",
  "inputSchema": {
    "type": "object",
    "required": ["condition", "message"],
    "properties": {
      "condition": {
        "type": "object",
        "properties": {
          "type": {
            "type": "string",
            "enum": ["file_changed", "test_failed", "build_failed", "time_elapsed", "error_logged"]
          },
          "target": {
            "type": "string",
            "description": "File path, test name, or time duration"
          }
        }
      },
      "message": {
        "type": "string",
        "description": "Message to surface when trigger fires"
      },
      "expires_in_days": {
        "type": "integer",
        "default": 30
      }
    }
  }
}
```

---

## Implementation Phases

### Phase 1: Foundation (Core Infrastructure)

**Goal**: Event storage and basic observation

1. Create event store schema and models
2. Implement FileSystemObserver
3. Implement GitObserver
4. Create basic `get_briefing` tool (events only, no analysis)
5. Create `record_session` tool

**Deliverables**:
- Background daemon that records file and git events
- SQLite event store
- Basic briefing generation

### Phase 2: Memory System

**Goal**: Persistent knowledge storage

1. Create memory store schema and models
2. Implement MemoryStore with semantic search (reuse existing embedding infrastructure)
3. Create `remember` and `recall` tools
4. Integrate memories into briefing

**Deliverables**:
- Semantic memory storage and retrieval
- Memory integration in briefings

### Phase 3: Outcome Tracking

**Goal**: Know if changes worked

1. Implement BuildObserver
2. Implement TestObserver
3. Create outcome tracking logic (correlate builds/tests with sessions)
4. Enhance `get_briefing` with outcome data
5. Create `get_file_history` tool

**Deliverables**:
- Build and test observation
- Change outcome tracking
- File-level history and context

### Phase 4: Pattern Analysis

**Goal**: Detect patterns and anomalies

1. Implement PatternAnalyzer
2. Implement co-change correlation detection
3. Implement failure pattern detection
4. Implement anomaly detection
5. Create `get_insights` tool

**Deliverables**:
- Automatic pattern detection
- Insights tool with recommendations

### Phase 5: Advanced Features

**Goal**: Time-shifted capabilities and log analysis

1. Implement LogObserver
2. Implement trigger system
3. Create `set_trigger` tool
4. Add log correlation to outcomes
5. Performance optimization and caching

**Deliverables**:
- Log observation and correlation
- Trigger system for future events
- Production-ready performance

---

## Technical Considerations

### Storage

- **SQLite** for event store (already used by Grigori)
- Separate database file for consciousness data (`grigori-consciousness.db`)
- Consider time-based partitioning for events table (monthly tables)
- Implement retention policies (auto-delete events older than N days)

### Performance

- Events table will grow large - add appropriate indexes
- Pattern analysis should run in background, not on-demand
- Cache briefings with short TTL
- Use incremental analysis where possible

### Background Daemon

- Use .NET `IHostedService` / `BackgroundService`
- Can run as same process as MCP server or separate
- Consider watchdog for reliability
- Graceful shutdown with event flushing

### Concurrency

- Multiple AI sessions might run concurrently
- Event writes should be thread-safe
- Session tracking needs atomic operations

### Privacy/Security

- Events may contain sensitive file paths/content
- Consider what gets logged vs summarized
- Memory content should be treated as potentially sensitive
- No external data transmission

---

## Database Schema

```sql
-- Events table
CREATE TABLE events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    type TEXT NOT NULL,
    source TEXT NOT NULL,
    file_path TEXT,
    data TEXT NOT NULL,
    session_id TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_events_timestamp ON events(timestamp);
CREATE INDEX idx_events_type ON events(type);
CREATE INDEX idx_events_file_path ON events(file_path);
CREATE INDEX idx_events_session_id ON events(session_id);

-- Sessions table
CREATE TABLE sessions (
    session_id TEXT PRIMARY KEY,
    started_at TEXT NOT NULL,
    ended_at TEXT,
    files_touched TEXT,  -- JSON array
    changes_applied TEXT, -- JSON array
    summary TEXT
);

-- Outcomes table
CREATE TABLE outcomes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL,
    file_path TEXT NOT NULL,
    change_description TEXT NOT NULL,
    changed_at TEXT NOT NULL,
    build_succeeded INTEGER,
    tests_passed INTEGER,
    tests_failed INTEGER,
    was_reverted INTEGER,
    errors_in_logs INTEGER,
    pr_status TEXT,
    review_comments TEXT,  -- JSON array
    evaluated_at TEXT,
    FOREIGN KEY (session_id) REFERENCES sessions(session_id)
);

CREATE INDEX idx_outcomes_session ON outcomes(session_id);
CREATE INDEX idx_outcomes_file ON outcomes(file_path);

-- Memories table
CREATE TABLE memories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    type TEXT NOT NULL,
    key TEXT NOT NULL UNIQUE,
    content TEXT NOT NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT,
    session_id TEXT,
    embedding BLOB
);

CREATE INDEX idx_memories_type ON memories(type);
CREATE INDEX idx_memories_key ON memories(key);
CREATE INDEX idx_memories_expires ON memories(expires_at);

-- Patterns table
CREATE TABLE patterns (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    type TEXT NOT NULL,
    description TEXT NOT NULL,
    confidence REAL NOT NULL,
    evidence TEXT NOT NULL,  -- JSON
    detected_at TEXT NOT NULL,
    last_occurrence TEXT,
    occurrence_count INTEGER DEFAULT 1,
    is_active INTEGER DEFAULT 1
);

CREATE INDEX idx_patterns_type ON patterns(type);
CREATE INDEX idx_patterns_active ON patterns(is_active);

-- Triggers table
CREATE TABLE triggers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    condition_type TEXT NOT NULL,
    condition_target TEXT,
    message TEXT NOT NULL,
    created_at TEXT NOT NULL,
    created_by_session TEXT,
    expires_at TEXT,
    fired_at TEXT,
    is_active INTEGER DEFAULT 1
);

CREATE INDEX idx_triggers_active ON triggers(is_active);
CREATE INDEX idx_triggers_condition ON triggers(condition_type, condition_target);
```

---

## Project Structure

```
src/
├── Grigori.Core/
│   ├── Embeddings/          # (existing)
│   ├── Indexing/            # (existing)
│   ├── Search/              # (existing)
│   ├── Storage/             # (existing)
│   └── Consciousness/       # (new)
│       ├── Models/
│       │   ├── CodebaseEvent.cs
│       │   ├── AISession.cs
│       │   ├── ChangeOutcome.cs
│       │   ├── Memory.cs
│       │   ├── DetectedPattern.cs
│       │   └── Trigger.cs
│       ├── Observers/
│       │   ├── IObserver.cs
│       │   ├── FileSystemObserver.cs
│       │   ├── GitObserver.cs
│       │   ├── BuildObserver.cs
│       │   ├── TestObserver.cs
│       │   └── LogObserver.cs
│       ├── Analysis/
│       │   ├── PatternAnalyzer.cs
│       │   ├── BriefingGenerator.cs
│       │   └── OutcomeEvaluator.cs
│       ├── Storage/
│       │   ├── EventStore.cs
│       │   ├── MemoryStore.cs
│       │   ├── SessionStore.cs
│       │   └── ConsciousnessDbContext.cs
│       └── ConsciousnessEngine.cs
│
├── Grigori.Daemon/          # (new - background service)
│   ├── Program.cs
│   ├── DaemonService.cs
│   └── appsettings.json
│
├── Grigori.Mcp/
│   ├── Tools/
│   │   ├── SearchTool.cs    # (existing)
│   │   ├── IndexTool.cs     # (existing)
│   │   ├── BriefingTool.cs  # (new)
│   │   ├── InsightsTool.cs  # (new)
│   │   ├── MemoryTool.cs    # (new)
│   │   ├── FileHistoryTool.cs # (new)
│   │   ├── SessionTool.cs   # (new)
│   │   └── TriggerTool.cs   # (new)
│   └── Program.cs
│
└── Grigori.Api/             # (existing)
```

---

## Success Metrics

1. **Briefing usefulness**: Does the AI make better decisions with the briefing?
2. **Memory recall accuracy**: Are retrieved memories relevant?
3. **Pattern detection precision**: Are detected patterns real and actionable?
4. **Outcome tracking coverage**: What % of AI changes have outcome data?
5. **Performance**: Briefing generation < 500ms, memory recall < 100ms

---

## Future Enhancements (Out of Scope)

- Multi-repository awareness
- Team/multi-user support
- Integration with issue trackers (GitHub Issues, Jira)
- Integration with CI/CD systems (GitHub Actions, Jenkins)
- Slack/notification integrations
- Web dashboard for visualizing insights
- Natural language trigger conditions
- Predictive analysis ("this change will likely break X")
