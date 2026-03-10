using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class ArchitectureLayerRepository(GrigoriDbContext context) : IArchitectureLayerRepository
{
    public async IAsyncEnumerable<ArchitectureLayerForList> GetAsync(
        ArchitectureLayerParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.ArchitectureLayers.AsQueryable();

        if (parameters.PatternId.HasValue)
            query = query.Where(l => l.PatternId == parameters.PatternId.Value);

        if (parameters.AfterId.HasValue)
            query = query.Where(l => l.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static l => l.Id)
            .Take(parameters.PageSize)
            .Select(static l => new ArchitectureLayerForList(
                l.Id, l.PatternId, l.Name, l.Description, l.Responsibility, l.Order))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<ArchitectureLayerForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.ArchitectureLayers
            .Where(l => l.Id == id)
            .Select(static l => new ArchitectureLayerForDetail(
                l.Id,
                l.PatternId,
                l.Pattern.Name,
                l.Name,
                l.Description,
                l.Responsibility,
                l.Contains,
                l.Order,
                l.OutgoingDependencies.Select(d => new LayerDependencyForList(
                    d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name, d.IsAllowed)).ToList(),
                l.IncomingDependencies.Select(d => new LayerDependencyForList(
                    d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name, d.IsAllowed)).ToList(),
                l.Templates.Select(t => new CodeTemplateForList(
                    t.Id, t.Name, t.Description, t.Language, t.Category, t.LayerId, l.Name)).ToList(),
                l.NamingConventions.Select(n => new NamingConventionForList(
                    n.Id, n.Context, n.Pattern, n.Example, n.LayerId, l.Name)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ArchitectureLayerForDetail> CreateAsync(ArchitectureLayerForCreate dto, CancellationToken cancellationToken = default)
    {
        var layer = new ArchitectureLayer
        {
            PatternId = dto.PatternId,
            Name = dto.Name,
            Description = dto.Description,
            Responsibility = dto.Responsibility,
            Contains = dto.Contains,
            Order = dto.Order
        };

        context.ArchitectureLayers.Add(layer);
        await context.SaveChangesAsync(cancellationToken);

        var patternName = await context.ArchitecturePatterns
            .Where(p => p.Id == dto.PatternId)
            .Select(static p => p.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        return new ArchitectureLayerForDetail(
            layer.Id, layer.PatternId, patternName, layer.Name, layer.Description,
            layer.Responsibility, layer.Contains, layer.Order, [], [], [], []);
    }

    public async Task<ArchitectureLayerForDetail?> UpdateAsync(int id, ArchitectureLayerForUpdate dto, CancellationToken cancellationToken = default)
    {
        var layer = await context.ArchitectureLayers.FindAsync([id], cancellationToken);
        if (layer is null) return null;

        layer.Name = dto.Name;
        layer.Description = dto.Description;
        layer.Responsibility = dto.Responsibility;
        layer.Contains = dto.Contains;
        layer.Order = dto.Order;

        await context.SaveChangesAsync(cancellationToken);

        return await context.ArchitectureLayers
            .Where(l => l.Id == id)
            .Select(static l => new ArchitectureLayerForDetail(
                l.Id,
                l.PatternId,
                l.Pattern.Name,
                l.Name,
                l.Description,
                l.Responsibility,
                l.Contains,
                l.Order,
                l.OutgoingDependencies.Select(d => new LayerDependencyForList(
                    d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name, d.IsAllowed)).ToList(),
                l.IncomingDependencies.Select(d => new LayerDependencyForList(
                    d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name, d.IsAllowed)).ToList(),
                l.Templates.Select(t => new CodeTemplateForList(
                    t.Id, t.Name, t.Description, t.Language, t.Category, t.LayerId, l.Name)).ToList(),
                l.NamingConventions.Select(n => new NamingConventionForList(
                    n.Id, n.Context, n.Pattern, n.Example, n.LayerId, l.Name)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var layer = await context.ArchitectureLayers.FindAsync([id], cancellationToken);
        if (layer is null) return false;

        context.ArchitectureLayers.Remove(layer);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
