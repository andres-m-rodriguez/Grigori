using System.Text;
using System.Text.Json;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;

namespace Grigori.Desktop.Mcp;

public sealed class GrigoriMcpTools(
    ICodingPatternRepository codingPatternRepository,
    IDesignPreferenceRepository designPreferenceRepository,
    IAvoidanceRuleRepository avoidanceRuleRepository,
    IArchitecturePatternRepository architecturePatternRepository,
    IArchitectureLayerRepository architectureLayerRepository,
    ILayerDependencyRepository layerDependencyRepository,
    ICodeTemplateRepository codeTemplateRepository,
    INamingConventionRepository namingConventionRepository)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> GetCodingContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Coding Context");
        sb.AppendLine();

        // Patterns
        var patterns = new List<CodingPatternForList>();
        await foreach (var item in codingPatternRepository.GetAsync(new CodingPatternParameters(PageSize: 1000)))
        {
            patterns.Add(item);
        }
        if (patterns.Count > 0)
        {
            sb.AppendLine("## Coding Patterns");
            sb.AppendLine();
            var categories = patterns.Select(p => p.Category).Distinct().OrderBy(c => c);
            foreach (var category in categories)
            {
                sb.AppendLine($"### {category}");
                foreach (var pattern in patterns.Where(p => p.Category == category && p.IsActive))
                {
                    sb.AppendLine($"- **{pattern.Name}**: {pattern.Description}");
                    if (!string.IsNullOrEmpty(pattern.Example))
                    {
                        sb.AppendLine($"  ```");
                        sb.AppendLine($"  {pattern.Example}");
                        sb.AppendLine($"  ```");
                    }
                }
                sb.AppendLine();
            }
        }

        // Preferences
        var preferences = new List<DesignPreferenceForList>();
        await foreach (var item in designPreferenceRepository.GetAsync(new DesignPreferenceParameters(PageSize: 1000)))
        {
            preferences.Add(item);
        }
        if (preferences.Count > 0)
        {
            sb.AppendLine("## Design Preferences");
            sb.AppendLine();
            var categories = preferences.Select(p => p.Category).Distinct().OrderBy(c => c);
            foreach (var category in categories)
            {
                sb.AppendLine($"### {category}");
                foreach (var pref in preferences.Where(p => p.Category == category).OrderByDescending(p => p.Priority))
                {
                    sb.AppendLine($"- {pref.Preference}");
                    if (!string.IsNullOrEmpty(pref.Rationale))
                    {
                        sb.AppendLine($"  - *Rationale: {pref.Rationale}*");
                    }
                }
                sb.AppendLine();
            }
        }

        // Avoidances
        var avoidances = new List<AvoidanceRuleForList>();
        await foreach (var item in avoidanceRuleRepository.GetAsync(new AvoidanceRuleParameters(PageSize: 1000)))
        {
            avoidances.Add(item);
        }
        if (avoidances.Count > 0)
        {
            sb.AppendLine("## Things to Avoid");
            sb.AppendLine();
            var severities = avoidances.Select(a => a.Severity).Distinct().OrderByDescending(s => s);
            foreach (var severity in severities)
            {
                sb.AppendLine($"### {severity}");
                foreach (var rule in avoidances.Where(a => a.Severity == severity))
                {
                    sb.AppendLine($"- **{rule.Name}**: {rule.Description}");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public async Task<string> GetCodingPatterns()
    {
        var patterns = new List<CodingPatternForList>();
        await foreach (var item in codingPatternRepository.GetAsync(new CodingPatternParameters(PageSize: 1000)))
        {
            patterns.Add(item);
        }
        return JsonSerializer.Serialize(patterns, JsonOptions);
    }

    public async Task<string> GetDesignPreferences()
    {
        var preferences = new List<DesignPreferenceForList>();
        await foreach (var item in designPreferenceRepository.GetAsync(new DesignPreferenceParameters(PageSize: 1000)))
        {
            preferences.Add(item);
        }
        return JsonSerializer.Serialize(preferences, JsonOptions);
    }

    public async Task<string> GetAvoidanceRules()
    {
        var avoidances = new List<AvoidanceRuleForList>();
        await foreach (var item in avoidanceRuleRepository.GetAsync(new AvoidanceRuleParameters(PageSize: 1000)))
        {
            avoidances.Add(item);
        }
        return JsonSerializer.Serialize(avoidances, JsonOptions);
    }

    public async Task<string> SearchContext(string query)
    {
        var results = new List<object>();

        await foreach (var p in codingPatternRepository.GetAsync(new CodingPatternParameters(PageSize: 1000)))
        {
            if (p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new { Type = "CodingPattern", p.Name, p.Category, p.Description, p.IsActive });
            }
        }

        await foreach (var p in designPreferenceRepository.GetAsync(new DesignPreferenceParameters(PageSize: 1000)))
        {
            if (p.Preference.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new { Type = "DesignPreference", p.Category, p.Preference, p.Priority });
            }
        }

        await foreach (var a in avoidanceRuleRepository.GetAsync(new AvoidanceRuleParameters(PageSize: 1000)))
        {
            if (a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new { Type = "AvoidanceRule", a.Name, a.Category, a.Description, Severity = a.Severity.ToString() });
            }
        }

        return JsonSerializer.Serialize(results, JsonOptions);
    }

    // ===== WRITE OPERATIONS =====

    // Coding Patterns
    public async Task<string> AddCodingPattern(string name, string description, string category, string? example = null)
    {
        var dto = new CodingPatternForCreate(name, description, category, example);
        var result = await codingPatternRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateCodingPattern(int id, string name, string description, string category, string? example, bool isActive)
    {
        var dto = new CodingPatternForUpdate(name, description, category, example, isActive);
        var result = await codingPatternRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Pattern with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteCodingPattern(int id)
    {
        var success = await codingPatternRepository.DeleteAsync(id);
        return success ? $"Pattern {id} deleted" : $"Pattern with id {id} not found";
    }

    // Design Preferences
    public async Task<string> AddDesignPreference(string category, string preference, string? rationale = null, int priority = 0)
    {
        var dto = new DesignPreferenceForCreate(category, preference, rationale, priority);
        var result = await designPreferenceRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateDesignPreference(int id, string category, string preference, string? rationale, int priority)
    {
        var dto = new DesignPreferenceForUpdate(category, preference, rationale, priority);
        var result = await designPreferenceRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Preference with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteDesignPreference(int id)
    {
        var success = await designPreferenceRepository.DeleteAsync(id);
        return success ? $"Preference {id} deleted" : $"Preference with id {id} not found";
    }

    // Avoidance Rules
    public async Task<string> AddAvoidanceRule(string name, string description, string category, string severity, int? preferredAlternativeId = null)
    {
        var severityEnum = Enum.Parse<AvoidanceSeverity>(severity, ignoreCase: true);
        // Treat 0 or negative as "no link"
        var actualPreferredId = preferredAlternativeId is null or <= 0 ? null : preferredAlternativeId;
        var dto = new AvoidanceRuleForCreate(name, description, category, severityEnum, actualPreferredId);
        var result = await avoidanceRuleRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateAvoidanceRule(int id, string name, string description, string category, string severity, int? preferredAlternativeId = null)
    {
        var severityEnum = Enum.Parse<AvoidanceSeverity>(severity, ignoreCase: true);
        // Treat 0 or negative as "no link"
        var actualPreferredId = preferredAlternativeId is null or <= 0 ? null : preferredAlternativeId;
        var dto = new AvoidanceRuleForUpdate(name, description, category, severityEnum, actualPreferredId);
        var result = await avoidanceRuleRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Avoidance rule with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteAvoidanceRule(int id)
    {
        var success = await avoidanceRuleRepository.DeleteAsync(id);
        return success ? $"Avoidance rule {id} deleted" : $"Avoidance rule with id {id} not found";
    }

    // ===== ARCHITECTURE CONTEXT =====

    public async Task<string> GetArchitectureContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Architecture Context");
        sb.AppendLine();

        var activePattern = await architecturePatternRepository.GetActiveAsync();
        if (activePattern is null)
        {
            sb.AppendLine("No active architecture pattern defined.");
            return sb.ToString();
        }

        sb.AppendLine($"## {activePattern.Name}");
        sb.AppendLine();
        sb.AppendLine(activePattern.Description);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(activePattern.Diagram))
        {
            sb.AppendLine("### Diagram");
            sb.AppendLine("```");
            sb.AppendLine(activePattern.Diagram);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (activePattern.Layers.Count > 0)
        {
            sb.AppendLine("### Layers (Top to Bottom)");
            sb.AppendLine();
            foreach (var layer in activePattern.Layers.OrderBy(l => l.Order))
            {
                sb.AppendLine($"#### {layer.Order}. {layer.Name}");
                sb.AppendLine($"- **Responsibility**: {layer.Responsibility}");
                sb.AppendLine($"- **Description**: {layer.Description}");
                sb.AppendLine();
            }
        }

        var dependencies = new List<LayerDependencyForList>();
        await foreach (var item in layerDependencyRepository.GetAsync(new LayerDependencyParameters(PageSize: 1000, PatternId: activePattern.Id)))
        {
            dependencies.Add(item);
        }
        if (dependencies.Count > 0)
        {
            sb.AppendLine("### Layer Dependencies");
            sb.AppendLine();
            var allowed = dependencies.Where(d => d.IsAllowed).ToList();
            var forbidden = dependencies.Where(d => !d.IsAllowed).ToList();

            if (allowed.Count > 0)
            {
                sb.AppendLine("**Allowed:**");
                foreach (var dep in allowed)
                {
                    sb.AppendLine($"- {dep.SourceLayerName} → {dep.TargetLayerName}");
                }
                sb.AppendLine();
            }

            if (forbidden.Count > 0)
            {
                sb.AppendLine("**Forbidden:**");
                foreach (var dep in forbidden)
                {
                    sb.AppendLine($"- {dep.SourceLayerName} → {dep.TargetLayerName}");
                }
                sb.AppendLine();
            }
        }

        var templates = new List<CodeTemplateForList>();
        await foreach (var item in codeTemplateRepository.GetAsync(new CodeTemplateParameters(PageSize: 1000)))
        {
            templates.Add(item);
        }
        if (templates.Count > 0)
        {
            sb.AppendLine("### Code Templates");
            sb.AppendLine();
            foreach (var cat in templates.Select(t => t.Category).Distinct().OrderBy(c => c))
            {
                sb.AppendLine($"#### {cat}");
                foreach (var t in templates.Where(t => t.Category == cat))
                {
                    sb.AppendLine($"- **{t.Name}** ({t.Language}): {t.Description}");
                }
                sb.AppendLine();
            }
        }

        var conventions = new List<NamingConventionForList>();
        await foreach (var item in namingConventionRepository.GetAsync(new NamingConventionParameters(PageSize: 1000)))
        {
            conventions.Add(item);
        }
        if (conventions.Count > 0)
        {
            sb.AppendLine("### Naming Conventions");
            sb.AppendLine();
            foreach (var ctx in conventions.Select(c => c.Context).Distinct().OrderBy(c => c))
            {
                sb.AppendLine($"#### {ctx}");
                foreach (var conv in conventions.Where(c => c.Context == ctx))
                {
                    sb.AppendLine($"- `{conv.Pattern}` → e.g., `{conv.Example}`");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public async Task<string> GetActiveArchitecturePattern()
    {
        var pattern = await architecturePatternRepository.GetActiveAsync();
        return pattern is null
            ? "No active architecture pattern"
            : JsonSerializer.Serialize(pattern, JsonOptions);
    }

    public async Task<string> GetArchitecturePatterns()
    {
        var patterns = new List<ArchitecturePatternForList>();
        await foreach (var item in architecturePatternRepository.GetAsync(new ArchitecturePatternParameters(PageSize: 1000)))
        {
            patterns.Add(item);
        }
        return JsonSerializer.Serialize(patterns, JsonOptions);
    }

    public async Task<string> GetArchitecturePattern(int id)
    {
        var pattern = await architecturePatternRepository.GetByIdAsync(id);
        return pattern is null
            ? $"Pattern with id {id} not found"
            : JsonSerializer.Serialize(pattern, JsonOptions);
    }

    public async Task<string> AddArchitecturePattern(string name, string description, string? diagram = null)
    {
        var dto = new ArchitecturePatternForCreate(name, description, diagram);
        var result = await architecturePatternRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateArchitecturePattern(int id, string name, string description, string? diagram, bool isActive)
    {
        var dto = new ArchitecturePatternForUpdate(name, description, diagram, isActive);
        var result = await architecturePatternRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Pattern with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> SetActiveArchitecturePattern(int id)
    {
        var success = await architecturePatternRepository.SetActiveAsync(id);
        return success ? $"Pattern {id} set as active" : $"Pattern with id {id} not found";
    }

    public async Task<string> DeleteArchitecturePattern(int id)
    {
        var success = await architecturePatternRepository.DeleteAsync(id);
        return success ? $"Pattern {id} deleted" : $"Pattern with id {id} not found";
    }

    // Architecture Layers
    public async Task<string> GetLayersByPattern(int patternId)
    {
        var layers = new List<ArchitectureLayerForList>();
        await foreach (var item in architectureLayerRepository.GetAsync(new ArchitectureLayerParameters(PageSize: 1000, PatternId: patternId)))
        {
            layers.Add(item);
        }
        return JsonSerializer.Serialize(layers, JsonOptions);
    }

    public async Task<string> GetArchitectureLayer(int id)
    {
        var layer = await architectureLayerRepository.GetByIdAsync(id);
        return layer is null
            ? $"Layer with id {id} not found"
            : JsonSerializer.Serialize(layer, JsonOptions);
    }

    public async Task<string> AddArchitectureLayer(int patternId, string name, string description, string responsibility, string? contains, int order)
    {
        var dto = new ArchitectureLayerForCreate(patternId, name, description, responsibility, contains, order);
        var result = await architectureLayerRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateArchitectureLayer(int id, string name, string description, string responsibility, string? contains, int order)
    {
        var dto = new ArchitectureLayerForUpdate(name, description, responsibility, contains, order);
        var result = await architectureLayerRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Layer with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteArchitectureLayer(int id)
    {
        var success = await architectureLayerRepository.DeleteAsync(id);
        return success ? $"Layer {id} deleted" : $"Layer with id {id} not found";
    }

    // Layer Dependencies
    public async Task<string> GetLayerDependencies(int patternId)
    {
        var dependencies = new List<LayerDependencyForList>();
        await foreach (var item in layerDependencyRepository.GetAsync(new LayerDependencyParameters(PageSize: 1000, PatternId: patternId)))
        {
            dependencies.Add(item);
        }
        return JsonSerializer.Serialize(dependencies, JsonOptions);
    }

    public async Task<string> AddLayerDependency(int sourceLayerId, int targetLayerId, bool isAllowed, string? rationale = null)
    {
        var dto = new LayerDependencyForCreate(sourceLayerId, targetLayerId, isAllowed, rationale);
        var result = await layerDependencyRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateLayerDependency(int id, bool isAllowed, string? rationale)
    {
        var dto = new LayerDependencyForUpdate(isAllowed, rationale);
        var result = await layerDependencyRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Dependency with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteLayerDependency(int id)
    {
        var success = await layerDependencyRepository.DeleteAsync(id);
        return success ? $"Dependency {id} deleted" : $"Dependency with id {id} not found";
    }

    // Code Templates
    public async Task<string> GetCodeTemplates()
    {
        var templates = new List<CodeTemplateForList>();
        await foreach (var item in codeTemplateRepository.GetAsync(new CodeTemplateParameters(PageSize: 1000)))
        {
            templates.Add(item);
        }
        return JsonSerializer.Serialize(templates, JsonOptions);
    }

    public async Task<string> GetCodeTemplate(int id)
    {
        var template = await codeTemplateRepository.GetByIdAsync(id);
        return template is null
            ? $"Template with id {id} not found"
            : JsonSerializer.Serialize(template, JsonOptions);
    }

    public async Task<string> GetCodeTemplatesByCategory(string category)
    {
        var templates = new List<CodeTemplateForList>();
        await foreach (var item in codeTemplateRepository.GetAsync(new CodeTemplateParameters(PageSize: 1000, Category: category)))
        {
            templates.Add(item);
        }
        return JsonSerializer.Serialize(templates, JsonOptions);
    }

    public async Task<string> GetCodeTemplatesByLanguage(string language)
    {
        var templates = new List<CodeTemplateForList>();
        await foreach (var item in codeTemplateRepository.GetAsync(new CodeTemplateParameters(PageSize: 1000, Language: language)))
        {
            templates.Add(item);
        }
        return JsonSerializer.Serialize(templates, JsonOptions);
    }

    public async Task<string> AddCodeTemplate(string name, string description, string language, string category, string template, int? layerId = null)
    {
        var dto = new CodeTemplateForCreate(name, description, language, category, template, layerId);
        var result = await codeTemplateRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateCodeTemplate(int id, string name, string description, string language, string category, string template, int? layerId)
    {
        var dto = new CodeTemplateForUpdate(name, description, language, category, template, layerId);
        var result = await codeTemplateRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Template with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteCodeTemplate(int id)
    {
        var success = await codeTemplateRepository.DeleteAsync(id);
        return success ? $"Template {id} deleted" : $"Template with id {id} not found";
    }

    // Naming Conventions
    public async Task<string> GetNamingConventions()
    {
        var conventions = new List<NamingConventionForList>();
        await foreach (var item in namingConventionRepository.GetAsync(new NamingConventionParameters(PageSize: 1000)))
        {
            conventions.Add(item);
        }
        return JsonSerializer.Serialize(conventions, JsonOptions);
    }

    public async Task<string> GetNamingConvention(int id)
    {
        var convention = await namingConventionRepository.GetByIdAsync(id);
        return convention is null
            ? $"Convention with id {id} not found"
            : JsonSerializer.Serialize(convention, JsonOptions);
    }

    public async Task<string> GetNamingConventionsByContext(string context)
    {
        var conventions = new List<NamingConventionForList>();
        await foreach (var item in namingConventionRepository.GetAsync(new NamingConventionParameters(PageSize: 1000, Context: context)))
        {
            conventions.Add(item);
        }
        return JsonSerializer.Serialize(conventions, JsonOptions);
    }

    public async Task<string> AddNamingConvention(string context, string pattern, string example, string? description = null, int? layerId = null)
    {
        var dto = new NamingConventionForCreate(context, pattern, example, description, layerId);
        var result = await namingConventionRepository.CreateAsync(dto);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> UpdateNamingConvention(int id, string context, string pattern, string example, string? description, int? layerId)
    {
        var dto = new NamingConventionForUpdate(context, pattern, example, description, layerId);
        var result = await namingConventionRepository.UpdateAsync(id, dto);
        return result is null
            ? $"Convention with id {id} not found"
            : JsonSerializer.Serialize(result, JsonOptions);
    }

    public async Task<string> DeleteNamingConvention(int id)
    {
        var success = await namingConventionRepository.DeleteAsync(id);
        return success ? $"Convention {id} deleted" : $"Convention with id {id} not found";
    }
}
