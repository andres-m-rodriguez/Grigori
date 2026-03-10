using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class CodeTemplateRepository(GrigoriDbContext context) : ICodeTemplateRepository
{
    public async IAsyncEnumerable<CodeTemplateForList> GetAsync(
        CodeTemplateParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.CodeTemplates.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.Category))
            query = query.Where(t => t.Category == parameters.Category);

        if (!string.IsNullOrWhiteSpace(parameters.Language))
            query = query.Where(t => t.Language == parameters.Language);

        if (parameters.LayerId.HasValue)
            query = query.Where(t => t.LayerId == parameters.LayerId.Value);

        if (parameters.AfterId.HasValue)
            query = query.Where(t => t.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static t => t.Id)
            .Take(parameters.PageSize)
            .Select(static t => new CodeTemplateForList(
                t.Id, t.Name, t.Description, t.Language, t.Category, t.LayerId,
                t.Layer != null ? t.Layer.Name : null))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await context.CodeTemplates
            .Select(static t => t.Category)
            .Distinct()
            .OrderBy(static c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        return await context.CodeTemplates
            .Select(static t => t.Language)
            .Distinct()
            .OrderBy(static l => l)
            .ToListAsync(cancellationToken);
    }

    public async Task<CodeTemplateForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.CodeTemplates
            .Where(t => t.Id == id)
            .Select(static t => new CodeTemplateForDetail(
                t.Id, t.Name, t.Description, t.Language, t.Category, t.Template,
                t.LayerId, t.Layer != null ? t.Layer.Name : null, t.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CodeTemplateForDetail> CreateAsync(CodeTemplateForCreate dto, CancellationToken cancellationToken = default)
    {
        var actualLayerId = dto.LayerId is null or <= 0 ? null : dto.LayerId;

        var template = new CodeTemplate
        {
            Name = dto.Name,
            Description = dto.Description,
            Language = dto.Language,
            Category = dto.Category,
            Template = dto.Template,
            LayerId = actualLayerId,
            CreatedAt = DateTime.UtcNow
        };

        context.CodeTemplates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        string? layerName = null;
        if (actualLayerId.HasValue)
        {
            layerName = await context.ArchitectureLayers
                .Where(l => l.Id == actualLayerId.Value)
                .Select(static l => l.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new CodeTemplateForDetail(
            template.Id, template.Name, template.Description, template.Language,
            template.Category, template.Template, template.LayerId, layerName, template.CreatedAt);
    }

    public async Task<CodeTemplateForDetail?> UpdateAsync(int id, CodeTemplateForUpdate dto, CancellationToken cancellationToken = default)
    {
        var template = await context.CodeTemplates.FindAsync([id], cancellationToken);
        if (template is null) return null;

        var actualLayerId = dto.LayerId is null or <= 0 ? null : dto.LayerId;

        template.Name = dto.Name;
        template.Description = dto.Description;
        template.Language = dto.Language;
        template.Category = dto.Category;
        template.Template = dto.Template;
        template.LayerId = actualLayerId;

        await context.SaveChangesAsync(cancellationToken);

        string? layerName = null;
        if (actualLayerId.HasValue)
        {
            layerName = await context.ArchitectureLayers
                .Where(l => l.Id == actualLayerId.Value)
                .Select(static l => l.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new CodeTemplateForDetail(
            template.Id, template.Name, template.Description, template.Language,
            template.Category, template.Template, template.LayerId, layerName, template.CreatedAt);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var template = await context.CodeTemplates.FindAsync([id], cancellationToken);
        if (template is null) return false;

        context.CodeTemplates.Remove(template);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
