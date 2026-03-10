# Grigori

A desktop application for managing coding context that AI assistants can access via MCP (Model Context Protocol).

## Overview

Grigori helps you define and organize your coding standards, architecture patterns, and development preferences in a structured way. It runs a local MCP server that AI coding assistants (like Claude Code) can connect to, giving them access to your personalized coding context.

## Features

### Coding Context
- **Coding Patterns** - Document patterns you use consistently (e.g., "Repository Pattern", "CQRS")
- **Design Preferences** - Define your coding style choices with rationale and priority
- **Avoidance Rules** - Specify patterns and practices to avoid, with severity levels

### Architecture Context
- **Architecture Patterns** - Define layered architectures with ASCII diagrams
- **Layer Dependencies** - Specify allowed and forbidden dependencies between layers
- **Code Templates** - Reusable code templates with placeholders for scaffolding
- **Naming Conventions** - Document naming patterns for different contexts (DTOs, repositories, etc.)

### MCP Integration
- Built-in MCP server exposes 40+ tools for reading and writing context
- Connect from Claude Code, Cursor, or any MCP-compatible client
- AI assistants can query your coding standards while helping you code

## Getting Started

### Prerequisites
- .NET 10 SDK
- Windows, macOS, or Linux (via MAUI)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/andres-m-rodriguez/Grigori.git
cd Grigori

# Build
dotnet build

# Run the desktop app
dotnet run --project src/Grigori.Desktop
```

### Connect from Claude Code

Add to your Claude Code MCP settings:

```json
{
  "mcpServers": {
    "grigori": {
      "url": "http://localhost:3001/sse"
    }
  }
}
```

## MCP Tools

### Coding Context Tools
| Tool | Description |
|------|-------------|
| `get_coding_context` | Get complete coding context as markdown |
| `get_coding_patterns` | Get all coding patterns as JSON |
| `get_design_preferences` | Get all design preferences as JSON |
| `get_avoidance_rules` | Get all avoidance rules as JSON |
| `search_context` | Search across all context for a keyword |
| `add_coding_pattern` | Add a new coding pattern |
| `add_design_preference` | Add a new design preference |
| `add_avoidance_rule` | Add a new avoidance rule |

### Architecture Context Tools
| Tool | Description |
|------|-------------|
| `get_architecture_context` | Get complete architecture context as markdown |
| `get_architecture_patterns` | Get all architecture patterns |
| `get_active_architecture_pattern` | Get the currently active pattern |
| `add_architecture_pattern` | Add a new architecture pattern |
| `add_architecture_layer` | Add a layer to a pattern |
| `add_layer_dependency` | Define allowed/forbidden dependencies |
| `get_code_templates` | Get all code templates |
| `add_code_template` | Add a new code template |
| `get_naming_conventions` | Get all naming conventions |
| `add_naming_convention` | Add a new naming convention |

## Project Structure

```
src/
├── Grigori.Desktop/        # MAUI Blazor Hybrid desktop app
│   ├── Components/         # Blazor pages and components
│   └── Mcp/               # MCP server integration
├── Grigori.Contracts/      # DTOs and repository interfaces
├── Grigori.DataAccess/     # Repository implementations
├── Grigori.Database/       # EF Core DbContext and models
└── Grigori.Common.Pagination/  # Cursor-based pagination
```

## Technology Stack

- **.NET 10** - Latest .NET runtime
- **MAUI Blazor Hybrid** - Cross-platform desktop UI
- **MudBlazor** - Material Design component library
- **SQLite** - Local database storage
- **Entity Framework Core** - ORM
- **MinimalMcp** - MCP server implementation
- **BlazingSingularity** - Async command state management

## Export

The Settings page includes an export feature that generates a `CLAUDE.md`-style file from your coding context, useful for projects that don't use MCP.

## License

MIT
