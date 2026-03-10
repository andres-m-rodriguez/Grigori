using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class NamingConventionRepository(GrigoriDbContext context) : INamingConventionRepository
{
    public async IAsyncEnumerable<NamingConventionForList> GetAsync(
        NamingConventionParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.NamingConventions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.Context))
            query = query.Where(n => n.Context == parameters.Context);

        if (parameters.LayerId.HasValue)
            query = query.Where(n => n.LayerId == parameters.LayerId.Value);

        if (parameters.AfterId.HasValue)
            query = query.Where(n => n.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static n => n.Id)
            .Take(parameters.PageSize)
            .Select(static n => new NamingConventionForList(
                n.Id, n.Context, n.Pattern, n.Example, n.LayerId,
                n.Layer != null ? n.Layer.Name : null))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<IReadOnlyList<string>> GetContextsAsync(CancellationToken cancellationToken = default)
    {
        return await context.NamingConventions
            .Select(static n => n.Context)
            .Distinct()
            .OrderBy(static c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<NamingConventionForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.NamingConventions
            .Where(n => n.Id == id)
            .Select(static n => new NamingConventionForDetail(
                n.Id, n.Context, n.Pattern, n.Example, n.Description,
                n.LayerId, n.Layer != null ? n.Layer.Name : null, n.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NamingConventionForDetail> CreateAsync(NamingConventionForCreate dto, CancellationToken cancellationToken = default)
    {
        var actualLayerId = dto.LayerId is null or <= 0 ? null : dto.LayerId;

        var convention = new NamingConvention
        {
            Context = dto.Context,
            Pattern = dto.Pattern,
            Example = dto.Example,
            Description = dto.Description,
            LayerId = actualLayerId,
            CreatedAt = DateTime.UtcNow
        };

        context.NamingConventions.Add(convention);
        await context.SaveChangesAsync(cancellationToken);

        string? layerName = null;
        if (actualLayerId.HasValue)
        {
            layerName = await context.ArchitectureLayers
                .Where(l => l.Id == actualLayerId.Value)
                .Select(static l => l.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new NamingConventionForDetail(
            convention.Id, convention.Context, convention.Pattern, convention.Example,
            convention.Description, convention.LayerId, layerName, convention.CreatedAt);
    }

    public async Task<NamingConventionForDetail?> UpdateAsync(int id, NamingConventionForUpdate dto, CancellationToken cancellationToken = default)
    {
        var convention = await context.NamingConventions.FindAsync([id], cancellationToken);
        if (convention is null) return null;

        var actualLayerId = dto.LayerId is null or <= 0 ? null : dto.LayerId;

        convention.Context = dto.Context;
        convention.Pattern = dto.Pattern;
        convention.Example = dto.Example;
        convention.Description = dto.Description;
        convention.LayerId = actualLayerId;

        await context.SaveChangesAsync(cancellationToken);

        string? layerName = null;
        if (actualLayerId.HasValue)
        {
            layerName = await context.ArchitectureLayers
                .Where(l => l.Id == actualLayerId.Value)
                .Select(static l => l.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new NamingConventionForDetail(
            convention.Id, convention.Context, convention.Pattern, convention.Example,
            convention.Description, convention.LayerId, layerName, convention.CreatedAt);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var convention = await context.NamingConventions.FindAsync([id], cancellationToken);
        if (convention is null) return false;

        context.NamingConventions.Remove(convention);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
