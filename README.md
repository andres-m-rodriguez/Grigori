# Grigori

**Semantic code search and AI-aware codebase intelligence for .NET**

Grigori provides AI assistants with deep contextual awareness of software projects using embeddings, semantic search, and Model Context Protocol (MCP) integration. Index your codebase once, then let AI tools find exactly what they need.

## Features

- **Semantic Search** - Find code by meaning, not just keywords. Uses all-MiniLM-L6-v2 embeddings for accurate contextual matching
- **Fast Vector Search** - HNSW algorithm provides 50-100x speedup over linear search on large codebases
- **MCP Integration** - First-class support for Claude and other AI assistants via Model Context Protocol
- **Language-Aware Chunking** - Smart code splitting that preserves semantic context (C# support, extensible)
- **Multiple Deployment Modes** - Run locally via stdio, as HTTP server, or in Docker
- **Interactive Dashboard** - Blazor-based web UI for searching and managing your index
- **Native AOT CLI** - Lightweight CLI tool for remote indexing operations

## Quick Start

### Docker (Recommended)

```bash
# Pull and run
docker run -d -p 5151:5150 -v grigori-data:/data ghcr.io/your-org/grigori:latest

# Index a project
curl -X POST http://localhost:5151/api/index \
  -H "Content-Type: application/json" \
  -d '{"path": "/path/to/project"}'

# Search
curl "http://localhost:5151/api/search?query=authentication+logic"
```

### With Claude Code

Add to your MCP configuration:

```json
{
  "mcpServers": {
    "grigori": {
      "command": "dotnet",
      "args": ["run", "--project", "src/Grigori.Mcp", "--", "--mcp"]
    }
  }
}
```

Then use in Claude:
```
Search for code that handles user authentication
```

### From Source

```bash
# Clone and build
git clone https://github.com/your-org/grigori.git
cd grigori
dotnet build

# Run the server with dashboard
dotnet run --project src/Grigori.Mcp -- --server --dashboard

# Access dashboard at http://localhost:5150
```

## Architecture

```
src/
├── Grigori.Cli/           # Native AOT CLI tool
├── Grigori.Contracts/     # Interfaces and DTOs
├── Grigori.Database/      # SQLite models and context
├── Grigori.DataAccess/    # Repository pattern implementation
├── Grigori.Infrastructure/# Core logic (embeddings, chunking, indexing)
└── Grigori.Mcp/           # Web server, MCP endpoints, Dashboard
```

### Technology Stack

- **.NET 10** - Latest framework
- **Blazor Server + MudBlazor** - Interactive dashboard UI
- **SQLite** - Persistent storage for chunks and embeddings
- **ONNX Runtime** - Local embedding generation (all-MiniLM-L6-v2, 384 dimensions)
- **HNSW** - Hierarchical Navigable Small World for approximate nearest neighbor search
- **Model Context Protocol** - AI assistant integration

## Configuration

Configuration via `appsettings.json` or environment variables:

```json
{
  "Grigori": {
    "EmbeddingProvider": "onnx",
    "OnnxModelPath": "models/all-MiniLM-L6-v2.onnx",
    "HnswM": 16,
    "HnswEfConstruction": 200,
    "HnswEfSearch": 50,
    "SupportedExtensions": [".cs", ".ts", ".js", ".py", ".go", ".rs", ".java", ".tsx", ".jsx"],
    "ExcludedPatterns": ["**/obj/**", "**/bin/**", "**/node_modules/**", "**/.git/**"]
  }
}
```

## Server Modes

| Mode | Command | Use Case |
|------|---------|----------|
| `--mcp` | Stdio transport | Local Claude Code integration |
| `--mcp-http` | HTTP/SSE transport | Remote AI clients |
| `--server` | HTTP API only | Containerized deployment |
| `--dashboard` | Dashboard + API | Interactive use |

## MCP Tools

When integrated with an AI assistant, Grigori exposes:

- **search_code** - Semantic search across indexed code
- **index** - Index directories or files
- **metrics** - Performance and system metrics
- **benchmark** - Performance testing

### Search Output Modes

- `full` - Complete chunk content with context
- `compact` - Condensed results
- `summary` - Brief overview
- `paths` - File paths only

## CLI Tool

The native AOT CLI enables remote indexing:

```bash
# Build the CLI
dotnet publish src/Grigori.Cli -c Release

# Index a project to a remote server
./grigori index ./my-project --server http://localhost:5151
```

## Docker

### Images

- **Slim** (`grigori:slim`) - Downloads model on first run (~400MB)
- **Full** (`grigori:latest`) - Includes model (~1.2GB)

### Docker Compose

```yaml
services:
  grigori:
    image: ghcr.io/your-org/grigori:latest
    ports:
      - "5151:5150"
    volumes:
      - grigori-data:/data
      - ./projects:/projects:ro
    environment:
      - Grigori__EmbeddingProvider=onnx

volumes:
  grigori-data:
```

## Roadmap

Planned features and improvements:

- [ ] **Incremental indexing** - Only re-index changed files (#3)
- [ ] **Multi-project management** - Project metadata and switching (#2)
- [ ] **Dashboard search UI** - Interactive search interface (#4)
- [ ] **API authentication** - Multi-user deployment support (#5)
- [ ] **File watcher** - Automatic re-indexing on changes (#6)
- [ ] **GPU acceleration** - CUDA/DirectML for faster embeddings (#7)
- [ ] **Alternative embedding models** - Support for other models (#8)
- [ ] **Dependency tracking** - Code dependency analysis (#16)

### Future Vision: Codebase Consciousness

Grigori is evolving toward a comprehensive codebase intelligence system:

1. **Event Storage** - Track file changes, git commits, build results
2. **Persistent Memory** - Remember decisions, conventions, and context
3. **Pattern Detection** - Identify co-change patterns and recurring issues
4. **Intelligent Briefings** - Summarize changes since last session

## Contributing

Contributions are welcome! Please open an issue to discuss significant changes before submitting a PR.

```bash
# Run tests
dotnet test

# Build all projects
dotnet build

# Format code
dotnet format
```

## License

[Add your license here]

---

*Grigori - Giving AI assistants the context they need to help you code better.*
