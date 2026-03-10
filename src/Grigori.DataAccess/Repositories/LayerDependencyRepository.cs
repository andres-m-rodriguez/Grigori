using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class LayerDependencyRepository(GrigoriDbContext context) : ILayerDependencyRepository
{
    public async IAsyncEnumerable<LayerDependencyForList> GetAsync(
        LayerDependencyParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.LayerDependencies.AsQueryable();

        if (parameters.PatternId.HasValue)
            query = query.Where(d => d.SourceLayer.PatternId == parameters.PatternId.Value);

        if (parameters.LayerId.HasValue)
            query = query.Where(d => d.SourceLayerId == parameters.LayerId.Value || d.TargetLayerId == parameters.LayerId.Value);

        if (parameters.AfterId.HasValue)
            query = query.Where(d => d.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static d => d.Id)
            .Take(parameters.PageSize)
            .Select(static d => new LayerDependencyForList(
                d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name, d.IsAllowed))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<LayerDependencyForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.LayerDependencies
            .Where(d => d.Id == id)
            .Select(static d => new LayerDependencyForDetail(
                d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name,
                d.IsAllowed, d.Rationale))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LayerDependencyForDetail> CreateAsync(LayerDependencyForCreate dto, CancellationToken cancellationToken = default)
    {
        var dependency = new LayerDependency
        {
            SourceLayerId = dto.SourceLayerId,
            TargetLayerId = dto.TargetLayerId,
            IsAllowed = dto.IsAllowed,
            Rationale = dto.Rationale
        };

        context.LayerDependencies.Add(dependency);
        await context.SaveChangesAsync(cancellationToken);

        var sourceLayerName = await context.ArchitectureLayers
            .Where(l => l.Id == dto.SourceLayerId)
            .Select(static l => l.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        var targetLayerName = await context.ArchitectureLayers
            .Where(l => l.Id == dto.TargetLayerId)
            .Select(static l => l.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        return new LayerDependencyForDetail(
            dependency.Id, dependency.SourceLayerId, sourceLayerName,
            dependency.TargetLayerId, targetLayerName,
            dependency.IsAllowed, dependency.Rationale);
    }

    public async Task<LayerDependencyForDetail?> UpdateAsync(int id, LayerDependencyForUpdate dto, CancellationToken cancellationToken = default)
    {
        var dependency = await context.LayerDependencies.FindAsync([id], cancellationToken);
        if (dependency is null) return null;

        dependency.IsAllowed = dto.IsAllowed;
        dependency.Rationale = dto.Rationale;

        await context.SaveChangesAsync(cancellationToken);

        return await context.LayerDependencies
            .Where(d => d.Id == id)
            .Select(static d => new LayerDependencyForDetail(
                d.Id, d.SourceLayerId, d.SourceLayer.Name, d.TargetLayerId, d.TargetLayer.Name,
                d.IsAllowed, d.Rationale))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var dependency = await context.LayerDependencies.FindAsync([id], cancellationToken);
        if (dependency is null) return false;

        context.LayerDependencies.Remove(dependency);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
