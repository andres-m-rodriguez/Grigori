using Grigori.Contracts.Interfaces;
using MinimalMcp;

namespace Grigori.Desktop.Mcp;

public sealed class McpServerService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _port;

    public McpServerService(IServiceProvider serviceProvider, int port = 3001)
    {
        _serviceProvider = serviceProvider;
        _port = port;
    }

    public string ServerUrl => $"http://localhost:{_port}/sse";
    public bool IsRunning => _serverTask != null && !_serverTask.IsCompleted;

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        var mcp = McpApp.Create("Grigori", "1.0.0");

        // Configure DI - share the app's service provider
        mcp.ConfigureServices(services =>
        {
            // Register repositories from the main app's service provider
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<ICodingPatternRepository>()
            );
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<IDesignPreferenceRepository>()
            );
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<IAvoidanceRuleRepository>()
            );
            // Architecture repositories
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<IArchitecturePatternRepository>()
            );
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<IArchitectureLayerRepository>()
            );
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<ILayerDependencyRepository>()
            );
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<ICodeTemplateRepository>()
            );
            services.AddScoped(_ =>
                _serviceProvider.GetRequiredService<INamingConventionRepository>()
            );
            services.AddScoped(_ => _serviceProvider.GetRequiredService<GrigoriMcpTools>());
        });

        // Register tools - GrigoriMcpTools will be injected via DI
        mcp.AddTool(
            "get_coding_context",
            "Get the complete coding context including patterns, preferences, and avoidance rules formatted as markdown",
            async (GrigoriMcpTools tools) => await tools.GetCodingContext()
        );

        mcp.AddTool(
            "get_coding_patterns",
            "Get all coding patterns as JSON",
            async (GrigoriMcpTools tools) => await tools.GetCodingPatterns()
        );

        mcp.AddTool(
            "get_design_preferences",
            "Get all design preferences as JSON",
            async (GrigoriMcpTools tools) => await tools.GetDesignPreferences()
        );

        mcp.AddTool(
            "get_avoidance_rules",
            "Get all avoidance rules as JSON",
            async (GrigoriMcpTools tools) => await tools.GetAvoidanceRules()
        );

        mcp.AddTool(
            "search_context",
            "Search across all coding context (patterns, preferences, avoidances) for a keyword",
            async (string query, GrigoriMcpTools tools) => await tools.SearchContext(query)
        );

        // ===== WRITE TOOLS =====

        // Coding Patterns
        mcp.AddTool(
            "add_coding_pattern",
            "Add a new coding pattern. Returns the created pattern.",
            async (
                string name,
                string description,
                string category,
                string? example,
                GrigoriMcpTools tools
            ) => await tools.AddCodingPattern(name, description, category, example)
        );

        mcp.AddTool(
            "update_coding_pattern",
            "Update an existing coding pattern by ID",
            async (
                int id,
                string name,
                string description,
                string category,
                string? example,
                bool isActive,
                GrigoriMcpTools tools
            ) => await tools.UpdateCodingPattern(id, name, description, category, example, isActive)
        );

        mcp.AddTool(
            "delete_coding_pattern",
            "Delete a coding pattern by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteCodingPattern(id)
        );

        // Design Preferences
        mcp.AddTool(
            "add_design_preference",
            "Add a new design preference. Priority is optional (default 0, higher = more important).",
            async (
                string category,
                string preference,
                string? rationale,
                int priority,
                GrigoriMcpTools tools
            ) => await tools.AddDesignPreference(category, preference, rationale, priority)
        );

        mcp.AddTool(
            "update_design_preference",
            "Update an existing design preference by ID",
            async (
                int id,
                string category,
                string preference,
                string? rationale,
                int priority,
                GrigoriMcpTools tools
            ) => await tools.UpdateDesignPreference(id, category, preference, rationale, priority)
        );

        mcp.AddTool(
            "delete_design_preference",
            "Delete a design preference by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteDesignPreference(id)
        );

        // Avoidance Rules
        mcp.AddTool(
            "add_avoidance_rule",
            "Add a new avoidance rule. Severity must be: Avoid, StronglyAvoid, or Never. Optionally link to a design preference as the recommended alternative.",
            async (
                string name,
                string description,
                string category,
                string severity,
                int? preferredAlternativeId,
                GrigoriMcpTools tools
            ) => await tools.AddAvoidanceRule(name, description, category, severity, preferredAlternativeId)
        );

        mcp.AddTool(
            "update_avoidance_rule",
            "Update an existing avoidance rule by ID. Severity must be: Avoid, StronglyAvoid, or Never. Optionally link to a design preference as the recommended alternative.",
            async (
                int id,
                string name,
                string description,
                string category,
                string severity,
                int? preferredAlternativeId,
                GrigoriMcpTools tools
            ) => await tools.UpdateAvoidanceRule(id, name, description, category, severity, preferredAlternativeId)
        );

        mcp.AddTool(
            "delete_avoidance_rule",
            "Delete an avoidance rule by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteAvoidanceRule(id)
        );

        // ===== ARCHITECTURE CONTEXT TOOLS =====

        mcp.AddTool(
            "get_architecture_context",
            "Get the complete architecture context including patterns, layers, dependencies, templates, and naming conventions formatted as markdown",
            async (GrigoriMcpTools tools) => await tools.GetArchitectureContext()
        );

        mcp.AddTool(
            "get_active_architecture_pattern",
            "Get the currently active architecture pattern with all its layers",
            async (GrigoriMcpTools tools) => await tools.GetActiveArchitecturePattern()
        );

        mcp.AddTool(
            "get_architecture_patterns",
            "Get all architecture patterns as JSON",
            async (GrigoriMcpTools tools) => await tools.GetArchitecturePatterns()
        );

        mcp.AddTool(
            "get_architecture_pattern",
            "Get a specific architecture pattern by ID with its layers",
            async (int id, GrigoriMcpTools tools) => await tools.GetArchitecturePattern(id)
        );

        mcp.AddTool(
            "add_architecture_pattern",
            "Add a new architecture pattern. Returns the created pattern.",
            async (
                string name,
                string description,
                string? diagram,
                GrigoriMcpTools tools
            ) => await tools.AddArchitecturePattern(name, description, diagram)
        );

        mcp.AddTool(
            "set_active_architecture_pattern",
            "Set an architecture pattern as the active one (deactivates others)",
            async (int id, GrigoriMcpTools tools) => await tools.SetActiveArchitecturePattern(id)
        );

        mcp.AddTool(
            "delete_architecture_pattern",
            "Delete an architecture pattern by ID (also deletes its layers)",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteArchitecturePattern(id)
        );

        // Architecture Layers
        mcp.AddTool(
            "get_layers_by_pattern",
            "Get all layers for a specific architecture pattern",
            async (int patternId, GrigoriMcpTools tools) => await tools.GetLayersByPattern(patternId)
        );

        mcp.AddTool(
            "get_architecture_layer",
            "Get a specific architecture layer by ID with all its details",
            async (int id, GrigoriMcpTools tools) => await tools.GetArchitectureLayer(id)
        );

        mcp.AddTool(
            "add_architecture_layer",
            "Add a new layer to an architecture pattern. Order 0 = top layer.",
            async (
                int patternId,
                string name,
                string description,
                string responsibility,
                string? contains,
                int order,
                GrigoriMcpTools tools
            ) => await tools.AddArchitectureLayer(patternId, name, description, responsibility, contains, order)
        );

        mcp.AddTool(
            "delete_architecture_layer",
            "Delete an architecture layer by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteArchitectureLayer(id)
        );

        // Layer Dependencies
        mcp.AddTool(
            "get_layer_dependencies",
            "Get all layer dependencies for a specific architecture pattern",
            async (int patternId, GrigoriMcpTools tools) => await tools.GetLayerDependencies(patternId)
        );

        mcp.AddTool(
            "add_layer_dependency",
            "Add a dependency rule between layers. isAllowed=true means allowed, false means forbidden.",
            async (
                int sourceLayerId,
                int targetLayerId,
                bool isAllowed,
                string? rationale,
                GrigoriMcpTools tools
            ) => await tools.AddLayerDependency(sourceLayerId, targetLayerId, isAllowed, rationale)
        );

        mcp.AddTool(
            "delete_layer_dependency",
            "Delete a layer dependency by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteLayerDependency(id)
        );

        // Code Templates
        mcp.AddTool(
            "get_code_templates",
            "Get all code templates as JSON",
            async (GrigoriMcpTools tools) => await tools.GetCodeTemplates()
        );

        mcp.AddTool(
            "get_code_template",
            "Get a specific code template by ID with full template content",
            async (int id, GrigoriMcpTools tools) => await tools.GetCodeTemplate(id)
        );

        mcp.AddTool(
            "get_code_templates_by_category",
            "Get code templates filtered by category",
            async (string category, GrigoriMcpTools tools) => await tools.GetCodeTemplatesByCategory(category)
        );

        mcp.AddTool(
            "get_code_templates_by_language",
            "Get code templates filtered by programming language",
            async (string language, GrigoriMcpTools tools) => await tools.GetCodeTemplatesByLanguage(language)
        );

        mcp.AddTool(
            "add_code_template",
            "Add a new code template. Use {{placeholders}} for variables in the template.",
            async (
                string name,
                string description,
                string language,
                string category,
                string template,
                int? layerId,
                GrigoriMcpTools tools
            ) => await tools.AddCodeTemplate(name, description, language, category, template, layerId)
        );

        mcp.AddTool(
            "delete_code_template",
            "Delete a code template by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteCodeTemplate(id)
        );

        // Naming Conventions
        mcp.AddTool(
            "get_naming_conventions",
            "Get all naming conventions as JSON",
            async (GrigoriMcpTools tools) => await tools.GetNamingConventions()
        );

        mcp.AddTool(
            "get_naming_convention",
            "Get a specific naming convention by ID",
            async (int id, GrigoriMcpTools tools) => await tools.GetNamingConvention(id)
        );

        mcp.AddTool(
            "get_naming_conventions_by_context",
            "Get naming conventions filtered by context (e.g., DTO, Repository)",
            async (string context, GrigoriMcpTools tools) => await tools.GetNamingConventionsByContext(context)
        );

        mcp.AddTool(
            "add_naming_convention",
            "Add a new naming convention. Use {{Entity}} as placeholder in pattern.",
            async (
                string context,
                string pattern,
                string example,
                string? description,
                int? layerId,
                GrigoriMcpTools tools
            ) => await tools.AddNamingConvention(context, pattern, example, description, layerId)
        );

        mcp.AddTool(
            "delete_naming_convention",
            "Delete a naming convention by ID",
            async (int id, GrigoriMcpTools tools) => await tools.DeleteNamingConvention(id)
        );

        try
        {
            await mcp.RunHttpAsync(_port, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
